using System.Globalization;
using System.Text.Json;

namespace QuietIsland.Services;

internal static class WeatherFallback
{
    internal static WeatherForecast Parse(JsonElement root)
    {
        static double Number(JsonElement node, string key) => double.Parse(node.GetProperty(key).ToString(), CultureInfo.InvariantCulture);
        static int Code(JsonElement node) => (int)Number(node, "weatherCode") switch
        {
            113 => 0, 116 => 2, 119 or 122 => 3, 143 or 248 or 260 => 45,
            176 or 263 or 266 => 51, 179 or 182 or 185 or 227 or 230 or 281 or 284 or 311 or 314 or 317 or 320 or 323 or 326 or 329 or 332 or 335 or 338 or 350 or 362 or 365 or 368 or 371 or 374 or 377 => 73,
            200 or 386 or 389 or 392 or 395 => 95, 293 or 296 or 299 or 302 or 305 or 308 or 353 or 356 or 359 => 63, _ => 3
        };
        var current = root.GetProperty("current_condition")[0];
        var localTimeKnown = current.TryGetProperty("localObsDateTime", out var observed) && DateTime.TryParse(observed.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _);
        var localTime = localTimeKnown ? DateTime.Parse(observed.GetString()!, CultureInfo.InvariantCulture) : DateTime.MaxValue;
        var firstHour = localTimeKnown ? localTime.Date.AddHours(localTime.Hour) : DateTime.MaxValue;
        var hours = new List<WeatherHour>(); var days = new List<WeatherDay>();
        foreach (var day in root.GetProperty("weather").EnumerateArray())
        {
            var date = day.GetProperty("date").GetString()!;
            var hourly = day.GetProperty("hourly").EnumerateArray().ToArray();
            days.Add(new(date, Number(day, "mintempC"), Number(day, "maxtempC"), hourly.Length == 0 ? Code(current) : Code(hourly[hourly.Length / 2])));
            foreach (var hour in hourly)
            {
                var time = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddHours(Number(hour, "time") / 100);
                if (time >= firstHour) hours.Add(new(time.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), Number(hour, "tempC"), Number(hour, "chanceofrain"), Code(hour)));
            }
        }
        return new(Number(current, "temp_C"), Number(current, "FeelsLikeC"), Code(current), Number(current, "windspeedKmph") / 3.6, Number(current, "humidity"), hours.OrderBy(x => x.Time, StringComparer.Ordinal).Take(24).ToList(), days, DateTimeOffset.Now, HourStep: 3);
    }
}
