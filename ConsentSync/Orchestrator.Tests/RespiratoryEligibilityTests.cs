using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Csv;
using ConsentSyncCore.Services.Eligibility;
using Xunit;

namespace Orchestrator.Tests;

public sealed class RespiratoryEligibilityTests
{
    [Theory]
    [InlineData("2026-04-02", EligibilityStatus.Ineligible)]
    [InlineData("2026-04-01", EligibilityStatus.Eligible)]
    [InlineData("2021-10-02", EligibilityStatus.Eligible)]
    [InlineData("2021-10-01", EligibilityStatus.Ineligible)]
    public void ScreeningUsesCalendarAgeBoundaries(string dob, EligibilityStatus expected)
    {
        foreach (string category in new[] { VaccineTypeNormalizer.Influenza, VaccineTypeNormalizer.Covid19, VaccineTypeNormalizer.CovidAndInfluenza })
            Assert.Equal(expected, Evaluate(category, dob, null).Status);
    }

    [Theory]
    [InlineData("Influenza", "Influenza-quad", 27, 28)]
    [InlineData("Influenza", "influenza-trivalent", 28, 28)]
    [InlineData("Influenza", "Influenza-unspecified", 29, 28)]
    [InlineData("COVID-19", "COVID19 -mRNA", 55, 56)]
    [InlineData("COVID-19", "COVID-19 pediatric mRNA", 56, 56)]
    [InlineData("COVID-19", "SARS-CoV-2", 57, 56)]
    public void PriorDoseRequiresReviewOnBothSidesOfReferenceInterval(string category, string agent, int days, int reference)
    {
        var history = History((agent, new DateTime(2026, 10, 1).AddDays(-days)));
        EligibilityEvaluationResult result = Evaluate(category, "2025-01-01", history);
        Assert.Equal(EligibilityStatus.ManualReview, result.Status);
        Assert.Contains($"{days} days", result.EvaluationReason);
        Assert.Contains($"reference interval: {reference} days", result.EvaluationReason);
        Assert.Contains("Review dose sequence", result.EvaluationReason);
    }

    [Fact]
    public void OnlyMatchingPrefixesAreUsedAndLatestDateWins()
    {
        var history = History(("Influenza-quad", new DateTime(2026, 8, 1)), (" influenza-trivalent", new DateTime(2026, 9, 1)), ("Haemophilus influenzae", new DateTime(2026, 9, 30)));
        EligibilityEvaluationResult result = Evaluate(VaccineTypeNormalizer.Influenza, "2025-01-01", history);
        Assert.Equal(EligibilityStatus.ManualReview, result.Status);
        Assert.Contains("2026/09/01", result.EvaluationReason);
    }

    [Theory]
    [InlineData("2nd dose Influenza Vaccine: Children 6 months to 8 years 11 months who are receiving the Influenza vaccine for the 1st time")]
    [InlineData("2e dose du vaccin contre la grippe")]
    public void ExplicitSecondDoseWithoutRecordedHistoryRequiresReview(string booking)
    {
        EligibilityEvaluationResult result = Evaluate(VaccineTypeNormalizer.Influenza, "2025-01-01", null, booking);
        Assert.Equal(EligibilityStatus.ManualReview, result.Status);
        Assert.Contains("second Influenza dose", result.EvaluationReason);
    }

    [Fact]
    public void ExplicitSecondDoseFallsBackToAppointmentTypeWhenCatalogIsBlank()
    {
        var source = Source(VaccineTypeNormalizer.Influenza, "2025-01-01");
        source.AppointmentType = "2e dose du vaccin contre la grippe";
        Assert.Equal(EligibilityStatus.ManualReview, new EligibilityEvaluationService().EvaluateRecord(new CohortReviewRow { Source = source }, null, default).Status);
    }

    [Fact]
    public void FutureDoseRequiresReviewWithoutNegativeInterval()
    {
        EligibilityEvaluationResult result = Evaluate(VaccineTypeNormalizer.Covid19, "2025-01-01", History(("COVID-19", new DateTime(2026, 10, 2))));
        Assert.Equal(EligibilityStatus.ManualReview, result.Status);
        Assert.Contains("after the clinic date", result.EvaluationReason);
        Assert.DoesNotContain("-1 days", result.EvaluationReason);
    }

    [Fact]
    public void ComboUsesBothResultsAndExportsReasons()
    {
        const string booking = "COVID-19 & Influenza Vaccines (1st dose only)";
        var history = History(("Influenza", new DateTime(2026, 9, 1)));
        EligibilityEvaluationResult result = Evaluate(VaccineTypeNormalizer.CovidAndInfluenza, "2025-01-01", history, booking);
        Assert.Equal(EligibilityStatus.ManualReview, result.Status);
        Assert.Contains("first-dose-only booking conflicts", result.EvaluationReason);
        Assert.Contains("No prior COVID-19 dose", result.EvaluationReason);
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "clinical.csv");
            ClinicalCohortCsvExporterService.SaveToCsv([new ClinicalCohortExportRow("1", "Synthetic Client", "2026-10-01", "2025-01-01", null, VaccineTypeNormalizer.CovidAndInfluenza, result.Status, result.EvaluationReason)], path);
            Assert.Contains(result.EvaluationReason, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ComboWithoutHistoryIsEligibleAndOutOfWindowIsIneligible()
    {
        Assert.Equal(EligibilityStatus.Eligible, Evaluate(VaccineTypeNormalizer.CovidAndInfluenza, "2025-01-01", null).Status);
        EligibilityEvaluationResult result = Evaluate(VaccineTypeNormalizer.CovidAndInfluenza, "2021-10-01", null);
        Assert.Equal(EligibilityStatus.Ineligible, result.Status);
        Assert.Contains("Influenza:", result.EvaluationReason);
        Assert.Contains("COVID-19:", result.EvaluationReason);
    }

    private static EligibilityEvaluationResult Evaluate(string category, string dob, ClientImmunizationHistory? history, string? booking = null)
    {
        ClinicPdfClientRecord source = Source(category, dob);
        source.CatalogItem = booking;
        return new EligibilityEvaluationService().EvaluateRecord(new CohortReviewRow { Source = source }, history, default);
    }

    private static ClinicPdfClientRecord Source(string category, string dob) => new()
    {
        FullName = "Synthetic Client", DateOfBirth = dob, ClinicDate = "2026-10-01", VaccineType = category
    };

    private static ClientImmunizationHistory History(params (string Agent, DateTime Date)[] doses)
    {
        var history = new ClientImmunizationHistory();
        foreach (var dose in doses) history.Records.Add(new ImmunizationRecord { Agent = dose.Agent, AdministeredDate = dose.Date });
        return history;
    }
}
