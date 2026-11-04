using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;

public sealed class HomeView : IslandView
{
    private readonly TextBlock title = Ui.Text("Музыка", 16, bold: true), artist = Ui.Text("Нет активного плеера", 12, "Muted");
    private readonly TextBlock elapsed = Ui.Text("0:00", 10, "Muted"), duration = Ui.Text("0:00", 10, "Muted"), volumeText = Ui.Text("", 10, "Muted");
    private readonly Image cover = new() { Width = 82, Height = 82, Stretch = Stretch.UniformToFill };
    private readonly Slider seek = new() { Minimum = 0, Margin = new Thickness(0, 10, 0, 0), Height = 16 };
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 9, 0) };
    private readonly Button play, record;
    private readonly Border musicCard;
    private readonly FrameworkElement coverPlaceholder;
    private System.Windows.Media.Imaging.BitmapSource? coloredCover;
    private bool paletteReady;
    private readonly StackPanel saved = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer launcherScroll;
    private readonly StackPanel launcher = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private string launcherKey = "";
    private readonly ComboBox outputs = new() { ItemTemplate = Ui.OptionLabel("Name"), SelectedValuePath = "Id", FontSize = 11, MinHeight = 28, Margin = new Thickness(10, 0, 0, 0), Width = 140, ToolTip = "Устройство вывода звука" };
    private readonly Func<IReadOnlyList<(string Id, string Name)>> readOutputs;
    private bool updatingOutputs;
    private bool shownRecordingIcon;
    internal int OutputChoiceCount => outputs.Items.Count;
    internal string? OutputSelection => outputs.SelectedValue as string;
    private string? shownOutput;
    private string? dismissedOutput;
    private bool updating;
    public HomeView(IslandContext c, MainWindow window, Func<IReadOnlyList<(string Id, string Name)>>? outputReader = null) : base(c)
    {
        readOutputs = outputReader ?? (() => C.System.AudioDevices(DataFlow.Render));
        var root = new Grid(); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = new GridLength(52) });
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new GridLength(1.8, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(12) }); columns.ColumnDefinitions.Add(new()); root.Children.Add(columns);
        title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        artist.TextWrapping = TextWrapping.NoWrap; artist.TextTrimming = TextTrimming.CharacterEllipsis; artist.Margin = new Thickness(0, 6, 0, 0);
        var art = new Grid { Width = 82, Height = 82, Clip = new RectangleGeometry(new Rect(0, 0, 82, 82), 12, 12) }; coverPlaceholder = Icons.Tile("music", 82); art.Children.Add(coverPlaceholder); art.Children.Add(cover);
        var info = new Grid(); info.ColumnDefinitions.Add(new() { Width = new GridLength(98) }); info.ColumnDefinitions.Add(new()); info.Children.Add(art);
        var names = Ui.Stack(title, artist); names.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(names, 1); info.Children.Add(names);
        var clocks = new Grid(); clocks.ColumnDefinitions.Add(new()); clocks.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); clocks.Children.Add(elapsed); Grid.SetColumn(duration, 1); clocks.Children.Add(duration);
        play = Ui.AsyncButton("", C.Media.ToggleAsync, C.Error); play.Background = Brushes.White; play.Width = play.Height = 36; play.Padding = new Thickness(0); play.Margin = new Thickness(12, 0, 12, 0); play.SetResourceReference(StyleProperty, "RoundPlayback");
        var previous = Ui.AsyncButton("", C.Media.PreviousAsync, C.Error); var next = Ui.AsyncButton("", C.Media.NextAsync, C.Error); previous.Content = Icons.Make("previous", 19); next.Content = Icons.Make("next", 19); previous.ToolTip = "Предыдущий трек"; next.ToolTip = "Следующий трек";
        foreach (var b in new[] { previous, next }) { b.Background = Brushes.Transparent; b.Padding = new Thickness(9, 4, 9, 4); b.Margin = new Thickness(0); }
        var playback = Ui.Row(previous, play, next, Icons.Button("music", "Плеер и текст песни", () => C.Go("media", false))); playback.HorizontalAlignment = HorizontalAlignment.Center; playback.Margin = new Thickness(90, 5, 0, 0);
        names.Children.Add(seek); names.Children.Add(clocks);
        musicCard = Ui.Card(Ui.Stack(info, playback), 12);
        var volumeRow = new Grid(); volumeRow.ColumnDefinitions.Add(new() { Width = new GridLength(20) }); volumeRow.ColumnDefinitions.Add(new()); volumeRow.ColumnDefinitions.Add(new() { Width = new GridLength(30) }); volumeRow.Children.Add(Icons.Colored("volume", 19)); Grid.SetColumn(volume, 1); volumeRow.Children.Add(volume); Grid.SetColumn(volumeText, 2); volumeText.VerticalAlignment = VerticalAlignment.Center; volumeRow.Children.Add(volumeText);
        RefreshOutputChoices(); outputs.DropDownOpened += (_, _) => RefreshOutputChoices();
        outputs.SelectionChanged += (_, _) => { if (!updatingOutputs && outputs.SelectedValue is string id) { C.Settings.AudioOutputId = id; C.State.Touch(); C.Media.StartAudio(id); Refresh(); } };
        var sound = new Grid(); sound.ColumnDefinitions.Add(new()); sound.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); sound.Children.Add(volumeRow); Grid.SetColumn(outputs, 1); sound.Children.Add(outputs);
        var left = Ui.Stack(musicCard, Ui.Card(sound, 10)); columns.Children.Add(left);
        seek.PreviewMouseLeftButtonUp += async (_, _) => { try { await C.Media.SeekAsync(seek.Value); } catch (Exception e) { C.Error(e); } };
        volume.ValueChanged += (_, _) => { if (updating) return; try { C.System.Volume = (float)(volume.Value / 100); volumeText.Text = $"{volume.Value:0}%"; } catch (Exception e) { C.Error(e); } };

        var tools = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Rows = 4 };
        Button Tool(string icon, string text, Func<Task> action)
        {
            var b = Ui.AsyncButton(text, action, C.Error); var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; content.Children.Add(Icons.Colored(icon, 23));
            var label = Ui.Text(text, 12, bold: true); label.MaxWidth = 60; label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(8, 0, 0, 0); content.Children.Add(label);
            b.Content = content; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(11, 8, 8, 8); b.Margin = new Thickness(0, 0, 6, 6); tools.Children.Add(b); return b;
        }
        Tool("shot", "Область", () => window.ScreenshotAsync("shot"));
        Tool("screen", "Весь экран", window.ScreenshotFullAsync);
        Tool("ocr", "Текст с экрана", () => window.ScreenshotAsync("ocr"));
        Tool("qr", "QR-код", () => window.ScreenshotAsync("qr"));
        Tool("color", "Пипетка", () => window.ScreenshotAsync("color"));
        Tool("paste", "Чистый текст", () => { if (System.Windows.Clipboard.ContainsText()) { var text = System.Windows.Clipboard.GetText(); System.Windows.Clipboard.SetText(text); C.Collapse(); Native.Paste(C.PasteTarget); } return Task.CompletedTask; });
        record = Tool("record", "Запись экрана", () => window.ToggleRecordingAsync());
        var menu = new ContextMenu();
        foreach (var (label, gif) in new[] { ("Записать область в MP4", false), ("Записать область в GIF", true) })
        { var item = new MenuItem { Header = label }; item.Click += async (_, _) => await window.ToggleRecordingAsync(gif); menu.Items.Add(item); }
        var folder = new MenuItem { Header = "Открыть папку записей" }; folder.Click += (_, _) => { Directory.CreateDirectory(C.Settings.CaptureFolder); SystemService.Open(C.Settings.CaptureFolder); }; menu.Items.Add(folder); record.ContextMenu = menu;
        var recordingSettings = new MenuItem { Header = "Настройки записи…" }; recordingSettings.Click += (_, _) => C.Go("settings:recording"); menu.Items.Add(recordingSettings);
        Tool("command", "Команда", () => { C.Go("search", true); return Task.CompletedTask; });
        Grid.SetColumn(tools, 2); columns.Children.Add(tools);

        BuildLauncher();
        launcherScroll = new ScrollViewer { Content = launcher, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, PanningMode = PanningMode.HorizontalOnly, CanContentScroll = false };
        var dock = Ui.Card(launcherScroll, 5); dock.Margin = new Thickness(0, 3, 6, 0); Grid.SetRow(dock, 1); root.Children.Add(dock); Grid.SetRow(saved, 1); root.Children.Add(saved); Content = root; Refresh();
    }
    private void BuildLauncher()
    {
        var key = string.Join("\0", C.State.Data.Launcher.Select(item => item.Name + "|" + item.Target + "|" + item.Group + "|" + item.Hotkey));
        if (key == launcherKey && launcher.Children.Count > 0) return;
        launcherKey = key; launcher.Children.Clear();
        foreach (var item in C.State.Data.Launcher)
        {
            var b = Ui.Button(item.Name, () => { try { SystemService.Open(item.Target); C.Collapse(); } catch (Exception e) { C.Error(e); } });
            b.Width = b.Height = 42; b.Padding = new Thickness(0); b.Background = Brushes.Transparent; b.Margin = new Thickness(0, 0, 8, 0); b.ToolTip = item.Name;
            b.Content = Icons.Launcher(item.Target, item.Name, 38);
            b.ContextMenu = LauncherActions.Menu(C, item, BuildLauncher);
            launcher.Children.Add(b);
        }
        launcher.Children.Add(Icons.Button("plus", "Добавить ярлык", () => LauncherActions.Add(C, BuildLauncher), 38));
    }
    public override void Refresh()
    {
        BuildLauncher();
        var m = C.Media.Current;
        title.Text = m.HasMedia ? m.Title : "Музыка"; artist.Text = m.HasMedia ? m.Artist : "Нет активного плеера"; cover.Source = m.Cover; coverPlaceholder.Visibility = m.Cover == null ? Visibility.Visible : Visibility.Hidden;
        if (!paletteReady || !ReferenceEquals(coloredCover, m.Cover))
        {
            paletteReady = true; coloredCover = m.Cover; var accent = AlbumPalette.Accent(m.Cover); musicCard.Background = AlbumPalette.Surface(accent); seek.Foreground = new SolidColorBrush(accent);
        }
        play.Content = Icons.Make(m.IsPlaying ? "pause" : "play", 18, "PlayerInk"); play.ToolTip = m.IsPlaying ? "Пауза" : "Воспроизвести"; seek.Maximum = Math.Max(1, m.Duration); seek.IsEnabled = m.HasMedia && m.Duration > 0;
        if (!seek.IsMouseCaptureWithin) seek.Value = Math.Clamp(m.Position, 0, seek.Maximum);
        elapsed.Text = Ui.Time(m.Position); duration.Text = Ui.Time(m.Duration);
        updating = true; if (!volume.IsMouseCaptureWithin) volume.Value = C.System.Volume * 100; volumeText.Text = $"{volume.Value:0}%"; updating = false;
        var text = C.Recording.IsStarting ? "Запуск…" : C.Recording.IsFinalizing ? "Сохранение…" : C.Recording.IsRecording ? "Остановить запись" : "Запись экрана";
        if (record.Content is StackPanel stack && stack.Children[1] is TextBlock label)
        {
            label.Text = text;
            if (shownRecordingIcon != C.Recording.IsRecording) { shownRecordingIcon = C.Recording.IsRecording; stack.Children.RemoveAt(0); stack.Children.Insert(0, Icons.Colored(C.Recording.IsRecording ? "stop" : "record", 23)); }
        }
        record.IsEnabled = !C.Recording.IsStarting && !C.Recording.IsFinalizing; record.ToolTip = text + " · Ctrl+Alt+R\nПравая кнопка: MP4 / GIF и папка записей";
        var output = C.Recording.LastOutputPath;
        var showOutput = output != null && output != dismissedOutput;
        saved.Visibility = showOutput ? Visibility.Visible : Visibility.Collapsed; launcherScroll.Visibility = showOutput ? Visibility.Collapsed : Visibility.Visible;
        if (output != null && shownOutput != output)
        {
            shownOutput = output; saved.Children.Clear();
            var savedLabel = Ui.Text("Запись сохранена", 11, "Muted"); savedLabel.VerticalAlignment = VerticalAlignment.Center; savedLabel.Margin = new Thickness(0, 0, 12, 0); saved.Children.Add(savedLabel);
            saved.Children.Add(Ui.Button("Открыть", () => SystemService.Open(output))); saved.Children.Add(Ui.Button("В папку", () => SystemService.Reveal(output)));
            saved.Children.Add(Ui.Button("На полку", () => { if (!C.State.Data.Shelf.Contains(output)) C.State.Data.Shelf.Add(output); C.State.Touch(); C.Go("shelf", false); }));
            saved.Children.Add(Ui.Button("×", () => { dismissedOutput = output; saved.Visibility = Visibility.Collapsed; launcherScroll.Visibility = Visibility.Visible; }));
        }
    }
    private record DeviceOption(string Id, string Name) { public override string ToString() => Name; }
    internal void RefreshOutputChoices()
    {
        updatingOutputs = true;
        try
        {
            var items = readOutputs().Select(x => new DeviceOption(x.Id, ShortDevice(x.Name))).Prepend(new DeviceOption("", "По умолчанию")).ToArray();
            outputs.ItemsSource = items; outputs.SelectedValue = C.Settings.AudioOutputId;
        }
        catch (Exception e) { Log.Write("Audio output choices", e); }
        finally { updatingOutputs = false; }
    }
    private static string ShortDevice(string name) { var bracket = name.IndexOf('('); return (bracket > 0 ? name[..bracket] : name).Trim(); }
}
