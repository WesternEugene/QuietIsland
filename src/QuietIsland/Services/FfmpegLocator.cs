namespace QuietIsland.Services;

/// <summary>Resolves the encoder independently of the PATH inherited by the GUI at sign-in.</summary>
public static class FfmpegLocator
{
    public static string Resolve(string? configuredPath)
    {
        var configured = Environment.ExpandEnvironmentVariables((configuredPath ?? "ffmpeg").Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(configured)) configured = "ffmpeg";
        var isDefault = configured.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase)
            || configured.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase);
        var hasDirectory = Path.IsPathRooted(configured) || configured.Contains('/') || configured.Contains('\\');
        foreach (var candidate in Candidates(configured, isDefault, hasDirectory).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { if (File.Exists(candidate)) return Path.GetFullPath(candidate); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        throw new InvalidOperationException(hasDirectory || !isDefault
            ? $"Файл FFmpeg не найден: {configured}. Укажите существующий ffmpeg.exe в настройках записи."
            : "FFmpeg не найден. Установите FFmpeg для Windows или укажите полный путь к ffmpeg.exe в настройках записи. Проверены встроенная копия, PATH и ссылки WinGet.");
    }

    private static IEnumerable<string> Candidates(string configured, bool isDefault, bool hasDirectory)
    {
        if (hasDirectory)
        {
            if (Path.IsPathRooted(configured)) yield return configured;
            else
            {
                yield return Path.Combine(AppContext.BaseDirectory, configured);
                yield return Path.GetFullPath(configured);
            }
            yield break; // A broken explicit path must not silently select another installation.
        }

        var executable = Path.HasExtension(configured) ? configured : configured + ".exe";
        yield return Path.Combine(AppContext.BaseDirectory, executable);
        if (isDefault)
        {
            yield return Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", executable);
            yield return Path.Combine(AppContext.BaseDirectory, "tools", executable);
        }
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            var paths = Environment.GetEnvironmentVariable("PATH", target) ?? "";
            foreach (var entry in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var directory = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
                if (Path.IsPathRooted(directory)) yield return Path.Combine(directory, executable);
            }
        }
        if (!isDefault) yield break;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "ffmpeg.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FFmpeg", "bin", "ffmpeg.exe");
    }
}
