using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class WeatherView : IslandView
{
    private readonly StackPanel forecast = new();
    private readonly TextBox city = Ui.Input("Название города", "");
    private readonly StackPanel places = new();
    private readonly TextBlock status = Ui.Text("", 10, "Muted");
    private WeatherForecast? renderedForecast;
    private string? renderedCity;
    private bool rendered;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBlock provider = Ui.Text("", 9, "Muted");
    public WeatherView(IslandContext c) : base(c)
    {
        var row = new DockPanel(); var find = Ui.AsyncButton("Найти город", Find, C.Error); find.Margin = new Thickness(0); find.MinHeight = 34; find.Padding = new Thickness(10, 6, 10, 6); DockPanel.SetDock(find, Dock.Right); row.Children.Add(find);
        city.Margin = new Thickness(0, 0, 8, 0); city.Height = 34; city.Padding = new Thickness(10, 6, 10, 6); city.Text = C.Settings.WeatherCity; row.Children.Add(city);
        forecast.Margin = new Thickness(0, 8, 0, 0);
        var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var refresh = Ui.AsyncButton("Обновить", () => C.Weather.RefreshAsync(), C.Error); refresh.Margin = new Thickness(12, 0, 0, 0); refresh.Padding = new Thickness(9, 4, 9, 4); refresh.FontSize = 11; DockPanel.SetDock(refresh, Dock.Right); footer.Children.Add(refresh);
        provider.VerticalAlignment = VerticalAlignment.Center; provider.Margin = new Thickness(12, 0, 0, 0); DockPanel.SetDock(provider, Dock.Right); footer.Children.Add(provider);
        status.VerticalAlignment = VerticalAlignment.Center; status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis; footer.Children.Add(status);
        Content = Ui.Scroll(Ui.Stack(row, places, forecast, footer));
        C.Weather.Changed += Changed; Refresh();
        city.PreviewKeyDown += async (_, e) => { if (e.Key != System.Windows.Input.Key.Enter) return; e.Handled = true; try { await Find(); } catch (OperationCanceledException) { } catch (Exception error) { C.Error(error); } };
    }
    private void Changed() => Dispatcher.InvokeAsync(Refresh);
    private async Task Find()
    {
        if (city.Text.Length < 2) return; status.Text = "Ищу город…"; var results = await C.Weather.FindAsync(city.Text, lifetime.Token); if (Disposed) return; places.Children.Clear();
        foreach (var place in results) places.Children.Add(Ui.AsyncButton(place.Label, async () => { C.Settings.WeatherCity = place.Name; C.Settings.Latitude = place.Latitude; C.Settings.Longitude = place.Longitude; C.State.Touch(); city.Text = place.Name; places.Children.Clear(); await C.Weather.RefreshAsync(lifetime.Token); Refresh(); }, C.Error));
        status.Text = results.Count > 0 ? "Выберите город из списка" : "Город не найден";
    }
    public override void Refresh()
    {
        if (Disposed) return; status.Text = C.Weather.Status; status.ToolTip = C.Weather.Status; provider.Text = C.Weather.Provider;
        if (rendered && ReferenceEquals(renderedForecast, C.Weather.Current) && renderedCity == C.Settings.WeatherCity) return;
        rendered = true; renderedForecast = C.Weather.Current; renderedCity = C.Settings.WeatherCity; forecast.Children.Clear();
        if (C.Weather.Current is not { } w) { forecast.Children.Add(Ui.Empty("☀", "Погода вашего города", "Введите название и выберите город. Температура появится и в свёрнутом острове.")); return; }
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new GridLength(226) }); layout.ColumnDefinitions.Add(new() { Width = new GridLength(12) }); layout.ColumnDefinitions.Add(new());
        layout.Children.Add(CurrentCard(w));
        var upcoming = new StackPanel(); Grid.SetColumn(upcoming, 2); layout.Children.Add(upcoming);
        var hours = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var h in w.Hours)
        {
            var glyph = Icons.Weather(h.Code, 22); glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.Margin = new Thickness(0, 3, 0, 3);
            var rain = CenteredText($"{h.Rain:0}%", 10, "Muted"); rain.ToolTip = "Вероятность осадков";
            var box = Ui.Stack(CenteredText(DateTime.Parse(h.Time).ToString("HH:mm"), 10, "Muted"), glyph, CenteredText($"{h.Temperature:0}°", 13, bold: true), rain); box.Width = 60; box.Margin = new Thickness(0, 0, 8, 0); hours.Children.Add(box);
        }
        upcoming.Children.Add(ForecastCard(w.HourStep > 1 ? $"По часам · шаг {w.HourStep} ч" : "По часам", hours));
        var days = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var day in w.Days)
        {
            var glyph = Icons.Weather(day.Code, 22); glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.Margin = new Thickness(0, 3, 0, 3);
            var box = Ui.Stack(CenteredText(DateTime.Parse(day.Date).ToString("ddd, d MMM", CultureInfo.GetCultureInfo("ru-RU")), 10, "Muted"), glyph, CenteredText($"{day.Min:0}° / {day.Max:0}°", 12, bold: true));
            box.Width = 90; box.Margin = new Thickness(0, 0, 8, 0); box.ToolTip = WeatherService.Description(day.Code); days.Children.Add(box);
        }
        var dailyCard = ForecastCard("По дням", days); dailyCard.Margin = new Thickness(0); upcoming.Children.Add(dailyCard);
        forecast.Children.Add(layout);
    }
    private Border CurrentCard(WeatherForecast w)
    {
        var cityName = HeroText(C.Settings.WeatherCity, 16, true); cityName.TextTrimming = TextTrimming.CharacterEllipsis; cityName.TextWrapping = TextWrapping.NoWrap; cityName.Margin = new Thickness(0, 3, 0, 0);
        var temperatureText = w.Temperature.ToString("0") + "°";
        var formatted = new FormattedText(temperatureText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 44, Brushes.White, 1);
        var outline = formatted.BuildGeometry(new Point()); outline.Freeze();
        // Outlines keep the large temperature reliable on Windows render targets.
        var temperature = new System.Windows.Shapes.Path { Data = outline, Fill = Brushes.White, Width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace), Height = Math.Ceiling(formatted.Height), Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(temperature, "Температура: " + temperatureText);
        var reading = new Grid { Margin = new Thickness(0, 6, 0, 6) }; reading.ColumnDefinitions.Add(new()); reading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); reading.Children.Add(temperature);
        var glyph = Icons.Weather(w.Code, 38); glyph.VerticalAlignment = VerticalAlignment.Center; glyph.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(glyph, 1); reading.Children.Add(glyph);
        var feelsLike = HeroText($"Ощущается как {w.FeelsLike:0}°", 11, muted: true); feelsLike.Margin = new Thickness(0, 3, 0, 0);
        var wind = HeroText($"Ветер {w.Wind:0.#} м/с", 11, muted: true); wind.Margin = new Thickness(0, 11, 0, 0);
        var humidity = HeroText($"Влажность {w.Humidity:0}%", 11, muted: true); humidity.Margin = new Thickness(0, 3, 0, 0);
        var content = Ui.Stack(HeroText("Сейчас", 10, muted: true), cityName, reading, HeroText(WeatherService.Description(w.Code), 13, true), feelsLike, wind, humidity);
        if (w.HourStep <= 1 && w.Hours.Take(2).Any(x => x.Rain >= 60))
        {
            var rain = HeroText("В ближайший час вероятны осадки", 10); rain.Margin = new Thickness(0, 8, 0, 0); content.Children.Add(rain);
        }
        var card = new Border { Background = WeatherGradient(w.Code), CornerRadius = new CornerRadius(17), Padding = new Thickness(14), Child = content };
        AutomationProperties.SetName(card, "Текущая погода"); return card;
    }
    private static Border ForecastCard(string heading, StackPanel items)
    {
        var title = Ui.Text(heading, 12, bold: true); title.Margin = new Thickness(0, 0, 0, 4);
        // Let WPF reserve the actual scrollbar height, including the system
        // template's track row; a fixed 85px viewport clipped the rain labels.
        var scroll = new ScrollViewer { Content = items, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.HorizontalOnly, CanContentScroll = false };
        AutomationProperties.SetName(scroll, heading); var card = Ui.Card(Ui.Stack(title, scroll), 8); card.Margin = new Thickness(0, 0, 0, 6); return card;
    }
    private static TextBlock CenteredText(string text, double size, string color = "Text", bool bold = false)
    {
        var label = Ui.Text(text, size, color, bold); label.HorizontalAlignment = HorizontalAlignment.Center; label.TextAlignment = TextAlignment.Center; label.TextWrapping = TextWrapping.NoWrap; return label;
    }
    private static TextBlock HeroText(string text, double size, bool bold = false, bool muted = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Brushes.White, Opacity = muted ? .76 : 1, TextWrapping = TextWrapping.Wrap
    };
    private static Brush WeatherGradient(int code)
    {
        var (start, end) = code switch
        {
            0 or 1 => ("#76552E", "#33495A"),
            2 => ("#5A6672", "#344553"),
            45 or 48 => ("#626B74", "#35434E"),
            >= 51 and <= 67 or >= 80 and <= 82 => ("#385C78", "#22354E"),
            >= 71 and <= 77 or 85 or 86 => ("#55788F", "#2D435B"),
            >= 95 => ("#58466E", "#2D314B"),
            _ => ("#515C6A", "#2A3442")
        };
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(start), (Color)ColorConverter.ConvertFromString(end), new Point(0, 0), new Point(1, 1)); brush.Freeze(); return brush;
    }
    public override void Dispose() { lifetime.Cancel(); lifetime.Dispose(); C.Weather.Changed -= Changed; base.Dispose(); }
}
