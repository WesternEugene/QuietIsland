using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace QuietIsland.Core;

public sealed class UpdateClient : IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsClient;
    public UpdateClient(HttpClient? http = null)
    {
        ownsClient = http == null;
        this.http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<AppRelease?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = Request(new Uri($"https://api.github.com/repos/{AppRelease.Repository}/releases/latest"));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return AppRelease.Parse(await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false), current);
    }

    public async Task<string> DownloadAsync(AppRelease release, string directory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!release.CanInstall || !AppRelease.IsInstallerUrl(release.InstallerUrl!, release.Tag, release.InstallerName!)
            || Path.GetFileName(release.InstallerName) != release.InstallerName)
            throw new InvalidOperationException("Установщик отсутствует в официальном релизе.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, release.InstallerName!);
        if (File.Exists(destination) && await IsValidAsync(destination, release, cancellationToken).ConfigureAwait(false)) return destination;
        var temporary = destination + ".part-" + Guid.NewGuid().ToString("N");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            using var request = Request(release.InstallerUrl!);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Size)
                throw new InvalidDataException("Размер установщика не совпал с релизом. Повторите загрузку.");
            await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; long total = 0; var lastPercent = -1;
                while (await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false) is var count && count > 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("Установщик больше файла, указанного в релизе.");
                    await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                    var percent = (int)(100 * total / release.Size);
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
                }
            }
            deadline.Token.ThrowIfCancellationRequested();
            if (!await IsValidAsync(temporary, release, deadline.Token).ConfigureAwait(false))
                throw new InvalidDataException("Загруженный установщик повреждён или не прошёл проверку SHA-256. Повторите загрузку.");
            File.Move(temporary, destination, true);
            return destination;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<bool> IsValidAsync(string path, AppRelease release, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length != release.Size) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var signature = new byte[2];
        if (await file.ReadAsync(signature, cancellationToken).ConfigureAwait(false) != 2 || signature[0] != 'M' || signature[1] != 'Z') return false;
        if (release.Sha256 == null) return true;
        file.Position = 0;
        var hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private static HttpRequestMessage Request(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("QuietIsland-Updater/1.0");
        return request;
    }
    public void Dispose() { if (ownsClient) http.Dispose(); }
}
