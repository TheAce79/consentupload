using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Phis;
using Xunit;

namespace Orchestrator.Tests;

public sealed class CohortSearchCheckpointTests
{
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
