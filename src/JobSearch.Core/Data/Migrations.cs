using Microsoft.Data.Sqlite;

namespace JobSearch.Core.Data;

public class Migrations
{
    private readonly Database _db;

    public Migrations(Database db) => _db = db;

    public void RunAll()
    {
        using var conn = _db.OpenConnection();
        EnsureMigrationsTable(conn);
        Apply(conn, "001_create_postings", CreatePostingsTable);
        Apply(conn, "002_create_companies", CreateCompaniesTable);
    }

    private static void EnsureMigrationsTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS _migrations (
                name TEXT PRIMARY KEY,
                applied_at TEXT NOT NULL
            )
            """);
    }

    private static void Apply(SqliteConnection conn, string name, Action<SqliteConnection> migration)
    {
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM _migrations WHERE name = @name";
        check.Parameters.AddWithValue("@name", name);
        if ((long)check.ExecuteScalar()! > 0) return;

        migration(conn);

        using var record = conn.CreateCommand();
        record.CommandText = "INSERT INTO _migrations (name, applied_at) VALUES (@name, @at)";
        record.Parameters.AddWithValue("@name", name);
        record.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
        record.ExecuteNonQuery();
    }

    private static void CreatePostingsTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS postings (
                id TEXT PRIMARY KEY,
                company TEXT NOT NULL,
                title TEXT NOT NULL,
                location TEXT,
                url TEXT NOT NULL,
                posted_date TEXT,
                discovered_date TEXT NOT NULL,
                ats_source TEXT NOT NULL,
                raw_json TEXT,
                status TEXT NOT NULL DEFAULT 'new',
                submitted_date TEXT,
                confirmation_text TEXT,
                error_message TEXT,
                dedupe_reason TEXT
            )
            """);
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_postings_status ON postings(status)");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_postings_discovered ON postings(discovered_date)");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_postings_submitted ON postings(submitted_date)");
    }

    private static void CreateCompaniesTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS companies (
                slug TEXT PRIMARY KEY,
                ats_source TEXT NOT NULL,
                last_submission_date TEXT
            )
            """);
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
