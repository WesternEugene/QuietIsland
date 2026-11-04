using System.Globalization;
using System.Text.RegularExpressions;

namespace QuietIsland.Core;

public static class AnimationPolicy
{
    public static bool ShouldAnimate(bool hasMedia, bool isPlaying) => hasMedia && isPlaying;
}

public enum CommandKind { Search, Calculate, Timer, Reminder, Web, Ai, Shell, System }
public record ParsedCommand(CommandKind Kind, string Text, string? Result = null, TimeSpan Duration = default);

public static class CommandParser
{
    public static ParsedCommand Parse(string input)
    {
        var text = input.Trim();
        if (text.StartsWith('>')) return new(CommandKind.Shell, text[1..].Trim());
        if (text.StartsWith('?')) return new(CommandKind.Ai, text[1..].Trim());
        if (text.StartsWith("web ", StringComparison.OrdinalIgnoreCase)) return new(CommandKind.Web, text[4..]);
        if (Regex.IsMatch(text, @"^(таймер|timer)\b", RegexOptions.IgnoreCase))
        {
            var duration = ParseDuration(text);
            if (duration > TimeSpan.Zero && duration <= TimeSpan.FromDays(30)) return new(CommandKind.Timer, text, Duration: duration);
        }
        var reminder = Regex.Match(text, @"^(?:напомни(?:ть)?|remind(?: me)?)\s+(.+)$", RegexOptions.IgnoreCase);
        if (reminder.Success) return new(CommandKind.Reminder, reminder.Groups[1].Value);
        if (Regex.IsMatch(text, @"^(wifi|wi-fi|bluetooth|vpn|happ|theme|тема|lock|блокировка|settings|настройки|звук|sound)$", RegexOptions.IgnoreCase))
            return new(CommandKind.System, text.ToLowerInvariant());
        var unit = UnitConverter.Convert(text);
        if (unit != null) return new(CommandKind.Calculate, text, unit);
        if (text.Any(char.IsDigit) && Regex.IsMatch(text, @"^[\d\s.,+*/%^()a-z−-]+$", RegexOptions.IgnoreCase))
        {
            var result = Calculator.Evaluate(text);
            if (result != null) return new(CommandKind.Calculate, text, result);
        }
        return new(CommandKind.Search, text);
    }

    public static TimeSpan ParseDuration(string text)
    {
        double seconds = 0;
        foreach (Match match in Regex.Matches(text, @"(\d+(?:[.,]\d+)?)\s*(час(?:а|ов)?|ч\b|hours?|hrs?|h\b|мин(?:ут(?:а|ы)?)?|м\b|minutes?|mins?|m\b|сек(?:унд(?:а|ы)?)?|с\b|seconds?|secs?|s\b)", RegexOptions.IgnoreCase))
        {
            if (!double.TryParse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture, out var n)) continue;
            var unit = match.Groups[2].Value.ToLowerInvariant();
            seconds += n * (unit.StartsWith('ч') || unit.StartsWith('h') ? 3600 : unit.StartsWith('м') || unit.StartsWith('m') ? 60 : 1);
        }
        if (seconds == 0 && double.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var minutes)) seconds = minutes * 60;
        return double.IsFinite(seconds) && seconds > 0 && seconds <= TimeSpan.FromDays(365).TotalSeconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }
}

public static class Calculator
{
    public static string? Evaluate(string expression)
    {
        if (expression.Length > 1024) return null;
        try
        {
            var parser = new Parser(expression.Replace(',', '.').Replace('−', '-'));
            var result = parser.Expression();
            parser.White();
            return parser.End && double.IsFinite(result) ? result.ToString("G12", CultureInfo.InvariantCulture) : null;
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException) { return null; }
    }

    private sealed class Parser(string text)
    {
        private int p, depth;
        public bool End => p == text.Length;
        public void White() { while (p < text.Length && char.IsWhiteSpace(text[p])) p++; }
        private bool Eat(char c) { White(); if (p >= text.Length || text[p] != c) return false; p++; return true; }
        public double Expression()
        {
            if (++depth > 48) throw new FormatException();
            var n = Product();
            while (true) { if (Eat('+')) n += Product(); else if (Eat('-')) n -= Product(); else break; }
            depth--;
            return n;
        }
        private double Product()
        {
            var n = Unary();
            while (true) { if (Eat('*')) n *= Unary(); else if (Eat('/')) n /= Unary(); else break; }
            return n;
        }
        private double Unary()
        {
            if (++depth > 48) throw new FormatException();
            double n;
            if (Eat('+')) n = Unary();
            else if (Eat('-')) n = -Unary();
            else { n = Primary(); while (Eat('%')) n /= 100; if (Eat('^')) n = Math.Pow(n, Unary()); }
            depth--;
            return n;
        }
        private double Primary()
        {
            if (Eat('(')) { var n = Expression(); if (!Eat(')')) throw new FormatException(); return n; }
            White();
            var start = p;
            while (p < text.Length && char.IsLetter(text[p])) p++;
            if (p > start)
            {
                var name = text[start..p].ToLowerInvariant();
                if (name == "pi") return Math.PI;
                if (name == "e") return Math.E;
                if (!Eat('(')) throw new FormatException();
                var value = Expression();
                if (!Eat(')')) throw new FormatException();
                return name switch
                {
                    "sqrt" => Math.Sqrt(value), "abs" => Math.Abs(value), "sin" => Math.Sin(value), "cos" => Math.Cos(value),
                    "tan" => Math.Tan(value), "log" => Math.Log10(value), "ln" => Math.Log(value), "round" => Math.Round(value),
                    "floor" => Math.Floor(value), "ceil" => Math.Ceiling(value), _ => throw new FormatException()
                };
            }
            while (p < text.Length && (char.IsDigit(text[p]) || text[p] == '.')) p++;
            if (start == p) throw new FormatException();
            return double.Parse(text[start..p], CultureInfo.InvariantCulture);
        }
    }
}

public static class UnitConverter
{
    private sealed record Unit(string Group, double Factor, string Symbol, string RussianSymbol,
        string? RussianFew = null, string? RussianMany = null);

    private sealed record UnitCatalog(Dictionary<string, List<Unit>> Names, Dictionary<string, Unit> ExactSymbols);

    private static readonly UnitCatalog Units = CreateUnits();

    private static UnitCatalog CreateUnits()
    {
        var units = new UnitCatalog(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.Ordinal));
        void Add(string group, double factor, string symbol, string russianSymbol, string aliases,
            string? russianFew = null, string? russianMany = null, string? exactSymbols = null)
        {
            var unit = new Unit(group, factor, symbol, russianSymbol, russianFew, russianMany);
            var names = exactSymbols == null ? aliases + "|" + symbol + "|" + russianSymbol : aliases;
            foreach (var alias in names.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var key = NormalizeName(alias);
                if (!units.Names.TryGetValue(key, out var matches)) units.Names.Add(key, matches = []);
                if (!matches.Contains(unit)) matches.Add(unit);
            }
            if (exactSymbols != null)
                foreach (var alias in exactSymbols.Split('|')) units.ExactSymbols.Add(NormalizeName(alias), unit);
        }

        // The endings cover the case forms of new Russian names without stem-based matching.
        static string Forms(string stem, string endings = "|а|ы|ов|у|ом|е|ам|ами|ах") =>
            string.Join('|', endings.Split('|').Select(ending => stem + ending));

        Add("length", .001, "mm", "мм", "mm|мм|millimeter|millimeters|millimetre|millimetres|миллиметр|миллиметра|миллиметры|миллиметров|миллиметру|миллиметром|миллиметре|миллиметрам|миллиметрами|миллиметрах");
        Add("length", .01, "cm", "см", "cm|см|centimeter|centimeters|centimetre|centimetres|сантиметр|сантиметра|сантиметры|сантиметров|сантиметру|сантиметром|сантиметре|сантиметрам|сантиметрами|сантиметрах");
        Add("length", 1, "m", "м", "m|м|meter|meters|metre|metres|метр|метра|метры|метров|метру|метром|метре|метрам|метрами|метрах");
        Add("length", 1000, "km", "км", "km|км|kilometer|kilometers|kilometre|kilometres|километр|километра|километры|километров|километру|километром|километре|километрам|километрами|километрах");
        Add("length", .0254, "inch", "дюйм", "in|inch|inches|дюйм|дюйма|дюймы|дюймов|дюйму|дюймом|дюйме|дюймам|дюймами|дюймах", "дюйма", "дюймов");
        Add("length", .3048, "ft", "фут", "ft|foot|feet|фут|фута|футы|футов|футу|футом|футе|футам|футами|футах", "фута", "футов");
        Add("length", 1609.344, "mile", "миля", "mi|mile|miles|миля|мили|миль|милю|миле|милей|милям|милями|милях", "мили", "миль");

        Add("mass", 1, "g", "г", "g|г|gram|grams|грамм|грамма|граммы|граммов|грамму|граммом|грамме|граммам|граммами|граммах");
        Add("mass", 1000, "kg", "кг", "kg|кг|kilogram|kilograms|килограмм|килограмма|килограммы|килограммов|килограмму|килограммом|килограмме|килограммам|килограммами|килограммах");
        Add("mass", 453.59237, "lb", "фунт", "lb|lbs|pound|pounds|фунт|фунта|фунты|фунтов|фунту|фунтом|фунте|фунтам|фунтами|фунтах", "фунта", "фунтов");
        Add("mass", 28.349523125, "oz", "унция", "oz|ounce|ounces|унция|унции|унций|унцию|унцией|унциям|унциями|унциях", "унции", "унций");

        Add("data", 1, "B", "Б", "b|б|byte|bytes|байт|байта|байты|байтов|байту|байтом|байте|байтам|байтами|байтах");
        Add("data", 1000, "KB", "КБ", "kb|кб|kilobyte|kilobytes|килобайт|килобайта|килобайты|килобайтов|килобайту|килобайтом|килобайте|килобайтам|килобайтами|килобайтах");
        Add("data", 1e6, "MB", "МБ", "mb|мб|megabyte|megabytes|мегабайт|мегабайта|мегабайты|мегабайтов|мегабайту|мегабайтом|мегабайте|мегабайтам|мегабайтами|мегабайтах");
        Add("data", 1e9, "GB", "ГБ", "gb|гб|gigabyte|gigabytes|гигабайт|гигабайта|гигабайты|гигабайтов|гигабайту|гигабайтом|гигабайте|гигабайтам|гигабайтами|гигабайтах");
        Add("data", 1024, "KiB", "КиБ", "kib|киб|kibibyte|kibibytes|кибибайт|кибибайта|кибибайты|кибибайтов|кибибайту|кибибайтом|кибибайте|кибибайтам|кибибайтами|кибибайтах");
        Add("data", 1048576, "MiB", "МиБ", "mib|миб|mebibyte|mebibytes|мебибайт|мебибайта|мебибайты|мебибайтов|мебибайту|мебибайтом|мебибайте|мебибайтам|мебибайтами|мебибайтах");
        Add("data", 1073741824, "GiB", "ГиБ", "gib|гиб|gibibyte|gibibytes|гибибайт|гибибайта|гибибайты|гибибайтов|гибибайту|гибибайтом|гибибайте|гибибайтам|гибибайтами|гибибайтах");

        Add("temperature", 1, "C", "°C", "c|с|celsius|цельсий|цельсия|цельсию|цельсием|цельсии");
        Add("temperature", 1, "F", "°F", "f|ф|fahrenheit|фаренгейт|фаренгейта|фаренгейту|фаренгейтом|фаренгейте");
        Add("temperature", 1, "K", "К", "k|к|kelvin|kelvins|кельвин|кельвина|кельвины|кельвинов|кельвину|кельвином|кельвине|кельвинам|кельвинами|кельвинах");

        Add("length", 1e-6, "µm", "мкм", "um|мкм|micrometer|micrometers|micrometre|micrometres|micron|microns|" + Forms("микрометр") + "|" + Forms("микрон"));
        Add("length", 1e-9, "nm", "нм", "nm|нм|nanometer|nanometers|nanometre|nanometres|" + Forms("нанометр"));
        Add("length", .1, "dm", "дм", "dm|дм|decimeter|decimeters|decimetre|decimetres|" + Forms("дециметр"));
        Add("length", .9144, "yd", "ярд", "yd|yard|yards|" + Forms("ярд"), "ярда", "ярдов");
        Add("length", 1852, "nmi", "мор. миля", "nmi|nautical mile|nautical miles|морская миля|морской мили|морские мили|морских миль|морскую милю|морской миле|морскими милями|морских милях", "мор. мили", "мор. миль");

        Add("mass", .001, "mg", "мг", "mg|мг|milligram|milligrams|" + Forms("миллиграмм"));
        Add("mass", 1e-6, "µg", "мкг", "ug|мкг|microgram|micrograms|" + Forms("микрограмм"));
        Add("mass", 1e6, "t", "т", "t|т|tonne|tonnes|metric ton|metric tons|" + Forms("тонн", "а|ы||у|ой|е|ам|ами|ах"));
        Add("mass", 907184.74, "US ton", "кор. т", "ton|tons|us ton|us tons|short ton|short tons|короткая тонна|короткие тонны|коротких тонн");
        Add("mass", 1016046.9088, "UK ton", "дл. т", "uk ton|uk tons|long ton|long tons|imperial ton|imperial tons|длинная тонна|длинные тонны|длинных тонн");
        Add("mass", 6350.29318, "st", "стоун", "st|stone|stones|" + Forms("стоун"), "стоуна", "стоунов");

        Add("data", 1e12, "TB", "ТБ", "tb|тб|terabyte|terabytes|" + Forms("терабайт"));
        Add("data", 1e15, "PB", "ПБ", "pb|пб|petabyte|petabytes|" + Forms("петабайт"));
        Add("data", 1099511627776, "TiB", "ТиБ", "tib|тиб|tebibyte|tebibytes|" + Forms("тебибайт"));
        Add("data", 1125899906842624, "PiB", "ПиБ", "pib|пиб|pebibyte|pebibytes|" + Forms("пебибайт"));
        // Keep legacy B/b byte aliases; bit names and kbit/Mbit symbols are unambiguous.
        Add("data", .125, "bit", "бит", "bit|bits|" + Forms("бит"));
        Add("data", 125, "kbit", "кбит", "kbit|кбит|kilobit|kilobits|" + Forms("килобит"));
        Add("data", 125000, "Mbit", "Мбит", "mbit|мбит|megabit|megabits|" + Forms("мегабит"));
        Add("data", 125000000, "Gbit", "Гбит", "gbit|гбит|gigabit|gigabits|" + Forms("гигабит"));
        Add("dataRate", .125, "bit/s", "бит/с", "bps");
        Add("dataRate", 125, "kbit/s", "кбит/с", "kbps");
        Add("dataRate", 125000, "Mbit/s", "Мбит/с", "mbps");
        Add("dataRate", 125000000, "Gbit/s", "Гбит/с", "gbps");

        Add("time", 1e-9, "ns", "нс", "ns|нс|nanosecond|nanoseconds|" + Forms("наносекунд", "а|ы||у|ой|е|ам|ами|ах"));
        Add("time", 1e-6, "µs", "мкс", "us|мкс|microsecond|microseconds|" + Forms("микросекунд", "а|ы||у|ой|е|ам|ами|ах"));
        Add("time", .001, "ms", "мс", "ms|мс|millisecond|milliseconds|" + Forms("миллисекунд", "а|ы||у|ой|е|ам|ами|ах"));
        Add("time", 1, "s", "с", "s|sec|secs|с|сек|second|seconds|" + Forms("секунд", "а|ы||у|ой|е|ам|ами|ах"));
        Add("time", 60, "min", "мин", "min|mins|мин|minute|minutes|" + Forms("минут", "а|ы||у|ой|е|ам|ами|ах"));
        Add("time", 3600, "h", "ч", "h|hr|hrs|ч|hour|hours|час|часа|часы|часов|часу|часом|часе|часам|часами|часах");
        Add("time", 86400, "d", "дн", "d|day|days|д|дн|день|дня|дни|дней|дню|днем|дне|дням|днями|днях|сутки|суток|суткам|сутками|сутках");
        Add("time", 604800, "wk", "нед", "wk|wks|week|weeks|нед|" + Forms("недел", "я|и|ь|ю|ей|е|ям|ями|ях"));

        Add("area", 100, "a", "ар", "a|are|ares|" + Forms("ар"), "ара", "аров");
        Add("area", 100, "a", "сотка", Forms("сот", "ка|ки|ок|ку|кой|ке|кам|ками|ках"), "сотки", "соток");
        Add("area", 10000, "ha", "га", "ha|га|hectare|hectares|" + Forms("гектар"));
        Add("area", 4046.8564224, "acre", "акр", "acre|acres|ac|" + Forms("акр"), "акра", "акров");

        // Volume uses m³ as its base. Unqualified customary volumes are U.S. liquid units.
        Add("volume", 1, "m³", "м³", Forms("кубометр") + "|" + Forms("куб"));
        Add("volume", .001, "L", "л", "l|л|ltr|liter|liters|litre|litres|" + Forms("литр"));
        Add("volume", 1e-6, "mL", "мл", "ml|мл|milliliter|milliliters|millilitre|millilitres|" + Forms("миллилитр"));
        Add("volume", 1e-5, "cL", "сл", "cl|сл|centiliter|centiliters|centilitre|centilitres|" + Forms("сантилитр"));
        Add("volume", 1e-4, "dL", "дл", "dl|дл|deciliter|deciliters|decilitre|decilitres|" + Forms("децилитр"));
        Add("volume", .003785411784, "US gal", "галлон США", "gal|gallon|gallons|us gal|us gallon|us gallons|" + Forms("галлон"), "галлона США", "галлонов США");
        Add("volume", .00454609, "UK gal", "брит. галлон", "uk gal|uk gallon|uk gallons|imp gal|imperial gallon|imperial gallons|британский галлон|британских галлонов|имперский галлон|имперских галлонов", "брит. галлона", "брит. галлонов");
        Add("volume", .000946352946, "US qt", "кварта США", "qt|quart|quarts|us qt|us quart|us quarts|" + Forms("кварт", "а|ы||у|ой|е|ам|ами|ах"), "кварты США", "кварт США");
        Add("volume", .000473176473, "US pt", "пинта США", "pt|pint|pints|us pt|us pint|us pints|" + Forms("пинт", "а|ы||у|ой|е|ам|ами|ах"), "пинты США", "пинт США");
        Add("volume", .0000295735295625, "US fl oz", "жидк. унция США", "fl oz|fl. oz|fluid ounce|fluid ounces|us fl oz|us fluid ounce|us fluid ounces|жидкая унция|жидкие унции|жидких унций|жидкую унцию|жидкой унции", "жидк. унции США", "жидк. унций США");

        Add("speed", 1, "m/s", "м/с", "mps");
        Add("speed", 1000d / 3600, "km/h", "км/ч", "kph|kmph|кмч");
        Add("speed", 1609.344 / 3600, "mph", "миль/ч", "mph");
        Add("speed", .3048, "ft/s", "фут/с", "fps");
        Add("speed", 1852d / 3600, "kn", "уз", "kt|knot|knots|уз|узел|узла|узлы|узлов|узлу|узлом|узле|узлам|узлами|узлах", exactSymbols: "kn");

        Add("pressure", 1, "Pa", "Па", "pa|па|pascal|pascals|" + Forms("паскал", "ь|я|и|ей|ю|ем|е|ям|ями|ях"));
        Add("pressure", .001, "mPa", "мПа", "millipascal|millipascals|" + Forms("миллипаскал", "ь|я|и|ей|ю|ем|е|ям|ями|ях"), exactSymbols: "mPa|мПа");
        Add("pressure", 100, "hPa", "гПа", "hpa|гпа|hectopascal|hectopascals|" + Forms("гектопаскал", "ь|я|и|ей|ю|ем|е|ям|ями|ях"));
        Add("pressure", 1000, "kPa", "кПа", "kpa|кпа|kilopascal|kilopascals|" + Forms("килопаскал", "ь|я|и|ей|ю|ем|е|ям|ями|ях"));
        Add("pressure", 1e6, "MPa", "МПа", "megapascal|megapascals|" + Forms("мегапаскал", "ь|я|и|ей|ю|ем|е|ям|ями|ях"), exactSymbols: "MPa|МПа");
        Add("pressure", 100000, "bar", "бар", "bar|bars|" + Forms("бар"));
        Add("pressure", 100, "mbar", "мбар", "mbar|мбар|millibar|millibars|" + Forms("миллибар"));
        Add("pressure", 101325, "atm", "атм", "atm|атм|atmosphere|atmospheres|" + Forms("атмосфер", "а|ы||у|ой|е|ам|ами|ах"));
        Add("pressure", 101325d / 760, "Torr", "торр", "torr|" + Forms("торр"));
        Add("pressure", 133.3224, "mmHg", "мм рт. ст.", "mmhg|mm hg|мм рт ст|мм рт. ст|мм. рт. ст|миллиметр ртутного столба|миллиметра ртутного столба|миллиметров ртутного столба");
        Add("pressure", 6894.757293168361, "psi", "psi", "psi|lbf/in2|lbf/in^2|фунт на квадратный дюйм|фунтов на квадратный дюйм");

        // Thermochemical calories; mechanical hp and metric л. с. have different factors.
        Add("energy", 1, "J", "Дж", "j|дж|joule|joules|" + Forms("джоул", "ь|я|и|ей|ю|ем|е|ям|ями|ях"));
        Add("energy", .001, "mJ", "мДж", "millijoule|millijoules|" + Forms("миллиджоул", "ь|я|и|ей|ю|ем|е|ям|ями|ях"), exactSymbols: "mJ|мДж");
        Add("energy", 1000, "kJ", "кДж", "kj|кдж|kilojoule|kilojoules|" + Forms("килоджоул", "ь|я|и|ей|ю|ем|е|ям|ями|ях"));
        Add("energy", 1e6, "MJ", "МДж", "megajoule|megajoules|" + Forms("мегаджоул", "ь|я|и|ей|ю|ем|е|ям|ями|ях"), exactSymbols: "MJ|МДж");
        Add("energy", 4.184, "cal", "кал", "cal|кал|calorie|calories|" + Forms("калори", "я|и|й|ю|ей|ям|ями|ях"));
        Add("energy", 4184, "kcal", "ккал", "kcal|ккал|kilocalorie|kilocalories|" + Forms("килокалори", "я|и|й|ю|ей|ям|ями|ях"));
        Add("energy", 3600, "Wh", "Вт·ч", "wh|w h|w*h|w·h|watt-hour|watt-hours|watt hour|watt hours|втч|вт ч|вт*ч|вт·ч|" + Forms("ватт-час") + "|" + Forms("ватт час"));
        Add("energy", 3600000, "kWh", "кВт·ч", "kwh|kw h|kw*h|kw·h|kilowatt-hour|kilowatt-hours|kilowatt hour|kilowatt hours|квтч|квт ч|квт*ч|квт·ч|" + Forms("киловатт-час") + "|" + Forms("киловатт час"));

        Add("power", 1, "W", "Вт", "w|вт|watt|watts|" + Forms("ватт"));
        Add("power", .001, "mW", "мВт", "milliwatt|milliwatts|" + Forms("милливатт"), exactSymbols: "mW|мВт");
        Add("power", 1000, "kW", "кВт", "kw|квт|kilowatt|kilowatts|" + Forms("киловатт"));
        Add("power", 1e6, "MW", "МВт", "megawatt|megawatts|" + Forms("мегаватт"), exactSymbols: "MW|МВт");
        Add("power", 745.6998715822702, "hp", "hp", "hp|horsepower|mechanical horsepower");
        Add("power", 735.49875, "PS", "л. с.", "ps|metric horsepower|лс|л.с|л. с|лошадиная сила|лошадиной силы|лошадиные силы|лошадиных сил|лошадиную силу");

        Add("angle", 1, "rad", "рад", "rad|рад|radian|radians|" + Forms("радиан"));
        Add("angle", Math.PI / 180, "°", "°", "°|deg|degree|degrees|град|" + Forms("градус"));
        Add("angle", Math.PI / 200, "gon", "гон", "gon|grad|gradians|" + Forms("гон") + "|" + Forms("градиан"));
        Add("frequency", 1, "Hz", "Гц", "hz|гц|hertz|герц|герца|герцы|герцев|герцу|герцем|герце|герцам|герцами|герцах");
        Add("frequency", 1000, "kHz", "кГц", "khz|кгц|kilohertz|килогерц|килогерца|килогерцы|килогерцев|килогерцу|килогерцем|килогерце|килогерцам|килогерцами|килогерцах");
        Add("frequency", 1e6, "MHz", "МГц", "megahertz|мегагерц|мегагерца|мегагерцы|мегагерцев|мегагерцу|мегагерцем|мегагерце|мегагерцам|мегагерцами|мегагерцах", exactSymbols: "MHz|МГц");
        Add("frequency", .001, "mHz", "мГц", "millihertz|миллигерц|миллигерца|миллигерцы|миллигерцев", exactSymbols: "mHz|мГц");
        Add("frequency", 1e9, "GHz", "ГГц", "ghz|ггц|gigahertz|гигагерц|гигагерца|гигагерцы|гигагерцев|гигагерцу|гигагерцем|гигагерце|гигагерцам|гигагерцами|гигагерцах");
        Add("force", 1, "N", "Н", "n|н|newton|newtons|" + Forms("ньютон"));
        Add("force", 1000, "kN", "кН", "кн|kilonewton|kilonewtons|" + Forms("килоньютон"), exactSymbols: "kN|кН");
        Add("force", 9.80665, "kgf", "кгс", "kgf|кгс|kilogram-force|kilograms-force|килограмм-сила|килограмма-силы|килограммов-сил");
        Add("force", 4.4482216152605, "lbf", "фунт-сила", "lbf|pound-force|pounds-force|фунт-сила|фунта-силы|фунтов-сил", "фунта-силы", "фунтов-сил");
        return units;
    }

    public static string? Convert(string input)
    {
        if (input.Length > 4096) return null;
        var match = Regex.Match(input.Trim(), @"^([+−-]?(?:\d+(?:[.,]\d+)?|[.,]\d+))\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        if (!double.TryParse(match.Groups[1].Value.Replace(',', '.').Replace('−', '-'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) return null;

        // Try complete unit names at each separator: "километра в час в метры в секунду"
        // contains separators inside both units as well as between them.
        var query = match.Groups[2].Value;
        foreach (Match separator in Regex.Matches(query, @"\s+(?:to|in|в|во)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var fromUnits = FindUnits(query[..separator.Index]);
            var toUnits = FindUnits(query[(separator.Index + separator.Length)..]).ToArray();
            foreach (var from in fromUnits)
                foreach (var to in toUnits)
                    if (from.Group == to.Group) return FormatConversion(value, from, to, input);
        }
        return null;
    }

    private static string? FormatConversion(double value, Unit from, Unit to, string input)
    {
        double result;
        if (from.Group == "temperature")
        {
            var c = from.Symbol switch { "F" => (value - 32) * 5 / 9, "K" => value - 273.15, _ => value };
            result = to.Symbol switch { "F" => c * 9 / 5 + 32, "K" => c + 273.15, _ => c };
        }
        else result = value * from.Factor / to.Factor;

        if (!double.IsFinite(result)) return null;
        var number = result.ToString("G10", CultureInfo.InvariantCulture);
        var symbol = Regex.IsMatch(input, @"[А-Яа-яЁё]") ? RussianSymbol(to, number) : to.Symbol;
        return number + " " + symbol;
    }

    private static string NormalizeName(string text)
    {
        var name = text.Trim().Replace('ё', 'е').Replace('Ё', 'Е').Replace('µ', 'u').Replace('μ', 'u')
            .Replace("²", "^2").Replace("³", "^3").Replace('⋅', '·');
        name = Regex.Replace(name, @"\s+", " ");
        return Regex.Replace(name, @"\s*([/*·^])\s*", "$1").TrimEnd('.');
    }

    private static IReadOnlyList<Unit> AtomicUnits(string name)
    {
        // Preserve SI distinctions such as mW/MW while keeping existing case-insensitive aliases.
        if (Units.ExactSymbols.TryGetValue(name, out var symbol)) return [symbol];
        return Units.Names.TryGetValue(name, out var units) ? units : [];
    }

    private static IEnumerable<Unit> FindUnits(string text)
    {
        var name = NormalizeName(text);
        var direct = AtomicUnits(name);
        if (direct.Count > 0)
        {
            foreach (var unit in direct) yield return unit;
            yield break;
        }

        var power = Regex.Match(name, @"^(.+?)\s*\^?([23])$", RegexOptions.CultureInvariant);
        var prefix = Regex.Match(name,
            @"^(?:(?<square>square|sq\.?|квадратн(?:ый|ая|ое|ые|ого|ой|ых|ому|ым|ыми|ую|ом)|кв\.?)|(?<cube>cubic|cu\.?|кубическ(?:ий|ая|ое|ие|ого|ой|их|ому|им|ими|ую|ом)|куб\.?))\s*(?<unit>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (power.Success || prefix.Success)
        {
            var exponent = power.Success ? int.Parse(power.Groups[2].Value, CultureInfo.InvariantCulture) : prefix.Groups["square"].Success ? 2 : 3;
            var baseName = power.Success ? power.Groups[1].Value.TrimEnd() : prefix.Groups["unit"].Value;
            foreach (var unit in AtomicUnits(baseName))
            {
                if (unit.Group != "length") continue;
                var superscript = exponent == 2 ? "²" : "³";
                yield return new(exponent == 2 ? "area" : "volume", Math.Pow(unit.Factor, exponent),
                    CompoundSymbol(unit) + superscript, unit.RussianSymbol + superscript);
            }
            yield break;
        }

        var ratio = Regex.Match(name, @"^(.+?)(?:/|\s+(?:per|в)\s+)(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (ratio.Success)
        {
            foreach (var numerator in AtomicUnits(ratio.Groups[1].Value))
                foreach (var denominator in AtomicUnits(ratio.Groups[2].Value))
                {
                    if (denominator.Group != "time" || numerator.Group is not ("length" or "data")) continue;
                    var russianNumerator = numerator.Symbol == "mile" ? "миль" : numerator.RussianSymbol;
                    yield return new(numerator.Group == "length" ? "speed" : "dataRate", numerator.Factor / denominator.Factor,
                        CompoundSymbol(numerator) + "/" + denominator.Symbol, russianNumerator + "/" + denominator.RussianSymbol);
                }
            yield break;
        }

        // Temperature names also accept "градусов по Фаренгейту" and "° С".
        var scale = Regex.Replace(name, @"^(?:градус(?:а|ы|ов|ах|ам|ами|ом|е|у)?\s+|гр\.\s*|degrees?\s+|deg\.?\s*)", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        scale = Regex.Replace(scale, @"^по\s+", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (scale.StartsWith('°')) scale = scale[1..].TrimStart();
        foreach (var unit in AtomicUnits(scale.TrimEnd('.')))
            if (unit.Group == "temperature") yield return unit;
    }

    private static string CompoundSymbol(Unit unit) => unit.Symbol switch { "inch" => "in", "mile" => "mi", _ => unit.Symbol };

    private static string RussianSymbol(Unit unit, string number)
    {
        if (unit.RussianFew == null || unit.RussianMany == null) return unit.RussianSymbol;
        // Use the displayed precision so a value shown as "12" gets "дюймов".
        var value = Math.Abs(double.Parse(number, CultureInfo.InvariantCulture));
        if (value != Math.Truncate(value)) return unit.RussianFew;
        if (value % 100 is >= 11 and <= 14) return unit.RussianMany;
        return (value % 10) switch
        {
            1 => unit.RussianSymbol,
            >= 2 and <= 4 => unit.RussianFew,
            _ => unit.RussianMany
        };
    }
}
