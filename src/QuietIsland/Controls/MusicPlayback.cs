using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuietIsland.Services;
using Windows.Media;

namespace QuietIsland.Controls;

/// <summary>Keeps Play on the centre line when the side actions change.</summary>
internal sealed class MusicTransport : Grid
{
    private readonly MediaService media;
    private readonly LyricsService lyrics;
    private readonly Action<Exception> error;
    private bool busy;
    public Button PlayButton { get; }
    public Button LyricsButton { get; }
    private readonly Button shuffle, repeat;
    private readonly SolidColorBrush activeBrush = new(Colors.Gray);
    private readonly SolidColorBrush activeInk = new(Colors.Black);
    public void SetAlbumColor(Color color)
    {
        activeBrush.Color = AlbumPalette.Control(color);
        var c = activeBrush.Color;
        activeInk.Color = c.R * .299 + c.G * .587 + c.B * .114 > 150 ? Color.FromRgb(20, 20, 20) : Colors.White;
    }

    public MusicTransport(MediaService media, LyricsService lyrics, Action<Exception> error, bool compact = false, Action? openPlayer = null)
    {
        this.media = media; this.lyrics = lyrics; this.error = error;
        Resources["MusicActive"] = activeBrush; Resources["MusicActiveInk"] = activeInk;
        AutomationProperties.SetName(this, "Управление музыкой");
        ColumnDefinitions.Add(new()); ColumnDefinitions.Add(new() { Width = GridLength.Auto }); ColumnDefinitions.Add(new());
        var size = compact ? 28 : 32;
        var modes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        shuffle = ActionButton(media.ToggleShuffleAsync, size); repeat = ActionButton(media.CycleRepeatAsync, size);
        modes.Children.Add(shuffle); modes.Children.Add(repeat); Children.Add(modes);
        var central = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var previous = ActionButton(media.PreviousAsync, size); Icon(previous, "previous", "Предыдущий трек");
        var next = ActionButton(media.NextAsync, size); Icon(next, "next", "Следующий трек");
        PlayButton = ActionButton(media.ToggleAsync, compact ? 36 : 42); PlayButton.Margin = new Thickness(compact ? 8 : 12, 0, compact ? 8 : 12, 0);
        PlayButton.Background = Brushes.White; PlayButton.SetResourceReference(StyleProperty, "RoundPlayback");
        central.Children.Add(previous); central.Children.Add(PlayButton); central.Children.Add(next); Grid.SetColumn(central, 1); Children.Add(central);
        var extra = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        LyricsButton = ActionButton(() => { lyrics.SetEnabled(!lyrics.Enabled); return Task.CompletedTask; }, size); extra.Children.Add(LyricsButton);
        if (openPlayer != null)
        {
            var open = ActionButton(() => { openPlayer(); return Task.CompletedTask; }, size); Icon(open, "music", "Открыть медиаплеер"); extra.Children.Add(open);
        }
        Grid.SetColumn(extra, 2); Children.Add(extra); Refresh();
    }

    private Button ActionButton(Func<Task> action, double size)
    {
        var button = new Button { Width = size, Height = size, Padding = new Thickness(4), Margin = new Thickness(0), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true; IsEnabled = false;
            try { await action(); }
            catch (Exception exception) { error(exception); }
            finally { busy = false; IsEnabled = true; Refresh(); }
        };
        return button;
    }

    private static void Icon(Button button, string icon, string label, bool active = false, string? color = null)
    {
        if (!Equals(button.Tag, (icon, label, active, color)))
        {
            button.Tag = (icon, label, active, color);
            button.Content = Icons.Make(icon, 18, color ?? (active ? "MusicActiveInk" : "Text")); button.ToolTip = label;
            if (active) button.SetResourceReference(Control.BackgroundProperty, "MusicActive"); else button.Background = Brushes.Transparent;
        }
        AutomationProperties.SetName(button, label);
    }

    public void Refresh()
    {
        Icon(PlayButton, media.Current.IsPlaying ? "pause" : "play", media.Current.IsPlaying ? "Пауза" : "Воспроизвести", color: "PlayerInk"); PlayButton.Background = Brushes.White;
        shuffle.IsEnabled = media.CanShuffle;
        Icon(shuffle, "shuffle", media.CanShuffle ? (media.IsShuffleActive ? "Перемешивание включено" : "Перемешивание выключено") : "Перемешивание недоступно в этом плеере", media.IsShuffleActive);
        repeat.IsEnabled = media.CanRepeat;
        var repeatLabel = media.RepeatMode switch { MediaPlaybackAutoRepeatMode.List => "Повтор всех треков", MediaPlaybackAutoRepeatMode.Track => "Повтор одного трека", _ => "Повтор выключен" };
        Icon(repeat, media.RepeatMode == MediaPlaybackAutoRepeatMode.Track ? "repeat-one" : "repeat", media.CanRepeat ? repeatLabel : "Повтор недоступен в этом плеере", media.RepeatMode != MediaPlaybackAutoRepeatMode.None);
        Icon(LyricsButton, "microphone", lyrics.Enabled ? "Скрыть текст песни" : "Показать текст песни", lyrics.Enabled);
        AutomationProperties.SetItemStatus(shuffle, media.IsShuffleActive ? "Включено" : "Выключено");
        AutomationProperties.SetItemStatus(repeat, repeatLabel);
        AutomationProperties.SetItemStatus(LyricsButton, lyrics.Enabled ? "Включено" : "Выключено");
    }
}

/// <summary>Moves the artwork and metadata together when lyrics take over the right side.</summary>
internal sealed class MusicArtworkLayout : Grid
{
    private readonly FrameworkElement artwork;
    private readonly StackPanel names;
    private readonly TextBlock title, artist;
    private readonly OverflowLabel titleLine, artistLine;
    private readonly LyricsPanel lyrics;
    private readonly bool compact;
    private readonly Func<double> animationSpeed;
    private readonly TranslateTransform artworkShift = new(), namesShift = new(), lyricsShift = new();
    private bool? enabled;
    private int transition;

    public MusicArtworkLayout(FrameworkElement artwork, TextBlock title, TextBlock artist, LyricsPanel lyrics, bool compact, Func<double> animationSpeed)
    {
        this.artwork = artwork; this.title = title; this.artist = artist; this.lyrics = lyrics; this.compact = compact; this.animationSpeed = animationSpeed;
        ColumnDefinitions.Add(new() { Width = new GridLength(compact ? 112 : 184) }); ColumnDefinitions.Add(new() { Width = new GridLength(compact ? 14 : 22) }); ColumnDefinitions.Add(new());
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new());
        title.FontSize = compact ? 19 : 22; artist.FontSize = compact ? 14 : 15;
        artist.Margin = new Thickness(0);
        titleLine = new(title); artistLine = new(artist) { Margin = new Thickness(0, 5, 0, 0) };
        names = Ui.Stack(titleLine, artistLine); names.RenderTransform = namesShift;
        artwork.RenderTransform = artworkShift; lyrics.RenderTransform = lyricsShift;
        title.TextTrimming = TextTrimming.None; artist.TextTrimming = TextTrimming.None;
        title.TextWrapping = TextWrapping.NoWrap; artist.TextWrapping = TextWrapping.NoWrap;
        Grid.SetRowSpan(lyrics, 2); Grid.SetColumn(lyrics, 2);
        Children.Add(artwork); Children.Add(names); Children.Add(lyrics); SetLyricsEnabled(false);
    }

    public void SetLyricsEnabled(bool value)
    {
        titleLine.Refresh(); artistLine.Refresh();
        if (enabled == value) return;
        var generation = ++transition;
        var animate = enabled.HasValue && IsLoaded && IsVisible && ActualWidth > 0;
        var oldArt = animate ? artwork.TranslatePoint(new Point(), this) : new Point();
        var oldNames = animate ? names.TranslatePoint(new Point(), this) : new Point();
        Stop(artworkShift); Stop(namesShift); Stop(lyricsShift);
        enabled = value;
        ColumnDefinitions[0].Width = new GridLength(compact ? (value ? 108 : 112) : (value ? 154 : 184));
        MinHeight = compact ? (value ? 146 : 96) : (value ? 230 : 174);
        artwork.HorizontalAlignment = value ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        artwork.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(artwork, 0); Grid.SetRowSpan(artwork, value ? 1 : 2);
        Grid.SetColumn(names, value ? 0 : 2); Grid.SetRow(names, value ? 1 : 0); Grid.SetRowSpan(names, value ? 1 : 2);
        names.VerticalAlignment = value ? VerticalAlignment.Top : VerticalAlignment.Center; names.Margin = value ? new Thickness(0, 7, 0, 0) : new Thickness(0);
        lyrics.Margin = new Thickness(0); lyrics.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (!animate) return;
        // Arrange and apply inverse transforms in this turn, before WPF can show the new layout.
        UpdateLayout();
        if (generation != transition || !IsLoaded || !IsVisible) return;
        var duration = TimeSpan.FromMilliseconds(300 / Math.Clamp(animationSpeed(), .6, 1.8));
        Move(artworkShift, oldArt - artwork.TranslatePoint(new Point(), this), duration);
        Move(namesShift, oldNames - names.TranslatePoint(new Point(), this), duration);
        if (value)
        {
            Move(lyricsShift, new Vector(16, 0), duration);
            lyrics.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop });
        }
    }

    private static void Move(TranslateTransform transform, Vector delta, TimeSpan duration)
    {
        DoubleAnimation Animation(double start) => new(start, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        transform.BeginAnimation(TranslateTransform.XProperty, Animation(delta.X)); transform.BeginAnimation(TranslateTransform.YProperty, Animation(delta.Y));
    }
    private static void Stop(TranslateTransform transform) { transform.BeginAnimation(TranslateTransform.XProperty, null); transform.BeginAnimation(TranslateTransform.YProperty, null); }
}
