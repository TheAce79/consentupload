namespace Orchestrator.Phase4.Auditing.ClientIdentity;

public static class ClientIdentityAuditReasonCodes
{
    public const string SuccessExactDigitalMatch = "SUCCESS_EXACT_DIGITAL_MATCH";
    public const string SuccessCompatibleDigitalMatch = "SUCCESS_COMPATIBLE_DIGITAL_MATCH";
    public const string SuccessTokenOrderEquivalent = "SUCCESS_TOKEN_ORDER_EQUIVALENT";
    public const string SuccessUniquePartialIdentityMatch = "SUCCESS_UNIQUE_PARTIAL_IDENTITY_MATCH";
    public const string UploadNotCompleted = "UPLOAD_NOT_COMPLETED";
    public const string UploadRequiresReview = "UPLOAD_REQUIRES_REVIEW";
    public const string UploadFailureAcceptedException = "UPLOAD_FAILURE_ACCEPTED_EXCEPTION";
    public const string InvalidUploadStatus = "INVALID_UPLOAD_STATUS";
    public const string ClientIdMissing = "CLIENT_ID_MISSING";
    public const string ManualConsentRequiresReview = "MANUAL_CONSENT_REQUIRES_REVIEW";
    public const string ManualConsentClientIdNotInRoster = "MANUAL_CONSENT_CLIENT_ID_NOT_IN_ROSTER";
    public const string IncompleteStudentName = "INCOMPLETE_STUDENT_NAME";
    public const string ClientIdNotInRoster = "CLIENT_ID_NOT_IN_ROSTER";
    public const string DuplicateClientIdInRoster = "DUPLICATE_CLIENT_ID_IN_ROSTER";
    public const string NameColumnsReversed = "NAME_COLUMNS_REVERSED";
    public const string AmbiguousRosterIdentity = "AMBIGUOUS_ROSTER_IDENTITY";
    public const string ClientIdNameMismatch = "CLIENT_ID_NAME_MISMATCH";
    public const string ClientIdUsedForMultipleNames = "CLIENT_ID_USED_FOR_MULTIPLE_NAMES";
}
