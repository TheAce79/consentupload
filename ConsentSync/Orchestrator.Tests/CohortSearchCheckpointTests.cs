using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Phis;
using Xunit;

namespace Orchestrator.Tests;

public sealed class CohortSearchCheckpointTests
{
    [Fact]
    public void RecoverUnsearchedAbleAssessRows_ResetsOnlyTheAffectedRowsAndPreservesOrder()
    {
        var records = Enumerable.Range(1, 20).Select(index => new ClinicPdfClientRecord
        {
            FullName = $"Client {index}", DateOfBirth = "2020-01-01", BookingId = $"BOOK-{index}",
            ClientIdStatus = ClientIdStatus.NeedsManualReview
        }).ToList();
        records[0].ClientId = "1511687";
        records[0].ClientIdStatus = ClientIdStatus.Found;

        int recovered = CohortSearchCheckpoint.RecoverUnsearchedAbleAssessRows(records);

        Assert.Equal(19, recovered);
        Assert.Equal("1511687", records[0].ClientId);
        Assert.Equal(ClientIdStatus.Found, records[0].ClientIdStatus);
        Assert.Equal(19, records.Count(record => record.ClientIdStatus == ClientIdStatus.NotProcessed));
        Assert.Equal(Enumerable.Range(1, 20).Select(index => $"Client {index}"), records.Select(record => record.FullName));
    }

    [Fact]
    public void RecoverUnsearchedAbleAssessRows_PreservesCompletedManualReviews()
    {
        var records = new List<ClinicPdfClientRecord>
        {
            new() { FullName = "Pending", DateOfBirth = "2020-01-01", BookingId = "BOOK-1", ClientIdStatus = ClientIdStatus.NeedsManualReview },
            new() { FullName = "Failed", DateOfBirth = "2020-01-01", BookingId = "BOOK-2", ClientIdStatus = ClientIdStatus.NeedsManualReview, ErrorDetails = "NameMatchBelowThreshold" },
            new() { FullName = "Hinted", DateOfBirth = "2020-01-01", BookingId = "BOOK-3", ClientIdStatus = ClientIdStatus.NeedsManualReview, BestMatch = "A#B##123#95.0%" },
            new() { FullName = "Canonical", DateOfBirth = "2020-01-01", ClientIdStatus = ClientIdStatus.NeedsManualReview }
        };

        Assert.Equal(1, CohortSearchCheckpoint.RecoverUnsearchedAbleAssessRows(records));
        Assert.Equal(ClientIdStatus.NotProcessed, records[0].ClientIdStatus);
        Assert.All(records.Skip(1), record => Assert.Equal(ClientIdStatus.NeedsManualReview, record.ClientIdStatus));
    }

    [Fact]
    public void SaveAndLoad_PreservesDuplicateRowsByPosition()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ConsentSyncTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "cohort.csv.search-progress.json");
        try
        {
            var checkpoint = new CohortSearchCheckpoint
            {
                InputFingerprint = "input", CohortIdentity = "cohort",
                Records =
                [
                    new ClinicPdfClientRecord { FullName = "Duplicate", DateOfBirth = "2017/01/01", ClientIdStatus = ClientIdStatus.Found, ClientId = "1" },
                    new ClinicPdfClientRecord { FullName = "Duplicate", DateOfBirth = "2017/01/01", ClientIdStatus = ClientIdStatus.NeedsManualReview, ErrorDetails = "Timeout" }
                ]
            };
            CohortSearchCheckpoint.SaveAtomically(path, checkpoint);
            CohortSearchCheckpoint? loaded = CohortSearchCheckpoint.TryLoadCompatible(path, "input", "cohort", out string? problem);
            Assert.Null(problem);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.Records.Count);
            Assert.Equal("1", loaded.Records[0].ClientId);
            Assert.Equal(ClientIdStatus.NeedsManualReview, loaded.Records[1].ClientIdStatus);
            Assert.Null(CohortSearchCheckpoint.TryLoadCompatible(path, "changed", "cohort", out problem));
            Assert.NotNull(problem);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
