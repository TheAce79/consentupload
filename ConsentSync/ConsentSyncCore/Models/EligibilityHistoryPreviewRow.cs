namespace ConsentSyncCore.Models;

public sealed class EligibilityHistoryPreviewRow
{
    public string ClientId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public string VaccineType { get; set; } = string.Empty;
    public string HistoryMatchStatus { get; set; } = string.Empty;
    public int DoseCount { get; set; }
    public string? LatestDoseDate { get; set; }
    public string Warning { get; set; } = string.Empty;
}
