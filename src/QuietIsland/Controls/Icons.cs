using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IOPath = System.IO.Path;
using VectorPath = System.Windows.Shapes.Path;

namespace QuietIsland.Controls;

/// <summary>
/// Quiet Island's own rounded, two-tone vector family. Each drawing uses a
/// 24-unit canvas, without shell resources, emoji, or symbol fonts.
/// </summary>
public static class Icons
{
    private static readonly Color Coral = Color.FromRgb(255, 139, 129);
    private static readonly Color Amber = Color.FromRgb(249, 198, 108);
    private static readonly Color Lavender = Color.FromRgb(185, 162, 246);
    private static readonly Color Sky = Color.FromRgb(123, 191, 249);
    private static readonly Color Mint = Color.FromRgb(117, 218, 185);

    /// <summary>A theme-aware monochrome icon for compact controls.</summary>
    public static FrameworkElement Make(string name, double size = 18, string color = "Text") =>
        Draw(Normalize(name), size, color);

    /// <summary>A small illustration using the icon family's semantic colors.</summary>
    public static FrameworkElement Colored(string name, double size = 22) =>
        Draw(Normalize(name), size);

    /// <summary>A soft rounded app badge with an original vector inside.</summary>
    public static FrameworkElement Tile(string name, double size = 40)
    {
        name = Normalize(name);
        return Badge(name, Colored(name, size * .64), size);
    }

    /// <summary>Recognizable file types without Windows shell icon lookups.</summary>
    public static FrameworkElement File(string path, double size = 42)
    {
        path ??= "";
        if (IOPath.EndsInDirectorySeparator(path) || Directory.Exists(path)) return Colored("folder", size);
        var extension = IOPath.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var kind = extension switch
        {
            "png" or "jpg" or "jpeg" or "webp" or "gif" or "bmp" or "svg" or "ico" or "avif" or "heic" or "tif" or "tiff" => "image",
            "pdf" => "pdf",
            "mp3" or "wav" or "flac" or "ogg" or "m4a" or "aac" or "opus" or "wma" or "aiff" => "audio",
            "mp4" or "mkv" or "mov" or "webm" or "avi" or "wmv" or "m4v" or "mpeg" or "mpg" => "video",
            "zip" or "7z" or "rar" or "tar" or "gz" or "bz2" or "xz" or "tgz" or "zst" => "archive",
            "cs" or "fs" or "vb" or "js" or "jsx" or "ts" or "tsx" or "vue" or "html" or "htm" or "css" or "scss" or "less" or "json" or "yaml" or "yml" or "xml" or "py" or "go" or "rs" or "c" or "cpp" or "h" or "java" or "kt" or "swift" or "sql" or "sh" or "ps1" or "toml" => "code",
            "exe" or "msi" or "lnk" or "url" or "appref-ms" => "app",
            _ => "file"
        };
        if (kind == "app") return Tile(kind, size);
        if (kind != "file") return Colored(kind, size);
        var label = extension switch
        {
            "doc" or "docx" or "odt" or "rtf" => "DOC",
            "xls" or "xlsx" or "ods" or "csv" or "tsv" => "XLS",
            "ppt" or "pptx" or "odp" => "PPT",
            "txt" or "log" => "TXT",
            "md" or "mdx" => "MD",
            _ => extension.Length is > 0 and <= 4 && extension.All(char.IsAsciiLetterOrDigit) ? extension.ToUpperInvariant() : ""
        };
        var accent = label switch { "XLS" => Mint, "PPT" => Coral, "DOC" => Sky, _ => Lavender };
        return Draw("file", size, accent: accent, label: label);
    }

    /// <summary>The registered Windows app icon, with a readable fallback while it loads.</summary>
    public static FrameworkElement Launcher(string target, string name, double size = 40) =>
        ShellIcons.File(target, size, LauncherFallback(target, name, size));

    private static FrameworkElement LauncherFallback(string target, string name, double size)
    {
        var key = LauncherKind(target);
        if (key != null) return Tile(key, size);
        var initials = Initials(name);
        if (initials.Length == 0) return Tile("app", size);
        var label = new TextBlock
        {
            Text = initials,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = size * (initials.Length == 1 ? .4 : .31),
            Foreground = Solid(Accent("app")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            IsHitTestVisible = false
        };
        return Badge("app", label, size);
    }

    /// <summary>Original illustrations for the WMO codes returned by Open-Meteo.</summary>
    public static FrameworkElement Weather(int code, double size = 36)
    {
        var kind = code switch
        {
            0 => "sun",
            1 or 2 => "partly",
            45 or 48 => "fog",
            >= 51 and <= 67 or >= 80 and <= 82 => "rain",
            >= 71 and <= 77 or 85 or 86 => "snow",
            >= 95 and <= 99 => "thunder",
            _ => "cloud"
        };
        return Draw(kind, size);
    }

    public static Color Accent(string name) => Normalize(name) switch
    {
        "amber" or "home" or "shelf" or "folder" or "notes" or "weather" or "sun" or "partly" or "archive" or "lock" => Amber,
        "lavender" or "ai" or "music" or "audio" or "mic" or "app" or "moon" or "video" => Lavender,
        "mint" or "clipboard" or "paste" or "image" or "check" or "power" or "activity" or "play" => Mint,
        "coral" or "shot" or "record" or "color" or "pdf" or "pin" or "close" or "trash" or "stop" or "thunder" => Coral,
        _ => Sky
    };

    /// <summary>A translucent accent background for light and dark cards.</summary>
    public static Brush Soft(string name)
    {
        var color = Accent(name);
        return Solid(Color.FromArgb(37, color.R, color.G, color.B));
    }

    public static Button Button(string icon, string tip, Action action, double size = 32)
    {
        var button = Ui.Button("", action, tip: tip);
        button.Content = Make(icon);
        button.Width = button.Height = size;
        button.Padding = new Thickness(7);
        button.Margin = new Thickness(0, 0, 3, 0);
        button.Background = Brushes.Transparent;
        AutomationProperties.SetName(button, tip);
        return button;
    }

    private static FrameworkElement Badge(string name, UIElement content, double size) => new Border
    {
        Width = size, Height = size,
        CornerRadius = new CornerRadius(size * .29),
        Background = Soft(name),
        Child = content,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static FrameworkElement Draw(string name, double size, string? resource = null, Color? accent = null, string? label = null)
    {
        var g = new Glyph(accent ?? Accent(name), resource);
        switch (name)
        {
            case "unpin":
                g.Path("M8,3 H16 M9,3 V9 L6,13 V15 H18 V13 L15,9 V3 M12,15 V21", width: 1.8);
                g.Path("M3,3 L21,21", width: 2.2);
                break;
            case "repeat":
            case "repeat-one":
                g.Path("M19.5,8 A8,8 0 0 0 5,6.5 L3.5,9 M3.5,4.5 V9 H8", width: 1.9);
                g.Path("M4.5,16 A8,8 0 0 0 19,17.5 L20.5,15 M16,15 H20.5 V19.5", width: 1.9);
                if (name == "repeat-one") g.Path("M10.3,10.1 L12,8.8 V15 M10.4,15 H13.6", width: 1.6);
                break;
            case "shuffle":
                g.Path("M3,6 H5.5 C10,6 12,18 17,18 H21 M17.5,14.5 L21,18 L17.5,21", width: 1.9);
                g.Path("M3,18 H5.5 C7.4,18 8.7,15.9 10,13.5 M14,7.7 C15,6.6 15.7,6 17,6 H21 M17.5,2.5 L21,6 L17.5,9.5", width: 1.9);
                break;
            case "home":
                g.Path("M4.5,10.5 V19 A2,2 0 0 0 6.5,21 H17.5 A2,2 0 0 0 19.5,19 V10.5", Tone.Soft);
                g.Path("M2.8,10.4 L10.5,3.7 Q12,2.4 13.5,3.7 L21.2,10.4", width: 2.1);
                g.Rect(9.2, 13, 5.6, 8, 1.5, Tone.Main, null);
                g.Rect(10.4, 7.6, 3.2, 3.2, 1, Tone.Light, null);
                break;
            case "search":
                g.Ellipse(3.1, 3.1, 13.8, 13.8, Tone.Soft);
                g.Path("M14.8,14.8 L20.6,20.6", width: 3.3);
                g.Path("M6.3,9 A4,4 0 0 1 9,6.2", stroke: Tone.Light, width: 1.8);
                break;
            case "clipboard":
            case "paste":
                g.Rect(5, 5, 14, 16, 2.5, Tone.Soft);
                g.Rect(8.3, 2.7, 7.4, 4.6, 1.8, Tone.Main, null);
                if (name == "paste") g.Path("M9,14 H15 M12,11 V17", stroke: Tone.Light, width: 1.9);
                else g.Path("M8.8,12 H15.2 M8.8,16 H13.7", stroke: Tone.Light);
                break;
            case "shelf":
                g.Rect(3.2, 4.1, 17.6, 16.6, 3, Tone.Soft);
                g.Path("M3.6,12.6 H7.5 L9.4,15.7 H14.6 L16.5,12.6 H20.4", width: 1.8);
                g.Path("M8,8 H16", stroke: Tone.Light);
                break;
            case "tools":
                g.Path("M8,7.8 V5.3 Q8,3.6 9.7,3.6 H14.3 Q16,3.6 16,5.3 V7.8", width: 1.8);
                g.Rect(3.1, 7.7, 17.8, 12.6, 3, Tone.Soft);
                g.Path("M3.5,12.3 H20.5", width: 1.4);
                g.Rect(10.2, 10.6, 3.6, 3.8, 1, Tone.Light, null);
                g.Path("M8,17.2 H16", stroke: Tone.Light, width: 1.4);
                break;
            case "control":
                g.Path("M5.2,4 V20 M12,4 V20 M18.8,4 V20", width: 1.6);
                g.Rect(2.7, 7.2, 5, 4.2, 1.5, Tone.Light);
                g.Rect(9.5, 13.1, 5, 4.2, 1.5, Tone.Light);
                g.Rect(16.3, 5.3, 5, 4.2, 1.5, Tone.Light);
                break;
            case "ai":
                g.Path("M6,4.6 H18 A3,3 0 0 1 21,7.6 V15.4 A3,3 0 0 1 18,18.4 H10 L5.6,21 V18.3 A3,3 0 0 1 3,15.4 V7.6 A3,3 0 0 1 6,4.6 Z", Tone.Soft);
                g.Path("M12.2,7.8 L13.3,10.9 L16.4,12 L13.3,13.1 L12.2,16.2 L11.1,13.1 L8,12 L11.1,10.9 Z", Tone.Light, null);
                g.Ellipse(6.4, 7.4, 1.7, 1.7, Tone.Main, null);
                break;
            case "notes":
                g.Rect(4.6, 3, 15, 18, 2.7, Tone.Body);
                g.Path("M8,3 V21", stroke: Tone.Detail, width: 1.5);
                g.Path("M11.2,8.1 H16.1 M11.2,11.8 H16.1 M11.2,15.5 H14.6", stroke: Tone.Detail);
                g.Rect(2.8, 7, 3.4, 1.8, .8, Tone.Light, null);
                g.Rect(2.8, 14.4, 3.4, 1.8, .8, Tone.Light, null);
                break;
            case "activity":
                g.Rect(2.6, 4.2, 18.8, 15.6, 4, Tone.Soft);
                g.Path("M5.2,12 H8 L10.2,7.8 L13.2,16.2 L15.6,11.3 H18.8", width: 1.9);
                break;
            case "pin":
                g.Path("M8.1,3.5 H15.9 L15.1,9.8 L18.4,13.2 Q19.1,14.1 17.7,14.2 H6.3 Q4.9,14.1 5.6,13.2 L8.9,9.8 Z", Tone.Soft);
                g.Path("M8,3.5 H16 M12,14.6 V21", width: 1.9);
                break;
            case "close":
                g.Path("M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5", width: 2.1);
                break;
            case "shot":
                g.Rect(6.7, 7.2, 10.6, 9.6, 2.1, Tone.Soft, null);
                g.Path("M8,3 H4.5 A1.5,1.5 0 0 0 3,4.5 V8 M16,3 H19.5 A1.5,1.5 0 0 1 21,4.5 V8 M21,16 V19.5 A1.5,1.5 0 0 1 19.5,21 H16 M8,21 H4.5 A1.5,1.5 0 0 1 3,19.5 V16", width: 1.9);
                g.Ellipse(9.4, 9.4, 5.2, 5.2, Tone.Main, null);
                break;
            case "screen":
                g.Rect(2.7, 4, 18.6, 13.2, 2.6, Tone.Soft);
                g.Path("M12,17.6 V21 M7.8,21 H16.2", width: 1.8);
                g.Path("M5.7,13.8 H18.3", stroke: Tone.Light, width: 1.2);
                break;
            case "record":
                g.Rect(2.8, 5, 18.4, 14, 3.2, Tone.Soft);
                g.Ellipse(7.7, 7.7, 8.6, 8.6, Tone.Main, null);
                g.Ellipse(10.2, 10.2, 3.6, 3.6, Tone.Light, null);
                break;
            case "ocr":
                g.Path("M7,3.5 H4.5 A1,1 0 0 0 3.5,4.5 V8 M17,3.5 H19.5 A1,1 0 0 1 20.5,4.5 V8 M20.5,16 V19.5 A1,1 0 0 1 19.5,20.5 H17 M7,20.5 H4.5 A1,1 0 0 1 3.5,19.5 V16", width: 1.8);
                g.Rect(6.3, 6.8, 11.4, 10.4, 1.5, Tone.Soft, null);
                g.Path("M8,8 H16 M12,8 V16 M9.6,16 H14.4", stroke: Tone.Light, width: 1.8);
                break;
            case "qr":
                g.Rect(3, 3, 7, 7, 1.5, Tone.Soft);
                g.Rect(14, 3, 7, 7, 1.5, Tone.Soft);
                g.Rect(3, 14, 7, 7, 1.5, Tone.Soft);
                g.Rect(5.3, 5.3, 2.4, 2.4, .5, Tone.Main, null);
                g.Rect(16.3, 5.3, 2.4, 2.4, .5, Tone.Main, null);
                g.Rect(5.3, 16.3, 2.4, 2.4, .5, Tone.Main, null);
                g.Path("M14.5,14.5 H18 V18 H21 M14.5,18 V21 H17.5 M21,14.5 V15", width: 2);
                break;
            case "color":
                g.Path("M15,3.4 Q16.7,1.7 18.4,3.4 L20.6,5.6 Q22.3,7.3 20.6,9 L10.5,19.1 L4,20 L4.9,13.5 Z", Tone.Soft);
                g.Path("M12.8,5.8 L18.2,11.2", width: 2.3);
                g.Path("M6.6,14.1 L10,17.5 L6.1,18 Z", Tone.Main, null);
                g.Ellipse(2.9, 20.2, 2.1, 2.1, Tone.Light, null);
                break;
            case "command":
            case "terminal":
                g.Rect(2.8, 4.3, 18.4, 15.4, 3, Tone.Soft);
                g.Ellipse(5, 6.4, 1.2, 1.2, Tone.Main, null);
                g.Ellipse(7.5, 6.4, 1.2, 1.2, Tone.Light, null);
                g.Path("M6.5,10.1 L9.5,12.8 L6.5,15.5 M12.7,15.6 H17.3", stroke: Tone.Light, width: 1.9);
                break;
            case "mic":
            case "microphone-off":
                g.Rect(7.4, 2.1, 9.2, 13.8, 4.6, Tone.Soft);
                g.Path("M9.8,6 H14.2 M9.8,9 H14.2 M9.8,12 H14.2", stroke: Tone.Light, width: 1.3);
                g.Path("M4.6,10.7 V12 A7.4,7.4 0 0 0 19.4,12 V10.7 M12,19.4 V22 M8.8,22 H15.2", width: 1.8);
                if (name == "microphone-off") g.Path("M3,3 L21,21", width: 2.2);
                break;
            case "volume":
                g.Path("M3,9 H6.8 L11.5,5 V19 L6.8,15 H3 Q2.5,15 2.5,14.5 V9.5 Q2.5,9 3,9 Z", Tone.Soft);
                g.Path("M15.3,8.4 Q18.5,12 15.3,15.6 M18.5,5.5 Q24,12 18.5,18.5", width: 1.8);
                break;
            case "music":
            case "audio":
                g.Path("M9.3,16.7 V5.4 Q9.3,4.5 10.2,4.3 L19.2,2.8 Q20.4,2.6 20.4,3.8 V14.7", width: 2);
                g.Path("M9.3,7.9 L20.4,6", width: 2.7);
                g.Ellipse(3.2, 15, 6.1, 4.9, Tone.Main, null);
                g.Ellipse(14.3, 13, 6.1, 4.9, Tone.Light, null);
                break;
            case "play":
                g.Path("M8.5,4.7 Q6.5,3.6 6.5,6 V18 Q6.5,20.4 8.5,19.3 L19,13.3 Q21,12 19,10.7 Z", Tone.Main, null);
                break;
            case "pause":
                g.Rect(5.3, 4.5, 4.6, 15, 1.7, Tone.Main, null);
                g.Rect(14.1, 4.5, 4.6, 15, 1.7, Tone.Main, null);
                break;
            case "previous":
            case "next":
                g.Rect(18.5, 5.3, 2.6, 13.4, 1.2, Tone.Main, null);
                g.Path("M5.8,5.3 Q4.2,4.3 4.2,6.3 V17.7 Q4.2,19.7 5.8,18.7 L15,13.2 Q16.8,12 15,10.8 Z", Tone.Main, null);
                if (name == "previous") g.Canvas.RenderTransform = new MatrixTransform(-1, 0, 0, 1, 24, 0);
                break;
            case "stop":
                g.Rect(5, 5, 14, 14, 3.2, Tone.Main, null);
                break;
            case "folder":
                g.Path("M3,7 V5.9 Q3,4 4.9,4 H9.1 Q10.2,4 11,5 L12.5,7 H19.4 Q21,7 21,8.8 V17.8 Q21,20 18.8,20 H5.2 Q3,20 3,17.8 Z", Tone.Soft);
                g.Path("M3.7,9 H20.3 Q22,9 21.7,10.8 L20.7,18.1 Q20.4,20 18.5,20 H5.5 Q3.6,20 3.3,18.1 L2.3,10.8 Q2,9 3.7,9 Z", Tone.Body);
                g.Path("M6,12 H13.5", stroke: Tone.Detail, width: 1.5);
                break;
            case "file":
            case "pdf":
                Document(g);
                if (name == "pdf") g.Label("PDF", 5.2, 13, 13.6, 6.4);
                else if (!string.IsNullOrEmpty(label)) g.Label(label, 5.2, 13, 13.6, 6.4);
                else g.Path("M8,12 H15.5 M8,15.4 H15.5 M8,18.8 H12.5", width: 1.5);
                break;
            case "image":
                g.Rect(2.7, 3.8, 18.6, 16.4, 3, Tone.Soft);
                g.Ellipse(6.3, 7, 4.2, 4.2, Tone.Light, null);
                g.Path("M4.5,17.7 L10.4,11.9 Q11.2,11.1 12,11.9 L15.3,15.4 L17.3,13.5 Q18.1,12.7 18.9,13.6 L20,15 V17.4 Q20,19 18.5,19 H5.5 Q4.5,19 4.5,17.7 Z", Tone.Main, null);
                break;
            case "code":
                g.Rect(2.4, 4.3, 19.2, 15.4, 3.2, Tone.Soft);
                g.Path("M7.4,8.6 L4.3,12 L7.4,15.4 M16.6,8.6 L19.7,12 L16.6,15.4", stroke: Tone.Light, width: 1.9);
                g.Path("M13.8,8 L10.2,16", width: 1.9);
                break;
            case "archive":
                g.Rect(4.3, 3.1, 15.4, 17.8, 2.6, Tone.Soft);
                g.Path("M9.5,3.3 V7.3 M12.5,7.3 V10.7 M9.5,10.7 V14.1 M12.5,14.1 V16.3", width: 2.6);
                g.Rect(9.1, 16.2, 5.1, 3.2, 1.1, Tone.Light);
                break;
            case "video":
                g.Rect(2.6, 4.3, 18.8, 15.4, 3.1, Tone.Soft);
                g.Path("M5.6,4.7 V19.3 M18.4,4.7 V19.3", width: 1.2);
                g.Path("M2.9,9 H5.4 M2.9,15 H5.4 M18.6,9 H21.1 M18.6,15 H21.1", width: 1.2);
                g.Path("M10,8.5 Q9.2,8 9.2,9 V15 Q9.2,16 10,15.5 L15,12.6 Q16,12 15,11.4 Z", Tone.Light, null);
                break;
            case "app":
                g.Rect(3.2, 3.2, 7.4, 7.4, 2.4, Tone.Body);
                g.Rect(13.4, 3.2, 7.4, 7.4, 2.4, Tone.Light, null);
                g.Rect(3.2, 13.4, 7.4, 7.4, 2.4, Tone.Light, null);
                g.Rect(13.4, 13.4, 7.4, 7.4, 2.4, Tone.Soft);
                break;
            case "browser":
                g.Ellipse(2.7, 2.7, 18.6, 18.6, Tone.Soft);
                g.Path("M3.5,8.6 H20.5 M3.5,15.4 H20.5 M12,2.9 C6.3,7.3 6.3,16.7 12,21.1 C17.7,16.7 17.7,7.3 12,2.9 Z", width: 1.3);
                g.Path("M12,9.4 L18.2,16.7 L14.8,16.1 L13,18.8 Z", Tone.Light, null);
                break;
            case "calculator":
                g.Rect(5, 2.7, 14, 18.6, 3.1, Tone.Soft);
                g.Rect(7.6, 5.3, 8.8, 4.2, 1.1, Tone.Light, null);
                foreach (var x in new[] { 8d, 12d, 16d })
                    foreach (var y in new[] { 12.5, 17d }) g.Ellipse(x - 1, y - 1, 2, 2, Tone.Main, null);
                break;
            case "settings":
                g.Path("M9.4,3.3 L10.1,1.9 H13.9 L14.6,3.3 L15,5.1 L16.8,6.1 L18.6,5.7 L20.5,9 L19.3,10.4 V12.5 L20.5,14 L18.6,17.3 L16.8,16.9 L15,17.9 L14.6,19.7 L13.9,21.1 H10.1 L9.4,19.7 L9,17.9 L7.2,16.9 L5.4,17.3 L3.5,14 L4.7,12.5 V10.4 L3.5,9 L5.4,5.7 L7.2,6.1 L9,5.1 Z", Tone.Soft);
                g.Ellipse(8.2, 7.7, 7.6, 7.6, Tone.Main, null);
                g.Ellipse(10, 9.5, 4, 4, Tone.Detail, null);
                break;
            case "plus":
                g.Path("M12,5 V19 M5,12 H19", width: 2.1);
                break;
            case "chevron-down":
                g.Path("M5.5,8.8 L12,15.3 L18.5,8.8", width: 2);
                break;
            case "chevron-right":
                g.Path("M8.8,5.5 L15.3,12 L8.8,18.5", width: 2);
                break;
            case "check":
                g.Path("M4.8,12.4 L9.5,17 L19.2,7", width: 2.4);
                break;
            case "copy":
                g.Rect(3.6, 3.4, 12.6, 13.8, 2.5, Tone.Soft);
                g.Rect(8, 7.2, 12.6, 13.4, 2.5, Tone.Body);
                g.Path("M11.4,11.3 H17.1 M11.4,15 H15.7", stroke: Tone.Detail, width: 1.5);
                break;
            case "cut":
                g.Ellipse(2.5, 14.3, 6.6, 6.6, Tone.Soft);
                g.Ellipse(14.9, 14.3, 6.6, 6.6, Tone.Soft);
                g.Path("M7.8,15.2 L18.7,3.5 M16.2,15.2 L5.3,3.5", width: 1.8);
                g.Ellipse(10.7, 10, 2.6, 2.6, Tone.Light, null);
                break;
            case "undo":
                g.Path("M7.2,5 L3.2,9 L7.2,13 M3.8,9 H14.1 A5.3,5.3 0 0 1 14.1,19.6 H10", width: 1.9);
                break;
            case "redo":
                g.Path("M16.8,5 L20.8,9 L16.8,13 M20.2,9 H9.9 A5.3,5.3 0 0 0 9.9,19.6 H14", width: 1.9);
                break;
            case "trash":
                g.Path("M5.8,7.3 L6.7,19 Q6.8,21 8.8,21 H15.2 Q17.2,21 17.3,19 L18.2,7.3", Tone.Soft);
                g.Path("M4.1,6.7 H19.9 M9,6.5 V4.4 Q9,3 10.4,3 H13.6 Q15,3 15,4.4 V6.5 M9.8,10.5 V16.9 M14.2,10.5 V16.9", width: 1.7);
                break;
            case "clock":
                g.Ellipse(2.8, 2.8, 18.4, 18.4, Tone.Soft);
                g.Path("M12,6.6 V12 L16,14.3", stroke: Tone.Light, width: 1.9);
                g.Ellipse(10.6, 10.6, 2.8, 2.8, Tone.Main, null);
                break;
            case "wifi":
                g.Path("M2.7,8.8 Q12,1.1 21.3,8.8 M6.1,12.4 Q12,7.4 17.9,12.4 M9.2,15.8 Q12,13.4 14.8,15.8", width: 2.1);
                g.Ellipse(10.5, 18, 3, 3, Tone.Main, null);
                break;
            case "bluetooth":
                g.Path("M12,2.8 L18.5,8.1 L5.8,17.3 M12,2.8 V21.2 L18.5,15.9 L5.8,6.7", width: 1.9);
                break;
            case "power":
                g.Path("M7.3,5.7 A8,8 0 1 0 16.7,5.7", width: 2.1);
                g.Path("M12,2.7 V11", stroke: Tone.Light, width: 2.4);
                break;
            case "restart":
                g.Path("M20.2,10 A8.3,8.3 0 1 0 19,17 M20.5,3.5 V10 H14", width: 2);
                break;
            case "theme":
                g.Ellipse(5.5, 5.5, 13, 13, Tone.Soft);
                g.Path("M12,5.5 A6.5,6.5 0 0 1 12,18.5 Z", Tone.Main, null);
                g.Path("M12,1.8 V3 M12,21 V22.2 M1.8,12 H3 M21,12 H22.2 M4.8,4.8 L5.7,5.7 M18.3,18.3 L19.2,19.2 M4.8,19.2 L5.7,18.3 M18.3,5.7 L19.2,4.8", width: 1.5);
                break;
            case "notifications":
                g.Path("M5.4,10 A6.6,6.6 0 0 1 18.6,10 V14.1 L21,18.2 H3 L5.4,14.1 Z", Tone.Soft);
                g.Path("M9,21 A3.3,3.3 0 0 0 15,21 M12,2.1 V3.4", width: 1.8);
                break;
            case "lock":
                g.Path("M7.6,10 V7.2 A4.4,4.4 0 0 1 16.4,7.2 V10", width: 1.9);
                g.Rect(4.7, 9.2, 14.6, 12, 3, Tone.Body);
                g.Ellipse(10.6, 12.5, 2.8, 2.8, Tone.Detail, null);
                g.Path("M12,14.9 V17.6", stroke: Tone.Detail, width: 1.7);
                break;
            case "moon":
                g.Path("M18.7,15.6 A8.6,8.6 0 0 1 8.4,5.3 A8.5,8.5 0 1 0 18.7,15.6 Z", Tone.Main, null);
                g.Path("M17.6,3.1 V7.5 M15.4,5.3 H19.8", stroke: Tone.Light, width: 1.4);
                g.Ellipse(20.2, 9.1, 1.6, 1.6, Tone.Light, null);
                break;
            case "sun":
            case "partly":
            case "weather":
            case "cloud":
            case "fog":
            case "rain":
            case "snow":
            case "thunder":
                DrawWeather(g, name);
                break;
            default:
                g.Rect(3.5, 3.5, 17, 17, 5, Tone.Soft);
                g.Path("M8,12 H16 M12,8 V16", stroke: Tone.Light, width: 2);
                break;
        }
        return new Viewbox
        {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            Child = g.Canvas, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static void Document(Glyph g)
    {
        g.Path("M6.7,2.8 H13.8 L19.4,8.4 V19.2 Q19.4,21.2 17.4,21.2 H6.7 Q4.7,21.2 4.7,19.2 V4.8 Q4.7,2.8 6.7,2.8 Z", Tone.Paper, Tone.Main, 1.3);
        g.Path("M13.6,3.1 V6.9 Q13.6,8.6 15.3,8.6 H19", Tone.Soft, Tone.Main, 1.3);
    }

    private static void DrawWeather(Glyph g, string kind)
    {
        if (kind == "sun")
        {
            g.Ellipse(7, 7, 10, 10, Tone.Amber, null);
            g.Path("M12,2.3 V4.4 M12,19.6 V21.7 M2.3,12 H4.4 M19.6,12 H21.7 M5.1,5.1 L6.6,6.6 M17.4,17.4 L18.9,18.9 M18.9,5.1 L17.4,6.6 M6.6,17.4 L5.1,18.9", stroke: Tone.Amber, width: 1.8);
            return;
        }
        if (kind is "partly" or "weather")
        {
            g.Ellipse(4.8, 4.4, 9.6, 9.6, Tone.Amber, null);
            g.Path("M9.6,1.8 V3 M2.2,9.2 H3.4 M3.8,3.5 L4.7,4.4 M15.4,3.5 L14.5,4.4", stroke: Tone.Amber, width: 1.7);
        }
        var rain = kind is "rain" or "snow" or "thunder";
        var cloud = rain
            ? "M6.2,15.1 A3.7,3.7 0 0 1 5.2,7.8 A5.4,5.4 0 0 1 15.4,6.3 A4.4,4.4 0 1 1 18.4,15.1 Z"
            : "M6.1,19 A4.2,4.2 0 0 1 5.1,10.7 A5.8,5.8 0 0 1 16.1,9.2 A4.9,4.9 0 1 1 18.5,19 Z";
        g.Path(cloud, Tone.Cloud, null);
        g.Path(rain ? "M6.5,12.4 H17.5" : "M6.8,16.1 H17.7", stroke: Tone.CloudShade, width: 1.2);
        switch (kind)
        {
            case "rain":
                g.Path("M7.1,17.7 L5.9,20.8 M12.6,17.7 L11.4,20.8 M18.1,17.7 L16.9,20.8", stroke: Tone.Sky, width: 1.9);
                break;
            case "snow":
                foreach (var x in new[] { 7d, 12d, 17d })
                    g.Path(FormattableString.Invariant($"M{x},17.5 V21.3 M{x - 1.65},18.45 L{x + 1.65},20.35 M{x - 1.65},20.35 L{x + 1.65},18.45"), stroke: Tone.Sky, width: 1.1);
                break;
            case "thunder":
                g.Path("M12,13.3 L8.3,18.6 H11.9 L10.6,22 L16.3,16.5 H12.7 L14.1,13.3 Z", Tone.Amber, null);
                break;
            case "fog":
                g.Path("M4.5,18.4 H19.5 M7.2,21.2 H17", stroke: Tone.CloudShade, width: 1.7);
                break;
        }
    }

    private static string Normalize(string name) => name?.Trim().ToLowerInvariant() switch
    {
        "note" or "notepad" => "notes",
        "calc" => "calculator",
        "explorer" or "files" => "folder",
        "capture" or "screenshot" => "shot",
        "photo" or "picture" => "image",
        "prev" or "back" => "previous",
        "add" => "plus",
        "delete" => "trash",
        "microphone" => "mic",
        "mic-off" => "microphone-off",
        "sleep" => "moon",
        "sound" => "volume",
        var value => value ?? "app"
    };

    private static string? LauncherKind(string target)
    {
        target = (target ?? "").Trim().Trim('"');
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "browser";
        if (target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)) return "settings";
        if (Directory.Exists(target)) return "folder";
        var file = IOPath.GetFileNameWithoutExtension(target).ToLowerInvariant();
        return file switch
        {
            "explorer" or "filemanager" => "folder",
            "notepad" or "notepad++" or "wordpad" => "notes",
            "calc" or "calculator" => "calculator",
            "systemsettings" or "control" or "settings" => "settings",
            "wt" or "cmd" or "powershell" or "pwsh" or "windowsterminal" or "terminal" => "terminal",
            "chrome" or "msedge" or "firefox" or "opera" or "brave" or "vivaldi" or "browser" => "browser",
            "code" or "codium" or "devenv" or "rider64" or "pycharm64" => "code",
            "spotify" or "wmplayer" or "music" => "music",
            "vlc" or "mpv" => "video",
            "photos" or "mspaint" or "paintdotnet" => "image",
            _ => null
        };
    }

    private static string Initials(string name)
    {
        var parts = (name ?? "").Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var letters = parts.Where(p => p.Length > 0 && char.IsLetterOrDigit(p[0])).Take(2).Select(p => p[..1]);
        return string.Concat(letters).ToUpperInvariant();
    }

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }

    private enum Tone { Main, Body, Soft, Light, Detail, Paper, Amber, Sky, Cloud, CloudShade }

    private sealed class Glyph(Color accent, string? resource)
    {
        public Canvas Canvas { get; } = new() { Width = 24, Height = 24, IsHitTestVisible = false };

        public void Path(string data, Tone? fill = null, Tone? stroke = Tone.Main, double width = 1.65) =>
            Add(Geometry.Parse(data), fill, stroke, width);

        public void Rect(double x, double y, double width, double height, double radius, Tone? fill, Tone? stroke = Tone.Main) =>
            Add(new RectangleGeometry(new Rect(x, y, width, height), radius, radius), fill, stroke, 1.55);

        public void Ellipse(double x, double y, double width, double height, Tone? fill, Tone? stroke = Tone.Main) =>
            Add(new EllipseGeometry(new Rect(x, y, width, height)), fill, stroke, 1.65);

        public void Label(string text, double x, double y, double width, double height)
        {
            Rect(x, y, width, height, 1.4, resource == null ? Tone.Main : Tone.Soft, null);
            var label = new TextBlock
            {
                Text = text, Width = width, Height = height,
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = text.Length > 3 ? 4.7 : 5.4,
                FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = Brush(Tone.Detail),
                IsHitTestVisible = false
            };
            if (resource != null) label.SetResourceReference(TextBlock.ForegroundProperty, resource);
            System.Windows.Controls.Canvas.SetLeft(label, x);
            System.Windows.Controls.Canvas.SetTop(label, y - .15);
            Canvas.Children.Add(label);
        }

        private void Add(Geometry geometry, Tone? fill, Tone? stroke, double width)
        {
            geometry.Freeze();
            if (resource == null)
            {
                var shape = Shape(geometry, width);
                if (fill.HasValue) shape.Fill = Brush(fill.Value);
                if (stroke.HasValue) shape.Stroke = Brush(stroke.Value);
                Canvas.Children.Add(shape);
            }
            else
            {
                // Separate vector layers keep the fill translucent and the outline crisp.
                // Both layers use dynamic resources, so existing theme switching still works.
                if (fill.HasValue)
                {
                    var shape = Shape(geometry, width);
                    shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, resource);
                    shape.Opacity = Alpha(fill.Value);
                    Canvas.Children.Add(shape);
                }
                if (stroke.HasValue)
                {
                    var shape = Shape(geometry, width);
                    shape.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, resource);
                    shape.Opacity = Alpha(stroke.Value);
                    Canvas.Children.Add(shape);
                }
            }
        }

        private static VectorPath Shape(Geometry data, double width) => new()
        {
            Data = data, StrokeThickness = width,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };

        private static double Alpha(Tone tone) => tone switch
        {
            Tone.Body or Tone.Soft or Tone.Paper => .18,
            Tone.CloudShade => .48,
            _ => 1
        };

        private Brush Brush(Tone tone) => Solid(tone switch
        {
            Tone.Soft => Color.FromArgb(65, accent.R, accent.G, accent.B),
            Tone.Light => Mix(accent, Colors.White, .59),
            Tone.Detail => Mix(accent, Color.FromRgb(21, 28, 38), .78),
            Tone.Paper => Color.FromRgb(226, 234, 246),
            Tone.Amber => Amber,
            Tone.Sky => Sky,
            Tone.Cloud => Color.FromRgb(221, 233, 249),
            Tone.CloudShade => Color.FromRgb(143, 176, 211),
            _ => accent
        });

        private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * amount),
            (byte)(a.G + (b.G - a.G) * amount),
            (byte)(a.B + (b.B - a.B) * amount));
    }
}
