using System.Windows.Input;
using QuietIsland.Models;

namespace QuietIsland.Services;

public sealed record ShortcutDefinition(int Id, string Key, string Title, string Default);
public readonly record struct ShortcutChord(uint Modifiers, uint VirtualKey)
{
    public static bool TryParse(string? text, out ShortcutChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint modifiers = 0; Key key = Key.None;
        try
        {
            foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
            {
                switch (part.ToUpperInvariant())
                {
                    case "CTRL": case "CONTROL": modifiers |= 2; break;
                    case "ALT": modifiers |= 1; break;
                    case "SHIFT": modifiers |= 4; break;
                    case "WIN": case "WINDOWS": modifiers |= 8; break;
                    default:
                        if (key != Key.None || part.Length == 0) return false;
                        key = part.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase) ? Key.Snapshot : (Key)new KeyConverter().ConvertFromString(part)!;
                        break;
                }
            }
            if (key is Key.None or Key.F12 or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System) return false;
            if (modifiers == 0 && key != Key.Snapshot && !(key >= Key.F1 && key <= Key.F24)) return false;
            var code = KeyInterop.VirtualKeyFromKey(key); if (code == 0) return false;
            chord = new(modifiers, (uint)code); return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or FormatException) { return false; }
    }
}

public static class GlobalShortcuts
{
    public static IReadOnlyList<ShortcutDefinition> Definitions { get; } = [
        new(1, "search", "Открыть поиск", "Alt+Space"),
        new(2, "clipboard", "Буфер обмена", "Ctrl+Alt+V"),
        new(3, "notes", "Заметки", "Ctrl+Alt+N"),
        new(4, "screenshot", "Снимок области", "Ctrl+Alt+S"),
        new(5, "recording", "Запись экрана / стоп", "Ctrl+Alt+R"),
        new(6, "dictation", "Диктовка / стоп", "Ctrl+Alt+D"),
        new(7, "desktop", "Снимок всех экранов", "PrintScreen"),
        new(8, "region", "Выбор области (дополнительно)", "Ctrl+PrintScreen")
    ];
    public static Dictionary<string, string> Bindings(AppSettings settings)
    {
        if (settings.GlobalHotkeys == null)
        {
            settings.GlobalHotkeys = Definitions.ToDictionary(x => x.Key, x => x.Default);
            // Preserve the previously explicit preference, without probing for a substitute.
            if (!settings.HotkeyAltSpace) settings.GlobalHotkeys["search"] = "Ctrl+Alt+Space";
        }
        return settings.GlobalHotkeys;
    }
    public static string Label(AppSettings settings, string key)
        => Bindings(settings).TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : "не назначено";
    public static string? ValidationError(AppSettings settings, string key, string value, IEnumerable<LaunchItem> launchers)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!ShortcutChord.TryParse(value, out var chord)) return "Укажите сочетание, например Ctrl+Shift+S, Alt+Space или F8.";
        foreach (var other in Definitions.Where(x => x.Key != key))
            if (Bindings(settings).TryGetValue(other.Key, out var assigned) && ShortcutChord.TryParse(assigned, out var existing) && existing == chord)
                return "Это сочетание уже назначено: " + other.Title;
        if (launchers.Any(x => ShortcutChord.TryParse(x.Hotkey, out var existing) && existing == chord)) return "Это сочетание уже назначено закреплённому ярлыку.";
        return null;
    }
}
