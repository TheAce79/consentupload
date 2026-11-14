using ConsentSync.Data;
using ConsentSync.Data.Entities;
using ConsentSyncCore.Services.Configuration;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Orchestrator.Tests;

public sealed class CohortDeletionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CohortDeletionTests", Guid.NewGuid().ToString("N"));
    private string CohortRoot => Path.Combine(_root, "Cohort");
    private IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["BaseDirectory"] = _root,
        ["CohortContext:Workspace:BaseCohortPath"] = Path.Combine(CohortRoot, "{ClientListName}")
    }).Build();

    [Fact]
    public async Task Delete_CascadesOnlySelectedCohortAndPreservesClientCache()
    {
        Directory.CreateDirectory(Path.Combine(CohortRoot, "FIRST"));
        File.WriteAllText(Path.Combine(CohortRoot, "FIRST", "roster.csv"), "test");
        var db = new DbManager(_root, "test.db");
        var first = Context("FIRST", 1);
        var second = Context("SECOND", 2);
        await db.SaveCohortContextAsync(first);
        await db.SaveCohortContextAsync(second);
        await db.LogClientListHistoryAsync(first.CohortContextId, "FIRST", 1, "test");
        await db.LogClientListHistoryAsync(second.CohortContextId, "SECOND", 1, "test");
        await db.SaveClientIdAsync(new PhisClientCacheEntity { CacheKey = "PERSON_2020/01/01", ClientId = "123", FullName = "Person", DateOfBirth = "2020/01/01", Source = ClientSource.PhisSearch });
        await using (var connection = Open())
        {
            await connection.ExecuteAsync("INSERT INTO PhisUploadSnapshots (CohortContextId, PhisCohortId, PhisClientListId, ClientListName, ClientSnapshotJson, UploadedOn) VALUES (@id, 1, 2, 'FIRST', '[]', 'now'); INSERT INTO ScheduleSnapshots (CohortContextId, ClientListName, BatchId, SourceFilesJson, ClientSnapshotJson, ImportedOn) VALUES (@id, 'FIRST', 'batch', '[]', '[]', 'now');", new { id = first.CohortContextId });
        }
        CohortDeletionService.DeleteDirectory(Config, "FIRST", CohortRoot);
        Assert.True(await db.DeleteCohortContextAsync(first.CohortContextId, "FIRST"));
        Assert.False(Directory.Exists(Path.Combine(CohortRoot, "FIRST")));
        Assert.Null(await db.GetCohortContextByListNameAsync("FIRST"));
        Assert.NotNull(await db.GetCohortContextByListNameAsync("SECOND"));
        Assert.Single(await db.GetClientListHistoryAsync(second.CohortContextId));
        Assert.Empty(await db.GetClientListHistoryAsync(first.CohortContextId));
        Assert.Equal("123", await db.GetClientIdAsync("PERSON_2020/01/01"));
        Assert.Equal("123", await new DbManager(_root, "test.db").GetClientIdAsync("PERSON_2020/01/01"));
        await using var check = Open();
        Assert.Equal(0, await check.ExecuteScalarAsync<int>("SELECT (SELECT COUNT(*) FROM PhisUploadSnapshots) + (SELECT COUNT(*) FROM ScheduleSnapshots)"));
        Assert.Equal(1, await check.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PhisClientCache"));
        CohortDeletionService.DeleteDirectory(Config, "FIRST", CohortRoot);
    }

    [Fact]
    public async Task Delete_DatabaseFailureRollsBackCascade()
    {
        Directory.CreateDirectory(_root);
        var db = new DbManager(_root, "test.db");
        var context = Context("FIRST", 1);
        await db.SaveCohortContextAsync(context);
        await db.LogClientListHistoryAsync(context.CohortContextId, "FIRST", 1, "test");
        await using (var connection = Open())
            await connection.ExecuteAsync("CREATE TRIGGER prevent_history_delete BEFORE DELETE ON ClientListHistory BEGIN SELECT RAISE(ABORT, 'blocked'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => db.DeleteCohortContextAsync(context.CohortContextId, "FIRST"));
        Assert.NotNull(await db.GetCohortContextByListNameAsync("FIRST"));
        Assert.Single(await db.GetClientListHistoryAsync(context.CohortContextId));
    }

    [Fact]
    public async Task Delete_LockedFileLeavesDatabaseRecordsForRetry()
    {
        string folder = Path.Combine(CohortRoot, "FIRST");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "roster.csv");
        File.WriteAllText(file, "test");
        var db = new DbManager(_root, "test.db");
        var context = Context("FIRST", 1);
        await db.SaveCohortContextAsync(context);
        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => CohortDeletionService.DeleteDirectory(Config, "FIRST", CohortRoot));
        Assert.NotNull(await db.GetCohortContextByListNameAsync("FIRST"));
        CohortDeletionService.DeleteDirectory(Config, "FIRST", CohortRoot);
        Assert.True(await db.DeleteCohortContextAsync(context.CohortContextId, "FIRST"));
    }

    [Theory]
    [InlineData("")][InlineData("..")][InlineData("../other")][InlineData("1. InputFolder")][InlineData("2. OutputFolder")][InlineData("name.")]
    public void Delete_RejectsUnsafeNames(string name) =>
        Assert.ThrowsAny<ArgumentException>(() => CohortDeletionService.ValidateTarget(Config, name, CohortRoot));

    [Fact]
    public void Delete_RejectsDifferentConfiguredWorkspace()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseDirectory"] = _root,
            ["CohortContext:Workspace:BaseCohortPath"] = Path.Combine(_root, "Other", "{ClientListName}")
        }).Build();
        Assert.Throws<InvalidOperationException>(() => CohortDeletionService.ValidateTarget(config, "FIRST", CohortRoot));
    }

    private static CohortContextEntity Context(string name, int day) => new()
    {
        ClientListName = name, Prefix = "CIP", Location = "MONCTON", Type = "SP",
        Jurisdiction = "NB", EncounterGroup = "Immunization", CohortDate = new DateTime(2026, 1, day)
    };

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_root, "test.db") }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
