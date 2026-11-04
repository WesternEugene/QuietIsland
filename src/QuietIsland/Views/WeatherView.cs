using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class WeatherView : IslandView
{
    private readonly StackPanel forecast = new();
    private readonly TextBox city = Ui.Input("Название города", "");
    private readonly StackPanel places = new();
    private readonly TextBlock status = Ui.Text("", 11, "Muted");
    private WeatherForecast? renderedForecast;
    private string? renderedCity;
    private bool rendered;
    public WeatherView(IslandContext c) : base(c)
    {
        var row = new DockPanel(); var find = Ui.AsyncButton("Найти город", Find, C.Error); DockPanel.SetDock(find, Dock.Right); row.Children.Add(find); row.Children.Add(city); city.Text = C.Settings.WeatherCity;
        Content = Ui.Scroll(Ui.Stack(row, places, forecast, Ui.AsyncButton("Обновить", () => C.Weather.RefreshAsync(), C.Error), status, Ui.Text("Прогноз: Open-Meteo", 10, "Muted")));
        C.Weather.Changed += Changed; Refresh();
    }
    private void Changed() => Dispatcher.InvokeAsync(Refresh);
    private async Task Find()
    {
        if (city.Text.Length < 2) return; status.Text = "Ищу город…"; var results = await C.Weather.FindAsync(city.Text); if (Disposed) return; places.Children.Clear();
        foreach (var place in results) places.Children.Add(Ui.AsyncButton(place.Name + ", " + place.Country, async () => { C.Settings.WeatherCity = place.Name; C.Settings.Latitude = place.Latitude; C.Settings.Longitude = place.Longitude; C.State.Touch(); city.Text = place.Name; places.Children.Clear(); await C.Weather.RefreshAsync(); Refresh(); }, C.Error));
        status.Text = results.Count > 0 ? "Выберите город из списка" : "Город не найден";
    }
    public override void Refresh()
    {
        if (Disposed) return; status.Text = C.Weather.Status;
        if (rendered && ReferenceEquals(renderedForecast, C.Weather.Current) && renderedCity == C.Settings.WeatherCity) return;
        rendered = true; renderedForecast = C.Weather.Current; renderedCity = C.Settings.WeatherCity; forecast.Children.Clear();
        if (C.Weather.Current is not { } w) { forecast.Children.Add(Ui.Empty("☀", "Погода вашего города", "Введите название и выберите город. Температура появится и в свёрнутом острове.")); return; }
        var weatherGlyph = Icons.Weather(w.Code, 50); weatherGlyph.VerticalAlignment = VerticalAlignment.Center; weatherGlyph.Margin = new Thickness(0, 0, 16, 0);
        var temperatureText = w.Temperature.ToString("0") + "°";
        var formatted = new System.Windows.Media.FormattedText(temperatureText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new System.Windows.Media.Typeface("Segoe UI"), 58, Ui.Brush("Text"), 1);
        var outline = formatted.BuildGeometry(new Point()); outline.Freeze();
        // Outlines avoid the large-glyph cache dropping this short reading on some Windows render targets.
        var temperature = new System.Windows.Shapes.Path { Data = outline, Width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace), Height = Math.Ceiling(formatted.Height), Stretch = System.Windows.Media.Stretch.None };
        temperature.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Text");
        System.Windows.Automation.AutomationProperties.SetName(temperature, "Температура: " + temperatureText);
        var reading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 8) }; reading.Children.Add(weatherGlyph); reading.Children.Add(temperature);
        var hero = Ui.Stack(Ui.Text(C.Settings.WeatherCity, 18, bold: true), reading, Ui.Text(WeatherService.Description(w.Code) + $" · ощущается как {w.FeelsLike:0}°", 14), Ui.Text($"Ветер {w.Wind:0} км/ч · влажность {w.Humidity:0}%", 11, "Muted")); forecast.Children.Add(Ui.Card(hero, 20));
        if (w.Hours.Take(2).Any(x => x.Rain >= 60)) forecast.Children.Add(Ui.Card(Ui.Text("☂ В ближайший час вероятны осадки", 13, "Accent")));
        var hours = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var h in w.Hours) { var box = Ui.Stack(Ui.Text(DateTime.Parse(h.Time).ToString("HH:mm"), 10, "Muted"), Icons.Weather(h.Code, 25), Ui.Text($"{h.Temperature:0}°", 14), Ui.Text($"{h.Rain:0}%", 9, "Muted")); box.Width = 54; hours.Children.Add(box); }
        forecast.Children.Add(new ScrollViewer { Content = hours, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.HorizontalOnly, CanContentScroll = false, Margin = new Thickness(0, 4, 0, 18) });
        foreach (var day in w.Days)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(100) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(38) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(Ui.Text(DateTime.Parse(day.Date).ToString("ddd, d MMM", CultureInfo.GetCultureInfo("ru-RU")), 12));
            var icon = Icons.Weather(day.Code, 23); Grid.SetColumn(icon, 1); row.Children.Add(icon);
            var description = Ui.Text(WeatherService.Description(day.Code), 12, "Muted"); Grid.SetColumn(description, 2); row.Children.Add(description);
            var degrees = Ui.Text($"{day.Min:0}° / {day.Max:0}°", 12); Grid.SetColumn(degrees, 3); row.Children.Add(degrees); forecast.Children.Add(Ui.Card(row, 9));
        }
    }
    public override void Dispose() { C.Weather.Changed -= Changed; base.Dispose(); }
}
