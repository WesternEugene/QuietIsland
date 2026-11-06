namespace QuietIsland.Services;

public sealed record FocusPlayback(string Id, string App, bool IsPlaying, bool CanPause = true, bool CanPlay = true, bool IsAudible = false);
public sealed record FocusAudio(string Id, string App, bool IsActive);
public sealed record FocusSnapshot(IReadOnlyList<FocusPlayback> Playback, IReadOnlyList<FocusAudio> Audio, string? CurrentId = null);

/// <summary>Windows transport and audio observations used by the interruption coordinator.</summary>
public interface IMediaFocusBackend : IDisposable
{
    FocusSnapshot ReadSnapshot(bool includeAudio);
    Task<bool> SetPausedAsync(string id, bool paused);
}
