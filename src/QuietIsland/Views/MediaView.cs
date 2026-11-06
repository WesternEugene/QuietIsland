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
    private readonly MusicTransport transport;
    private readonly MusicArtworkLayout artwork;
    private readonly ComboBox sources = new();
    private readonly LyricsPanel lyrics = new();
    private readonly FrameworkElement coverPlaceholder;
    private readonly DispatcherTimer playbackTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
    private MediaSnapshot? playbackSnapshot;
    private long playbackStamp;
    private bool updatingSources;
    public MediaView(IslandContext c) : base(c)
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        sources.Width = 220; sources.MinHeight = 28; sources.FontSize = 11; sources.Margin = new Thickness(12, 0, 0, 0); sources.ToolTip = "Активный плеер";
        DockPanel.SetDock(sources, Dock.Right); header.Children.Add(sources);
        var heading = Ui.Text("Медиаплеер", 12, "Muted"); heading.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(heading); root.Children.Add(header);
        var art = new Grid { Width = 154, Height = 154, Clip = new RectangleGeometry(new Rect(0, 0, 154, 154), 18, 18) }; coverPlaceholder = Icons.Tile("music", 154); art.Children.Add(coverPlaceholder); art.Children.Add(cover);
        artwork = new MusicArtworkLayout(art, title, artist, lyrics, compact: false, () => C.Settings.AnimationSpeed); Grid.SetRow(artwork, 1); root.Children.Add(artwork);
        transport = new MusicTransport(C.Media, C.Lyrics, C.Error) { Margin = new Thickness(0, 6, 0, 0) };
        var times = new DockPanel(); DockPanel.SetDock(duration, Dock.Right); times.Children.Add(duration); times.Children.Add(position);
        seek.Margin = new Thickness(0, 8, 0, 0);
        var progress = Ui.Stack(seek, times); Grid.SetRow(progress, 2); root.Children.Add(progress); Grid.SetRow(transport, 3); root.Children.Add(transport); Content = root;
        seek.PreviewMouseLeftButtonUp += async (_, _) => { try { await C.Media.SeekAsync(seek.Value); } catch (Exception e) { C.Error(e); } };
        sources.SelectionChanged += async (_, _) => { if (!updatingSources && sources.SelectedItem is string source && source != C.Media.PreferredSource) { C.Media.PreferredSource = source; try { await C.Media.PollAsync(); if (!Disposed) Refresh(); } catch (Exception e) { if (!Disposed) C.Error(e); } } };
        playbackTimer.Tick += PlaybackTick; Loaded += PlaybackLoaded; Unloaded += PlaybackUnloaded; IsVisibleChanged += PlaybackVisibilityChanged;
        lyrics.RetryRequested += RetryLyrics; C.Lyrics.Changed += LyricsChanged; Refresh();
    }
    public override void Refresh()
    {
        if (Disposed) return;
        var m = C.Media.Current;
        if (!ReferenceEquals(playbackSnapshot, m)) { playbackSnapshot = m; playbackStamp = Stopwatch.GetTimestamp(); }
        title.Text = m.HasMedia ? m.Title : "Музыка не играет"; artist.Text = m.HasMedia ? m.Artist + (m.Album.Length > 0 ? " · " + m.Album : "") : "Нет активной медиасессии";
        C.Lyrics.UpdateTrack(m);
        cover.Source = m.Cover; transport.Refresh(); seek.Maximum = double.IsFinite(m.Duration) ? Math.Max(1, m.Duration) : 1; seek.IsEnabled = m.HasMedia && m.Duration > 0;
        coverPlaceholder.Visibility = m.Cover == null ? Visibility.Visible : Visibility.Collapsed;
        duration.Text = Ui.Time(m.Duration);
        var available = C.Media.Sources;
        updatingSources = true;
        try { if (!available.SequenceEqual(sources.Items.Cast<string>())) sources.ItemsSource = available; sources.SelectedItem = available.Contains(C.Media.PreferredSource) ? C.Media.PreferredSource : m.Source; }
        finally { updatingSources = false; }
        sources.Visibility = available.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        artwork.SetLyricsEnabled(C.Lyrics.Enabled); UpdatePlayback(); UpdatePlaybackTimer();
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
