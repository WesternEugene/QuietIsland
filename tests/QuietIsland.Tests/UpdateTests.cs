using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public sealed class UpdateTests
{
    private static readonly byte[] Installer = Encoding.ASCII.GetBytes("MZ synthetic installer, never executed");
    private static readonly Version Current = new(1, 1, 2, 0);

    [Theory]
    [InlineData("1.1.3")]
    [InlineData("v1.1.3")]
    [InlineData("1.1.10")]
    public void NewStableVersionSelectsOfficialInstaller(string tag)
    {
        var release = AppRelease.Parse(ReleaseJson(tag), Current);
        Assert.NotNull(release);
        Assert.Equal(tag == "1.1.10" ? "1.1.10" : "1.1.3", release.DisplayVersion);
        Assert.True(release.CanInstall);
        Assert.Equal("QuietIsland-" + release.DisplayVersion + "-Setup-win-x64.exe", release.InstallerName);
        Assert.Equal(Installer.Length, release.Size);
    }

    [Theory]
    [InlineData("1.1.2")]
    [InlineData("v1.1.2")]
    [InlineData("1.1.1")]
    [InlineData("1.1.3-beta")]
    [InlineData("invalid")]
    public void SameOlderOrUnstableVersionsAreIgnored(string tag) => Assert.Null(AppRelease.Parse(ReleaseJson(tag), Current));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DraftAndPrereleaseAreIgnored(bool draft, bool prerelease) => Assert.Null(AppRelease.Parse(ReleaseJson("1.1.3", draft: draft, prerelease: prerelease), Current));

    [Theory]
    [InlineData("https://example.com/QuietIsland-1.1.3-Setup-win-x64.exe")]
    [InlineData("http://github.com/WesternEugene/QuietIsland/releases/download/1.1.3/QuietIsland-1.1.3-Setup-win-x64.exe")]
    [InlineData("https://github.com/Other/QuietIsland/releases/download/1.1.3/QuietIsland-1.1.3-Setup-win-x64.exe")]
    public void ForeignInstallerIsNotOfferedForExecution(string url)
    {
        var release = AppRelease.Parse(ReleaseJson("1.1.3", downloadUrl: url), Current)!;
        Assert.False(release.CanInstall);
        Assert.Equal("https://github.com/WesternEugene/QuietIsland/releases/tag/1.1.3", release.Page.AbsoluteUri);
    }

    [Fact]
    public async Task CheckUsesPublicLatestEndpointAndUserAgent()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal("https://api.github.com/repos/WesternEugene/QuietIsland/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Contains("QuietIsland", request.Headers.UserAgent.ToString());
            return new(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson("1.1.3")) };
        });
        using var http = new HttpClient(handler); using var client = new UpdateClient(http);
        Assert.Equal("1.1.3", (await client.CheckAsync(Current))!.DisplayVersion);
    }

    [Fact]
    public async Task NoPublishedReleaseIsNotAnError()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler); using var client = new UpdateClient(http);
        Assert.Null(await client.CheckAsync(Current));
    }

    [Fact]
    public async Task DownloadVerifiesContentAndReusesVerifiedCache()
    {
        var requests = 0;
        using var handler = new Handler(_ => { requests++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }; });
        using var http = new HttpClient(handler); using var client = new UpdateClient(http);
        var folder = TemporaryFolder();
        try
        {
            var release = AppRelease.Parse(ReleaseJson("1.1.3"), Current)!;
            var path = await client.DownloadAsync(release, folder);
            Assert.Equal(Installer, await File.ReadAllBytesAsync(path));
            Assert.Equal(path, await client.DownloadAsync(release, folder));
            Assert.Equal(1, requests);
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenDownloadNeverBecomesAnInstaller(bool truncated)
    {
        var content = truncated ? Installer[..8] : Installer.ToArray();
        if (!truncated) content[^1] ^= 1;
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        using var http = new HttpClient(handler); using var client = new UpdateClient(http);
        var folder = TemporaryFolder();
        try
        {
            var release = AppRelease.Parse(ReleaseJson("1.1.3"), Current)!;
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, folder));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task CancelledDownloadLeavesNoPartialFile()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) });
        using var http = new HttpClient(handler); using var client = new UpdateClient(http);
        using var cancellation = new CancellationTokenSource();
        var folder = TemporaryFolder();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(AppRelease.Parse(ReleaseJson("1.1.3"), Current)!, folder,
                new CallbackProgress(_ => cancellation.Cancel()), cancellation.Token));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    private static string TemporaryFolder() { var path = Path.Combine(Path.GetTempPath(), "quiet-island-updates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static string ReleaseJson(string tag, bool draft = false, bool prerelease = false, string? downloadUrl = null)
    {
        var name = "QuietIsland-" + tag.TrimStart('v') + "-Setup-win-x64.exe";
        return JsonSerializer.Serialize(new
        {
            tag_name = tag, draft, prerelease, html_url = "https://github.com/WesternEugene/QuietIsland/releases/tag/" + tag,
            body = "Исправления и улучшения.", assets = new[] { new { name, size = Installer.Length,
                digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Installer)).ToLowerInvariant(),
                browser_download_url = downloadUrl ?? "https://github.com/WesternEugene/QuietIsland/releases/download/" + tag + "/" + name } }
        });
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
    private sealed class CallbackProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
}
