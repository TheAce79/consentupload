using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Models
{
    /// <summary>
    /// Validation record for Pre-Phase 3 manual review
    /// Contains all student data + PDF validation flags
    /// </summary>
    public class ValidationRecord
    {
        // ✅ All columns from StudentRecord (input CSV)
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string School { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty;
        public string MedicareNumber { get; set; } = string.Empty;
        public string ConsentStatus { get; set; } = string.Empty;
        public string Tdap { get; set; } = string.Empty;
        public string HPV { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public bool IsFileRoseDefault { get; set; }
        public int ClientIdStatus { get; set; }
        public string BestMatch { get; set; } = string.Empty;

        // ✅ NEW: Validation fields for Pre-Phase 3
        public bool FileFound { get; set; } = false;
        public bool IsMatch { get; set; } = false;
        public string ExtractedName { get; set; } = string.Empty;
        public string NormalizedPDF { get; set; } = string.Empty;
        public string NormalizedCSV { get; set; } = string.Empty;
        public bool IsPdfSave { get; set; } = false;

        // ✅ Additional helpful fields
        public double MatchScore { get; set; } = 0.0;
        public string ValidationNotes { get; set; } = string.Empty;

        // ✅ Set by DuplicateMergeService after resolving duplicates
        public string MergedFromDuplicate { get; set; } = string.Empty;
    }


    /// <summary>
    /// CSV mapping for ValidationRecord
    /// </summary>
    public sealed class ValidationRecordMap : ClassMap<ValidationRecord>
    {
        public ValidationRecordMap()
        {
            // Original student fields
            Map(m => m.LastName).Name("Last Name");
            Map(m => m.FirstName).Name("First Name");
            Map(m => m.School).Name("School");
            Map(m => m.Grade).Name("Grade");
            Map(m => m.DateOfBirth).Name("Date of Birth");
            Map(m => m.MedicareNumber).Name("Medicare Number");
            Map(m => m.ConsentStatus).Name("Consent Status");
            Map(m => m.Tdap).Name("Tdap");
            Map(m => m.HPV).Name("HPV");
            Map(m => m.ClientId).Name("ClientId");
            Map(m => m.IsFileRoseDefault).Name("IsFileRoseDefault");
            Map(m => m.ClientIdStatus).Name("ClientIdStatus");
            Map(m => m.BestMatch).Name("BestMatch");

            // Validation fields
            Map(m => m.FileFound).Name("FileFound");
            Map(m => m.IsMatch).Name("IsMatch");
            Map(m => m.ExtractedName).Name("ExtractedName");
            Map(m => m.NormalizedPDF).Name("NormalizedPDF");
            Map(m => m.NormalizedCSV).Name("NormalizedCSV");
            Map(m => m.IsPdfSave).Name("IsPdfSave");
            Map(m => m.MatchScore).Name("MatchScore");
            Map(m => m.ValidationNotes).Name("ValidationNotes");
            Map(m => m.MergedFromDuplicate).Name("MergedFromDuplicate");
        }
    }

}
