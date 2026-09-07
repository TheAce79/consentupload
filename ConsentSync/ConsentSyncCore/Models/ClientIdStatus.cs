namespace ConsentSyncCore.Models;

/// <summary>Status of a Client ID search.</summary>
public enum ClientIdStatus
{
    NotProcessed = 0,
    Found = 1,
    NeedsManualReview = 2
}
