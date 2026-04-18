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
        /// <summary>Files successfully renamed and copied to <c>2_Output_Ready_FileRose</c>.</summary>
        public int Extracted { get; set; }

        /// <summary>Files moved to <c>3_Error_FileRose_Extraction</c> because they could not be matched.</summary>
        public int Errors { get; set; }

        /// <summary>Files that were already extracted (skipped — output file already exists).</summary>
        public int AlreadyExtracted { get; set; }

        /// <summary>Detail per extracted file: ClientId → new file name.</summary>
        public List<(string ClientId, string NewFileName)> ExtractedFiles { get; set; } = new();

        /// <summary>
        /// Detail per error file: original file name, human-readable reason, and category.
        /// Category is used to group errors in the summary report.
        /// </summary>
        public List<(string FileName, string Reason, FileRoseErrorCategory Category)> ErrorFiles { get; set; } = new();

        // ── Convenience groupings (computed) ──────────────────────────────────

        public IEnumerable<(string FileName, string Reason, FileRoseErrorCategory Category)>
            InvalidFileNameErrors => ErrorFiles.Where(e => e.Category == FileRoseErrorCategory.InvalidFileName);

        public IEnumerable<(string FileName, string Reason, FileRoseErrorCategory Category)>
            ClientIdNotMatchedErrors => ErrorFiles.Where(e => e.Category == FileRoseErrorCategory.ClientIdNotMatched);

        public IEnumerable<(string FileName, string Reason, FileRoseErrorCategory Category)>
            CopyFailedErrors => ErrorFiles.Where(e => e.Category == FileRoseErrorCategory.CopyFailed);
    }


}