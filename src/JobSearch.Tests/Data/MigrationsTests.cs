using JobSearch.Core.Data;
using JobSearch.Tests.Helpers;
using Microsoft.Data.Sqlite;

namespace JobSearch.Tests.Data;

public class MigrationsTests
{
    [Fact]
    public void RunAll_CreatesPostingsTable()
    {
        using var testDb = new TestDatabase();
        TableExists(testDb.Db, "postings").Should().BeTrue();
    }

    [Fact]
    public void RunAll_CreatesCompaniesTable()
    {
        using var testDb = new TestDatabase();
        TableExists(testDb.Db, "companies").Should().BeTrue();
    }

    [Fact]
    public void RunAll_CreatesMigrationsTable()
    {
        using var testDb = new TestDatabase();
        TableExists(testDb.Db, "_migrations").Should().BeTrue();
    }

    [Fact]
    public void RunAll_IsIdempotent()
    {
        using var testDb = new TestDatabase();
        // Running again must not throw or alter state
        new Migrations(testDb.Db).RunAll();
        TableExists(testDb.Db, "postings").Should().BeTrue();
    }

    [Fact]
    public void RunAll_RecordsTwoMigrations()
    {
        using var testDb = new TestDatabase();
        using var conn = testDb.Db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM _migrations";
        var count = (long)cmd.ExecuteScalar()!;
        count.Should().Be(2); // 001_create_postings, 002_create_companies
    }

    [Fact]
    public void RunAll_SecondInvocation_DoesNotDuplicateMigrationRows()
    {
        using var testDb = new TestDatabase();
        new Migrations(testDb.Db).RunAll(); // second run

        using var conn = testDb.Db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM _migrations";
        var count = (long)cmd.ExecuteScalar()!;
        count.Should().Be(2);
    }

    [Fact]
    public void PostingsTable_HasDedupeReasonColumn()
    {
        using var testDb = new TestDatabase();
        ColumnExists(testDb.Db, "postings", "dedupe_reason").Should().BeTrue();
    }

    [Fact]
    public void PostingsTable_HasAllRequiredColumns()
    {
        using var testDb = new TestDatabase();
        var expected = new[]
        {
            "id", "company", "title", "location", "url", "posted_date",
            "discovered_date", "ats_source", "raw_json", "status",
            "submitted_date", "confirmation_text", "error_message", "dedupe_reason"
        };
        foreach (var col in expected)
            ColumnExists(testDb.Db, "postings", col)
                .Should().BeTrue(because: $"column '{col}' should exist in postings table");
    }

    private static bool TableExists(Database db, string tableName)
    {
        using var conn = db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", tableName);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    private static bool ColumnExists(Database db, string tableName, string columnName)
    {
        using var conn = db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
