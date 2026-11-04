using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace QuietIsland.Services;

public record WeatherHour(string Time, double Temperature, double Rain, int Code);
public record WeatherDay(string Date, double Min, double Max, int Code);
public record WeatherForecast(double Temperature, double FeelsLike, int Code, double Wind, double Humidity, List<WeatherHour> Hours, List<WeatherDay> Days, DateTimeOffset Updated);
public record WeatherPlace(string Name, string Country, double Latitude, double Longitude);
public sealed class WeatherService(DesktopState state)
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
    public WeatherForecast? Current { get; private set; }
    public string Status { get; private set; } = "Выберите город";
    public event Action? Changed;
    public async Task<List<WeatherPlace>> FindAsync(string city, CancellationToken token = default)
    {
        var json = await client.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(city) + "&count=5&language=ru&format=json", token);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("results", out var results) ? results.EnumerateArray().Select(x => new WeatherPlace(x.GetProperty("name").GetString()!, x.TryGetProperty("country", out var country) ? country.GetString()! : "", x.GetProperty("latitude").GetDouble(), x.GetProperty("longitude").GetDouble())).ToList() : [];
    }
    public async Task RefreshAsync(CancellationToken token = default)
    {
        var settings = state.Data.Settings;
        if (settings.Latitude == null || settings.Longitude == null) return;
        try
        {
            var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={settings.Latitude}&longitude={settings.Longitude}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&hourly=temperature_2m,precipitation_probability,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=7");
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
            Current = new(current.GetProperty("temperature_2m").GetDouble(), current.GetProperty("apparent_temperature").GetDouble(), current.GetProperty("weather_code").GetInt32(), current.GetProperty("wind_speed_10m").GetDouble(), current.GetProperty("relative_humidity_2m").GetDouble(), hours, days, DateTimeOffset.Now);
            Status = "Обновлено в " + DateTime.Now.ToString("HH:mm"); Changed?.Invoke();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { Status = "Прогноз недоступен. " + (Current != null ? "Показаны последние данные." : "Проверьте подключение."); Changed?.Invoke(); }
    }
    public static string Description(int code) => code switch { 0 => "Ясно", 1 => "Преимущественно ясно", 2 => "Переменная облачность", 3 => "Пасмурно", 45 or 48 => "Туман", >= 51 and <= 57 => "Морось", >= 61 and <= 67 => "Дождь", >= 71 and <= 77 => "Снег", >= 80 and <= 82 => "Ливень", 85 or 86 => "Снегопад", >= 95 => "Гроза", _ => "Облачно" };
    public static string Glyph(int code) => code switch { 0 or 1 => "☀", 2 => "⛅", 3 => "☁", >= 51 and <= 67 or >= 80 and <= 82 => "☂", >= 71 and <= 77 or 85 or 86 => "❄", >= 95 => "ϟ", _ => "☁" };
}
