using ConsentSyncCore.Services.Eligibility;
using Xunit;

namespace Orchestrator.Tests;

public sealed class EligibilityCriteriaCatalogTests
{
    [Fact]
    public void All_ContainsTheTenCriteriaExplorerRulesInDisplayOrder()
    {
        Assert.Equal(
        [
            "18 Month Appointment",
            "ETS (Assessment Only)",
            "ETS+ (HTA + Vaccine)",
            "2 Month Appointment",
            "4 Month Appointment",
            "6 Month Appointment",
            "12 Month Appointment",
            "Preschool Appointment",
            "Other / Autre",
            "ETS Unknown"
        ], EligibilityCriteriaCatalog.All.Select(rule => rule.AppointmentMilestone));
    }

    [Fact]
    public void All_RecordsTheExecutableAgeIntervalAndHistoryRules()
    {
        EligibilityCriterionDefinition eighteenMonth = EligibilityCriteriaCatalog.All[0];
        EligibilityCriterionDefinition ets = EligibilityCriteriaCatalog.All[1];
        EligibilityCriterionDefinition etsPlus = EligibilityCriteriaCatalog.All[2];
        EligibilityCriterionDefinition fourMonth = EligibilityCriteriaCatalog.All[4];
        EligibilityCriterionDefinition sixMonth = EligibilityCriteriaCatalog.All[5];
        EligibilityCriterionDefinition other = EligibilityCriteriaCatalog.All[8];
        EligibilityCriterionDefinition etsUnknown = EligibilityCriteriaCatalog.All[9];

        Assert.Contains("18.0 m (inclusive)", eighteenMonth.MinAge);
        Assert.Contains("> 180 days", eighteenMonth.RequiredInterval);
        Assert.Contains("24.0 m (exclusive)", ets.MaxAge);
        Assert.Contains("ignored", ets.MissingPhisHistoryAction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("> 180 days", etsPlus.RequiredInterval);
        Assert.Contains(">= 28 days", fourMonth.RequiredInterval);
        Assert.Contains("DTaP, DPT, Pneu, or Rota", fourMonth.RequiredInterval);
        Assert.Contains(">= 56 days", sixMonth.RequiredInterval);
        Assert.Contains("DTaP, DPT, or Pneu", sixMonth.RequiredInterval);
        Assert.Contains("Ineligible", other.MissingPhisHistoryAction);
        Assert.Equal("Manual Review", etsUnknown.MissingPhisHistoryAction);
        Assert.Contains("La date de la clinique", eighteenMonth.FrenchCalculationExplanation);
        Assert.Contains("180 jours", etsPlus.FrenchCalculationExplanation);
    }

    [Fact]
    public void IntervalConstants_MatchTheEligibilityRuleThresholds()
    {
        Assert.Equal(180, EligibilityCriteriaCatalog.MmrMmrvMinimumIntervalDays);
        Assert.Equal(28, EligibilityCriteriaCatalog.FourMonthMinimumIntervalDays);
        Assert.Equal(56, EligibilityCriteriaCatalog.SixMonthMinimumIntervalDays);
    }
}
