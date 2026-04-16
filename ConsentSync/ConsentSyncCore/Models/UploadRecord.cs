using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        /// <summary>Failed - needs manual review</summary>
        NeedsManualReview = 2
    }


    public class UploadRecord
    {

        public string ClientID { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string DocumentTitle { get; set; } = string.Empty; // Filename without extension
        public string Description { get; set; } = string.Empty;   // ConsentHPV9, ConsentTdap, etc.

        public string PhisAntigen { get; set; } = string.Empty;   // ✅ NEW: HPV-9, Tetanus (T), Men-C-ACYW-135

        public bool IsFeuilleRose { get; set; } = false;
        public string Status { get; set; } = string.Empty;
        public bool IsFeuilleRoseUpload { get; set; } = false;



        /// <summary>
        /// Verification status for document upload
        /// </summary>
        public UploadVerificationStatus VerifStatus { get; set; } = UploadVerificationStatus.NotProcessed;


    }



    /// <summary>
    /// Custom converter for UploadVerificationStatus enum
    /// Handles CSV serialization/deserialization
    /// </summary>
    public class UploadVerificationStatusConverter : DefaultTypeConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text))
                return UploadVerificationStatus.NotProcessed;

            if (int.TryParse(text, out int value))
            {
                return (UploadVerificationStatus)value;
            }

            return UploadVerificationStatus.NotProcessed;
        }

        public override string ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
        {
            if (value is UploadVerificationStatus status)
            {
                return ((int)status).ToString();
            }

            return "0";
        }
    }



    /// <summary>
    /// CSV mapping for UploadRecord
    /// </summary>
    public sealed class UploadRecordMap : ClassMap<UploadRecord>
    {
        public UploadRecordMap()
        {
            Map(m => m.ClientID).Name("ClientID");
            Map(m => m.LastName).Name("Last Name");
            Map(m => m.FirstName).Name("First Name");
            Map(m => m.DocumentTitle).Name("Document Title");
            Map(m => m.Description).Name("Description");
            Map(m => m.PhisAntigen).Name("PhisAntigen"); // ✅ NEW
            Map(m => m.IsFeuilleRose).Name("IsFeuilleRose");
            Map(m => m.Status).Name("Status");
            Map(m => m.IsFeuilleRoseUpload).Name("IsFeuilleRoseUpload");

            Map(m => m.VerifStatus).Name("VerifStatus")
                .TypeConverter<UploadVerificationStatusConverter>();

        }


    }


}
