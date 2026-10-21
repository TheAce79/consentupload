using System.Security.Cryptography;
using System.Text.Json;
using ConsentSyncCore.Models;

namespace ConsentSyncCore.Services.Phis;

/// <summary>Durable, per-roster PHIS search state. The output CSV remains the user-facing copy.</summary>
public sealed class CohortSearchCheckpoint
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public required string InputFingerprint { get; init; }
    public required string CohortIdentity { get; init; }
    public required List<ClinicPdfClientRecord> Records { get; init; }

    public static string GetPath(string outputCsvPath) => outputCsvPath + ".search-progress.json";

    /// <summary>
    /// Repairs the transient state written by the initial Able Assess import. Those rows had not
    /// been searched, but were incorrectly labelled as requiring manual review.
    /// </summary>
    public static int RecoverUnsearchedAbleAssessRows(IEnumerable<ClinicPdfClientRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        int recovered = 0;
        foreach (ClinicPdfClientRecord record in records)
        {
            if (string.IsNullOrWhiteSpace(record.BookingId) ||
                record.ClientIdStatus != ClientIdStatus.NeedsManualReview ||
                !string.IsNullOrWhiteSpace(record.ClientId) ||
                !string.IsNullOrWhiteSpace(record.ErrorDetails) ||
                !string.IsNullOrWhiteSpace(record.BestMatch))
            {
                continue;
            }

            record.ClientIdStatus = ClientIdStatus.NotProcessed;
            recovered++;
        }
        return recovered;
    }

    public static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static CohortSearchCheckpoint? TryLoadCompatible(string path, string inputFingerprint, string cohortIdentity, out string? problem)
    {
        problem = null;
        if (!File.Exists(path)) return null;
        try
        {
            var checkpoint = JsonSerializer.Deserialize<CohortSearchCheckpoint>(File.ReadAllText(path));
            if (checkpoint is null || checkpoint.Version != CurrentVersion || checkpoint.Records is null)
            {
                problem = "The saved PHIS progress file has an unsupported format.";
                return null;
            }
            if (!string.Equals(checkpoint.InputFingerprint, inputFingerprint, StringComparison.Ordinal) ||
                !string.Equals(checkpoint.CohortIdentity, cohortIdentity, StringComparison.Ordinal))
            {
                problem = "The saved PHIS progress belongs to a different input CSV or cohort.";
                return null;
            }
            return checkpoint;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            problem = $"The saved PHIS progress file could not be read: {ex.Message}";
            return null;
        }
    }

    public static void SaveAtomically(string path, CohortSearchCheckpoint checkpoint)
    {
        string directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Checkpoint path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
