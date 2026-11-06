using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuietIsland.Services;

namespace QuietIsland.Controls;

public sealed class LyricsPanel : Grid, IDisposable
{
    private readonly TextBlock caption = Ui.Text("", 10, "Muted");
    private readonly StackPanel lines = new();
    private readonly ScrollViewer scroll;
    private readonly StackPanel message = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = Ui.Text("", 14, "Muted");
    private readonly Button retry;
    private readonly bool compact;
    private readonly List<TextBlock> rows = [];
    private LyricsDocument? shown;
    private bool disposed;
    internal int ActiveIndex { get; private set; } = -2;
    public event Action? RetryRequested;
    public event Action<double>? SeekRequested;
    private static readonly DependencyProperty ScrollOffsetProperty = DependencyProperty.Register("ScrollOffset", typeof(double), typeof(LyricsPanel), new PropertyMetadata(0d, (target, args) => ((LyricsPanel)target).scroll.ScrollToVerticalOffset((double)args.NewValue)));

    public LyricsPanel(bool compact = false)
    {
        this.compact = compact;
        AutomationProperties.SetName(this, "Текст песни");
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new());
        caption.Margin = new Thickness(0, 6, 0, 0); caption.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; Children.Add(caption);
        scroll = new ScrollViewer { Content = lines, CanContentScroll = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = compact ? new Thickness(0, 4, 6, 12) : new Thickness(0, 14, 8, 24) };
        scroll.OpacityMask = new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Colors.White, .08), new(Colors.White, .86), new(Colors.Transparent, 1) }, new Point(0, 0), new Point(0, 1));
        Grid.SetRow(scroll, 1); Children.Add(scroll);
        status.FontSize = compact ? 12 : 14; status.LineHeight = compact ? 18 : 22; status.TextWrapping = TextWrapping.Wrap; message.Children.Add(status);
        retry = Ui.Button("Повторить", () => RetryRequested?.Invoke()); retry.HorizontalAlignment = HorizontalAlignment.Left; retry.Margin = new Thickness(0, compact ? 6 : 12, 0, 0); if (compact) retry.Padding = new Thickness(8, 4, 8, 4); message.Children.Add(retry);
        Grid.SetRow(message, 1); Children.Add(message);
        ClipToBounds = true;
        scroll.SizeChanged += (_, _) => QueueCenter(false);
    }

    public void Update(LyricsDocument document, double position)
    {
        if (disposed) return;
        if (!ReferenceEquals(shown, document)) { shown = document; Build(document); ActiveIndex = -2; }
        if (document.Status != LyricsStatus.Synced) return;
        var index = document.ActiveLineAt(position);
        if (index == ActiveIndex) return;
        var first = ActiveIndex == -2; ActiveIndex = index;
        for (var i = 0; i < rows.Count; i++)
        {
            var active = i == index; var distance = index < 0 ? i + 1 : Math.Abs(i - index);
            var opacity = active ? 1d : distance == 1 ? .7 : distance == 2 ? .4 : .22;
            var row = rows[i]; row.FontSize = compact ? (active ? 16 : 12) : (active ? 22 : 16); row.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            row.SetResourceReference(TextBlock.ForegroundProperty, active ? "Text" : "Muted");
            if (first) { row.BeginAnimation(OpacityProperty, null); row.Opacity = opacity; }
            else if (Math.Abs(row.Opacity - opacity) > .01) row.BeginAnimation(OpacityProperty, new DoubleAnimation(row.Opacity, opacity, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }
        QueueCenter(!first);
    }

    private void Build(LyricsDocument document)
    {
        foreach (var row in rows) row.BeginAnimation(OpacityProperty, null);
        rows.Clear(); lines.Children.Clear(); BeginAnimation(ScrollOffsetProperty, null); SetValue(ScrollOffsetProperty, 0d);
        var hasText = document.Status is LyricsStatus.Synced or LyricsStatus.Plain;
        scroll.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed; message.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        caption.Text = compact ? "" : document.Status == LyricsStatus.Synced ? "СИНХРОННЫЙ ТЕКСТ · LRCLIB" : document.Status == LyricsStatus.Plain ? "ТЕКСТ БЕЗ СИНХРОНИЗАЦИИ · LRCLIB" : "ТЕКСТ ПЕСНИ";
        retry.Visibility = document.Status is LyricsStatus.NotFound or LyricsStatus.Unavailable ? Visibility.Visible : Visibility.Collapsed;
        status.Text = document.Status switch
        {
            LyricsStatus.Loading => "Ищу текст песни…",
            LyricsStatus.NotFound => compact ? "Текст не найден." : "Для этого трека текст пока не найден.",
            LyricsStatus.Unavailable => compact ? "Текст недоступен." : "Не удалось загрузить текст.\nПроверьте подключение и попробуйте снова.",
            LyricsStatus.Instrumental => "Инструментальная композиция",
            _ => "Выберите трек в плеере"
        };
        if (document.Status == LyricsStatus.Synced)
            foreach (var line in document.Lines)
            {
                var row = Ui.Text(line.Text.Length > 0 ? line.Text : "♪", compact ? 12 : 16, "Muted"); row.MinHeight = compact ? 20 : 26; row.TextWrapping = TextWrapping.Wrap;
                var button = Ui.Button("", () => SeekRequested?.Invoke(line.Time), tip: "Перемотать на " + Ui.Time(line.Time));
                button.Content = row; button.Background = Brushes.Transparent; button.Margin = new Thickness(0, 0, 0, compact ? 6 : 10); button.Padding = new Thickness(2, 0, 2, 0); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                AutomationProperties.SetName(button, "Перемотать на " + Ui.Time(line.Time) + " · " + line.Text);
                lines.Children.Add(button); rows.Add(row);
            }
        else if (document.Status == LyricsStatus.Plain) { var text = Ui.Text(document.PlainText, compact ? 13 : 17); text.LineHeight = compact ? 20 : 27; text.TextWrapping = TextWrapping.Wrap; lines.Children.Add(text); }
    }

    private void QueueCenter(bool animate)
    {
        if (disposed || shown?.Status != LyricsStatus.Synced) return;
        var expected = ActiveIndex;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (disposed || ActiveIndex != expected || shown?.Status != LyricsStatus.Synced) return;
            UpdateLayout();
            var target = expected < 0 || expected >= rows.Count ? 0 : Math.Clamp(rows[expected].TranslatePoint(new Point(), lines).Y + rows[expected].ActualHeight / 2 - scroll.ViewportHeight * .42, 0, scroll.ScrollableHeight);
            if (animate) BeginAnimation(ScrollOffsetProperty, new DoubleAnimation(scroll.VerticalOffset, target, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else { BeginAnimation(ScrollOffsetProperty, null); SetValue(ScrollOffsetProperty, target); }
        }));
    }

    public void Dispose()
    {
        disposed = true; BeginAnimation(ScrollOffsetProperty, null);
        foreach (var row in rows) row.BeginAnimation(OpacityProperty, null);
    }
}
