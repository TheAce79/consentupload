namespace ConsentSyncCore.Models;

public sealed class EligibilityEvaluationResult
{
    public string ClientId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public DateTime? DateOfBirth { get; init; }
    public string VaccineType { get; init; } = string.Empty;
    public DateTime? ClinicDate { get; init; }
    public EligibilityStatus Status { get; set; }
    public string EvaluationReason { get; set; } = string.Empty;
    public int DoseCount { get; init; }
    public DateTime? LatestDoseDate { get; init; }
    public string HistoryStatus { get; init; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
}
