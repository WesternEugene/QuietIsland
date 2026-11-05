using System.Globalization;
using System.Text.RegularExpressions;

namespace QuietIsland.Core;

public class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Напоминание";
    public DateTimeOffset Due { get; set; }
    public bool Daily { get; set; }
    public List<DayOfWeek> Weekdays { get; set; } = [];
    public bool Completed { get; set; }
    public DateTimeOffset NextAfter(DateTimeOffset now)
    {
        if (!Daily && Weekdays.Count == 0) return DateTimeOffset.MaxValue;
        var next = Due;
        do { next = next.AddDays(1); } while (next <= now || (Weekdays.Count > 0 && !Weekdays.Contains(next.DayOfWeek)));
        return next;
    }
}

public static class ReminderParser
{
    public static Reminder? Parse(string text, DateTimeOffset now)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > 2000) return null;
        var reminder = new Reminder();
        var relative = Regex.Match(text, @"(?:через|in)\s+(\d+(?:[.,]\d+)?\s*(?:час(?:а|ов)?|hours?|мин(?:ут(?:а|ы)?)?|minutes?|сек(?:унд(?:а|ы)?)?|seconds?))", RegexOptions.IgnoreCase);
        if (relative.Success)
        {
            var duration = CommandParser.ParseDuration(relative.Groups[1].Value);
            if (duration <= TimeSpan.Zero) return null;
            reminder.Due = now.Add(duration);
            reminder.Title = Clean(text.Remove(relative.Index, relative.Length));
            return reminder;
        }
        const string clock = @"(?<hour>\d{1,2})(?::(?<minute>\d{2}))?";
        const string period = @"(?<period>утра|дня|вечера|ночи)";
        var time = Regex.Match(text, $@"\b(?:в|at)\s+{clock}\b(?:\s+{period}\b)?|\b{clock}\s+{period}\b", RegexOptions.IgnoreCase);
        if (!time.Success) return null;
        var hour = int.Parse(time.Groups["hour"].Value);
        var minute = time.Groups["minute"].Success ? int.Parse(time.Groups["minute"].Value) : 0;
        if (hour > 23 || minute > 59) return null;
        var timeOfDay = time.Groups["period"].Value.ToLowerInvariant();
        // «11 ночи» — поздний вечер, «3 ночи» — раннее утро.
        if (hour is > 0 and < 12 && (timeOfDay is "дня" or "вечера" || timeOfDay == "ночи" && hour >= 7)) hour += 12;
        else if (hour == 12 && timeOfDay is "утра" or "ночи" or "вечера") hour = 0;
        var title = text.Remove(time.Index, time.Length);
        var days = Regex.Match(title, @"каждый день|ежедневно|every day|daily", RegexOptions.IgnoreCase);
        reminder.Daily = days.Success;
        if (days.Success) title = title.Remove(days.Index, days.Length);
        var tomorrow = Regex.IsMatch(title, @"завтра|tomorrow", RegexOptions.IgnoreCase);
        var date = now.Date.AddDays(tomorrow ? 1 : 0);
        title = Regex.Replace(title, @"\b(завтра|tomorrow|сегодня|today)\b", "", RegexOptions.IgnoreCase);
        var repeating = reminder.Daily || Regex.IsMatch(title, @"\b(кажд\p{L}*|еженедельно|every|по)\b", RegexOptions.IgnoreCase);
        string[] ru = ["воскресень(?:е|я|ям|ю)", "понедельник(?:а|и|ам)?", "вторник(?:а|и|ам)?", "сред(?:а|у|ам|ы|е)", "четверг(?:а|и|ам)?", "пятниц(?:а|у|ам|ы|е)", "суббот(?:а|у|ам|ы|е)"];
        string[] en = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];
        var selectedDays = new List<DayOfWeek>();
        for (var i = 0; i < 7; i++)
        {
            var dayPattern = $@"\b(?:(?:в|по|on)\s+)?(?:{ru[i]}|{en[i]}s?)\b";
            if (!Regex.IsMatch(title, dayPattern, RegexOptions.IgnoreCase)) continue;
            selectedDays.Add((DayOfWeek)i);
            title = Regex.Replace(title, dayPattern, "", RegexOptions.IgnoreCase);
        }
        if (repeating && !reminder.Daily && selectedDays.Count == 0) return null;
        if (repeating) reminder.Weekdays = selectedDays;
        var explicitDate = Regex.Match(title, @"\b(\d{4}-\d{2}-\d{2}|\d{1,2}\.\d{1,2}\.\d{4})\b");
        if (explicitDate.Success)
        {
            if (!DateTime.TryParseExact(explicitDate.Value, ["yyyy-MM-dd", "d.M.yyyy", "dd.MM.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return null;
            title = title.Remove(explicitDate.Index, explicitDate.Length);
        }
        reminder.Due = new DateTimeOffset(date.AddHours(hour).AddMinutes(minute), now.Offset);
        if (!explicitDate.Success)
            while (reminder.Due <= now || (selectedDays.Count > 0 && !selectedDays.Contains(reminder.Due.DayOfWeek))) reminder.Due = reminder.Due.AddDays(1);
        if (reminder.Due <= now) return null;
        reminder.Title = Clean(Regex.Replace(title, @"\b(кажд\p{L}*|еженедельно|по|every)\b", "", RegexOptions.IgnoreCase));
        return reminder;
    }
    private static string Clean(string text)
    {
        var title = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', '.', '-');
        return title.Length > 0 ? title : "Напоминание";
    }
}
