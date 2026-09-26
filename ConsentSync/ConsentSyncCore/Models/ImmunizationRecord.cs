namespace ConsentSyncCore.Models;

public sealed class ImmunizationRecord
{
    public string Agent { get; set; } = string.Empty;
    public DateTime AdministeredDate { get; set; }
}
