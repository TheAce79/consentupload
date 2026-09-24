using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Csv;
using ConsentSync.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Orchestrator.Tests;

public sealed class CohortReviewServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CohortReviewTests", Guid.NewGuid().ToString("N"));
    private string SourcePath => Path.Combine(_directory, "TEST_Cohort.csv");
    private string ReviewPath => Path.Combine(_directory, "TEST_Cohort.review.json");

    public CohortReviewServiceTests()
    {
        Directory.CreateDirectory(_directory);
        CsvExporterService.SaveToCsv([
            new() { FullName = "First", DateOfBirth = "2020/02/26", ClientId = "001", ClientIdStatus = ClientIdStatus.Found, BookingId = "BOOK-1", ClinicDate = "2026-10-02", PreferredLanguage = "Français" },
            new() { FullName = "Second", DateOfBirth = "2020/02/26", ClientIdStatus = ClientIdStatus.NeedsManualReview, BestMatch = "A#B##001#95.0%" }
        ], SourcePath);
    }

    [Fact]
    public void SaveReload_PreservesOverridesExclusionsOrderAndSource()
    {
        var review = CohortReviewService.Load(SourcePath, ReviewPath);
        review.Rows[0].ClientId = "";
        review.Rows[0].Excluded = true;
        review.Rows[1].ClientId = " 00042 ";
        review.Save();
        var restored = CohortReviewService.Load(SourcePath, ReviewPath);
        Assert.Equal([1, 2], restored.Rows.Select(r => r.RowNumber));
        Assert.Equal("", restored.Rows[0].ClientId);
        Assert.True(restored.Rows[0].Excluded);
        Assert.True(restored.Rows[0].Unresolved);
        Assert.Equal("00042", restored.Rows[1].ClientId);
        Assert.False(restored.Rows[1].Unresolved);
        Assert.Equal(ClientIdStatus.Found, restored.Rows[1].SearchStatus);
        Assert.Null(restored.Rows[1].ErrorDetails);
        Assert.Equal("Client ID cleared during manual review.", restored.Rows[0].ErrorDetails);
        var csvRows = CsvImporterService.ReadFromCsv(SourcePath);
        Assert.Equal([null, "00042"], csvRows.Select(r => r.ClientId));
        Assert.Equal([ClientIdStatus.NeedsManualReview, ClientIdStatus.Found], csvRows.Select(r => r.ClientIdStatus));
        Assert.Equal("Client ID cleared during manual review.", csvRows[0].ErrorDetails);
        Assert.Equal("A#B##001#95.0%", csvRows[1].BestMatch);
        Assert.Equal("BOOK-1", csvRows[0].BookingId);
        Assert.Equal("2026-10-02", csvRows[0].ClinicDate);
        Assert.Equal("Français", csvRows[0].PreferredLanguage);
        restored.Rows[1].Excluded = true;
        restored.Save();
        Assert.True(CohortReviewService.Load(SourcePath, ReviewPath).Rows[1].Excluded);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void DemographicCorrections_SaveReloadAndExportIncludingMedicareClear()
    {
        var review = CohortReviewService.Load(SourcePath, ReviewPath);
        review.Rows[0].FullName = " Corrected Person ";
        review.Rows[0].DateOfBirth = " 26/02/2020 ";
        review.Rows[0].Medicare = "001234567";
        review.Rows[1].Medicare = "";
        review.Save();

        var csvRows = CsvImporterService.ReadFromCsv(SourcePath);
        Assert.Equal("Corrected Person", csvRows[0].FullName);
        Assert.Equal("26/02/2020", csvRows[0].DateOfBirth);
        Assert.Equal("001234567", csvRows[0].Medicare);
        Assert.Null(csvRows[1].Medicare);

        var restored = CohortReviewService.Load(SourcePath, ReviewPath);
        Assert.Equal("Corrected Person", restored.Rows[0].FullName);
        Assert.Equal("26/02/2020", restored.Rows[0].DateOfBirth);
        Assert.Equal("001234567", restored.Rows[0].Medicare);
        Assert.Equal(string.Empty, restored.Rows[1].Medicare);
        Assert.True(restored.Rows[0].HasManualCorrection);
        restored.Save();
        Assert.Equal("001234567", CsvImporterService.ReadFromCsv(SourcePath)[0].Medicare);
    }

    [Fact]
    public void ChangedSource_BlocksRestoreAndSaveUntilExplicitFreshReview()
    {
        var review = CohortReviewService.Load(SourcePath, ReviewPath);
        review.Rows[0].Excluded = true;
        review.Save();
        string saved = File.ReadAllText(ReviewPath);
        File.AppendAllText(SourcePath, Environment.NewLine);
        Assert.Throws<InvalidDataException>(() => CohortReviewService.Load(SourcePath, ReviewPath));
        Assert.Throws<InvalidDataException>(() => review.Save());
        Assert.Equal(saved, File.ReadAllText(ReviewPath));
        var fresh = CohortReviewService.Load(SourcePath, ReviewPath, startFresh: true);
        Assert.False(fresh.Rows[0].Excluded);
        Assert.Equal(saved, File.ReadAllText(ReviewPath));
        fresh.Save();
        Assert.False(CohortReviewService.Load(SourcePath, ReviewPath).Rows[0].Excluded);
    }

    [Fact]
    public void DuplicateIds_RecalculateAfterCorrectionExclusionAndRestore()
    {
        var review = CohortReviewService.Load(SourcePath, ReviewPath);
        Assert.True(review.Rows[1].RequiresAttention);
        review.Rows[1].ClientId = "001";
        review.RefreshDuplicates();
        Assert.All(review.Rows, r => Assert.True(r.DuplicateId));
        review.Rows[0].Excluded = true;
        review.RefreshDuplicates();
        Assert.All(review.Rows, r => Assert.False(r.DuplicateId));
        Assert.False(review.Rows[0].RequiresAttention);
        review.Rows[0].Excluded = false;
        review.RefreshDuplicates();
        Assert.All(review.Rows, r => Assert.True(r.DuplicateId));
        review.Rows[1].ClientId = "002";
        review.RefreshDuplicates();
        Assert.All(review.Rows, r => Assert.False(r.DuplicateId));
        review.Rows[1].ClientId = " ";
        Assert.True(review.Rows[1].Unresolved);
    }

    [Fact]
    public void ManualCorrections_UpdateEffectiveStatusAndRetainCompleteCsvRows()
    {
        var review = CohortReviewService.Load(SourcePath, ReviewPath);
        review.Rows[0].ClientId = "00009";
        review.Rows[1].Excluded = true;
        review.Save();

        var csvRows = CsvImporterService.ReadFromCsv(SourcePath);
        Assert.Equal(2, csvRows.Count);
        Assert.Equal("00009", csvRows[0].ClientId);
        Assert.Equal(ClientIdStatus.Found, csvRows[0].ClientIdStatus);
        Assert.Null(csvRows[0].ErrorDetails);
        Assert.Equal("Second", csvRows[1].FullName);
        Assert.Equal("A#B##001#95.0%", csvRows[1].BestMatch);

        var reloaded = CohortReviewService.Load(SourcePath, ReviewPath);
        Assert.Equal("00009", reloaded.Rows[0].ClientId);
        Assert.Equal(ClientIdStatus.Found, reloaded.Rows[0].SearchStatus);
        Assert.True(reloaded.Rows[1].Excluded);
        reloaded.Save();
        Assert.Equal(2, CsvImporterService.ReadFromCsv(SourcePath).Count);
    }

    [Fact]
    public void LegacySidecarOverride_UsesEffectiveFoundStatusWhenLoaded()
    {
        var document = new
        {
            Version = 1,
            SourceFingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SourcePath))),
            Rows = new[]
            {
                new { RowNumber = 1, ClientIdOverride = (string?)null, Excluded = false },
                new { RowNumber = 2, ClientIdOverride = (string?)"00077", Excluded = false }
            }
        };
        File.WriteAllText(ReviewPath, JsonSerializer.Serialize(document));

        var review = CohortReviewService.Load(SourcePath, ReviewPath);
        Assert.Equal("00077", review.Rows[1].ClientId);
        Assert.Equal(ClientIdStatus.Found, review.Rows[1].SearchStatus);
        Assert.False(review.Rows[1].Unresolved);
        Assert.Null(review.Rows[1].ErrorDetails);
    }

    [Fact]
    public async Task SyncResolvedClientsAsync_CachesOnlyIncludedFoundNonDuplicateRows()
    {
        var review = CohortReviewService.Load(SourcePath, ReviewPath);
        review.Rows[1].ClientId = "00042";
        review.Rows[1].Excluded = true;
        review.RefreshDuplicates();
        var manager = new DbManager(_directory, "cache.db");

        var result = await review.SyncResolvedClientsAsync(manager);

        Assert.Equal(1, result.SavedClients);
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "cache.db"), Pooling = false }.ToString()))
        {
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PhisClientCache;"));
            Assert.Equal("001", await connection.ExecuteScalarAsync<string>("SELECT ClientId FROM PhisClientCache;"));
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task SyncResolvedClientsAsync_UsesManualDemographicsAndMedicare()
    {
        var review = CohortReviewService.Load(SourcePath, ReviewPath);
        review.Rows[0].FullName = "Corrected Person";
        review.Rows[0].DateOfBirth = "26/02/2020";
        review.Rows[0].Medicare = "001234567";
        var manager = new DbManager(_directory, "cache.db");

        await review.SyncResolvedClientsAsync(manager);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "cache.db"), Pooling = false }.ToString());
        var cached = await connection.QuerySingleAsync<ConsentSync.Data.Entities.PhisClientCacheEntity>("SELECT * FROM PhisClientCache;");
        Assert.Equal("Corrected Person", cached.FullName);
        Assert.Equal("2020/02/26", cached.DateOfBirth);
        Assert.Equal("001234567", cached.Medicare);
        Assert.Equal(ConsentSync.Data.Entities.ClientSource.ManualReview, cached.Source);
        SqliteConnection.ClearAllPools();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A#B")]
    [InlineData("A#B###95.0%")]
    [InlineData("A#B##12x#95.0%")]
    [InlineData("A#B##001#95.0%#extra")]
    public void MalformedHints_AreNotAccepted(string? hint)
        => Assert.False(CohortReviewService.TryGetSuggestedClientId(hint, out _));

    [Fact]
    public void ValidHint_PreservesLeadingZeros()
    {
        Assert.True(CohortReviewService.TryGetSuggestedClientId("A#B##00042#95.0%", out var id));
        Assert.Equal("00042", id);
    }

    [Fact]
    public void InvalidRowOrder_IsRejectedWithoutOverwritingSavedWork()
    {
        var review = CohortReviewService.Load(SourcePath, ReviewPath);
        review.Save();
        string invalid = File.ReadAllText(ReviewPath).Replace("\"RowNumber\": 1", "\"RowNumber\": 9");
        File.WriteAllText(ReviewPath, invalid);
        Assert.Throws<InvalidDataException>(() => CohortReviewService.Load(SourcePath, ReviewPath));
        Assert.Equal(invalid, File.ReadAllText(ReviewPath));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
