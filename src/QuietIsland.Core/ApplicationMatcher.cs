namespace QuietIsland.Core;

public static class ApplicationMatcher
{
    // Prefer names and word prefixes, then initials, then incidental substrings.
    // Initials include camel-case words so "vb" also matches "Oracle VirtualBox".
    public static int Score(string name, string query)
    {
        query = query.Trim();
        if (query.Length == 0 || name.Length == 0) return 0;
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 900;

        var starts = Enumerable.Range(0, name.Length).Where(i => IsWordStart(name, i)).ToArray();
        if (starts.Any(i => name.AsSpan(i).StartsWith(query, StringComparison.OrdinalIgnoreCase))) return 850;
        if (query.Length >= 2 && query.All(char.IsLetterOrDigit))
        {
            var initials = new string(starts.Select(i => name[i]).ToArray());
            var index = initials.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) return 800 - index - (initials.Length - query.Length);
        }
        return name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 600 : 0;
    }

    private static bool IsWordStart(string name, int index)
    {
        if (!char.IsLetterOrDigit(name[index])) return false;
        if (index == 0 || !char.IsLetterOrDigit(name[index - 1])) return true;
        if (char.IsDigit(name[index]) != char.IsDigit(name[index - 1])) return true;
        return char.IsUpper(name[index]) && (char.IsLower(name[index - 1]) ||
            (index + 1 < name.Length && char.IsLower(name[index + 1])));
    }
}
