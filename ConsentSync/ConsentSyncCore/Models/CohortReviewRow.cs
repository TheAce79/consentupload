namespace ConsentSyncCore.Models;

public sealed class CohortReviewRow
{
    public required ClinicPdfClientRecord Source { get; init; }
    public int RowNumber { get; init; }
    public string? ClientIdOverride { get; set; }
    public bool Excluded { get; set; }
    public bool DuplicateId { get; internal set; }
    public string ClientId
    {
        get => ClientIdOverride ?? Source.ClientId ?? string.Empty;
        set => ClientIdOverride = value?.Trim() ?? string.Empty;
    }
    public string FullName => Source.FullName;
    public string DateOfBirth => Source.DateOfBirth;
    public string? Medicare => Source.Medicare;
    public string? FirstName => Source.FirstName;
    public string? LastName => Source.LastName;
    public string? MiddleName => Source.MiddleName;
    public string? Phone => Source.Phone;
    public string? Email => Source.Email;
    public string? ErrorDetails => ClientIdOverride switch
    {
        null => Source.ErrorDetails,
        "" => "Client ID cleared during manual review.",
        _ => null
    };
    public string? BestMatch => Source.BestMatch;
    public ClientIdStatus SearchStatus => ClientIdOverride switch
    {
        null => Source.ClientIdStatus,
        "" => ClientIdStatus.NeedsManualReview,
        _ => ClientIdStatus.Found
    };
    public bool Unresolved => SearchStatus != ClientIdStatus.Found || string.IsNullOrWhiteSpace(ClientId);
    public bool RequiresAttention => !Excluded && (Unresolved || DuplicateId);
}
