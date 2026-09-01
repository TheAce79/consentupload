namespace Orchestrator.Phase4.Auditing.ClientIdentity;

public sealed class ClientIdentityPreAuditResult
{
    public int TotalRows { get; init; }
    public int UniqueClientIds { get; init; }
    public int SuccessfulUploadRows { get; init; }
    public int AutomaticallyVerifiedRows { get; init; }
    public int NeedsManualReviewRows { get; init; }
    public int AcceptedUploadExceptionRows { get; init; }
    public int AcceptedUploadExceptionClientIds { get; init; }
    public int ExactMatchRows { get; init; }
    public int CompatibleMatchRows { get; init; }
    public int TokenOrderEquivalentRows { get; init; }
    public int UniquePartialMatchRows { get; init; }
    public int UploadNotCompletedRows { get; init; }
    public int UploadFailedRows { get; init; }
    public int InvalidStatusRows { get; init; }
    public int DigitalConsentRows { get; init; }
    public int ManualConsentRows { get; init; }
    public int UniqueManualConsentClientIds { get; init; }
    public int MissingClientIdRows { get; init; }
    public int ClientIdNotInRosterRows { get; init; }
    public int IncompleteNameRows { get; init; }
    public int DuplicateRosterClientIdRows { get; init; }
    public int DuplicateRosterClientIds { get; init; }
    public int ReversedNameRows { get; init; }
    public int NameMismatchRows { get; init; }
    public int AmbiguousNameRows { get; init; }
    public int MultipleUploadNameRows { get; init; }
    public string OutputPath { get; init; } = string.Empty;

    public bool HasReviewItems => NeedsManualReviewRows > 0 || UploadNotCompletedRows > 0;
}
