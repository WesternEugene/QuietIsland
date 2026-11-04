using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QuietIsland.Controls;
using QuietIsland.Models;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class MediaView : IslandView
{
    private readonly TextBlock title = Ui.Text("Музыка не играет", 20, bold: true), artist = Ui.Text("Нет активной медиасессии", 12, "Muted"), position = Ui.Text("0:00", 11, "Muted"), duration = Ui.Text("0:00", 11, "Muted");
    private readonly Image cover = new() { Width = 154, Height = 154, Stretch = Stretch.UniformToFill };
    private readonly Slider seek = new() { Minimum = 0, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Button play, lyricsToggle;
    private readonly ComboBox sources = new();
    private readonly LyricsPanel lyrics = new();
    private readonly FrameworkElement coverPlaceholder;
    private readonly DispatcherTimer playbackTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
    private MediaSnapshot? playbackSnapshot;
    private long playbackStamp;
    private bool updatingSources;
    public MediaView(IslandContext c) : base(c)
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        lyricsToggle = Ui.Button("Текст песни", () => C.Lyrics.SetEnabled(!C.Lyrics.Enabled)); lyricsToggle.Margin = new Thickness(0); lyricsToggle.Padding = new Thickness(12, 6, 12, 6); DockPanel.SetDock(lyricsToggle, Dock.Right); header.Children.Add(lyricsToggle);
        var heading = Ui.Text("Медиаплеер", 12, "Muted"); heading.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(heading); root.Children.Add(header);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(154) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(22) }); body.ColumnDefinitions.Add(new()); Grid.SetRow(body, 1); root.Children.Add(body);
        var art = new Grid { Width = 154, Height = 154, VerticalAlignment = VerticalAlignment.Top, Clip = new RectangleGeometry(new Rect(0, 0, 154, 154), 18, 18) }; coverPlaceholder = Icons.Tile("music", 154); art.Children.Add(coverPlaceholder); art.Children.Add(cover); body.Children.Add(art);
        var details = new Grid(); details.RowDefinitions.Add(new() { Height = GridLength.Auto }); details.RowDefinitions.Add(new() { Height = GridLength.Auto }); details.RowDefinitions.Add(new()); Grid.SetColumn(details, 2); body.Children.Add(details);
        title.MaxHeight = 58; title.TextTrimming = TextTrimming.CharacterEllipsis; title.Margin = new Thickness(0, 0, 0, 4); details.Children.Add(title);
        artist.TextWrapping = TextWrapping.NoWrap; artist.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetRow(artist, 1); details.Children.Add(artist);
        Grid.SetRow(lyrics, 2); details.Children.Add(lyrics);
        play = Ui.AsyncButton("Воспроизвести", C.Media.ToggleAsync, C.Error, true); play.MinWidth = 52;
        var previous = Ui.AsyncButton("", C.Media.PreviousAsync, C.Error); previous.Content = Icons.Make("previous", 20); previous.ToolTip = "Предыдущий трек";
        var next = Ui.AsyncButton("", C.Media.NextAsync, C.Error); next.Content = Icons.Make("next", 20); next.ToolTip = "Следующий трек";
        var buttons = Ui.Row(previous, play, next); buttons.HorizontalAlignment = HorizontalAlignment.Center;
        var times = new DockPanel(); DockPanel.SetDock(duration, Dock.Right); times.Children.Add(duration); times.Children.Add(position);
        var controls = Ui.Stack(seek, times, buttons, sources); Grid.SetRow(controls, 2); root.Children.Add(controls); Content = root;
        seek.PreviewMouseLeftButtonUp += async (_, _) => { try { await C.Media.SeekAsync(seek.Value); } catch (Exception e) { C.Error(e); } };
        sources.SelectionChanged += async (_, _) => { if (!updatingSources && sources.SelectedItem is string source && source != C.Media.PreferredSource) { C.Media.PreferredSource = source; try { await C.Media.PollAsync(); if (!Disposed) Refresh(); } catch (Exception e) { if (!Disposed) C.Error(e); } } };
        playbackTimer.Tick += PlaybackTick; Loaded += PlaybackLoaded; Unloaded += PlaybackUnloaded; IsVisibleChanged += PlaybackVisibilityChanged;
        lyrics.RetryRequested += RetryLyrics; C.Lyrics.Changed += LyricsChanged; Refresh();
    }
    public override void Refresh()
    {
        var m = C.Media.Current;
        if (!ReferenceEquals(playbackSnapshot, m)) { playbackSnapshot = m; playbackStamp = Stopwatch.GetTimestamp(); }
        title.Text = m.HasMedia ? m.Title : "Музыка не играет"; artist.Text = m.HasMedia ? m.Artist + (m.Album.Length > 0 ? " · " + m.Album : "") : "Нет активной медиасессии";
        C.Lyrics.UpdateTrack(m);
        cover.Source = m.Cover; play.Content = Icons.Make(m.IsPlaying ? "pause" : "play", 20, "AccentText"); play.ToolTip = m.IsPlaying ? "Пауза" : "Воспроизвести"; seek.Maximum = double.IsFinite(m.Duration) ? Math.Max(1, m.Duration) : 1;
        coverPlaceholder.Visibility = m.Cover == null ? Visibility.Visible : Visibility.Collapsed;
        duration.Text = Ui.Time(m.Duration);
        var available = C.Media.Sources;
        updatingSources = true;
        try { if (!available.SequenceEqual(sources.Items.Cast<string>())) sources.ItemsSource = available; sources.SelectedItem = available.Contains(C.Media.PreferredSource) ? C.Media.PreferredSource : m.Source; }
        finally { updatingSources = false; }
        sources.Visibility = available.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var enabled = C.Lyrics.Enabled; lyricsToggle.Content = enabled ? "Текст песни: вкл" : "Текст песни: выкл"; lyricsToggle.ToolTip = enabled ? "Скрыть текст песни" : "Показать текст песни";
        lyricsToggle.SetResourceReference(BackgroundProperty, enabled ? "Accent" : "Card"); lyricsToggle.SetResourceReference(ForegroundProperty, enabled ? "AccentText" : "Text");
        lyrics.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; UpdatePlayback(); UpdatePlaybackTimer();
    }
    private void UpdatePlayback()
    {
        if (Disposed || playbackSnapshot is not { } media) return;
        var seconds = Math.Max(0, double.IsFinite(media.Position) ? media.Position : 0) + (media.IsPlaying ? Stopwatch.GetElapsedTime(playbackStamp).TotalSeconds : 0);
        if (double.IsFinite(media.Duration) && media.Duration > 0) seconds = Math.Min(seconds, media.Duration);
        if (!seek.IsMouseCaptureWithin) seek.Value = Math.Clamp(seconds, 0, seek.Maximum);
        position.Text = Ui.Time(seconds); lyrics.Update(C.Lyrics.Current, seconds);
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
    private void LyricsChanged()
    {
        if (Disposed) return;
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.InvokeAsync(() => { if (!Disposed) Refresh(); });
    }
    public override void Dispose()
    {
        playbackTimer.Stop(); playbackTimer.Tick -= PlaybackTick; Loaded -= PlaybackLoaded; Unloaded -= PlaybackUnloaded; IsVisibleChanged -= PlaybackVisibilityChanged;
        C.Lyrics.Changed -= LyricsChanged; lyrics.RetryRequested -= RetryLyrics; lyrics.Dispose(); base.Dispose();
    }
}
