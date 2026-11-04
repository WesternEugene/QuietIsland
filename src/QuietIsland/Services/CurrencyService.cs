using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace QuietIsland.Services;
public static class CurrencyService
{
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static Dictionary<string, decimal>? rates;
    private static DateTime loaded;
    private static string date = "";
    public static bool IsQuery(string text) => Regex.IsMatch(text, @"^\d+(?:[.,]\d+)?\s+[A-Z]{3}\s+(?:to|в)\s+[A-Z]{3}$", RegexOptions.IgnoreCase);
    public static async Task<string?> ConvertAsync(string text, CancellationToken token)
    {
        var match = Regex.Match(text, @"^(\d+(?:[.,]\d+)?)\s+([A-Z]{3})\s+(?:to|в)\s+([A-Z]{3})$", RegexOptions.IgnoreCase); if (!match.Success) return null;
        if (rates == null || loaded.Date != DateTime.Today)
        {
            var bytes = await client.GetByteArrayAsync("https://www.cbr.ru/scripts/XML_daily.asp", token);
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            using var stream = new MemoryStream(bytes); var xml = XDocument.Load(stream); var data = new Dictionary<string, decimal> { ["RUB"] = 1 };
            foreach (var rate in xml.Descendants("Valute"))
            {
                var code = rate.Element("CharCode")?.Value; var nominal = rate.Element("Nominal")?.Value; var value = rate.Element("Value")?.Value;
                if (code != null && decimal.TryParse(nominal, out var n) && n > 0 && decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("ru-RU"), out var v)) data[code] = v / n;
            }
            rates = data; date = xml.Root?.Attribute("Date")?.Value ?? ""; loaded = DateTime.Now;
        }
        var from = match.Groups[2].Value.ToUpperInvariant(); var to = match.Groups[3].Value.ToUpperInvariant();
        if (!rates.TryGetValue(from, out var a) || !rates.TryGetValue(to, out var b)) return null;
        var amount = decimal.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return (amount * a / b).ToString("0.####", CultureInfo.InvariantCulture) + " " + to + " · ЦБ РФ " + date;
    }
}
