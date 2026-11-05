using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class ClipboardView : IslandView
{
    private readonly TextBox filter = Ui.Input("Поиск в буфере обмена");
    private readonly StackPanel rows = new();
    public ClipboardView(IslandContext context) : base(context)
    {
        var root = new DockPanel(); var top = Ui.Stack(filter); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(Ui.Scroll(rows)); Content = root;
        filter.TextChanged += (_, _) => Build(); C.Clipboard.Changed += Changed; Build();
    }
    private void Changed() => Dispatcher.InvokeAsync(Build);
    public override void FocusInput() => filter.Focus();
    private void Build()
    {
        if (Disposed) return; rows.Children.Clear();
        var clips = C.State.Data.Clipboard.Where(x => x.Preview.Contains(filter.Text, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(x => x.Pinned).ToList();
        if (clips.Count == 0) { rows.Children.Add(Ui.Empty("▤", "История начнётся с копирования", "Текст, картинки и файлы хранятся на этом компьютере. Клик по записи возвращает её в окно, где вы печатали.")); return; }
        foreach (var entry in clips)
        {
            var content = new DockPanel();
            var buttons = Ui.Row(Ui.Button(entry.Pinned ? "◆" : "◇", () => { entry.Pinned = !entry.Pinned; C.State.Touch(); Build(); }, tip: "Закрепить"), Ui.Button("×", () => C.Clipboard.Remove(entry), tip: "Удалить запись")); DockPanel.SetDock(buttons, Dock.Right); content.Children.Add(buttons);
            var text = Ui.Text(entry.Preview.Length > 350 ? entry.Preview[..350] + "…" : entry.Preview); text.MaxHeight = 75;
            var body = Ui.Stack(text, Ui.Text(entry.Created.ToString("dd.MM · HH:mm") + " · " + entry.Kind, 10, "Muted"));
            if (entry.Kind == "image" && File.Exists(entry.ImagePath)) { try { var image = Thumbnail(entry.ImagePath); body.Children.Insert(0, new Image { Source = image, Height = 90, HorizontalAlignment = HorizontalAlignment.Left }); body.Children.Add(Ui.Button("Открыть изображение", () => SystemService.Open(entry.ImagePath!))); } catch { } }
            var copy = Ui.Button("", () => { try { C.Collapse(); C.Clipboard.Copy(entry, C.PasteTarget); } catch (Exception e) { C.Error(e); } }); copy.Content = body; copy.HorizontalContentAlignment = HorizontalAlignment.Left; copy.Background = System.Windows.Media.Brushes.Transparent;
            content.Children.Add(copy); rows.Children.Add(Ui.Card(content, 8));
        }
    }
    private static BitmapImage Thumbnail(string path)
    {
        using var stream = File.OpenRead(path);
        // Read dimensions without decoding the original capture. Decode only enough pixels for
        // a 160x90 preview at 200% scale, including tall portrait images.
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var scale = Math.Min(1, Math.Min(320d / frame.PixelWidth, 180d / frame.PixelHeight));
        stream.Position = 0;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.DecodePixelWidth = Math.Max(1, (int)Math.Floor(frame.PixelWidth * scale));
        bitmap.DecodePixelHeight = Math.Max(1, (int)Math.Floor(frame.PixelHeight * scale));
        bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    public override void Dispose() { C.Clipboard.Changed -= Changed; base.Dispose(); }
}
