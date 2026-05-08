using System.Text.RegularExpressions;

namespace JobSearch.Core.Filtering;

public static class LocationFilter
{
    // Word-boundary regex for WA/Washington (covers "WA, USA", "Washington State", etc.)
    private static readonly Regex WaRegex = new(@"\b(WA|Washington)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Literal city/keyword checks (case-insensitive)
    private static readonly string[] LiteralKeywords = ["Seattle", "Remote", "Bellevue", "Redmond", "Kirkland"];

    public static bool Matches(string? location, IEnumerable<string>? extraKeywords = null)
    {
        if (string.IsNullOrWhiteSpace(location)) return false;

        if (WaRegex.IsMatch(location)) return true;

        foreach (var kw in LiteralKeywords)
            if (location.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;

        if (extraKeywords != null)
            foreach (var kw in extraKeywords)
                if (location.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
