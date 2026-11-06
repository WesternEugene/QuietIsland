using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace QuietIsland.Services;

public record WeatherHour(string Time, double Temperature, double Rain, int Code);
public record WeatherDay(string Date, double Min, double Max, int Code);
public record WeatherForecast(double Temperature, double FeelsLike, int Code, double Wind, double Humidity, List<WeatherHour> Hours, List<WeatherDay> Days, DateTimeOffset Updated, int HourStep = 1);
public record WeatherPlace(string Name, string Country, double Latitude, double Longitude, string Region = "")
{
    public string Label => string.Join(", ", new[] { Name, Region, Country }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
}
public sealed class WeatherService : IDisposable
{
    private readonly DesktopState state;
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly SemaphoreSlim findGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private (double?, double?) location;
    private DateTimeOffset lastGeocode;
    public string Provider { get; private set; } = "Open-Meteo";
    public WeatherService(DesktopState state)
    {
        this.state = state; location = (state.Data.Settings.Latitude, state.Data.Settings.Longitude);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuietIsland/1.3 (+https://github.com/WesternEugene/QuietIsland)");
        try
        {
            var path = Path.Combine(state.DirectoryPath, "weather-cache.json");
            if (File.Exists(path) && new FileInfo(path).Length < 262144)
            {
                var cached = JsonSerializer.Deserialize<Cache>(File.ReadAllText(path));
                if (cached != null && (cached.Latitude, cached.Longitude) == location)
                { Current = cached.Forecast; Provider = cached.Provider; Status = "Последний прогноз · " + Current.Updated.ToString("dd.MM HH:mm"); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }
    public WeatherForecast? Current { get; private set; }
    public string Status { get; private set; } = "Выберите город";
    public event Action? Changed;
    public async Task<List<WeatherPlace>> FindAsync(string city, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        await findGate.WaitAsync(token);
        try
        {
            List<WeatherPlace> places;
            try
            {
                var json = await client.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(city.Trim()) + "&count=10&language=ru&format=json", token);
                using var doc = JsonDocument.Parse(json);
                places = doc.RootElement.TryGetProperty("results", out var results) ? results.EnumerateArray().Select(x => new WeatherPlace(x.GetProperty("name").GetString()!, x.TryGetProperty("country", out var country) ? country.GetString()! : "", x.GetProperty("latitude").GetDouble(), x.GetProperty("longitude").GetDouble(), x.TryGetProperty("admin1", out var region) ? region.GetString() ?? "" : "")).ToList() : [];
            }
            catch (Exception e) when (!token.IsCancellationRequested && Recoverable(e))
            {
                var wait = TimeSpan.FromSeconds(1) - (DateTimeOffset.UtcNow - lastGeocode);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
                lastGeocode = DateTimeOffset.UtcNow;
                using var doc = JsonDocument.Parse(await client.GetStringAsync("https://nominatim.openstreetmap.org/search?city=" + Uri.EscapeDataString(city.Trim()) + "&format=jsonv2&addressdetails=1&limit=8&accept-language=ru", token));
                places = doc.RootElement.EnumerateArray().Select(x =>
                {
                    var address = x.GetProperty("address");
                    string Part(string name) => address.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
                    return new WeatherPlace(x.TryGetProperty("name", out var name) ? name.GetString() ?? city : city, Part("country"), double.Parse(x.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture), double.Parse(x.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture), Part("state"));
                }).ToList();
            }
            return places.DistinctBy(x => (x.Name.ToUpperInvariant(), Math.Round(x.Latitude, 2), Math.Round(x.Longitude, 2))).ToList();
        }
        finally { findGate.Release(); }
    }
    public async Task RefreshAsync(CancellationToken token = default)
    {
        var settings = state.Data.Settings;
        if (settings.Latitude == null || settings.Longitude == null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        if (!await refreshGate.WaitAsync(0, token)) return;
        var requestedLocation = (settings.Latitude, settings.Longitude);
        if (location != requestedLocation) { location = requestedLocation; Current = null; Changed?.Invoke(); }
        try
        {
            WeatherForecast forecast;
            try
            {
            var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={settings.Latitude}&longitude={settings.Longitude}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&wind_speed_unit=ms&hourly=temperature_2m,precipitation_probability,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=7");
            var json = await client.GetStringAsync(url, token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement; var current = root.GetProperty("current"); var hourly = root.GetProperty("hourly"); var daily = root.GetProperty("daily");
            var currentTime = current.GetProperty("time").GetString()!;
            var hours = new List<WeatherHour>();
            var times = hourly.GetProperty("time").EnumerateArray().Select(x => x.GetString()!).ToArray();
            for (var i = 0; i < times.Length && hours.Count < 24; i++)
                if (string.CompareOrdinal(times[i], currentTime[..13] + ":00") >= 0) hours.Add(new(times[i], hourly.GetProperty("temperature_2m")[i].GetDouble(), hourly.GetProperty("precipitation_probability")[i].GetDouble(), hourly.GetProperty("weather_code")[i].GetInt32()));
            var days = new List<WeatherDay>();
            for (var i = 0; i < daily.GetProperty("time").GetArrayLength(); i++) days.Add(new(daily.GetProperty("time")[i].GetString()!, daily.GetProperty("temperature_2m_min")[i].GetDouble(), daily.GetProperty("temperature_2m_max")[i].GetDouble(), daily.GetProperty("weather_code")[i].GetInt32()));
            forecast = new(current.GetProperty("temperature_2m").GetDouble(), current.GetProperty("apparent_temperature").GetDouble(), current.GetProperty("weather_code").GetInt32(), current.GetProperty("wind_speed_10m").GetDouble(), current.GetProperty("relative_humidity_2m").GetDouble(), hours, days, DateTimeOffset.Now);
            Provider = "Open-Meteo";
            }
            catch (Exception e) when (!token.IsCancellationRequested && Recoverable(e))
            {
                var point = FormattableString.Invariant($"{requestedLocation.Item1},{requestedLocation.Item2}");
                using var fallback = JsonDocument.Parse(await client.GetStringAsync("https://wttr.in/" + point + "?format=j1&lang=ru", token));
                forecast = WeatherFallback.Parse(fallback.RootElement); Provider = "wttr.in";
            }
            if (requestedLocation != (settings.Latitude, settings.Longitude)) return;
            Current = forecast;
            try
            {
                Directory.CreateDirectory(state.DirectoryPath);
                var path = Path.Combine(state.DirectoryPath, "weather-cache.json");
                await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new Cache(settings.Latitude, settings.Longitude, forecast, Provider)), token);
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Weather cache", e); }
            Status = "Обновлено в " + DateTime.Now.ToString("HH:mm"); Changed?.Invoke();
        }
        catch (Exception e) when (Recoverable(e)) { if (!token.IsCancellationRequested) { Status = "Прогноз недоступен. " + (Current != null ? "Последние данные: " + Current.Updated.ToString("dd.MM HH:mm") : "Не ответили оба сервиса; повторите обновление."); Changed?.Invoke(); } }
        finally
        {
            refreshGate.Release();
            if (!token.IsCancellationRequested && requestedLocation != (settings.Latitude, settings.Longitude)) _ = RefreshAsync();
        }
    }
    private static bool Recoverable(Exception e) => e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException;
    private sealed record Cache(double? Latitude, double? Longitude, WeatherForecast Forecast, string Provider);
    public void Dispose() { lifetime.Cancel(); client.Dispose(); lifetime.Dispose(); }
    public static string Description(int code) => code switch { 0 => "Ясно", 1 => "Преимущественно ясно", 2 => "Переменная облачность", 3 => "Пасмурно", 45 or 48 => "Туман", >= 51 and <= 57 => "Морось", >= 61 and <= 67 => "Дождь", >= 71 and <= 77 => "Снег", >= 80 and <= 82 => "Ливень", 85 or 86 => "Снегопад", >= 95 => "Гроза", _ => "Облачно" };
    public static string Glyph(int code) => code switch { 0 or 1 => "☀", 2 => "⛅", 3 => "☁", >= 51 and <= 67 or >= 80 and <= 82 => "☂", >= 71 and <= 77 or 85 or 86 => "❄", >= 95 => "ϟ", _ => "☁" };
}
