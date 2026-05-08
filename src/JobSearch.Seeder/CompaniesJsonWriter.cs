using System.Text.Json;
using System.Text.Json.Nodes;
using JobSearch.Core.Config;
using Serilog;

namespace JobSearch.Seeder;

public class CompaniesJsonWriter
{
    private readonly string _path;
    private readonly ILogger _logger;

    public CompaniesJsonWriter(string path, ILogger logger)
    {
        _path = path;
        _logger = logger;
    }

    public void MergeAndSave(Dictionary<string, List<string>> newSlugsPerAts)
    {
        var existing = LoadExisting();

        foreach (var (ats, slugs) in newSlugsPerAts)
        {
            if (!existing.ContainsKey(ats)) existing[ats] = [];
            var set = new HashSet<string>(existing[ats], StringComparer.OrdinalIgnoreCase);
            foreach (var slug in slugs) set.Add(slug);
            existing[ats] = [.. set.OrderBy(s => s)];
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(existing, options);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, json);
        _logger.Information("Saved companies.json: Greenhouse={G}, Lever={L}, Ashby={A}",
            existing.GetValueOrDefault("greenhouse")?.Count ?? 0,
            existing.GetValueOrDefault("lever")?.Count ?? 0,
            existing.GetValueOrDefault("ashby")?.Count ?? 0);
    }

    private Dictionary<string, List<string>> LoadExisting()
    {
        if (!File.Exists(_path)) return new() { ["greenhouse"] = [], ["lever"] = [], ["ashby"] = [] };
        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new() { ["greenhouse"] = [], ["lever"] = [], ["ashby"] = [] };
    }
}
