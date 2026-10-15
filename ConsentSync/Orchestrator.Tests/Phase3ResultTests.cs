using Orchestrator.Phase3;
using Xunit;

namespace Orchestrator.Tests;

public sealed class Phase3ResultTests
{
    [Fact]
    public void IsSuccessful_requires_every_selected_record_to_finish_without_failures()
    {
        var result = new Phase3Result
        {
            TotalRecords = 7,
            AttemptedRecords = 7,
            SuccessfulUploads = 3,
            AlreadyExistingDocuments = 4
        };

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Failed_records_prevent_a_success_dialog_even_when_all_records_were_attempted()
    {
        var result = new Phase3Result
        {
            TotalRecords = 7,
            AttemptedRecords = 7,
            FailedUploads = 7,
            HasErrors = true
        };

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public void Empty_and_interrupted_runs_are_not_successful()
    {
        Assert.False(new Phase3Result { NoRecordsFound = true }.IsSuccessful);
        Assert.False(new Phase3Result { TotalRecords = 7, AttemptedRecords = 3 }.IsSuccessful);
        Assert.False(new Phase3Result { TotalRecords = 7, AttemptedRecords = 7, BatchLimitReached = true }.IsSuccessful);
    }
}
