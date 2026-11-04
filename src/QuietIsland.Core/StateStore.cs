using System.Text.Json;

namespace QuietIsland.Core;

/// <summary>Atomic save with one last-known-good backup. A broken file is preserved for recovery.</summary>
public sealed class StateStore<T>(string directory) where T : class
{
    private readonly object gate = new();
    private readonly JsonSerializerOptions options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly Dictionary<string, (long Length, DateTime Modified)> preserved = [];
    private bool readFailed;
    public T Load()
    {
        lock (gate)
        {
            readFailed = false;
            foreach (var name in new[] { "state.json", "state.backup.json" })
            {
                try
                {
                    var path = Path.Combine(directory, name);
                    if (!File.Exists(path)) continue;
                    if (JsonSerializer.Deserialize<T>(File.ReadAllText(path), options) is { } state) return state;
                    Preserve(path);
                }
                catch (JsonException)
                {
                    try { Preserve(Path.Combine(directory, name)); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { readFailed = true; }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { readFailed = true; }
            }
            return JsonSerializer.Deserialize<T>("{}", options) ?? throw new InvalidOperationException("Cannot create default state");
        }
    }
    public void Save(T value)
    {
        lock (gate)
        {
            if (readFailed) throw new IOException("State could not be read or preserved; refusing to overwrite it.");
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, "state.json");
            var temporary = Path.Combine(directory, "state." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, options));
            if (File.Exists(target))
            {
                // Do not replace a valid recovery copy with malformed data.
                try
                {
                    if (JsonSerializer.Deserialize<T>(File.ReadAllText(target), options) != null) File.Copy(target, Path.Combine(directory, "state.backup.json"), true);
                    else Preserve(target);
                }
                catch (JsonException) { Preserve(target); }
            }
            File.Move(temporary, target, true);
        }
    }
    private void Preserve(string path)
    {
        var info = new FileInfo(path);
        var identity = (info.Length, info.LastWriteTimeUtc);
        if (preserved.TryGetValue(path, out var previous) && previous == identity) return;
        var recovery = Path.Combine(directory, "state.corrupt." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "." + Guid.NewGuid().ToString("N")[..8] + ".json");
        File.Copy(path, recovery, false); preserved[path] = identity;
    }
}
