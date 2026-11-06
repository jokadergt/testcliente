using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PiceVoice;

record VoicePreset(string Name, string Description, float Pitch, float Robot, float Drive, float GateDb, Color Accent, bool UsesReference = false);
record VoiceSettings(float Pitch, float Robot, float Drive, float GateDb, bool ReferenceEnabled);

sealed class ReferenceVoiceModel
{
    public bool Ready { get; private set; }
    public string Label { get; private set; } = "nenhuma referência";
    public float LowGain { get; private set; } = 1;
    public float MidGain { get; private set; } = 1;
    public float HighGain { get; private set; } = 1;
    public float Compression { get; private set; } = 1;
    float lowState, highState;

    public async Task AnalyzeAsync(string path)
    {
        await Task.Run(() =>
        {
            using var reader = new AudioFileReader(path);
            var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels / 2];
            long samples = 0;
            double low = 0, mid = 0, high = 0, total = 0;
            float lp = 0, hp = 0;
            int read;
            var maxSamples = reader.WaveFormat.SampleRate * reader.WaveFormat.Channels * 35L;
            while (samples < maxSamples && (read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    var x = buffer[i];
                    lp += .018f * (x - lp);
                    hp += .22f * (x - hp);
                    var lo = lp;
                    var hi = x - hp;
                    var mi = x - lo - hi;
                    low += lo * lo; mid += mi * mi; high += hi * hi; total += x * x;
                }
                samples += read;
            }
            if (samples < 2048 || total < .00001) throw new InvalidDataException("A referência precisa conter voz audível.");
            var sum = Math.Max(low + mid + high, .000001);
            var l = low / sum; var m = mid / sum; var h = high / sum;
            LowGain = Math.Clamp((float)(.7 + l * 2.1), .65f, 1.65f);
            MidGain = Math.Clamp((float)(.72 + m * 2.0), .7f, 1.7f);
            HighGain = Math.Clamp((float)(.7 + h * 2.2), .62f, 1.6f);
            var rms = Math.Sqrt(total / samples);
            Compression = Math.Clamp((float)(.16 / Math.Max(rms, .025)), .75f, 2.2f);
            Label = Path.GetFileName(path);
            Ready = true;
        });
    }

    public float Process(float x)
    {
        lowState += .018f * (x - lowState);
        highState += .22f * (x - highState);
        var low = lowState;
        var high = x - highState;
        var mid = x - low - high;
        var shaped = low * LowGain + mid * MidGain + high * HighGain;
        return MathF.Tanh(shaped * Compression);
    }
}

sealed class VoiceEngine : IDisposable
{
    WasapiCapture? capture;
    WasapiOut? output;
    WasapiOut? monitor;
    BufferedWaveProvider? outputQueue;
    BufferedWaveProvider? monitorQueue;
    double pitchPhase;
    float envelope;
    float robotPhase;
    public ReferenceVoiceModel Reference { get; } = new();
    public VoiceSettings Settings { get; set; } = new(0, 0, .05f, -52, false);
    public bool Running => capture is not null;
    public bool Monitoring => monitor is not null;
    public event Action<float>? LevelChanged;

    public void Start(MMDevice inputDevice, MMDevice outputDevice, VoiceSettings settings)
    {
        Stop();
        Settings = settings;
        capture = new WasapiCapture(inputDevice, true, 20);
        outputQueue = NewQueue(capture.WaveFormat);
        output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 35);
        output.Init(outputQueue);
        capture.DataAvailable += OnAudio;
        output.Play();
        capture.StartRecording();
    }

    static BufferedWaveProvider NewQueue(WaveFormat f) => new(f)
    {
        BufferDuration = TimeSpan.FromMilliseconds(300),
        DiscardOnBufferOverflow = true,
        ReadFully = true
    };

    public void StartMonitor(MMDevice monitorDevice)
    {
        if (capture is null) throw new InvalidOperationException("Ative a voz primeiro.");
        StopMonitor();
        monitorQueue = NewQueue(capture.WaveFormat);
        monitor = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, 45);
        monitor.Init(monitorQueue);
        monitor.Play();
    }

    public void StopMonitor()
    {
        monitor?.Stop(); monitor?.Dispose(); monitor = null; monitorQueue = null;
    }

    void OnAudio(object? sender, WaveInEventArgs e)
    {
        if (capture is null || outputQueue is null) return;
        var format = capture.WaveFormat;
        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
        {
            outputQueue.AddSamples(e.Buffer, 0, e.BytesRecorded);
            monitorQueue?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            return;
        }

        var count = e.BytesRecorded / 4;
        var data = new float[count];
        Buffer.BlockCopy(e.Buffer, 0, data, 0, e.BytesRecorded);
        var source = (float[])data.Clone();
        int channels = format.Channels, frames = count / channels;
        var s = Settings;
        var ratio = Math.Pow(2, s.Pitch / 12.0);
        var threshold = MathF.Pow(10, s.GateDb / 20f);
        float peak = 0;

        for (int frame = 0; frame < frames; frame++)
        {
            var pos = pitchPhase;
            int a = Math.Clamp((int)pos, 0, frames - 1), b = Math.Min(a + 1, frames - 1);
            var frac = (float)(pos - a);
            robotPhase += .055f;
            var ring = MathF.Sin(robotPhase);
            for (int ch = 0; ch < channels; ch++)
            {
                var dry = source[frame * channels + ch];
                var shifted = source[a * channels + ch] * (1 - frac) + source[b * channels + ch] * frac;
                var x = s.Pitch == 0 ? dry : shifted;
                envelope = Math.Max(Math.Abs(x), envelope * .996f);
                var gate = envelope < threshold ? envelope / Math.Max(threshold, .00001f) : 1f;
                x = x * (1 - s.Robot) + x * ring * s.Robot;
                if (s.ReferenceEnabled && Reference.Ready) x = Reference.Process(x);
                x = MathF.Tanh(x * (1 + s.Drive * 8)) * gate;
                data[frame * channels + ch] = Math.Clamp(x, -1, 1);
                peak = Math.Max(peak, Math.Abs(x));
            }
            pitchPhase += ratio;
            while (pitchPhase >= frames) pitchPhase -= frames;
        }

        var bytes = new byte[e.BytesRecorded];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        outputQueue.AddSamples(bytes, 0, bytes.Length);
        monitorQueue?.AddSamples(bytes, 0, bytes.Length);
        LevelChanged?.Invoke(peak);
    }

    public void Stop()
    {
        StopMonitor();
        var old = capture; capture = null;
        if (old is not null)
        {
            old.DataAvailable -= OnAudio;
            try { old.StopRecording(); } catch { }
            old.Dispose();
        }
        output?.Stop(); output?.Dispose(); output = null; outputQueue = null;
        LevelChanged?.Invoke(0);
    }

    public void Dispose() => Stop();
}