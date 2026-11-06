using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoxForge;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new VoiceForm());
    }
}

sealed class VoiceForm : Form
{
    readonly ComboBox mic = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    readonly ComboBox output = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    readonly ComboBox preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    readonly TrackBar pitch = new() { Minimum = -12, Maximum = 12, TickStyle = TickStyle.None, Dock = DockStyle.Top };
    readonly TrackBar robot = new() { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, Dock = DockStyle.Top };
    readonly TrackBar drive = new() { Minimum = 0, Maximum = 100, Value = 8, TickStyle = TickStyle.None, Dock = DockStyle.Top };
    readonly TrackBar gate = new() { Minimum = -70, Maximum = -20, Value = -52, TickStyle = TickStyle.None, Dock = DockStyle.Top };
    readonly Button toggle = new() { Text = "ATIVAR VOZ", Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(107, 79, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly Label status = new() { Text = "parado", AutoSize = true, ForeColor = Color.Silver };
    readonly List<MMDevice> inputs = [];
    readonly List<MMDevice> outputs = [];
    readonly VoiceEngine engine = new();

    public VoiceForm()
    {
        Text = "VoxForge";
        ClientSize = new Size(560, 610);
        BackColor = Color.FromArgb(17, 19, 28);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24), AutoScroll = true };
        Controls.Add(panel);
        panel.Controls.Add(new Label { Text = "VOXFORGE", Font = new Font("Segoe UI Semibold", 24), AutoSize = true });
        panel.Controls.Add(new Label { Text = "voz em tempo real para jogos e navegador", AutoSize = true, ForeColor = Color.FromArgb(160, 170, 200), Margin = new Padding(3, 0, 3, 18) });
        Add(panel, "microfone", mic);
        Add(panel, "saída virtual (CABLE Input)", output);
        preset.Items.AddRange(["Natural", "Grave", "Aguda", "Robô", "Rádio", "Monstro"]);
        preset.SelectedIndex = 0;
        Add(panel, "preset", preset);
        Add(panel, "tom", pitch);
        Add(panel, "robô", robot);
        Add(panel, "saturação", drive);
        Add(panel, "redução de ruído", gate);
        toggle.Width = 490;
        panel.Controls.Add(toggle);
        panel.Controls.Add(status);
        LoadDevices();
        toggle.Click += (_, _) => Toggle();
        preset.SelectedIndexChanged += (_, _) => ApplyPreset();
        foreach (var bar in new[] { pitch, robot, drive, gate }) bar.ValueChanged += (_, _) => engine.Settings = Current();
        FormClosed += (_, _) => engine.Dispose();
    }

    static void Add(FlowLayoutPanel p, string title, Control c)
    {
        p.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.FromArgb(180, 190, 215), Margin = new Padding(3, 7, 3, 1) });
        c.Width = 490;
        p.Controls.Add(c);
    }

    void LoadDevices()
    {
        using var e = new MMDeviceEnumerator();
        inputs.AddRange(e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active));
        outputs.AddRange(e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active));
        mic.Items.AddRange(inputs.Select(x => x.FriendlyName).ToArray());
        output.Items.AddRange(outputs.Select(x => x.FriendlyName).ToArray());
        if (mic.Items.Count > 0) mic.SelectedIndex = 0;
        var cable = outputs.FindIndex(x => x.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
        if (output.Items.Count > 0) output.SelectedIndex = cable >= 0 ? cable : 0;
    }

    VoiceSettings Current() => new(pitch.Value, robot.Value / 100f, drive.Value / 100f, gate.Value);

    void Toggle()
    {
        if (engine.Running)
        {
            engine.Stop();
            toggle.Text = "ATIVAR VOZ";
            status.Text = "parado";
            return;
        }
        if (mic.SelectedIndex < 0 || output.SelectedIndex < 0) { status.Text = "selecione entrada e saída"; return; }
        try
        {
            engine.Start(inputs[mic.SelectedIndex], outputs[output.SelectedIndex], Current());
            toggle.Text = "DESATIVAR";
            status.Text = "ativo — use CABLE Output como microfone no jogo";
        }
        catch (Exception ex) { engine.Stop(); status.Text = ex.Message; }
    }

    void ApplyPreset()
    {
        (pitch.Value, robot.Value, drive.Value, gate.Value) = preset.SelectedIndex switch
        {
            1 => (-6, 0, 12, -54), 2 => (5, 0, 5, -50), 3 => (-1, 75, 18, -48),
            4 => (0, 10, 55, -45), 5 => (-9, 12, 48, -50), _ => (0, 0, 8, -52)
        };
    }
}

record VoiceSettings(float Pitch, float Robot, float Drive, float GateDb);

sealed class VoiceEngine : IDisposable
{
    WasapiCapture? capture;
    WasapiOut? render;
    BufferedWaveProvider? queue;
    double phase;
    float envelope;
    int robotPhase;
    public VoiceSettings Settings { get; set; } = new(0, 0, .08f, -52);
    public bool Running => capture is not null;

    public void Start(MMDevice input, MMDevice output, VoiceSettings settings)
    {
        Stop();
        Settings = settings;
        capture = new WasapiCapture(input, true, 20);
        queue = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromMilliseconds(250), DiscardOnBufferOverflow = true };
        render = new WasapiOut(output, AudioClientShareMode.Shared, true, 30);
        render.Init(queue);
        capture.DataAvailable += Process;
        render.Play();
        capture.StartRecording();
    }

    void Process(object? sender, WaveInEventArgs e)
    {
        if (capture is null || queue is null) return;
        if (capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat || capture.WaveFormat.BitsPerSample != 32)
        {
            queue.AddSamples(e.Buffer, 0, e.BytesRecorded);
            return;
        }
        var count = e.BytesRecorded / 4;
        var samples = new float[count];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
        var source = (float[])samples.Clone();
        var channels = capture.WaveFormat.Channels;
        var frames = count / channels;
        var s = Settings;
        var ratio = Math.Pow(2, s.Pitch / 12.0);
        var threshold = MathF.Pow(10, s.GateDb / 20f);
        for (var f = 0; f < frames; f++)
        {
            var pos = phase;
            var a = Math.Clamp((int)pos, 0, frames - 1);
            var b = Math.Min(a + 1, frames - 1);
            var mix = (float)(pos - a);
            robotPhase += 91;
            var ring = MathF.Sin(robotPhase * .001f);
            for (var ch = 0; ch < channels; ch++)
            {
                var dry = source[f * channels + ch];
                var shifted = source[a * channels + ch] * (1 - mix) + source[b * channels + ch] * mix;
                var x = s.Pitch == 0 ? dry : shifted;
                envelope = Math.Max(Math.Abs(x), envelope * .996f);
                var g = envelope < threshold ? envelope / Math.Max(threshold, .00001f) : 1f;
                x = x * (1 - s.Robot) + x * ring * s.Robot;
                x = MathF.Tanh(x * (1 + s.Drive * 8)) * g;
                samples[f * channels + ch] = Math.Clamp(x, -1, 1);
            }
            phase += ratio;
            while (phase >= frames) phase -= frames;
        }
        Buffer.BlockCopy(samples, 0, e.Buffer, 0, e.BytesRecorded);
        queue.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    public void Stop()
    {
        var c = capture; capture = null;
        if (c is not null) { c.DataAvailable -= Process; try { c.StopRecording(); } catch { } c.Dispose(); }
        render?.Stop(); render?.Dispose(); render = null; queue = null;
    }
    public void Dispose() => Stop();
}