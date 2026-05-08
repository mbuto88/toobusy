using JobSearch.Core.Models;
using Microsoft.Data.Sqlite;
using Serilog;

namespace JobSearch.Core.Data;

public class PostingsRepository
{
    private readonly Database _db;
    private readonly ILogger _logger;

    public PostingsRepository(Database db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Upsert(JobPosting posting)
    {
        using var conn = _db.OpenConnection();

        // Primary dedupe: composite ID (INSERT OR IGNORE)
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO postings
                (id, company, title, location, url, posted_date, discovered_date,
                 ats_source, raw_json, status, dedupe_reason)
            VALUES
                (@id, @company, @title, @location, @url, @posted_date, @discovered_date,
                 @ats_source, @raw_json, @status, @dedupe_reason)
            """;
        cmd.Parameters.AddWithValue("@id", posting.Id);
        cmd.Parameters.AddWithValue("@company", posting.Company);
        cmd.Parameters.AddWithValue("@title", posting.Title);
        cmd.Parameters.AddWithValue("@location", (object?)posting.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@url", posting.Url);
        cmd.Parameters.AddWithValue("@posted_date", (object?)posting.PostedDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@discovered_date", posting.DiscoveredDate);
        cmd.Parameters.AddWithValue("@ats_source", posting.AtsSource);
        cmd.Parameters.AddWithValue("@raw_json", (object?)posting.RawJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", posting.Status);
        cmd.Parameters.AddWithValue("@dedupe_reason", (object?)posting.DedupeReason ?? DBNull.Value);
        var inserted = cmd.ExecuteNonQuery();

        if (inserted == 0) return; // primary key already existed

        // Secondary dedupe: same company + title, location overlap, within 30 days
        var duplicate = FindSecondaryDuplicate(conn, posting);
        if (duplicate != null)
        {
            var reason = $"duplicate_title_{duplicate}";
            _logger.Warning("Secondary dedupe: {NewId} matches existing {ExistingId} — marking skipped", posting.Id, duplicate);
            using var skip = conn.CreateCommand();
            skip.CommandText = "UPDATE postings SET status = 'skipped', dedupe_reason = @reason WHERE id = @id";
            skip.Parameters.AddWithValue("@reason", reason);
            skip.Parameters.AddWithValue("@id", posting.Id);
            skip.ExecuteNonQuery();
        }
    }

    private static string? FindSecondaryDuplicate(SqliteConnection conn, JobPosting posting)
    {
        using var cmd = conn.CreateCommand();
        // Find postings from the same company with the same title discovered in the last 30 days
        cmd.CommandText = """
            SELECT id, location FROM postings
            WHERE id <> @id
              AND lower(company) = lower(@company)
              AND lower(title) = lower(@title)
              AND discovered_date >= @cutoff
              AND status <> 'skipped'
            LIMIT 5
            """;
        cmd.Parameters.AddWithValue("@id", posting.Id);
        cmd.Parameters.AddWithValue("@company", posting.Company);
        cmd.Parameters.AddWithValue("@title", posting.Title);
        cmd.Parameters.AddWithValue("@cutoff", DateTime.UtcNow.AddDays(-30).ToString("o"));

        using var reader = cmd.ExecuteReader();
        var locationKeywords = new[] { "seattle", "remote", "wa", "bellevue", "redmond", "kirkland" };
        while (reader.Read())
        {
            var existingId = reader.GetString(0);
            var existingLoc = reader.IsDBNull(1) ? "" : reader.GetString(1).ToLowerInvariant();
            var newLoc = (posting.Location ?? "").ToLowerInvariant();
            // Location overlap: either both are empty or share a location keyword
            bool overlap = string.IsNullOrEmpty(existingLoc) || string.IsNullOrEmpty(newLoc)
                || locationKeywords.Any(k => existingLoc.Contains(k) && newLoc.Contains(k))
                || locationKeywords.Any(k => existingLoc.Contains(k) || newLoc.Contains(k));
            if (overlap) return existingId;
        }
        return null;
    }

    public void MarkSkipped(string id, string dedupeReason)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE postings SET status='skipped', dedupe_reason=@reason WHERE id=@id";
        cmd.Parameters.AddWithValue("@reason", dedupeReason);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public bool Exists(string id)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM postings WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public List<JobPosting> GetByStatus(string status, int limit = 200)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM postings WHERE status = @status ORDER BY discovered_date DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@limit", limit);
        return ReadPostings(cmd);
    }

    public List<JobPosting> GetByStatusAndAts(string status, string atsSource, int limit = 200)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM postings WHERE status = @status AND ats_source = @ats ORDER BY discovered_date DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@ats", atsSource);
        cmd.Parameters.AddWithValue("@limit", limit);
        return ReadPostings(cmd);
    }

    public void UpdateStatus(string id, string status, string? submittedDate = null,
        string? confirmationText = null, string? errorMessage = null)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE postings SET
                status = @status,
                submitted_date = COALESCE(@submitted_date, submitted_date),
                confirmation_text = COALESCE(@confirmation_text, confirmation_text),
                error_message = COALESCE(@error_message, error_message)
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@submitted_date", (object?)submittedDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@confirmation_text", (object?)confirmationText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error_message", (object?)errorMessage ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public int CountSubmittedToday()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        cmd.CommandText = "SELECT COUNT(*) FROM postings WHERE status = 'submitted' AND submitted_date LIKE @today";
        cmd.Parameters.AddWithValue("@today", today + "%");
        return (int)(long)cmd.ExecuteScalar()!;
    }

    public PostingStats GetStats()
    {
        using var conn = _db.OpenConnection();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var weekStart = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");

        return new PostingStats
        {
            DiscoveredToday = CountWhere(conn, "discovered_date LIKE @d", "@d", today + "%"),
            DiscoveredThisWeek = CountWhere(conn, "discovered_date >= @w", "@w", weekStart),
            DiscoveredTotal = CountWhere(conn, "1=1", null, null),
            SubmittedToday = CountWhere(conn, "status='submitted' AND submitted_date LIKE @d", "@d", today + "%"),
            SubmittedThisWeek = CountWhere(conn, "status='submitted' AND submitted_date >= @w", "@w", weekStart),
            SubmittedTotal = CountWhere(conn, "status='submitted'", null, null),
            NeedsManual = GetByStatusInternal(conn, PostingStatus.NeedsManual),
            Failed = GetByStatusInternal(conn, PostingStatus.Failed)
        };
    }

    private static int CountWhere(SqliteConnection conn, string where, string? param, string? value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM postings WHERE {where}";
        if (param != null) cmd.Parameters.AddWithValue(param, value!);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    private static List<JobPosting> GetByStatusInternal(SqliteConnection conn, string status)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM postings WHERE status = @status ORDER BY discovered_date DESC LIMIT 100";
        cmd.Parameters.AddWithValue("@status", status);
        return ReadPostings(cmd);
    }

    private static List<JobPosting> ReadPostings(SqliteCommand cmd)
    {
        var list = new List<JobPosting>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new JobPosting
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Company = reader.GetString(reader.GetOrdinal("company")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Location = reader.IsDBNull(reader.GetOrdinal("location")) ? null : reader.GetString(reader.GetOrdinal("location")),
                Url = reader.GetString(reader.GetOrdinal("url")),
                PostedDate = reader.IsDBNull(reader.GetOrdinal("posted_date")) ? null : reader.GetString(reader.GetOrdinal("posted_date")),
                DiscoveredDate = reader.GetString(reader.GetOrdinal("discovered_date")),
                AtsSource = reader.GetString(reader.GetOrdinal("ats_source")),
                Status = reader.GetString(reader.GetOrdinal("status")),
                SubmittedDate = reader.IsDBNull(reader.GetOrdinal("submitted_date")) ? null : reader.GetString(reader.GetOrdinal("submitted_date")),
                ConfirmationText = reader.IsDBNull(reader.GetOrdinal("confirmation_text")) ? null : reader.GetString(reader.GetOrdinal("confirmation_text")),
                ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString(reader.GetOrdinal("error_message")),
                DedupeReason = reader.IsDBNull(reader.GetOrdinal("dedupe_reason")) ? null : reader.GetString(reader.GetOrdinal("dedupe_reason")),
            });
        }
        return list;
    }
}
