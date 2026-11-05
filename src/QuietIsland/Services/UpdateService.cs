using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using QuietIsland.Core;

namespace QuietIsland.Services;

public sealed class UpdateService(Func<bool> automaticChecks, UpdateClient? client = null, Version? currentVersion = null) : IDisposable
{
    private readonly UpdateClient client = client ?? new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim checking = new(1, 1), downloading = new(1, 1);
    private CancellationTokenSource? downloadCancellation;
    private Task? loop;
    private bool disposed;
    public Version CurrentVersion { get; } = currentVersion ?? typeof(UpdateService).Assembly.GetName().Version ?? new(0, 0, 0);
    public string CurrentVersionText { get; } = currentVersion == null
        ? typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? (typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0")
        : currentVersion.Revision > 0 ? currentVersion.ToString(4) : currentVersion.ToString(3);
    public AppRelease? Available { get; private set; }
    public string Status { get; private set; } = "Обновления ещё не проверялись.";
    public bool IsChecking { get; private set; }
    public bool IsDownloading { get; private set; }
    public double Progress { get; private set; }
    public event Action? Changed;

    public void Start() { ObjectDisposedException.ThrowIf(disposed, this); loop ??= PollAsync(lifetime.Token); }
    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (automaticChecks()) await CheckAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
    public async Task CheckAsync()
    {
        if (disposed || !await checking.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            IsChecking = true; Status = "Проверяем обновления…"; Changed?.Invoke();
            Available = await client.CheckAsync(CurrentVersion, lifetime.Token, CurrentVersionText.StartsWith('a')).ConfigureAwait(false);
            Status = Available == null ? "У вас последняя версия." : $"Доступна версия {Available.DisplayVersion}.";
        }
        catch (OperationCanceledException) { Status = "Не удалось проверить обновления: время ожидания истекло."; }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException or InvalidOperationException)
        {
            Status = "Не удалось проверить обновления. Проверьте подключение и повторите попытку.";
            Log.Write("Update check", error);
        }
        finally { IsChecking = false; checking.Release(); Changed?.Invoke(); }
    }
    public async Task<string> DownloadAsync(AppRelease release, string directory)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!await downloading.WaitAsync(0).ConfigureAwait(false)) throw new InvalidOperationException("Обновление уже загружается.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        downloadCancellation = cancellation;
        try
        {
            IsDownloading = true; Progress = 0; Status = "Загрузка обновления…"; Changed?.Invoke();
            var progress = new DownloadProgress(value =>
            {
                Progress = value; Status = $"Загрузка обновления… {value:0}%"; Changed?.Invoke();
            });
            var path = await client.DownloadAsync(release, directory, progress, cancellation.Token).ConfigureAwait(false);
            Status = "Установщик загружен и проверен.";
            return path;
        }
        catch (OperationCanceledException)
        {
            Status = cancellation.IsCancellationRequested ? "Загрузка отменена." : "Сервер не ответил вовремя. Повторите загрузку.";
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = error is InvalidDataException ? error.Message : "Не удалось скачать обновление. Проверьте подключение и свободное место.";
            throw new InvalidOperationException(Status, error);
        }
        finally { downloadCancellation = null; IsDownloading = false; downloading.Release(); Changed?.Invoke(); }
    }
    public void CancelDownload() => downloadCancellation?.Cancel();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel(); CancelDownload(); client.Dispose();
        // Running operations still own linked cancellation registrations.
        if (loop == null) lifetime.Dispose();
        else _ = loop.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
    }
    private sealed class DownloadProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
}
