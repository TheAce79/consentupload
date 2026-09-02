namespace Orchestrator.Phase4.Auditing.DocumentReconciliation;

public enum DocumentReconciliationIssueSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2
}

public enum ConsentPageOrigin
{
    Blank,
    DigitalConsent,
    ManualConsent,
    Unknown
}

public enum ReconciliationCountSource
{
    UploaderConfirmedFromSnbWebsite = 1,
    UploaderConfirmedFromPhysicalBatch = 2
}

public enum BulkSourceCrossCheckStatus
{
    Unavailable = 0,
    Match = 1,
    Mismatch = 2
}

public sealed class DocumentReconciliationAuditRequest
{
    public int OriginalDigitalConsentSubmissions { get; init; }
    public int ExpectedManualConsentForms { get; init; }
    public int ExpectedFileRoseForms { get; init; }
}

public sealed class DocumentReconciliationIssue
{
    public string Code { get; init; } = string.Empty;
    public DocumentReconciliationIssueSeverity Severity { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public string DocumentTitle { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool AffectsCompleteness { get; init; }
}

public sealed class FileRoseReconciliationDetail
{
    public string ClientId { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string DocumentTitle { get; init; } = string.Empty;
    public string ArchivePath { get; init; } = string.Empty;
    public int PageCount { get; init; }
    public int PhysicalForms => PageCount;
    public int AdditionalMergedForms => Math.Max(0, PageCount - 1);
    public bool ContainsMultipleForms => PageCount > 1;

    [Obsolete("FileRose pages represent separate physical forms. Use AdditionalMergedForms.")]
    public int ContinuationPages => AdditionalMergedForms;
    public bool HasMatchingConsentClient { get; init; }
    public bool IsTrusted { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class DocumentReconciliationAuditResult
{
    public int OriginalDigitalConsentSubmissions { get; init; }
    public ReconciliationCountSource DigitalConsentCountSource { get; init; }
    public ReconciliationCountSource ManualConsentCountSource { get; init; }
    public ReconciliationCountSource FileRoseCountSource { get; init; }
    public int UniqueDigitalConsentClients { get; init; }
    public int? AdditionalDigitalSourceSubmissions { get; init; }
    public BulkSourceCrossCheckStatus BulkCrossCheckStatus { get; init; }
    public int? BulkCalculatedDigitalSubmissions { get; init; }
    public string SelectedGrade { get; init; } = string.Empty;
    public int ConfiguredConsentVaccineCount { get; init; }
    public IReadOnlyList<string> ConfiguredPhisAntigens { get; init; } = Array.Empty<string>();
    public int ConsentUploadRows { get; init; }
    public int FileRoseUploadRows { get; init; }
    public int ExpectedConsentArchiveFiles { get; init; }
    public int ExpectedFileRoseArchiveFiles { get; init; }
    public int FoundConsentArchiveFiles { get; init; }
    public int FoundFileRoseArchiveFiles { get; init; }
    public int ReadableConsentArchiveFiles { get; init; }
    public int ReadableFileRoseArchiveFiles { get; init; }
    public int ConsentArchiveCopies { get; init; }
    public int FileRosePdfDocuments { get; init; }

    [Obsolete("Use FileRosePdfDocuments.")]
    public int FileRoseDocuments => FileRosePdfDocuments;
    public int UniqueFileRoseClientIds { get; init; }
    public int UniqueConsentClientIds { get; init; }
    public int ExpectedPhysicalConsentClients { get; init; }
    public int TrustedConsentClientsCounted { get; init; }
    public int VaccineSpecificConsentRowsCollapsed { get; init; }
    public int IdenticalConsentArchiveCopiesCollapsed { get; init; }
    public int ConsentVaccineCopyMismatchGroups { get; init; }
    public int DuplicateConsentAntigenRows { get; init; }
    public int MissingConfiguredConsentAntigens { get; init; }
    public int UnexpectedConsentAntigenRows { get; init; }
    public int DuplicateFileRoseRowsCollapsed { get; init; }
    public int MultipleFileRoseDocumentClientGroups { get; init; }
    public int ConfirmedConsentSubmissions { get; init; }
    public int AdditionalDuplicateConsentSubmissions { get; init; }
    public int MixedDigitalManualClients { get; init; }

    [Obsolete("Use UniqueConsentClientIds. Physical consent is grouped by ClientID.")]
    public int UniqueConsentPackets => UniqueConsentClientIds;

    [Obsolete("Use IdenticalConsentArchiveCopiesCollapsed. This excludes missing, unreadable, and mismatched copies.")]
    public int AntigenCopiesDeduplicated => IdenticalConsentArchiveCopiesCollapsed;
    public int ConsentPages { get; init; }
    public int DigitalConsentPages { get; init; }
    public int ManualConsentPages { get; init; }
    public int ExpectedManualConsentForms { get; init; }
    public int DetectedManualConsentForms => ManualConsentPages;
    public int ManualConsentVariance => DetectedManualConsentForms - ExpectedManualConsentForms;
    public bool ManualConsentCountMatches => DetectedManualConsentForms == ExpectedManualConsentForms;
    public int BlankConsentPages { get; init; }
    public int UnknownConsentPages { get; init; }
    public int FileRosePages { get; init; }
    public int ExpectedFileRoseForms { get; init; }
    public int DetectedFileRoseForms { get; init; }
    public int FileRoseFormVariance => DetectedFileRoseForms - ExpectedFileRoseForms;
    public bool FileRoseFormCountMatches => DetectedFileRoseForms == ExpectedFileRoseForms;
    public int MultiPageFileRoseDocuments { get; init; }
    public int AdditionalMergedFileRoseForms { get; init; }
    public int FileRoseClientsWithMatchingConsent { get; init; }
    public int FileRoseClientsWithoutMatchingConsent { get; init; }
    public IReadOnlyList<FileRoseReconciliationDetail> FileRoseDetails { get; init; } = Array.Empty<FileRoseReconciliationDetail>();
    public int InvalidDocumentTitleRows { get; init; }
    public int InvalidIsFeuilleRoseRows { get; init; }
    public int PdfPageCountMismatchFiles { get; init; }
    public int RasterGeometryUnavailablePages { get; init; }
    public int ArchiveFilesChangedDuringAudit { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public IReadOnlyList<DocumentReconciliationIssue> Issues { get; init; } = Array.Empty<DocumentReconciliationIssue>();

    public bool HasReviewIssues => Issues.Any(issue => issue.Severity is DocumentReconciliationIssueSeverity.Warning or DocumentReconciliationIssueSeverity.Error);
    public bool CountsAreComplete => !Issues.Any(issue => issue.AffectsCompleteness);
    public bool PhysicalCountsAvailable => CountsAreComplete;
    public bool OverallPhysicalReconciliationReady => PhysicalCountsAvailable && ManualConsentCountMatches && FileRoseFormCountMatches;
    public bool HasIntegrityWarnings => HasReviewIssues;
}

public static class DocumentReconciliationIssueCodes
{
    public const string InvalidDocumentTitle = "INVALID_DOCUMENT_TITLE";
    public const string InvalidIsFeuilleRose = "INVALID_IS_FEUILLE_ROSE";
    public const string DuplicateArchivePath = "DUPLICATE_ARCHIVE_PATH";
    public const string MissingArchiveFile = "MISSING_ARCHIVE_FILE";
    public const string UnreadableArchiveFile = "UNREADABLE_ARCHIVE_FILE";
    public const string PdfStillInUploadFolder = "PDF_STILL_IN_UPLOAD_FOLDER";
    public const string PdfRasterGeometryUnavailable = "PDF_RASTER_GEOMETRY_UNAVAILABLE";
    public const string PdfPageCountMismatch = "PDF_PAGE_COUNT_MISMATCH";
    public const string ArchiveFileChangedDuringAudit = "ARCHIVE_FILE_CHANGED_DURING_AUDIT";
    public const string UnknownConsentPage = "UNKNOWN_CONSENT_PAGE";
    public const string BlankConsentPage = "BLANK_CONSENT_PAGE";
    public const string UnsupportedGradeVaccineConfiguration = "UNSUPPORTED_GRADE_VACCINE_CONFIGURATION";
    public const string DuplicateConsentAntigenRow = "DUPLICATE_CONSENT_ANTIGEN_ROW";
    public const string MissingConfiguredConsentAntigen = "MISSING_CONFIGURED_CONSENT_ANTIGEN";
    public const string UnexpectedConsentAntigen = "UNEXPECTED_CONSENT_ANTIGEN";
    public const string ConsentVaccineCopyContentMismatch = "CONSENT_VACCINE_COPY_CONTENT_MISMATCH";
    public const string DuplicateFileRoseRow = "DUPLICATE_FILEROSE_ROW";
    public const string MultipleFileRoseDocumentsForClient = "MULTIPLE_FILEROSE_DOCUMENTS_FOR_CLIENT";
    public const string SnbSourceCountBelowUniqueClientCount = "SNB_SOURCE_COUNT_BELOW_UNIQUE_CLIENT_COUNT";
    public const string BulkSourceCrossCheckMismatch = "BULK_SOURCE_CROSS_CHECK_MISMATCH";
    public const string DocumentTitleClientIdMismatch = "DOCUMENT_TITLE_CLIENT_ID_MISMATCH";
    public const string FileRoseClientWithoutConsent = "FILEROSE_CLIENT_WITHOUT_CONSENT";
    public const string FileRoseMultipleFormsMerged = "FILEROSE_MULTIPLE_FORMS_MERGED";
}
