using System.Security.Cryptography;
using System.Text.Json;
using ConsentSync.Data;
using ConsentSync.Data.Entities;
using ConsentSyncCore.Models;

namespace ConsentSyncCore.Services.Csv;

public sealed class CohortReviewService
{
    public string SourcePath { get; }
    public string ReviewPath { get; }
    public string SourceFingerprint { get; private set; }
    public List<CohortReviewRow> Rows { get; }
    private List<PhisClientCacheEntity>? _lastCacheSyncCandidates;

    private CohortReviewService(string sourcePath, string reviewPath)
    {
        SourcePath = sourcePath;
        ReviewPath = reviewPath;
        SourceFingerprint = Fingerprint(sourcePath);
        Rows = CsvImporterService.ReadFromCsv(sourcePath)
            .Select((source, index) => new CohortReviewRow { Source = source, RowNumber = index + 1 }).ToList();
        EnsureSourceUnchanged();
    }

    public static CohortReviewService Load(string sourcePath, string reviewPath, bool startFresh = false)
    {
        var review = new CohortReviewService(sourcePath, reviewPath);
        if (!startFresh && File.Exists(reviewPath))
        {
            var saved = JsonSerializer.Deserialize<ReviewDocument>(File.ReadAllText(reviewPath));
            if (saved is null || saved.Version != 1 || saved.SourceFingerprint != review.SourceFingerprint ||
                saved.Rows is null || saved.Rows.Count != review.Rows.Count ||
                saved.Rows.Where((row, index) => row is null || row.RowNumber != index + 1).Any())
                throw new InvalidDataException("Saved review does not match this source CSV or has an unsupported format. Start a fresh review explicitly to replace it.");
            for (int i = 0; i < review.Rows.Count; i++)
            {
                review.Rows[i].ClientIdOverride = saved.Rows[i].ClientIdOverride;
                review.Rows[i].FullNameOverride = saved.Rows[i].FullNameOverride;
                review.Rows[i].DateOfBirthOverride = saved.Rows[i].DateOfBirthOverride;
                review.Rows[i].MedicareOverride = saved.Rows[i].MedicareOverride;
                review.Rows[i].Excluded = saved.Rows[i].Excluded;
            }
        }
        review.RefreshDuplicates();
        return review;
    }

    public void RefreshDuplicates()
    {
        var duplicates = Rows.Where(r => !r.Excluded && !string.IsNullOrWhiteSpace(r.ClientId))
            .GroupBy(r => r.ClientId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
            row.DuplicateId = !row.Excluded && duplicates.Contains(row.ClientId.Trim());
    }

    public static bool TryGetSuggestedClientId(string? hint, out string clientId)
    {
        clientId = string.Empty;
        var parts = hint?.Split('#');
        if (parts is not { Length: 5 }) return false;
        string candidate = parts[3].Trim();
        if (candidate.Length == 0 || candidate.Any(c => c < '0' || c > '9')) return false;
        clientId = candidate;
        return true;
    }

    public void Save()
    {
        EnsureSourceUnchanged();
        RefreshDuplicates();
        string transactionId = Guid.NewGuid().ToString("N");
        string stagedCsv = SourcePath + "." + transactionId + ".csv.tmp";
        string stagedReview = ReviewPath + "." + transactionId + ".json.tmp";
        string csvBackup = SourcePath + "." + transactionId + ".csv.bak";
        string reviewBackup = ReviewPath + "." + transactionId + ".json.bak";
        bool hadReview = File.Exists(ReviewPath);
        bool retainRecoveryFiles = false;
        var saved = new ReviewDocument
        {
            Rows = CreateEntries()
        };
        try
        {
            CsvExporterService.SaveToCsv(Rows.Select(Materialize), stagedCsv);
            saved.SourceFingerprint = Fingerprint(stagedCsv);
            File.WriteAllText(stagedReview, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            EnsureSourceUnchanged();
            File.Move(SourcePath, csvBackup);
            File.Move(stagedCsv, SourcePath);
            if (hadReview) File.Move(ReviewPath, reviewBackup);
            File.Move(stagedReview, ReviewPath);
            SourceFingerprint = saved.SourceFingerprint;
        }
        catch (Exception saveError)
        {
            var recovery = RestorePreviousPair(csvBackup, reviewBackup, hadReview);
            if (recovery is not null)
            {
                retainRecoveryFiles = true;
                throw new IOException($"Review save failed and rollback also failed. Recovery files were retained: {csvBackup}; {reviewBackup}. {recovery.Message}", saveError);
            }
            throw;
        }
        finally
        {
            DeleteIfExists(stagedCsv); DeleteIfExists(stagedReview);
            if (!retainRecoveryFiles) { DeleteIfExists(csvBackup); DeleteIfExists(reviewBackup); }
        }
    }

    public async Task<CacheSyncResult> SyncResolvedClientsAsync(IConsentSyncRepository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var candidates = Rows.Where(row => !row.Excluded && !row.DuplicateId && row.SearchStatus == ClientIdStatus.Found && !string.IsNullOrWhiteSpace(row.ClientId))
            .Select(row => new { Row = row, Key = DbManager.BuildCacheKey(row.FullName, row.DateOfBirth) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)).ToList();
        var conflicts = candidates.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Row.ClientId).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entities = candidates.Where(item => !conflicts.Contains(item.Key)).GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Row).Select(row => new PhisClientCacheEntity
            {
                CacheKey = DbManager.BuildCacheKey(row.FullName, row.DateOfBirth), ClientId = row.ClientId,
                FullName = row.FullName, DateOfBirth = row.DateOfBirth, Email = row.Email,
                // Null preserves an existing cache value. An empty override intentionally clears it.
                Medicare = row.MedicareOverride,
                Source = row.HasManualCorrection ? ClientSource.ManualReview : ClientSource.PhisSearch,
                UpdatedOn = DateTime.UtcNow
            }).ToList();
        _lastCacheSyncCandidates = entities;
        if (entities.Count > 0) await repository.BulkSaveClientCacheAsync(entities, cancellationToken);
        return new CacheSyncResult(entities.Count, conflicts.Count, Rows.Count - candidates.Count);
    }

    public async Task<int> RetryLastCacheSyncAsync(IConsentSyncRepository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (_lastCacheSyncCandidates is null)
        {
            var result = await SyncResolvedClientsAsync(repository, cancellationToken);
            return result.SavedClients;
        }
        await repository.BulkSaveClientCacheAsync(_lastCacheSyncCandidates, cancellationToken);
        return _lastCacheSyncCandidates.Count;
    }

    private List<ReviewEntry> CreateEntries() => Rows.Select(r => new ReviewEntry
    {
        RowNumber = r.RowNumber, ClientIdOverride = r.ClientIdOverride,
        FullNameOverride = r.FullNameOverride, DateOfBirthOverride = r.DateOfBirthOverride,
        MedicareOverride = r.MedicareOverride, Excluded = r.Excluded
    }).ToList();

    private static ClinicPdfClientRecord Materialize(CohortReviewRow row) => new()
    {
        ClientId = string.IsNullOrWhiteSpace(row.ClientId) ? null : row.ClientId,
        FullName = row.FullName, DateOfBirth = row.DateOfBirth, Medicare = row.Medicare, VaccineType = row.Source.VaccineType,
        ClientIdStatus = row.SearchStatus, FirstName = row.Source.FirstName, LastName = row.Source.LastName,
        MiddleName = row.Source.MiddleName, ErrorDetails = row.ErrorDetails, BestMatch = row.Source.BestMatch,
        Phone = row.Source.Phone, Email = row.Source.Email
    };

    private Exception? RestorePreviousPair(string csvBackup, string reviewBackup, bool hadReview)
    {
        try
        {
            if (File.Exists(csvBackup)) { DeleteIfExists(SourcePath); File.Move(csvBackup, SourcePath); }
            if (hadReview && File.Exists(reviewBackup)) { DeleteIfExists(ReviewPath); File.Move(reviewBackup, ReviewPath); }
            if (!hadReview) DeleteIfExists(ReviewPath);
            return null;
        }
        catch (Exception ex) { return ex; }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private void EnsureSourceUnchanged()
    {
        if (Fingerprint(SourcePath) != SourceFingerprint)
            throw new InvalidDataException("The source CSV changed. Reload it and explicitly start a fresh review before saving.");
    }

    private static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public sealed class ReviewDocument
    {
        public int Version { get; set; } = 1;
        public string SourceFingerprint { get; set; } = string.Empty;
        public List<ReviewEntry> Rows { get; set; } = [];
    }

    public sealed class ReviewEntry
    {
        public int RowNumber { get; set; }
        public string? ClientIdOverride { get; set; }
        public string? FullNameOverride { get; set; }
        public string? DateOfBirthOverride { get; set; }
        public string? MedicareOverride { get; set; }
        public bool Excluded { get; set; }
    }

    public sealed record CacheSyncResult(int SavedClients, int ConflictingIdentities, int SkippedRows);
}
