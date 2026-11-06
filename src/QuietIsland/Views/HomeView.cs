using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using QuietIsland.Controls;
using QuietIsland.Models;
using QuietIsland.Services;

namespace QuietIsland.Views;

public sealed class HomeView : IslandView
{
    private readonly TextBlock title = Ui.Text("Музыка", 16, bold: true), artist = Ui.Text("Нет активного плеера", 12, "Muted");
    private readonly TextBlock elapsed = Ui.Text("0:00", 10, "Muted"), duration = Ui.Text("0:00", 10, "Muted"), volumeText = Ui.Text("", 10, "Muted");
    private readonly Image cover = new() { Width = 82, Height = 82, Stretch = Stretch.UniformToFill };
    private readonly Slider seek = new() { Minimum = 0, Margin = new Thickness(0, 10, 0, 0), Height = 16 };
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 9, 0) };
    private readonly Button record;
    private readonly MusicTransport transport;
    private readonly MusicArtworkLayout artwork;
    private readonly LyricsPanel lyrics = new(compact: true);
    private readonly DispatcherTimer playbackTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
    private MediaSnapshot? playbackSnapshot;
    private long playbackStamp;
    private readonly Border musicCard;
    private readonly FrameworkElement coverPlaceholder;
    private System.Windows.Media.Imaging.BitmapSource? coloredCover;
    private bool paletteReady;
    private readonly Border saved = new() { Visibility = Visibility.Collapsed, Height = 58, Margin = new Thickness(0, 6, 6, 0), Padding = new Thickness(10, 7, 8, 7), CornerRadius = new CornerRadius(12) };
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
    private bool savedVisible;
    public double PreferredBodyHeight => (C.Lyrics.Enabled ? 378 : 324) + (savedVisible ? 64 : 0);
    public event Action? PreferredBodyHeightChanged;
    public HomeView(IslandContext c, MainWindow window, Func<IReadOnlyList<(string Id, string Name)>>? outputReader = null) : base(c)
    {
        readOutputs = outputReader ?? (() => C.System.AudioDevices(DataFlow.Render));
        var root = new Grid(); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new GridLength(1.8, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(12) }); columns.ColumnDefinitions.Add(new()); root.Children.Add(columns);
        title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        artist.TextWrapping = TextWrapping.NoWrap; artist.TextTrimming = TextTrimming.CharacterEllipsis; artist.Margin = new Thickness(0, 6, 0, 0);
        var art = new Grid { Width = 82, Height = 82, Clip = new RectangleGeometry(new Rect(0, 0, 82, 82), 12, 12) }; coverPlaceholder = Icons.Tile("music", 82); art.Children.Add(coverPlaceholder); art.Children.Add(cover);
        artwork = new MusicArtworkLayout(art, title, artist, lyrics, compact: true, () => C.Settings.AnimationSpeed);
        var clocks = new Grid(); clocks.ColumnDefinitions.Add(new()); clocks.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); clocks.Children.Add(elapsed); Grid.SetColumn(duration, 1); clocks.Children.Add(duration);
        transport = new MusicTransport(C.Media, C.Lyrics, C.Error, compact: true) { Margin = new Thickness(0, 4, 0, 0) };
        seek.Margin = new Thickness(0, 9, 0, 0);
        var progress = Ui.Stack(seek, clocks);
        var music = new Grid(); music.RowDefinitions.Add(new()); music.RowDefinitions.Add(new() { Height = GridLength.Auto }); music.RowDefinitions.Add(new() { Height = GridLength.Auto });
        music.Children.Add(artwork); Grid.SetRow(transport, 1); music.Children.Add(transport); Grid.SetRow(progress, 2); music.Children.Add(progress);
        musicCard = Ui.Card(music, 12);
        musicCard.ContextMenu = new ContextMenu();
        musicCard.ContextMenuOpening += (_, _) =>
        {
            var menu = musicCard.ContextMenu; menu.Items.Clear();
            foreach (var id in new[] { "" }.Concat(C.Media.Sources).Distinct())
            {
                var item = new MenuItem { Header = id.Length == 0 ? "Автоматический выбор плеера" : id, IsCheckable = true, IsChecked = C.Media.PreferredSource == id };
                item.Click += async (_, _) => { C.Media.PreferredSource = id; try { await C.Media.PollAsync(); } catch (Exception e) { C.Error(e); } };
                menu.Items.Add(item);
            }
        };
        var volumeRow = new Grid(); volumeRow.ColumnDefinitions.Add(new() { Width = new GridLength(20) }); volumeRow.ColumnDefinitions.Add(new()); volumeRow.ColumnDefinitions.Add(new() { Width = new GridLength(30) }); volumeRow.Children.Add(Icons.Colored("volume", 19)); Grid.SetColumn(volume, 1); volumeRow.Children.Add(volume); Grid.SetColumn(volumeText, 2); volumeText.VerticalAlignment = VerticalAlignment.Center; volumeRow.Children.Add(volumeText);
        RefreshOutputChoices(); outputs.DropDownOpened += (_, _) => RefreshOutputChoices();
        outputs.SelectionChanged += (_, _) => { if (!updatingOutputs && outputs.SelectedValue is string id) { C.Settings.AudioOutputId = id; C.State.Touch(); C.Media.StartAudio(id); Refresh(); } };
        var sound = new Grid(); sound.ColumnDefinitions.Add(new()); sound.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); sound.Children.Add(volumeRow); Grid.SetColumn(outputs, 1); sound.Children.Add(outputs);
        var volumeCard = Ui.Card(sound, 10); volumeCard.Margin = new Thickness(0, 0, 0, 6);
        var left = new Grid(); left.RowDefinitions.Add(new()); left.RowDefinitions.Add(new() { Height = GridLength.Auto }); left.Children.Add(musicCard); Grid.SetRow(volumeCard, 1); left.Children.Add(volumeCard); columns.Children.Add(left);
        seek.AddHandler(System.Windows.Input.Mouse.PreviewMouseUpEvent, new System.Windows.Input.MouseButtonEventHandler(async (_, e) => { if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return; try { await C.Media.SeekAsync(seek.Value); } catch (Exception error) { C.Error(error); } }), true);
        volume.ValueChanged += (_, _) => { if (updating) return; try { C.System.Volume = (float)(volume.Value / 100); volumeText.Text = $"{volume.Value:0}%"; } catch (Exception e) { C.Error(e); } };

        var tools = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Rows = 4 };
        Button Tool(string icon, string text, Func<Task> action)
        {
            var b = Ui.AsyncButton(text, action, C.Error); var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; content.Children.Add(Icons.Colored(icon, 23));
            var label = Ui.Text(text, 12, bold: true); label.MaxWidth = 60; label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(8, 0, 0, 0); content.Children.Add(label);
            b.Content = content; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(11, 8, 8, 8); b.Margin = new Thickness(0, 0, 6, 6); tools.Children.Add(b); return b;
        }
        void CapturePreference(Button button)
        {
            var menu = button.ContextMenu ?? new ContextMenu();
            var hide = new MenuItem { Header = "Скрывать чёлку на снимках и в записи", IsCheckable = true, IsChecked = C.Settings.HideInCapture };
            menu.Opened += (_, _) => hide.IsChecked = C.Settings.HideInCapture;
            hide.Click += (_, _) => window.SetCapturePreference(hide.IsChecked);
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            menu.Items.Add(hide); button.ContextMenu = menu;
        }
        var shot = Tool("shot", "Область", () => window.ScreenshotAsync("shot")); CapturePreference(shot);
        shot.ToolTip = "Снимок области · правая кнопка: скрытие чёлки";
        var screenShot = Tool("screen", "Весь экран", window.ScreenshotFullAsync); CapturePreference(screenShot);
        screenShot.ToolTip = "Снимок экрана · правая кнопка: скрытие чёлки";
        Tool("ocr", "Текст с экрана", () => window.ScreenshotAsync("ocr"));
        Tool("qr", "QR-код", () => window.ScreenshotAsync("qr"));
        Tool("color", "Пипетка", () => window.ScreenshotAsync("color"));
        Tool("paste", "Чистый текст", () => { if (System.Windows.Clipboard.ContainsText()) { var text = System.Windows.Clipboard.GetText(); System.Windows.Clipboard.SetText(text); C.Collapse(); Native.Paste(C.PasteTarget); } return Task.CompletedTask; });
        record = Tool("record", "Запись экрана", () => window.ToggleRecordingAsync());
        var menu = new ContextMenu();
        foreach (var (label, gif) in new[] { ("Записать область в MP4", false), ("Записать область в GIF", true) })
        { var item = new MenuItem { Header = label }; item.Click += async (_, _) => await window.ToggleRecordingAsync(gif); menu.Items.Add(item); }
        foreach (var (label, all) in new[] { ("Записать весь экран в MP4", false), ("Записать все экраны в MP4", true) })
        { var item = new MenuItem { Header = label }; item.Click += async (_, _) => await window.ToggleRecordingAsync(fullScreen: true, allScreens: all); menu.Items.Add(item); }
        var folder = new MenuItem { Header = "Открыть папку записей" }; folder.Click += (_, _) => { Directory.CreateDirectory(C.Settings.CaptureFolder); SystemService.Open(C.Settings.CaptureFolder); }; menu.Items.Add(folder); record.ContextMenu = menu;
        var recordingSettings = new MenuItem { Header = "Настройки записи…" }; recordingSettings.Click += (_, _) => C.Go("settings:recording"); menu.Items.Add(recordingSettings);
        CapturePreference(record);
        Tool("command", "Команда", () => { C.Go("search", true); return Task.CompletedTask; });
        Grid.SetColumn(tools, 2); columns.Children.Add(tools);

        BuildLauncher();
        launcherScroll = new ScrollViewer { Content = launcher, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, PanningMode = PanningMode.HorizontalOnly, CanContentScroll = false };
        var dock = Ui.Card(launcherScroll, 5); dock.Margin = new Thickness(0, 4, 6, 0); Grid.SetRow(dock, 1); root.Children.Add(dock);
        saved.SetResourceReference(Border.BackgroundProperty, "Card"); Grid.SetRow(saved, 2); root.Children.Add(saved); Content = root;
        playbackTimer.Tick += PlaybackTick; Loaded += PlaybackLoaded; Unloaded += PlaybackUnloaded; IsVisibleChanged += PlaybackVisibilityChanged;
        lyrics.RetryRequested += RetryLyrics; lyrics.SeekRequested += SeekLyric; C.Lyrics.Changed += LyricsChanged; Refresh();
    }
    private void BuildLauncher()
    {
        var key = string.Join("\0", C.State.Data.Launcher.Select(item => item.Name + "|" + item.Target + "|" + item.Group + "|" + item.Hotkey));
        if (key == launcherKey && launcher.Children.Count > 0) return;
        launcherKey = key; launcher.Children.Clear();
        foreach (var item in C.State.Data.Launcher)
        {
            var b = Ui.Button(item.Name, () => { try { SystemService.Open(item.Target); C.Collapse(); } catch (Exception e) { C.Error(e); } });
            b.Width = b.Height = 42; b.Padding = new Thickness(0); b.Background = Brushes.Transparent; b.Margin = new Thickness(0, 0, 8, 0); b.ToolTip = item.Name; b.VerticalContentAlignment = VerticalAlignment.Center; b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.Content = Icons.Launcher(item.Target, item.Name, 36);
            b.ContextMenu = LauncherActions.Menu(C, item, BuildLauncher);
            launcher.Children.Add(b);
        }
        launcher.Children.Add(Icons.Button("plus", "Добавить ярлык", () => LauncherActions.Add(C, BuildLauncher), 42));
    }
    public override void Refresh()
    {
        if (Disposed) return;
        BuildLauncher();
        var m = C.Media.Current;
        if (!ReferenceEquals(playbackSnapshot, m)) { playbackSnapshot = m; playbackStamp = Stopwatch.GetTimestamp(); }
        C.Lyrics.UpdateTrack(m);
        title.Text = m.HasMedia ? m.Title : "Музыка"; artist.Text = m.HasMedia ? m.Artist : "Нет активного плеера"; cover.Source = m.Cover; coverPlaceholder.Visibility = m.Cover == null ? Visibility.Visible : Visibility.Hidden;
        if (!paletteReady || !ReferenceEquals(coloredCover, m.Cover))
        {
            paletteReady = true; coloredCover = m.Cover; var accent = AlbumPalette.Accent(m.Cover); musicCard.Background = AlbumPalette.Surface(accent); seek.Foreground = new SolidColorBrush(AlbumPalette.Control(accent)); transport.SetAlbumColor(accent);
        }
        transport.Refresh(); seek.Maximum = double.IsFinite(m.Duration) ? Math.Max(1, m.Duration) : 1; seek.IsEnabled = m.HasMedia && m.Duration > 0;
        duration.Text = Ui.Time(m.Duration); RefreshLyrics();
        updating = true; if (!volume.IsMouseCaptureWithin) volume.Value = C.System.Volume * 100; volumeText.Text = $"{volume.Value:0}%"; updating = false;
        var text = C.Recording.IsStarting ? "Запуск…" : C.Recording.IsFinalizing ? "Сохранение…" : C.Recording.IsRecording ? "Остановить запись" : "Запись экрана";
        if (record.Content is StackPanel stack && stack.Children[1] is TextBlock label)
        {
            label.Text = text;
            if (shownRecordingIcon != C.Recording.IsRecording) { shownRecordingIcon = C.Recording.IsRecording; stack.Children.RemoveAt(0); stack.Children.Insert(0, Icons.Colored(C.Recording.IsRecording ? "stop" : "record", 23)); }
        }
        record.IsEnabled = !C.Recording.IsStarting && !C.Recording.IsFinalizing; record.ToolTip = text + " · " + GlobalShortcuts.Label(C.Settings, "recording") + "\nПравая кнопка: MP4 / GIF, скрытие чёлки и папка записей";
        var output = C.Recording.LastOutputPath;
        var showOutput = output != null && output != dismissedOutput;
        SetSavedVisible(showOutput);
        if (output != null && shownOutput != output)
        {
            shownOutput = output;
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(36) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var icon = Icons.Tile("check", 28); icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon);
            var name = Ui.Text(Path.GetFileName(output), 10, "Muted"); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.Margin = new Thickness(0, 3, 12, 0);
            var caption = Ui.Text("Запись сохранена", 12, bold: true); var description = Ui.Stack(caption, name); description.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(description, 1); row.Children.Add(description);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button Action(string iconName, string label, string tip, Action action)
            {
                var button = Ui.Button("", action, tip: tip); button.Padding = new Thickness(8, 5, 8, 5); button.Margin = new Thickness(0, 0, 4, 0); button.Height = 32;
                var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; content.Children.Add(Icons.Make(iconName, 14));
                var text = Ui.Text(label, 11); text.Margin = new Thickness(5, 0, 0, 0); text.VerticalAlignment = VerticalAlignment.Center; content.Children.Add(text); button.Content = content; AutomationProperties.SetName(button, tip); actions.Children.Add(button); return button;
            }
            Action("play", "Открыть", "Открыть запись", () => SystemService.Open(output)); Action("folder", "Папка", "Показать запись в папке", () => SystemService.Reveal(output));
            Action("shelf", "На полку", "Добавить запись на полку", () => { if (!C.State.Data.Shelf.Contains(output)) C.State.Data.Shelf.Add(output); C.State.Touch(); C.Go("shelf", false); });
            var close = Icons.Button("close", "Скрыть сохранённую запись", () => { dismissedOutput = output; SetSavedVisible(false); }, 28); close.Margin = new Thickness(0); close.Padding = new Thickness(6); actions.Children.Add(close);
            Grid.SetColumn(actions, 2); row.Children.Add(actions); saved.Child = row;
        }
    }
    private void SetSavedVisible(bool value)
    {
        if (savedVisible == value) return;
        savedVisible = value; saved.Visibility = value ? Visibility.Visible : Visibility.Collapsed; PreferredBodyHeightChanged?.Invoke();
    }
    private void RefreshLyrics()
    {
        var enabled = C.Lyrics.Enabled;
        artwork.SetLyricsEnabled(enabled); transport.Refresh(); UpdatePlayback(); UpdatePlaybackTimer();
    }
    private void UpdatePlayback()
    {
        if (Disposed || playbackSnapshot is not { } media) return;
        var seconds = Math.Max(0, double.IsFinite(media.Position) ? media.Position : 0) + (media.IsPlaying ? Stopwatch.GetElapsedTime(playbackStamp).TotalSeconds : 0);
        if (double.IsFinite(media.Duration) && media.Duration > 0) seconds = Math.Min(seconds, media.Duration);
        if (!seek.IsMouseCaptureWithin) seek.Value = Math.Clamp(seconds, 0, seek.Maximum);
        elapsed.Text = Ui.Time(seconds); if (C.Lyrics.Enabled) lyrics.Update(C.Lyrics.Current, seconds);
    }
    private void UpdatePlaybackTimer()
    {
        var run = !Disposed && IsLoaded && IsVisible && C.Lyrics.Enabled && playbackSnapshot is { HasMedia: true, IsPlaying: true };
        if (run) { if (!playbackTimer.IsEnabled) playbackTimer.Start(); }
        else playbackTimer.Stop();
    }
    private void PlaybackTick(object? sender, EventArgs e) { UpdatePlaybackTimer(); if (playbackTimer.IsEnabled) UpdatePlayback(); }
    private void PlaybackLoaded(object sender, RoutedEventArgs e) { UpdatePlayback(); UpdatePlaybackTimer(); }
    private void PlaybackUnloaded(object sender, RoutedEventArgs e) => playbackTimer.Stop();
    private void PlaybackVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdatePlaybackTimer();
    private void RetryLyrics() => C.Lyrics.Retry();
    private async void SeekLyric(double seconds) { try { await C.Media.SeekAsync(seconds); } catch (Exception error) { if (!Disposed) C.Error(error); } }
    private void LyricsChanged()
    {
        if (Disposed) return;
        void Apply() { if (Disposed) return; RefreshLyrics(); PreferredBodyHeightChanged?.Invoke(); }
        if (Dispatcher.CheckAccess()) Apply();
        else Dispatcher.InvokeAsync(Apply);
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
    public override void Dispose()
    {
        playbackTimer.Stop(); playbackTimer.Tick -= PlaybackTick; Loaded -= PlaybackLoaded; Unloaded -= PlaybackUnloaded; IsVisibleChanged -= PlaybackVisibilityChanged;
        C.Lyrics.Changed -= LyricsChanged; lyrics.RetryRequested -= RetryLyrics; lyrics.SeekRequested -= SeekLyric; lyrics.Dispose(); base.Dispose();
    }
}
