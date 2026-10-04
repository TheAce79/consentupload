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
    public void IntervalRules_UseLatestMatchingAgentDate(string vaccineType, string agent, string clinic, int expectedInterval, EligibilityStatus expected)
    {
        DateTime prior = vaccineType.StartsWith("6", StringComparison.Ordinal) ? new(2026, 4, 2) : new(2026, 3, 6);
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
    [InlineData("Catchup Appointment", 1, EligibilityStatus.ManualReview)]
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

    [Fact]
    public void AgeMonths_UsesCalendarMonthFraction()
    {
        EligibilityEvaluationResult result = Evaluate("ETS", "2022-09-21", "2026-10-01", History());

        Assert.Equal(48.3m, Math.Round(result.AgeMonths!.Value, 1));
    }

    [Fact]
    public void AgeMonths_IsWholeAtCalendarMonthAnniversary()
    {
        EligibilityEvaluationResult result = Evaluate("ETS", "2022-09-21", "2026-09-21", History());

        Assert.Equal(48m, result.AgeMonths);
    }

    [Fact]
    public void Ets_IgnoresRecentMmrvHistoryWithinAgeRange()
    {
        EligibilityEvaluationResult result = Evaluate("ETS", "2025-03-01", "2026-10-01", History(("MMRV", new DateTime(2026, 8, 1))));

        Assert.Equal(EligibilityStatus.Eligible, result.Status);
        Assert.Equal("HTA (ETS) Assessment criteria OK. Client age is 19m.", result.EvaluationReason);
    }

    [Theory]
    [InlineData("ETS+", EligibilityStatus.Eligible)]
    [InlineData("ETS Plus", EligibilityStatus.Eligible)]
    [InlineData("ETS + Vaccine", EligibilityStatus.Eligible)]
    [InlineData("ETS/Vaccine", EligibilityStatus.Eligible)]
    public void EtsPlus_AcceptsAliasesAndNoPriorMmrv(string vaccineType, EligibilityStatus expected)
    {
        EligibilityEvaluationResult result = Evaluate(vaccineType, "2025-03-01", "2026-10-01", History(("BCG", new DateTime(2025, 4, 1))));

        Assert.Equal(expected, result.Status);
        Assert.Contains("HTA (ETS+) criteria OK", result.EvaluationReason);
        Assert.Contains("No prior MMRV dose found", result.EvaluationReason);
    }

    [Theory]
    [InlineData("2026-04-04", EligibilityStatus.Ineligible)]
    [InlineData("2026-04-03", EligibilityStatus.Eligible)]
    public void EtsPlus_RequiresStrictlyMoreThan180DaysSinceLatestMmrv(string priorDose, EligibilityStatus expected)
    {
        EligibilityEvaluationResult result = Evaluate("ETS+", "2025-03-01", "2026-10-01", History(
            ("MMRV", DateTime.Parse(priorDose)), ("MMR", new DateTime(2026, 1, 1))));

        Assert.Equal(expected, result.Status);
        Assert.Contains($"Previous MMRV = {priorDose}", result.EvaluationReason);
    }

    [Theory]
    [InlineData("2025-04-01", EligibilityStatus.Eligible)]
    [InlineData("2025-04-02", EligibilityStatus.Ineligible)]
    [InlineData("2024-10-01", EligibilityStatus.Ineligible)]
    [InlineData("2024-10-02", EligibilityStatus.Eligible)]
    public void EtsAndEtsPlus_Enforce18To24MonthCalendarBoundaries(string dob, EligibilityStatus expected)
    {
        EligibilityEvaluationResult ets = Evaluate("ETS", dob, "2026-10-01", History());
        EligibilityEvaluationResult etsPlus = Evaluate("ETS+", dob, "2026-10-01", History());

        Assert.Equal(expected, ets.Status);
        Assert.Equal(expected, etsPlus.Status);
    }

    [Fact]
    public void EtsAndEtsPlus_MissingHistoryUseNoRecordedMmrvRule()
    {
        Assert.Equal(EligibilityStatus.Eligible, Evaluate("ETS", "2025-03-01", "2026-10-01", null).Status);
        Assert.Equal(EligibilityStatus.Eligible, Evaluate("ETS+", "2025-03-01", "2026-10-01", null).Status);
    }

    [Fact]
    public void EighteenMonthAppointment_AtThirtyMonthsWithoutHistoryIsEligible()
    {
        EligibilityEvaluationResult result = Evaluate("18 Month Appointment", "2024-04-01", "2026-10-01", null);

        Assert.Equal(EligibilityStatus.Eligible, result.Status);
        Assert.Equal("18m Appointment criteria OK. Age = 30m (>= 18m). No prior MMRV dose found in PHIS history.", result.EvaluationReason);
    }

    [Fact]
    public void EighteenMonthAppointment_AtThirtyMonthsWithRecentMmrvIsIneligible()
    {
        EligibilityEvaluationResult result = Evaluate("18 Month Appointment", "2024-04-01", "2026-10-01", History(("MMRV", new DateTime(2026, 7, 1))));

        Assert.Equal(EligibilityStatus.Ineligible, result.Status);
        Assert.Equal("18m Appointment dose interval KO. Previous MMRV = 2026-07-01, Clinic Date = 2026-10-01. Interval <= 6 months.", result.EvaluationReason);
    }

    [Fact]
    public void EtsPlus_AtThirtyMonthsWithoutHistoryIsIneligible()
    {
        EligibilityEvaluationResult result = Evaluate("ETS+", "2024-04-01", "2026-10-01", null);

        Assert.Equal(EligibilityStatus.Ineligible, result.Status);
        Assert.Equal("HTA (ETS+) age criteria KO. Client age is 30m (must be >= 18m and < 24m for Healthy Toddler Assessment).", result.EvaluationReason);
    }

    [Fact]
    public void EtsPlus_AtTwentyOneMonthsWithMmrvMoreThanSixMonthsAgoIsEligible()
    {
        EligibilityEvaluationResult result = Evaluate("ETS+", "2025-01-01", "2026-10-01", History(("MMRV", new DateTime(2026, 3, 1))));

        Assert.Equal(EligibilityStatus.Eligible, result.Status);
        Assert.Equal("HTA (ETS+) criteria OK. Age = 21m (18-23m). Previous MMRV = 2026-03-01, interval > 6 months.", result.EvaluationReason);
    }

    [Fact]
    public void EtsUnknown_IsAlwaysManualReview()
    {
        EligibilityEvaluationResult result = Evaluate("ETS Unknown", "2024-01-01", "2026-10-01", null);

        Assert.Equal(EligibilityStatus.ManualReview, result.Status);
        Assert.Equal("Unrecognized or ambiguous ETS appointment type. Manual review required.", result.EvaluationReason);
    }

    [Fact]
    public void AmbiguousEtsLabel_IsManualReview()
    {
        EligibilityEvaluationResult result = Evaluate("ETS follow-up", "2025-01-01", "2026-10-01", null);

        Assert.Equal(EligibilityStatus.ManualReview, result.Status);
        Assert.Equal("Unrecognized or ambiguous ETS appointment type. Manual review required.", result.EvaluationReason);
    }

    [Theory]
    [InlineData("Assessment (HTA) Appointment", EligibilityStatus.Eligible)]
    [InlineData("18 Month Appointment with Assessment (HTA)", EligibilityStatus.Eligible)]
    public void RawHtaAppointmentLabels_UseTheMatchingEtsRule(string vaccineType, EligibilityStatus expected)
    {
        EligibilityEvaluationResult result = Evaluate(vaccineType, "2025-01-01", "2026-10-01", null);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void EighteenMonthAppointment_UsesLatestMmrvOrMmrDateAndRequiresMoreThan180Days()
    {
        EligibilityEvaluationResult at180 = Evaluate("18 Month Appointment", "2025-01-01", "2026-10-01", History(
            ("MMRV", new DateTime(2026, 3, 1)), ("MMR", new DateTime(2026, 4, 4))));
        EligibilityEvaluationResult at181 = Evaluate("18 Month Appointment", "2025-01-01", "2026-10-01", History(
            ("MMRV", new DateTime(2026, 3, 1)), ("MMR", new DateTime(2026, 4, 3))));

        Assert.Equal(EligibilityStatus.Ineligible, at180.Status);
        Assert.Contains("Previous MMRV = 2026-04-04", at180.EvaluationReason);
        Assert.Equal(EligibilityStatus.Eligible, at181.Status);
        Assert.Contains("Previous MMRV = 2026-04-03", at181.EvaluationReason);
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
