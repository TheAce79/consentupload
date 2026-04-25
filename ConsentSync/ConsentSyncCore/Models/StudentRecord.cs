using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Models
{
    /// <summary>
    /// Status of Client ID search for a student record
    /// </summary>
    public enum ClientIdStatus
    {
        /// <summary>Not yet searched</summary>
        NotProcessed = 0,

        /// <summary>Client ID found successfully</summary>
        Found = 1,

        /// <summary>Error occurred or no match found - needs manual review</summary>
        NeedsManualReview = 2
    }
    public class StudentRecord
    {
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string School { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty; // Format: yyyy-MM-dd
        public string MedicareNumber { get; set; } = string.Empty;
        public string ConsentStatus { get; set; } = string.Empty;
        public string Tdap { get; set; } = string.Empty;
        public string HPV { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public bool IsFileRoseDefault { get; set; }


        /// <summary>
        /// True when another row with the same normalised FirstName + LastName + DOB
        /// was already seen earlier in the file.
        /// Phase 1 robot will skip PHIS search and copy ClientId from the first occurrence.
        /// </summary>
        public bool IsDuplicate { get; set; } = false;


        /// <summary>
        /// Set to true by the user after reviewing the PDFs in 5_Duplicate\{LastName}_{FirstName}\.
        /// When true, the merge service will merge all PDFs in that subfolder into a single
        /// PDF and move it to 3_Output_Ready before Pre-Phase 3 runs.
        /// </summary>
        public bool DuplicateResolved { get; set; } = false;



        /// <summary>
        /// Status of Client ID search (0=NotProcessed, 1=Found, 2=NeedsManualReview)
        /// </summary>
        public ClientIdStatus ClientIdStatus { get; set; } = ClientIdStatus.NotProcessed;

        /// <summary>
        /// Best match suggestion for manual review (Format: FirstName#LastName#ClientID#Score)
        /// Only populated when ClientIdStatus = NeedsManualReview
        /// </summary>
        public string BestMatch { get; set; } = string.Empty;


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

   
   
}
