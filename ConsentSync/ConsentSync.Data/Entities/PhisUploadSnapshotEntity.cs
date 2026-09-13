namespace ConsentSync.Data.Entities;

public sealed class PhisUploadSnapshotEntity
{
    public int Id { get; set; }
    public int CohortContextId { get; set; }
    public int PhisCohortId { get; set; }
    public int PhisClientListId { get; set; }
    public string ClientListName { get; set; } = string.Empty;
    public string ClientSnapshotJson { get; set; } = "[]";
    public DateTime UploadedOn { get; set; }
}
