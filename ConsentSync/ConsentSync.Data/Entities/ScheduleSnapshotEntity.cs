namespace ConsentSync.Data.Entities;

public sealed class ScheduleSnapshotEntity
{
    public int Id { get; set; }
    public int CohortContextId { get; set; }
    public string ClientListName { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string SourceFilesJson { get; set; } = "[]";
    public string ClientSnapshotJson { get; set; } = "[]";
    public DateTime ImportedOn { get; set; }
}
