namespace ConsentSyncCore.Models;

public sealed class ClientImmunizationHistory
{
    public string ClientId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public List<ImmunizationRecord> Records { get; } = [];
}
