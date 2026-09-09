using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Phis;
using Xunit;

namespace Orchestrator.Tests;

public sealed class CohortPhisSearchRunnerTests
{
    [Fact]
    public async Task ExecuteSearchAsync_ResolvesInvertedAccentedNameAndEnrichesRecord()
    {
        var search = new FakeSearch { Dob = Success(Active("42", "LUC OLIVIER", "KOMBOU TCHAPDU", "MARTIN")) };
        var record = new ClinicPdfClientRecord { FullName = "Kombou Tchapdu, Luc Olivier Martin", DateOfBirth = "2017/10/01" };

        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);

        Assert.Equal(ClientIdStatus.Found, record.ClientIdStatus);
        Assert.Equal("42", record.ClientId);
        Assert.Equal("LUC OLIVIER", record.FirstName);
        Assert.Equal("KOMBOU TCHAPDU", record.LastName);
        Assert.Equal("MARTIN", record.MiddleName);
        Assert.Equal("LUC OLIVIER#KOMBOU TCHAPDU#MARTIN#42#100.0%", record.BestMatch);
    }

    [Fact]
    public async Task ExecuteSearchAsync_OneMatchingNameAmongMultipleActiveCandidatesResolves()
    {
        var search = new FakeSearch { Dob = Success(Active("1", "OTHER", "PERSON"), Active("2", "A", "B")) };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01", Medicare = "001" };
        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);
        Assert.Equal(ClientIdStatus.Found, record.ClientIdStatus);
        Assert.Equal("2", record.ClientId);
        Assert.Equal(0, search.MedicareCalls);
    }

    [Fact]
    public async Task ExecuteSearchAsync_OneMatchingNameAmongEighteenActiveCandidatesResolves()
    {
        var candidates = Enumerable.Range(1, 17)
            .Select(index => Active(index.ToString(), $"OTHER{index}", $"PERSON{index}"))
            .Append(Active("18", "TARGET", "PERSON"))
            .ToArray();
        var record = new ClinicPdfClientRecord { FullName = "Target Person", DateOfBirth = "2017/10/01" };

        await new CohortPhisSearchRunner(new FakeSearch { Dob = Success(candidates) }, 75).ExecuteSearchAsync([record]);

        Assert.Equal(ClientIdStatus.Found, record.ClientIdStatus);
        Assert.Equal("18", record.ClientId);
    }

    [Fact]
    public async Task ExecuteSearchAsync_ThirtyDobCandidatesWithOneSubsetMatchResolvesAndSetsHint()
    {
        var candidates = Enumerable.Range(1, 29)
            .Select(index => Active(index.ToString(), $"OTHER{index}", $"PERSON{index}"))
            .Append(Active("1511068", "ARIEL DELANGE", "KANATE SORY"))
            .ToArray();
        var record = new ClinicPdfClientRecord { FullName = "Ariel KANATE SORY", DateOfBirth = "2011/08/25" };

        await new CohortPhisSearchRunner(new FakeSearch { Dob = Success(candidates) }, 75).ExecuteSearchAsync([record]);

        Assert.Equal(ClientIdStatus.Found, record.ClientIdStatus);
        Assert.Equal("1511068", record.ClientId);
        Assert.Equal("ARIEL DELANGE#KANATE SORY##1511068#100.0%", record.BestMatch);
    }

    [Fact]
    public async Task ExecuteSearchAsync_MultipleMatchingNamesNeedManualReview()
    {
        var search = new FakeSearch { Dob = Success(Active("1", "A", "B"), Active("2", "B", "A")) };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01" };
        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);
        Assert.Equal("MultipleMatchingClientsFound", record.ErrorDetails);
    }

    [Fact]
    public async Task ExecuteSearchAsync_TokenAndFuzzyMatchesNeedManualReviewWithoutMedicare()
    {
        var search = new FakeSearch { Dob = Success(Active("1", "A", "B"), Active("2", "A", "C")), Medicare = Success(Active("9", "A", "B")) };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01", Medicare = "001" };

        await new CohortPhisSearchRunner(search, 40).ExecuteSearchAsync([record]);

        Assert.Equal("MultipleMatchingClientsFound", record.ErrorDetails);
        Assert.Equal(0, search.MedicareCalls);
    }

    [Fact]
    public async Task ExecuteSearchAsync_RepeatedSourceTokensRequireRepeatedCandidateTokens()
    {
        var search = new FakeSearch { Dob = Success(Active("1", "ANA", "LEE")) };
        var record = new ClinicPdfClientRecord { FullName = "Ana Ana Lee", DateOfBirth = "2017/10/01" };

        await new CohortPhisSearchRunner(search, 100).ExecuteSearchAsync([record]);

        Assert.Equal("NameMatchBelowThreshold", record.ErrorDetails);
        Assert.Equal("ANA#LEE##1#66.7%", record.BestMatch);
    }

    [Fact]
    public async Task ExecuteSearchAsync_LowNameScoreUsesMedicareFallback()
    {
        var search = new FakeSearch { Dob = Success(Active("1", "OTHER", "PERSON")), Medicare = Success(Active("9", "PHIS", "NAME", "MID")) };
        var record = new ClinicPdfClientRecord { FullName = "Wrong Name", DateOfBirth = "2017/10/01", Medicare = "002" };
        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);
        Assert.Equal("9", record.ClientId);
        Assert.Equal(1, search.MedicareCalls);
        Assert.Equal("PHIS#NAME#MID#9#36.4%", record.BestMatch);
    }

    [Fact]
    public async Task ExecuteSearchAsync_NoMedicareResultRetainsDobHintAndClearsStaleHint()
    {
        var search = new FakeSearch { Dob = Success(Active("1", "OTHER", "PERSON")), Medicare = SearchResult.NoResults() };
        var record = new ClinicPdfClientRecord { FullName = "Wrong Name", DateOfBirth = "2017/10/01", Medicare = "002", BestMatch = "stale" };

        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);

        Assert.Equal("NameMatchBelowThreshold", record.ErrorDetails);
        Assert.Equal("OTHER#PERSON##1#27.3%", record.BestMatch);
    }

    [Fact]
    public async Task ExecuteSearchAsync_MultipleMedicareResultsSetHighestScoringHint()
    {
        var search = new FakeSearch
        {
            Dob = Success(Active("1", "OTHER", "PERSON")),
            Medicare = Success(Active("9", "PHIS", "NAME"), Active("10", "WRONG", "NAME"))
        };
        var record = new ClinicPdfClientRecord { FullName = "Phis Name", DateOfBirth = "2017/10/01", Medicare = "002" };

        await new CohortPhisSearchRunner(search, 100).ExecuteSearchAsync([record]);

        Assert.Equal("MultipleClientsFoundInPhis", record.ErrorDetails);
        Assert.Equal("PHIS#NAME##9#100.0%", record.BestMatch);
    }

    [Fact]
    public async Task ExecuteSearchAsync_UnknownStatusNeedsReview()
    {
        var unknown = new PhisSearchResult { ClientId = "1", FirstName = "A", LastName = "B", ActiveStatus = "Pending" };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01" };
        await new CohortPhisSearchRunner(new FakeSearch { Dob = Success(unknown) }, 75).ExecuteSearchAsync([record]);
        Assert.Equal("ActiveStatusUnavailable", record.ErrorDetails);
    }

    [Fact]
    public async Task ExecuteSearchAsync_MatchingCandidateWithoutClientIdNeedsReview()
    {
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01" };

        await new CohortPhisSearchRunner(new FakeSearch { Dob = Success(Active("", "A", "B")) }, 75).ExecuteSearchAsync([record]);

        Assert.Equal("MissingClientId", record.ErrorDetails);
        Assert.Equal("A#B###100.0%", record.BestMatch);
    }

    [Fact]
    public async Task ExecuteSearchAsync_FailedSearchThrowsAndDoesNotContinue()
    {
        var search = new FakeSearch { Dob = SearchResult.Failed("timeout") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CohortPhisSearchRunner(search).ExecuteSearchAsync([
            new ClinicPdfClientRecord { FullName = "A", DateOfBirth = "2017/10/01" }]));
    }

    [Fact]
    public async Task ExecuteSearchAsync_IncompleteResultsThrowAndDoNotResolve()
    {
        var incomplete = SearchResult.IsSuccess([Active("1", "A", "B")], resultsComplete: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CohortPhisSearchRunner(new FakeSearch { Dob = incomplete }).ExecuteSearchAsync([
            new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01" }]));
    }

    [Fact]
    public async Task ExecuteSearchAsync_PopulatesEmptyEmailFromResolvedPreview()
    {
        var search = new FakeSearch
        {
            Dob = Success(Active("42", "A", "B")),
            Preview = new PhisClientPreview { ClientId = "42", EmailAddresses = [new() { Address = "parent@example.test", IsPreferred = true }] }
        };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01" };

        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);

        Assert.Equal("parent@example.test", record.Email);
        Assert.Equal(1, search.PreviewCalls);
    }

    [Fact]
    public async Task ExecuteSearchAsync_PreservesExistingEmailAndSkipsPreview()
    {
        var search = new FakeSearch { Dob = Success(Active("42", "A", "B")) };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01", Email = "supplied value" };

        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);

        Assert.Equal("supplied value", record.Email);
        Assert.Equal(0, search.PreviewCalls);
    }

    [Fact]
    public async Task ExecuteSearchAsync_LeavesEmailBlankWhenPreviewHasMultipleEligibleAddresses()
    {
        var search = new FakeSearch
        {
            Dob = Success(Active("42", "A", "B")),
            Preview = new PhisClientPreview
            {
                ClientId = "42",
                EmailAddresses = [new() { Address = "one@example.test" }, new() { Address = "two@example.test" }]
            }
        };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01" };

        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);

        Assert.Null(record.Email);
        Assert.Equal(ClientIdStatus.Found, record.ClientIdStatus);
    }

    [Fact]
    public async Task ExecuteSearchAsync_DoesNotUsePreviewForUnresolvedRecord()
    {
        var search = new FakeSearch { Dob = Success(Active("42", "Other", "Person")) };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01" };

        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);

        Assert.Equal(0, search.PreviewCalls);
        Assert.Equal(ClientIdStatus.NeedsManualReview, record.ClientIdStatus);
    }

    [Fact]
    public async Task ExecuteSearchAsync_UsesEmailFallbackWhenUniqueActiveResultHasSameDob()
    {
        var search = new FakeSearch
        {
            Dob = Success(Active("1", "Other", "Person")),
            Email = Success(Active("42", "Different", "Name", dateOfBirth: "2017 Oct 01"))
        };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01", Email = "parent@example.test" };

        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);

        Assert.Equal(ClientIdStatus.Found, record.ClientIdStatus);
        Assert.Equal("42", record.ClientId);
        Assert.Equal(1, search.EmailCalls);
    }

    [Fact]
    public async Task ExecuteSearchAsync_EmailDobMismatchNeedsManualReviewWithBestMatch()
    {
        var search = new FakeSearch
        {
            Dob = Success(Active("1", "Other", "Person")),
            Email = Success(Active("42", "A", "B", dateOfBirth: "2017 Oct 02"))
        };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01", Email = "parent@example.test" };

        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);

        Assert.Equal(ClientIdStatus.NeedsManualReview, record.ClientIdStatus);
        Assert.Equal("EmailDateOfBirthMismatch", record.ErrorDetails);
        Assert.Contains("#42#", record.BestMatch);
    }

    [Fact]
    public async Task ExecuteSearchAsync_EmailSearchFailureContinuesWithPriorOutcome()
    {
        var search = new FakeSearch { Dob = Success(Active("1", "Other", "Person")), ThrowOnEmailSearch = true };
        var record = new ClinicPdfClientRecord { FullName = "A B", DateOfBirth = "2017/10/01", Email = "parent@example.test" };

        await new CohortPhisSearchRunner(search, 75).ExecuteSearchAsync([record]);

        Assert.Equal(ClientIdStatus.NeedsManualReview, record.ClientIdStatus);
        Assert.Equal("NameMatchBelowThreshold", record.ErrorDetails);
        Assert.Equal(1, search.EmailCalls);
    }

    private static PhisSearchResult Active(string id, string first, string last, string middle = "", string dateOfBirth = "") => new() { ClientId = id, FirstName = first, LastName = last, MiddleName = middle, DateOfBirth = dateOfBirth, ActiveStatus = "Active" };
    private static SearchResult Success(params PhisSearchResult[] results) => SearchResult.IsSuccess(results.ToList());

    private sealed class FakeSearch : IPhisClientSearch
    {
        public SearchResult Dob { get; init; } = SearchResult.NoResults();
        public SearchResult Medicare { get; init; } = SearchResult.NoResults();
        public SearchResult Email { get; init; } = SearchResult.NoResults();
        public int MedicareCalls { get; private set; }
        public int EmailCalls { get; private set; }
        public bool ThrowOnEmailSearch { get; init; }
        public int PreviewCalls { get; private set; }
        public PhisClientPreview? Preview { get; init; }
        public Task<SearchResult> SearchByDobAsync(string dateOfBirth, string? expectedFirstName = null, string? expectedLastName = null, string? expectedMedicare = null) => Task.FromResult(Dob);
        public Task<SearchResult> SearchByMedicareAsync(string medicareNumber) { MedicareCalls++; return Task.FromResult(Medicare); }
        public Task<SearchResult> SearchByEmailAsync(string email) { EmailCalls++; if (ThrowOnEmailSearch) throw new InvalidOperationException("browser failure"); return Task.FromResult(Email); }
        public Task<PhisClientPreview?> GetClientPreviewAsync(string clientId) { PreviewCalls++; return Task.FromResult(Preview); }
    }
}
