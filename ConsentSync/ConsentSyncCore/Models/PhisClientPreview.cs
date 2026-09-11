namespace ConsentSyncCore.Models;

/// <summary>Data read from the PHIS client preview after a search result is selected.</summary>
public sealed class PhisClientPreview
{
    public string ClientId { get; init; } = string.Empty;
    public string? HealthCardNumber { get; init; }
    public string? PreferredTelephoneNumber { get; init; }
    public IReadOnlyList<PhisEmailAddress> EmailAddresses { get; init; } = [];
}

public sealed class PhisEmailAddress
{
    public string Address { get; init; } = string.Empty;
    public bool IsPreferred { get; init; }
}
