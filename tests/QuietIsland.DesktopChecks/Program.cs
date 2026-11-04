using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QuietIsland.Models;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class Program
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<object> Results = [];

    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var (key, color) in new[] { ("Text", "#F5F5F5"), ("Card", "#181818"), ("Surface", "#000000"), ("Hover", "#2A2A2A"), ("Line", "#303030"), ("Muted", "#929292"), ("Accent", "#FF8B81"), ("AccentText", "#111111") })
            app.Resources[key] = new BrushConverter().ConvertFromString(color);
        app.Startup += async (_, _) =>
        {
            try { app.Shutdown(await RunAsync()); }
            catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(1); }
        };
        return app.Run();
    }

    private static async Task<int> RunAsync()
    {
        using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "quiet-island-desktop-checks-" + Guid.NewGuid().ToString("N")));
        var applications = new[] { "AVS Converter", "Visual Studio 2022", "Visual Studio Code", "Oracle VirtualBox", "OBS Studio", "Windows Media Player", "XML Notepad", "XMLReader" }
            .Select(name => new SearchResult(name, Path.Combine(AppContext.BaseDirectory, name + ".lnk"), "Приложение")).ToArray();
        SetApplications(context, applications);
        using var search = new SearchView(context);
        foreach (var (query, expected) in new[] { ("vs", "Visual Studio 2022"), ("vsc", "Visual Studio Code"), ("vb", "Oracle VirtualBox"), ("VB", "Oracle VirtualBox"), ("wmp", "Windows Media Player"), ("xn", "XML Notepad"), ("xr", "XMLReader"), ("visual studio code", "Visual Studio Code"), ("virtual", "Oracle VirtualBox") })
        {
            await CheckAsync("application_query_" + query, async () =>
            {
                await SearchAsync(search, query);
                var list = Field<ListBox>(search, "list");
                Require(list.SelectedItem is ListBoxItem selected && RowName(selected) == expected,
                    $"Expected {expected} first for '{query}', got {string.Join(", ", list.Items.OfType<ListBoxItem>().Where(x => x.IsEnabled).Take(4).Select(RowName))}.");
                Require(RowName((ListBoxItem)list.Items[0]) == "Приложения", "Applications must precede Everything files.");
            });
        }
        await CheckAsync("applications_discovered_before_debounced_query", async () =>
        {
            SetApplications(context, []);
            var pending = SearchAsync(search, "vb");
            await Dispatcher.Yield(DispatcherPriority.Background);
            SetApplications(context, applications);
            await pending;
            var list = Field<ListBox>(search, "list");
            Require(list.SelectedItem is ListBoxItem selected && RowName(selected) == "Oracle VirtualBox", "Application catalog loaded during the debounce did not match vb.");
        });
        await CheckAsync("unrelated_query_keeps_file_and_web_search", async () =>
        {
            await SearchAsync(search, "zzqnonexistent");
            var list = Field<ListBox>(search, "list");
            Require(!list.Items.OfType<ListBoxItem>().Any(x => RowName(x) == "Приложения"), "Unrelated applications matched.");
            Require(list.Items.OfType<ListBoxItem>().Any(x => RowName(x).StartsWith("Искать «zzqnonexistent»")), "Web fallback disappeared.");
        });

        context.Settings.Latitude = 56.84; context.Settings.Longitude = 60.6;
        using var handler = new ForecastHandler(); using var client = new HttpClient(handler);
        Field<HttpClient>(context.Weather, "client").Dispose();
        typeof(WeatherService).GetField("client", Fields)!.SetValue(context.Weather, client);
        await context.Weather.RefreshAsync();
        await CheckAsync("weather_requests_and_preserves_metres_per_second", () =>
        {
            Require(handler.Request?.Query.Split('&').Contains("wind_speed_unit=ms") == true, "Forecast request does not select m/s.");
            Require(context.Weather.Current?.Wind == 4.5, "m/s response was incorrectly converted or failed to parse.");
            return Task.CompletedTask;
        });
        await CheckAsync("weather_displays_metres_per_second", () =>
        {
            using var weather = new WeatherView(context);
            weather.Measure(new Size(744, 430)); weather.Arrange(new Rect(0, 0, 744, 430)); weather.UpdateLayout();
            var labels = Descendants(weather).OfType<TextBlock>().Select(x => x.Text).ToArray();
            Require(labels.Any(x => x.StartsWith("Ветер ") && x.Contains("м/с") && !x.Contains("км/ч")), "Wind label does not display m/s.");
            return Task.CompletedTask;
        });
        Console.WriteLine(JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        return Results.Any(x => (bool)x.GetType().GetProperty("passed")!.GetValue(x)! == false) ? 1 : 0;
    }

    private static async Task CheckAsync(string name, Func<Task> check)
    {
        try { await check(); Results.Add(new { name, passed = true, error = "" }); }
        catch (Exception error) { Results.Add(new { name, passed = false, error = error.Message }); }
    }
    private static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static void SetApplications(IslandContext context, IReadOnlyList<SearchResult> applications) => typeof(IslandContext).GetProperty(nameof(IslandContext.Applications))!.SetValue(context, applications);
    private static async Task SearchAsync(SearchView view, string input)
    {
        Field<TextBox>(view, "query").Text = input;
        await (Task)typeof(SearchView).GetMethod("SearchAsync", Fields)!.Invoke(view, null)!;
    }
    private static string RowName(ListBoxItem row) => row.Content is TextBlock title ? title.Text : ((Grid)row.Content).Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().First().Text;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class ForecastHandler : HttpMessageHandler
    {
        public Uri? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request.RequestUri;
            var wind = Request!.Query.Split('&').Contains("wind_speed_unit=ms") ? 4.5 : 16.2;
            var json = $$$"""
                {"current_units":{"wind_speed_10m":"{{{(wind == 4.5 ? "m/s" : "km/h")}}}"},
                 "current":{"time":"2026-11-04T12:00","temperature_2m":1,"relative_humidity_2m":80,"apparent_temperature":-2,"weather_code":3,"wind_speed_10m":{{{wind.ToString(CultureInfo.InvariantCulture)}}}},
                 "hourly":{"time":["2026-11-04T12:00"],"temperature_2m":[1],"precipitation_probability":[20],"weather_code":[3]},
                 "daily":{"time":["2026-11-04"],"temperature_2m_min":[-3],"temperature_2m_max":[2],"weather_code":[3]}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
