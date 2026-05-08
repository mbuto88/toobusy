using Microsoft.Data.Sqlite;

namespace JobSearch.Core.Data;

public class CompaniesRepository
{
    private readonly Database _db;

    public CompaniesRepository(Database db) => _db = db;

    public void EnsureExists(string slug, string atsSource)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO companies (slug, ats_source) VALUES (@slug, @ats)";
        cmd.Parameters.AddWithValue("@slug", slug);
        cmd.Parameters.AddWithValue("@ats", atsSource);
        cmd.ExecuteNonQuery();
    }

    public string? GetLastSubmissionDate(string slug)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_submission_date FROM companies WHERE slug = @slug";
        cmd.Parameters.AddWithValue("@slug", slug);
        var result = cmd.ExecuteScalar();
        return result == DBNull.Value || result == null ? null : (string)result;
    }

    public void UpdateLastSubmissionDate(string slug, string isoDate)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE companies SET last_submission_date = @date WHERE slug = @slug";
        cmd.Parameters.AddWithValue("@date", isoDate);
        cmd.Parameters.AddWithValue("@slug", slug);
        cmd.ExecuteNonQuery();
    }

    public bool IsOnCooldown(string slug, int cooldownDays = 7)
    {
        var lastDate = GetLastSubmissionDate(slug);
        if (lastDate == null) return false;
        if (!DateTime.TryParse(lastDate, out var last)) return false;
        return (DateTime.UtcNow - last).TotalDays < cooldownDays;
    }
}
