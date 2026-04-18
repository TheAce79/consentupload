using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

namespace ConsentSyncCore.Models
{
    /// <summary>
    /// Verification status for document upload
    /// </summary>
    public enum UploadVerificationStatus
    {
        /// <summary>Not yet processed</summary>
        NotProcessed = 0,

        /// <summary>Successfully uploaded or already exists</summary>
        Success = 1,

        /// <summary>Failed - needs manual review. See FailureReason for details.</summary>
        NeedsManualReview = 2
    }

    public class UploadRecord
    {
        public string ClientID { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;

        /// <summary>Filename without extension — used as the PHIS document title.</summary>
        public string DocumentTitle { get; set; } = string.Empty;

        /// <summary>
        /// For consent: ConsentHPV9 / ConsentTdap / ConsentMenCACYW135.
        /// For FileRose: "Suivi scolaire".
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>PHIS antigen name (HPV-9, Tetanus (T), …). Empty for FileRose rows.</summary>
        public string PhisAntigen { get; set; } = string.Empty;

        /// <summary>True when this row represents a FileRose (feuille rose) document.</summary>
        public bool IsFeuilleRose { get; set; } = false;

        /// <summary>
        /// Upload status. Set to Success (1) by Phase 3 on success.
        /// Set to NeedsManualReview (2) on failure — see <see cref="FailureReason"/>.
        /// User can reset to NotProcessed (0) to retry.
        /// </summary>
        public UploadVerificationStatus VerifStatus { get; set; } = UploadVerificationStatus.NotProcessed;

        /// <summary>
        /// Human-readable reason populated by Phase 3 when VerifStatus = NeedsManualReview.
        /// Examples: "Timeout navigating to Immunization Service", "PDF file not found", etc.
        /// Empty on success or when not yet processed.
        /// </summary>
        public string FailureReason { get; set; } = string.Empty;
    }

    /// <summary>CSV mapping for <see cref="UploadRecord"/>.</summary>
    public sealed class UploadRecordMap : ClassMap<UploadRecord>
    {
        public UploadRecordMap()
        {
            Map(m => m.ClientID).Name("ClientID");
            Map(m => m.LastName).Name("Last Name");
            Map(m => m.FirstName).Name("First Name");
            Map(m => m.DocumentTitle).Name("Document Title");
            Map(m => m.Description).Name("Description");
            Map(m => m.PhisAntigen).Name("PhisAntigen");
            Map(m => m.IsFeuilleRose).Name("IsFeuilleRose");
            Map(m => m.VerifStatus).Name("VerifStatus")
                .TypeConverter<UploadVerificationStatusConverter>()
                .Optional();
            Map(m => m.FailureReason).Name("FailureReason")
                .Optional();
        }
    }

    public class UploadVerificationStatusConverter : DefaultTypeConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text))
                return UploadVerificationStatus.NotProcessed;

            if (int.TryParse(text, out int value) &&
                Enum.IsDefined(typeof(UploadVerificationStatus), value))
                return (UploadVerificationStatus)value;

            if (Enum.TryParse<UploadVerificationStatus>(text, ignoreCase: true, out var enumValue))
                return enumValue;

            return UploadVerificationStatus.NotProcessed;
        }

        public override string ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
        {
            if (value is UploadVerificationStatus status)
                return ((int)status).ToString();
            return "0";
        }
    }
}