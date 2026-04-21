using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsentSyncCore.Models
{
    /// <summary>
    /// Why a FileRose file could not be processed.
    /// Used to group errors in the summary report.
    /// </summary>
    public enum FileRoseErrorCategory
    {
        /// <summary>Filename is not a pure numeric string — cannot be a ClientId.</summary>
        InvalidFileName,

        /// <summary>Numeric filename but ClientId not found in Validation_Results.csv
        /// with ClientIdStatus=Found and IsFileRoseDefault=True.</summary>
        ClientIdNotMatched,

        /// <summary>File system error during copy (permissions, disk full, etc.).</summary>
        CopyFailed
    }

    /// <summary>
    /// Result returned by <c>FileRoseExtractionService.ExtractFileRose()</c>.
    /// </summary>
    public class FileRoseExtractionResult
    {
        /// <summary>Files successfully moved and renamed to the FileRose upload folder.</summary>
        public int Extracted { get; set; }

        /// <summary>Files whose error prevented extraction — left in the scan folder for the user to fix.</summary>
        public int Errors { get; set; }

        /// <summary>Files already present in the output folder — IsFileRoseExtracted patched to True.</summary>
        public int AlreadyExtracted { get; set; }

        public List<(string ClientId, string NewFileName)> ExtractedFiles { get; set; } = new();

        /// <summary>
        /// Error details — files are LEFT in the scan folder (not moved).
        /// </summary>
        public List<(string FileName, string Reason, FileRoseErrorCategory Category)> ErrorFiles { get; set; } = new();

        /// <summary>
        /// Records where IsFileRoseDefault=True but extraction failed (IsFileRoseExtracted=False).
        /// Used by the UI to warn the user before proceeding.
        /// </summary>
        public List<(string ClientId, string LastName, string FirstName)> PendingFileRoseRows { get; set; } = new();

        // ── Convenience groupings ──────────────────────────────────────────────
        public IEnumerable<(string FileName, string Reason, FileRoseErrorCategory Category)>
            InvalidFileNameErrors => ErrorFiles.Where(e => e.Category == FileRoseErrorCategory.InvalidFileName);

        public IEnumerable<(string FileName, string Reason, FileRoseErrorCategory Category)>
            ClientIdNotMatchedErrors => ErrorFiles.Where(e => e.Category == FileRoseErrorCategory.ClientIdNotMatched);

        public IEnumerable<(string FileName, string Reason, FileRoseErrorCategory Category)>
            CopyFailedErrors => ErrorFiles.Where(e => e.Category == FileRoseErrorCategory.CopyFailed);
    }


}