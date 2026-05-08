using System.Text.Json;

namespace JobSearch.Core.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static CompaniesConfig LoadCompanies(string path)
        => Deserialize<CompaniesConfig>(path);

    public static ProfileConfig LoadProfile(string path)
        => Deserialize<ProfileConfig>(path);

    public static AppConfig LoadAppSettings(string path)
        => Deserialize<AppConfig>(path);

    private static T Deserialize<T>(string path) where T : new()
    {
        if (!File.Exists(path))
            return new T();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
    }
}
