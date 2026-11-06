using System.Net;
using System.Net.Http;
using System.Reflection;
using QuietIsland.Services;

internal static class WeatherReliabilityChecks
{
    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("polish_weather_deduplicates_places", async () =>
        {
            using var context = Context();
            ReplaceClient(context.Weather, new Fixture(false));
            var places = await context.Weather.FindAsync("Town");
            Require(places.Count == 2, "Geocoding returns duplicate places.");
        });
        await check("polish_fallback_hours_start_at_provider_local_observation", () =>
        {
            using var json = System.Text.Json.JsonDocument.Parse("""
                {"current_condition":[{"temp_C":"3","FeelsLikeC":"1","weatherCode":"122","windspeedKmph":"18","humidity":"80","localObsDateTime":"2026-11-06 06:00 PM"}],"weather":[{"date":"2026-11-06","mintempC":"0","maxtempC":"5","hourly":[{"time":"0","tempC":"0","chanceofrain":"100","weatherCode":"122"},{"time":"1500","tempC":"2","chanceofrain":"100","weatherCode":"122"},{"time":"1800","tempC":"3","chanceofrain":"0","weatherCode":"122"},{"time":"2100","tempC":"2","chanceofrain":"0","weatherCode":"122"}]}]}
                """);
            var parser = typeof(WeatherService).Assembly.GetType("QuietIsland.Services.WeatherFallback")!.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!;
            var forecast = (WeatherForecast)parser.Invoke(null, [json.RootElement])!;
            Require(forecast.Hours.Count == 2 && forecast.Hours[0].Time.EndsWith("18:00"), "Fallback shows midnight or afternoon slots after a local 18:00 observation.");
            return Task.CompletedTask;
        });
        await check("polish_weather_uses_independent_fallback_and_disk_cache", async () =>
        {
            using var context = Context();
            context.Settings.Latitude = 56.84; context.Settings.Longitude = 60.6;
            var fixture = new Fixture(true); ReplaceClient(context.Weather, fixture);
            await context.Weather.RefreshAsync();
            Require(context.Weather.Current?.Temperature == 3 && Math.Abs(context.Weather.Current.Wind - 5) < .01, "Independent provider did not supply current weather in m/s.");
            Require(fixture.FallbackRequests == 1, "Forecast never reached the independent fallback.");
            context.State.Save();
            using var restarted = new IslandContext(context.State.DirectoryPath);
            Require(restarted.Weather.Current?.Temperature == 3, "Restart lost the last forecast.");
            context.Settings.Latitude = 1;
            ReplaceClient(context.Weather, new Offline());
            await context.Weather.RefreshAsync();
            Require(context.Weather.Current == null, "Changing city displays another city's cached forecast.");
        });
    }
    private static IslandContext Context() => new(Path.Combine(Path.GetTempPath(), "island-weather-" + Guid.NewGuid().ToString("N")));
    private static void ReplaceClient(WeatherService weather, HttpMessageHandler handler)
    {
        var field = typeof(WeatherService).GetField("client", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)field.GetValue(weather)!).Dispose(); field.SetValue(weather, new HttpClient(handler));
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new HttpRequestException("Offline fixture");
    }
    private sealed class Fixture(bool forecast) : HttpMessageHandler
    {
        public int FallbackRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (forecast && request.RequestUri!.Host.Contains("open-meteo")) throw new HttpRequestException("Blocked primary fixture");
            var json = """
                {"results":[{"name":"Town","country":"Country","admin1":"A","latitude":1,"longitude":2},{"name":"Town","country":"Country","admin1":"A","latitude":1,"longitude":2},{"name":"Town","country":"Country","admin1":"B","latitude":3,"longitude":4}]}
                """;
            if (forecast)
            {
                FallbackRequests++;
                json = """
                    {"current_condition":[{"temp_C":"3","FeelsLikeC":"1","weatherCode":"122","windspeedKmph":"18","humidity":"80"}],"weather":[{"date":"2026-11-06","mintempC":"0","maxtempC":"5","hourly":[{"time":"1200","tempC":"3","chanceofrain":"10","weatherCode":"122"}]}]}
                    """;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
