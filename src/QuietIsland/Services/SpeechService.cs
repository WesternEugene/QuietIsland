using System.Net.Http;
using System.Net.Http.Headers;
using System.Speech.Recognition;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using QuietIsland.Core;

namespace QuietIsland.Services;

public sealed class SpeechService(DesktopState state) : IDisposable
{
    private SpeechRecognitionEngine? recognizer;
    private WasapiCapture? capture;
    private WaveFileWriter? writer;
    private string? path;
    private readonly object audioGate = new();
    private readonly List<string> phrases = [];
    private readonly SpeechSessionGate gate = new();
    private (bool Local, string Endpoint, string Key) lastConfiguration;
    private bool configured;
    public bool IsConfigured
    {
        get
        {
            var settings = state.Data.Settings;
            var next = (settings.SpeechPreferLocal, settings.SpeechEndpoint, settings.SpeechKeyEncrypted);
            if (next != lastConfiguration)
            {
                lastConfiguration = next;
                configured = settings.SpeechPreferLocal || !string.IsNullOrWhiteSpace(settings.SpeechEndpoint)
                    && !string.IsNullOrWhiteSpace(DesktopState.Unprotect(settings.SpeechKeyEncrypted));
            }
            return configured;
        }
    }
    public bool IsListening => gate.Stage == SpeechStage.Listening;
    public bool IsTranscribing => gate.Stage == SpeechStage.Transcribing;
    public event Action<string>? Partial;
    public void Start()
    {
        if (!state.Data.Settings.SpeechPreferLocal) RemoteConfiguration();
        gate.BeginListening();
        try
        {
        phrases.Clear();
        if (state.Data.Settings.SpeechPreferLocal && TryStartLocal()) return;
        RemoteConfiguration();
        Directory.CreateDirectory(state.DirectoryPath); path = Path.Combine(state.DirectoryPath, "dictation-" + Guid.NewGuid().ToString("N") + ".wav");
        using var enumerator = new MMDeviceEnumerator();
        var device = string.IsNullOrEmpty(state.Data.Settings.MicrophoneId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia) : enumerator.GetDevice(state.Data.Settings.MicrophoneId);
        capture = new WasapiCapture(device); writer = new WaveFileWriter(path, capture.WaveFormat);
        capture.DataAvailable += (_, e) => { lock (audioGate) writer?.Write(e.Buffer, 0, e.BytesRecorded); };
        capture.StartRecording();
        }
        catch
        {
            recognizer?.Dispose(); recognizer = null; capture?.Dispose(); capture = null;
            lock (audioGate) { writer?.Dispose(); writer = null; }
            if (path != null && File.Exists(path)) File.Delete(path); path = null; gate.Finish(); throw;
        }
    }
    public async Task<string> StopAsync(CancellationToken token = default)
    {
        if (!gate.BeginTranscription()) return "";
        var recordedPath = path; path = null;
        try
        {
        if (recognizer != null)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            recognizer.RecognizeCompleted += (_, _) => completion.TrySetResult(); recognizer.RecognizeAsyncStop();
            try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(3), token); } catch (TimeoutException) { }
            finally { recognizer.Dispose(); recognizer = null; }
            lock (phrases) return string.Join(" ", phrases);
        }
        capture?.StopRecording(); capture?.Dispose(); capture = null; lock (audioGate) { writer?.Dispose(); writer = null; }
        if (recordedPath == null) return "";
            var (endpoint, key) = RemoteConfiguration();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint + "/audio/transcriptions");
            request.Headers.Authorization = new("Bearer", key);
            using var content = new MultipartFormDataContent(); using var stream = File.OpenRead(recordedPath);
            var file = new StreamContent(stream); file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav"); content.Add(file, "file", "dictation.wav"); content.Add(new StringContent(state.Data.Settings.SpeechModel), "model"); content.Add(new StringContent("ru"), "language"); request.Content = content;
            using var response = await client.SendAsync(request, token); if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Речевой сервис ответил HTTP " + (int)response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); return doc.RootElement.GetProperty("text").GetString() ?? "";
        }
        finally
        {
            try { if (recordedPath != null && File.Exists(recordedPath)) File.Delete(recordedPath); }
            finally { gate.Finish(); }
        }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private bool TryStartLocal()
    {
        var available = SpeechRecognitionEngine.InstalledRecognizers();
        if (available.Count == 0) return false;
        var info = available.OrderByDescending(x => x.Culture.TwoLetterISOLanguageName == "ru").First();
        recognizer = new SpeechRecognitionEngine(info); recognizer.LoadGrammar(new DictationGrammar()); recognizer.SetInputToDefaultAudioDevice();
        recognizer.SpeechRecognized += (_, e) => { lock (phrases) phrases.Add(e.Result.Text); Partial?.Invoke(e.Result.Text); };
        recognizer.RecognizeAsync(RecognizeMode.Multiple); return true;
    }
    private (string Endpoint, string Key) RemoteConfiguration()
    {
        var key = DesktopState.Unprotect(state.Data.Settings.SpeechKeyEncrypted).Trim();
        if (string.IsNullOrWhiteSpace(state.Data.Settings.SpeechEndpoint) || key.Length == 0)
            throw new InvalidOperationException("Диктовка отключена. В разделе «Настройки → Диктовка» включите локальный движок или укажите API URL и ключ речевого сервиса.");
        var endpoint = state.Data.Settings.SpeechEndpoint.Trim().TrimEnd('/');
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException("Некорректный URL речевого сервиса в разделе «Настройки → Диктовка».");
        return (endpoint, key);
    }
    public void Dispose() { recognizer?.RecognizeAsyncCancel(); recognizer?.Dispose(); capture?.StopRecording(); capture?.Dispose(); lock (audioGate) { writer?.Dispose(); writer = null; } gate.Finish(); }
}
