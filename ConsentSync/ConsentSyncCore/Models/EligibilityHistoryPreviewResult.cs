namespace ConsentSyncCore.Models;

public sealed class EligibilityHistoryPreviewResult
{
    public List<EligibilityHistoryPreviewRow> Rows { get; } = [];
    public List<string> Warnings { get; } = [];
}
