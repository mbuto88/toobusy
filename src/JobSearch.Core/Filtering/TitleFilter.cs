namespace JobSearch.Core.Filtering;

public static class TitleFilter
{
    // Bare "engineer" only matches when accompanied by a qualifying context word.
    // This prevents matching "Sound Engineer", "Civil Engineer", etc.
    private static readonly string[] EngineerQualifiers =
    [
        "software", "backend", "back-end", "back end", "platform",
        "systems", "infrastructure", "site reliability", "applications", ".net", "devops"
    ];

    public static bool Matches(string? title, IEnumerable<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var lower = title.ToLowerInvariant();

        foreach (var kw in keywords)
        {
            var kwLower = kw.ToLowerInvariant();

            if (kwLower == "engineer")
            {
                // Bare "engineer" only matches when title also contains a qualifying word
                if (lower.Contains("engineer") && EngineerQualifiers.Any(q => lower.Contains(q)))
                    return true;
                continue;
            }

            if (lower.Contains(kwLower)) return true;
        }

        return false;
    }
}
