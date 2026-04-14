using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Models
{
    /// <summary>
    /// PDF source type for tracking
    /// </summary>
    public enum PdfSourceType
    {
        Bulk,
        Scanned
    }

    public class BulkExtractionResult
    {

        public bool Success { get; set; }
        public int TotalExtracted { get; set; }
        public int FailedExtractions { get; set; }
        public int DuplicatesFound { get; set; }
        public int UnknownNameCount { get; set; } // ✅ NEW: Track PDFs with Unknown names
        public List<string> ExtractedFiles { get; set; } = new();
        public List<string> ErrorMessages { get; set; } = new();
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Merge another result into this one (for aggregate processing)
        /// </summary>
        public void Merge(BulkExtractionResult other)
        {
            TotalExtracted += other.TotalExtracted;
            FailedExtractions += other.FailedExtractions;
            DuplicatesFound += other.DuplicatesFound;
            UnknownNameCount += other.UnknownNameCount; // ✅ NEW
            ExtractedFiles.AddRange(other.ExtractedFiles);
            ErrorMessages.AddRange(other.ErrorMessages);

            if (!string.IsNullOrEmpty(other.ErrorMessage))
            {
                ErrorMessages.Add(other.ErrorMessage);
            }
        }

    }
}
