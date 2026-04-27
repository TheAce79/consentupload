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
    /// <summary>
    /// Validation record for Pre-Phase 3 manual review.
    /// Contains all student data + PDF validation flags.
    /// </summary>
    public class ValidationRecord
    {
        // ── Student fields (from input CSV) ───────────────────────────────────
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
        public int ClientIdStatus { get; set; }
        public string BestMatch { get; set; } = string.Empty;

        // ── FileRose flags ────────────────────────────────────────────────────

        /// <summary>
        /// True when a {ClientId}.pdf was found in <c>1 Scan File Rose</c>
        /// (set by <c>--check-filerose</c> / <see cref="FileRoseVerificationService"/>).
        /// </summary>
        public bool IsFileRoseDefault { get; set; }

        /// <summary>
        /// True when the FileRose PDF has been successfully renamed and copied
        /// to <c>2_Output_Ready_FileRose</c>
        /// (set by <c>--extract-filerose</c> / <see cref="FileRoseExtractionService"/>).
        /// </summary>
        public bool IsFileRoseExtracted { get; set; }

        // ── Consent PDF validation fields (Phase 2) ───────────────────────────
        public bool FileFound { get; set; } = false;
        public bool IsMatch { get; set; } = false;
        public string ExtractedName { get; set; } = string.Empty;
        public string NormalizedPDF { get; set; } = string.Empty;
        public string NormalizedCSV { get; set; } = string.Empty;
        public bool IsPdfSave { get; set; } = false;
        public double MatchScore { get; set; } = 0.0;
        public string ValidationNotes { get; set; } = string.Empty;
        public string MergedFromDuplicate { get; set; } = string.Empty;



        //other field here
        // ── Scanned PDF columns ───────────────────────────────────────────────

        /// <summary>
        /// True when this row was produced by reading from the scanned input folder.
        /// False when produced from the bulk PDF extraction path (production).
        /// </summary>
        public bool IsScanPdf { get; set; } = false;

        /// <summary>
        /// Original file name of the PDF that produced this row.
        /// </summary>
        public string PdfName { get; set; } = string.Empty;

        /// <summary>
        /// True only when IsScanPdf=true AND all fields (FirstName, LastName, DOB)
        /// were successfully extracted from the scanned PDF.
        /// </summary>
        public bool IsScanPdfReady { get; set; } = false;
    }



    /// <summary>
    /// CSV mapping for ValidationRecord
    /// </summary>
    public sealed class ValidationRecordMap : ClassMap<ValidationRecord>
    {
        public ValidationRecordMap()
        {
            // Student fields
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
            Map(m => m.ClientIdStatus).Name("ClientIdStatus");
            Map(m => m.BestMatch).Name("BestMatch");

            // FileRose flags
            Map(m => m.IsFileRoseDefault).Name("IsFileRoseDefault");
            Map(m => m.IsFileRoseExtracted).Name("IsFileRoseExtracted");

            // Consent validation fields
            Map(m => m.FileFound).Name("FileFound");
            Map(m => m.IsMatch).Name("IsMatch");
            Map(m => m.ExtractedName).Name("ExtractedName");
            Map(m => m.NormalizedPDF).Name("NormalizedPDF");
            Map(m => m.NormalizedCSV).Name("NormalizedCSV");
            Map(m => m.IsPdfSave).Name("IsPdfSave");
            Map(m => m.MatchScore).Name("MatchScore");
            Map(m => m.ValidationNotes).Name("ValidationNotes");
            Map(m => m.MergedFromDuplicate).Name("MergedFromDuplicate");


            // Inside ValidationRecordMap ctor
            Map(m => m.IsScanPdf).Name("IsScanPdf").Default(false);
            Map(m => m.PdfName).Name("PdfName").Default(string.Empty);
            Map(m => m.IsScanPdfReady).Name("IsScanPdfReady").Default(false);


        }

    }

}
