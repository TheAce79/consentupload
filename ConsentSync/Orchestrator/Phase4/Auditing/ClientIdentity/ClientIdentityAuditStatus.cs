namespace Orchestrator.Phase4.Auditing.ClientIdentity;

public enum ClientIdentityAuditStatus
{
    NotProcessed = 0,
    Success = 1,
    NeedsManualReview = 2,
    Excluded = 3
}
