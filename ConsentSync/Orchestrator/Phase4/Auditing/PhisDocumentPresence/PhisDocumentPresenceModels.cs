namespace Orchestrator.Phase4.Auditing.PhisDocumentPresence;

public enum PhisDocumentPresenceStatus { Found, Missing, VerificationError }

public enum PhisVerificationStatus { NotVerified = 0, VerifiedOk = 1, VerifiedKo = 2 }

public sealed class PhisDocumentPresenceTarget
{
    public string ClientId { get; init; } = string.Empty;
    public string DocumentTitle { get; init; } = string.Empty;
    public bool IsFileRose { get; init; }
    public string PhisAntigen { get; init; } = string.Empty;
    internal string VerificationKey { get; init; } = string.Empty;
}

public sealed class PhisDocumentPresenceVerificationPlan
{
    public int VerificationCsvRows { get; init; }
    public int EligibleRows { get; init; }
    public int DuplicateTargetsCollapsed { get; init; }
    public int ExcludedAcceptedExceptions { get; init; }
    public int AlreadyVerifiedRows { get; init; }
    public int PendingRows { get; init; }
    public bool BatchLimitReached { get; init; }
    public int RemainingAfterBatch { get; init; }
    public string VerificationCsvPath { get; init; } = string.Empty;
    public IReadOnlyList<PhisDocumentPresenceTarget> Targets { get; init; } = Array.Empty<PhisDocumentPresenceTarget>();
}

public sealed class PhisDocumentPresenceItemResult
{
    public PhisDocumentPresenceTarget Target { get; init; } = new();
    public PhisDocumentPresenceStatus Status { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed class PhisDocumentPresenceProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public PhisDocumentPresenceTarget Target { get; init; } = new();
    public PhisDocumentPresenceStatus? Status { get; init; }
}

public sealed class PhisDocumentPresenceVerificationResult
{
    public PhisDocumentPresenceVerificationPlan Plan { get; init; } = new();
    public IReadOnlyList<PhisDocumentPresenceItemResult> Items { get; init; } = Array.Empty<PhisDocumentPresenceItemResult>();
    public int ExpectedDocuments => Plan.Targets.Count;
    public int FoundDocuments => Items.Count(item => item.Status == PhisDocumentPresenceStatus.Found);
    public int MissingDocuments => Items.Count(item => item.Status == PhisDocumentPresenceStatus.Missing);
    public int VerificationErrors => Items.Count(item => item.Status == PhisDocumentPresenceStatus.VerificationError);
    public int UnprocessedDocuments { get; init; }
    public bool AllExpectedDocumentsPresent => ExpectedDocuments > 0 && MissingDocuments == 0 && VerificationErrors == 0 && UnprocessedDocuments == 0 && FoundDocuments == ExpectedDocuments;
}

public sealed class PhisDocumentPresencePreconditionItem
{
    public string ClientId { get; init; } = string.Empty;
    public string DocumentTitle { get; init; } = string.Empty;
    public string VerifStatus { get; init; } = string.Empty;
    public string VerifClientIdStatus { get; init; } = string.Empty;
    public string PhisVerificationStatus { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class PhisDocumentPresencePreconditionException : Exception
{
    public int UploadNotCompletedRows { get; init; }
    public int UploadReviewRows { get; init; }
    public int ClientIdentityUnresolvedRows { get; init; }
    public int InvalidStatusRows { get; init; }
    public IReadOnlyList<PhisDocumentPresencePreconditionItem> Examples { get; init; } = Array.Empty<PhisDocumentPresencePreconditionItem>();

    public PhisDocumentPresencePreconditionException(string message) : base(message) { }
}
