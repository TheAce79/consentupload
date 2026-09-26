namespace ConsentSyncCore.Models;

/// <summary>Administrative eligibility outcome for a cohort appointment.</summary>
public enum EligibilityStatus
{
    Eligible = 1,
    Ineligible = 2,
    ManualReview = 3
}
