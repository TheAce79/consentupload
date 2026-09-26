using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Eligibility;
using Xunit;

namespace Orchestrator.Tests;

public sealed class EligibilityEvaluationServiceTests
{
    private readonly EligibilityEvaluationService _service = new();

    [Theory]
    [InlineData("2 Month Appointment", "2026-03-01", "2026-05-01", EligibilityStatus.Eligible)]
    [InlineData("2 Month Appointment", "2026-03-02", "2026-05-01", EligibilityStatus.Ineligible)]
    [InlineData("12 Month Appointment", "2025-05-01", "2026-05-01", EligibilityStatus.Eligible)]
    [InlineData("12 Month Appointment", "2025-05-02", "2026-05-01", EligibilityStatus.Ineligible)]
    [InlineData("Preschool Appointment", "2022-05-01", "2026-05-01", EligibilityStatus.Eligible)]
    [InlineData("Preschool Appointment", "2022-05-02", "2026-05-01", EligibilityStatus.Ineligible)]
    public void AgeRules_ApplyAtBoundary(string vaccineType, string dob, string clinic, EligibilityStatus expected)
    {
        EligibilityEvaluationResult result = Evaluate(vaccineType, dob, clinic, History());

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData("4 Month Appointment", "DPT", "2026-04-03", 28, EligibilityStatus.Eligible)]
    [InlineData("4 Month Appointment", "Pneu-C", "2026-04-02", 27, EligibilityStatus.Ineligible)]
    [InlineData("6 Month Appointment", "DPT", "2026-05-28", 56, EligibilityStatus.Eligible)]
    [InlineData("6 Month Appointment", "Pneu-C", "2026-05-27", 55, EligibilityStatus.Ineligible)]
    [InlineData("18 Month Appointment", "MMRV", "2027-10-28", 180, EligibilityStatus.Eligible)]
    [InlineData("18 Month Appointment", "MMR", "2027-10-27", 179, EligibilityStatus.Ineligible)]
    public void IntervalRules_UseLatestMatchingAgentDate(string vaccineType, string agent, string clinic, int expectedInterval, EligibilityStatus expected)
    {
        DateTime prior = vaccineType.StartsWith("18", StringComparison.Ordinal) ? new(2027, 5, 1) :
            vaccineType.StartsWith("6", StringComparison.Ordinal) ? new(2026, 4, 2) : new(2026, 3, 6);
        string dob = "2026-01-01";
        var history = History((agent, prior), (agent, prior.AddDays(-4)));

        EligibilityEvaluationResult result = Evaluate(vaccineType, dob, clinic, history);

        Assert.Equal(expected, result.Status);
        Assert.Contains($"Interval = {expectedInterval} days", result.EvaluationReason);
    }

    [Fact]
    public void MissingMatchingDoseWithinHistory_IsEligibleWithExplanation()
    {
        EligibilityEvaluationResult result = Evaluate("4 Month Appointment", "2026-01-01", "2026-06-01", History(("BCG", new DateTime(2026, 1, 2))));

        Assert.Equal(EligibilityStatus.Eligible, result.Status);
        Assert.Contains("Previous 2-month dose not found", result.EvaluationReason);
    }

    [Theory]
    [InlineData("Other / Autre", 0, EligibilityStatus.Ineligible)]
    [InlineData("Other / Autre", 1, EligibilityStatus.Eligible)]
    [InlineData("Unknown", 1, EligibilityStatus.ManualReview)]
    public void OtherAndUnknownRules_UseHistoryAndManualReview(string vaccineType, int doses, EligibilityStatus expected)
    {
        var history = History();
        for (int index = 0; index < doses; index++) history.Records.Add(new ImmunizationRecord { Agent = "BCG", AdministeredDate = new DateTime(2026, 1, 2) });

        Assert.Equal(expected, Evaluate(vaccineType, "2025-01-01", "2026-06-01", history).Status);
    }

    [Fact]
    public void MissingHistoryInvalidDatesAndNonNb_AreManualReview()
    {
        Assert.Equal(EligibilityStatus.ManualReview, Evaluate("4 Month Appointment", "2025-01-01", "2026-06-01", null).Status);
        Assert.Equal(EligibilityStatus.ManualReview, Evaluate("4 Month Appointment", "not-a-date", "2026-06-01", History()).Status);
        Assert.Equal(EligibilityStatus.ManualReview, Evaluate("4 Month Appointment", "2025-01-01", "not-a-date", History(), new DateTime(2026, 6, 1), "NS").Status);
    }

    [Fact]
    public void MissingClinicDate_UsesSavedCohortDate()
    {
        EligibilityEvaluationResult result = Evaluate("2 Month Appointment", "2026-03-01", null, History(), new DateTime(2026, 5, 1));

        Assert.Equal(EligibilityStatus.Eligible, result.Status);
        Assert.Equal(new DateTime(2026, 5, 1), result.ClinicDate);
    }

    private EligibilityEvaluationResult Evaluate(string vaccineType, string dob, string? clinic, ClientImmunizationHistory? history, DateTime? cohortDate = null, string jurisdiction = "NB") =>
        _service.EvaluateRecord(new CohortReviewRow
        {
            Source = new ClinicPdfClientRecord
            {
                ClientId = "100", FullName = "Test Client", DateOfBirth = dob, ClinicDate = clinic,
                VaccineType = vaccineType, ClientIdStatus = ClientIdStatus.Found
            }
        }, history, cohortDate ?? new DateTime(2026, 6, 1), jurisdiction);

    private static ClientImmunizationHistory History(params (string Agent, DateTime Date)[] records)
    {
        var history = new ClientImmunizationHistory { ClientId = "100", FullName = "Test Client" };
        foreach ((string agent, DateTime date) in records)
            history.Records.Add(new ImmunizationRecord { Agent = agent, AdministeredDate = date });
        return history;
    }
}
