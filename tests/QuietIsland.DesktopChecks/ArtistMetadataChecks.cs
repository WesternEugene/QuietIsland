using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using QuietIsland.Core;
using QuietIsland.Models;
using QuietIsland.Services;

internal static class ArtistMetadataChecks
{
    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("media_artists_metadata_survives_polling_and_discards_previous_tracks", async () =>
        {
            using var handler = new CatalogHandler(); using var client = new HttpClient(handler);
            using var resolver = new MusicArtistResolver(client); var clock = new MediaClock(); using var media = new MediaService(resolver, clock);
            var publish = typeof(MediaService).GetMethod("PublishTrack", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            media.Changed += () => { if (media.Current.Artist == "Second, Guest") completed.TrySetResult(); };
            publish.Invoke(media, [new MediaSnapshot("Old song", "First", "", false, "Spotify.exe", 0, 132)]);
            await handler.OldStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var next = new MediaSnapshot("New song", "Second", "", true, "Spotify.exe", 12, 132);
            publish.Invoke(media, [next]); clock.Advance(TimeSpan.FromSeconds(5)); handler.ReleaseOld.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(6));
            if (media.Current.Title != "New song" || media.Current.Artist != "Second, Guest" || media.Current.NativeArtist != "Second") throw new InvalidOperationException("A previous track replaced the artist list or the native search artist was lost.");
            if (Math.Abs(media.Current.Position - 17) > .001) throw new InvalidOperationException("Artist metadata reset the playback clock to an old position.");
            publish.Invoke(media, [next with { Position = 15 }]);
            if (media.Current.Artist != "Second, Guest" || media.Current.Position != 15) throw new InvalidOperationException("Polling replaced the full artist list with the primary artist.");
            publish.Invoke(media, [MediaSnapshot.Empty]);
            if (media.Current.Artist.Length > 0) throw new InvalidOperationException("The previous artists remain after the media session disappears.");
        });
    }
    private sealed class MediaClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(TimeSpan elapsed) => ticks += elapsed.Ticks;
    }
    private sealed class CatalogHandler : HttpMessageHandler
    {
        public TaskCompletionSource OldStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Query.Contains("Old%20song"))
            {
                OldStarted.TrySetResult(); await ReleaseOld.Task;
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}") };
            }
            var search = request.RequestUri.Host == "itunes.apple.com";
            var body = search
                ? """{"results":[{"kind":"song","trackName":"New song","artistName":"Second & Guest","trackTimeMillis":132000,"trackId":42}]}"""
                : """<script type="application/ld+json">{"@type":"MusicRecording","name":"New song","duration":"PT2M12S","byArtist":[{"name":"Second"},{"name":"Guest"}]}</script>""";
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, search ? "application/json" : "text/html") };
        }
    }
}
