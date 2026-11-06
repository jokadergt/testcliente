using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PiceVoice;

record InstantSound(string Id, string Title, string Mp3, string PageUrl, string? LocalPath = null);

sealed class MyInstantsClient
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    const string Api = "https://myinstants-api.vercel.app";

    public MyInstantsClient()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PiceVoice/1.0");
    }

    public async Task<IReadOnlyList<InstantSound>> SearchAsync(string query, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(query)) query = "memes";
        var url = $"{Api}/search?q={Uri.EscapeDataString(query.Trim())}";
        var response = await http.GetFromJsonAsync<ApiResponse>(url, token);
        return response?.Data?
            .Where(x => !string.IsNullOrWhiteSpace(x.Mp3))
            .Take(18)
            .Select(x => new InstantSound(x.Id ?? Guid.NewGuid().ToString("N"), x.Title ?? "Som", x.Mp3!, x.Url ?? ""))
            .ToList() ?? [];
    }

    public async Task<InstantSound> DownloadAsync(InstantSound sound, IProgress<int>? progress = null, CancellationToken token = default)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PiceVoice", "Soundboard");
        Directory.CreateDirectory(folder);
        var safe = string.Concat(sound.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (safe.Length > 55) safe = safe[..55];
        var path = Path.Combine(folder, $"{safe}-{sound.Id}.mp3");
        using var response = await http.GetAsync(sound.Mp3, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var file = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), token);
            done += read;
            if (total > 0) progress?.Report((int)(done * 100 / total.Value));
        }
        return sound with { LocalPath = path };
    }

    sealed class ApiResponse
    {
        [JsonPropertyName("data")] public List<ApiSound>? Data { get; set; }
    }
    sealed class ApiSound
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("mp3")] public string? Mp3 { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}

sealed class SoundboardPlayer : IDisposable
{
    MediaFoundationReader? reader;
    WasapiOut? player;
    public bool Playing => player?.PlaybackState == PlaybackState.Playing;
    public event Action? Finished;

    public void Play(string path, MMDevice output)
    {
        Stop();
        reader = new MediaFoundationReader(path);
        player = new WasapiOut(output, AudioClientShareMode.Shared, true, 40);
        player.Init(reader);
        player.PlaybackStopped += OnStopped;
        player.Play();
    }

    void OnStopped(object? sender, StoppedEventArgs e)
    {
        Finished?.Invoke();
    }

    public void Stop()
    {
        if (player is not null) player.PlaybackStopped -= OnStopped;
        player?.Stop(); player?.Dispose(); player = null;
        reader?.Dispose(); reader = null;
    }

    public void Dispose() => Stop();
}