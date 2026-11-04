using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuietIsland.Core;

public static class TextTransforms
{
    public static string Apply(string operation, string text) => operation switch
    {
        "layout" => FixLayout(text), "upper" => text.ToUpperInvariant(), "lower" => text.ToLowerInvariant(),
        "title" => CultureInfo.GetCultureInfo("ru-RU").TextInfo.ToTitleCase(text.ToLowerInvariant()),
        "trim" => Regex.Replace(text.Trim(), @"[ \t]+", " "), "base64" => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
        "base64-decode" => Encoding.UTF8.GetString(Convert.FromBase64String(text)),
        "url" => Uri.EscapeDataString(text), "url-decode" => Uri.UnescapeDataString(text.Replace('+', ' ')),
        "json" => JsonSerializer.Serialize(JsonDocument.Parse(text).RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
        "translit" => Transliterate(text), _ => text
    };
    public static string FixLayout(string text)
    {
        const string en = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?";
        const string ru = "ёйцукенгшщзхъфывапролджэячсмитьбю.ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,";
        var reverse = text.Count(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я') > text.Count(c => char.IsAsciiLetter(c));
        var from = reverse ? ru : en; var to = reverse ? en : ru;
        return string.Concat(text.Select(c => { var i = from.IndexOf(c); return i >= 0 ? to[i] : c; }));
    }
    private static string Transliterate(string text)
    {
        const string alphabet = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        string[] letters = ["a","b","v","g","d","e","yo","zh","z","i","y","k","l","m","n","o","p","r","s","t","u","f","kh","ts","ch","sh","shch","","y","","e","yu","ya"];
        return string.Concat(text.Select(c => { var i = alphabet.IndexOf(char.ToLowerInvariant(c)); if (i < 0) return c.ToString(); return char.IsUpper(c) ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(letters[i]) : letters[i]; }));
    }
}
