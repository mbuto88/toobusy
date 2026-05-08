using JobSearch.Core.Data;
using Microsoft.Data.Sqlite;

namespace JobSearch.Tests.Helpers;

public sealed class TestDatabase : IDisposable
{
    private readonly string _dbPath;
    public Database Db { get; }

    public TestDatabase()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"jobsearch_test_{Guid.NewGuid():N}.db");
        Db = new Database(_dbPath);
        new Migrations(Db).RunAll();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
