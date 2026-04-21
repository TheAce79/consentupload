using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{
    /// <summary>
    /// Bulk PDF Extraction configuration (standalone - can run at any phase)
    /// Folder structure:
    ///   1_Input_Bulk
    ///   2_Input_Scanned
    ///   3_Output_Ready
    ///   4 FileRose Extraction/
    ///     ├── 1_Scan_FileRose/          ← users drop {ClientId}.pdf here
    ///     └── 3_Error_FileRose_Extraction/
    ///   5_Duplicate
    ///   6_Error
    ///   7_Archive/
    ///     ├── Bulk/
    ///     ├── Scanned/
    ///     └── FileRose/
    ///
    /// Successfully extracted files go directly to:
    ///   Phis\1_To_Upload\2 File Rose Upload\   (PhisWorkspace)
    /// </summary>
    public class BulkPdfExtractionConfig
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;

        // Base path for all PDF operations
        public string BasePdfPath { get; set; } = string.Empty;

        // Top-level folder names (relative to BasePdfPath)
        public string InputBulkFolder { get; set; } = "1_Input_Bulk";
        public string InputScannedFolder { get; set; } = "2_Input_Scanned";
        public string OutputReadyFolder { get; set; } = "3_Output_Ready";
        public string FileRoseFolder { get; set; } = "4 FileRose Extraction";
        public string DuplicateClientFolder { get; set; } = "5_Duplicate";
        public string ErrorFolder { get; set; } = "6_Error";
        public string ArchiveFolder { get; set; } = "7_Archive";

        // Subfolder names inside FileRoseFolder
        /// <summary>Users drop {ClientId}.pdf files here to be extracted.</summary>
        public string FileRoseScanSubFolder { get; set; } = "1_Scan_FileRose";

        /// <summary>
        /// Folder for FileRose files that could not be matched (wrong/invalid ClientId filename).
        /// User manually corrects files here then re-runs.
        /// </summary>
        public string FileRoseErrorSubFolder { get; set; } = "3_Error_FileRose_Extraction";

        // File naming suffixes
        /// <summary>Suffix for FileRose renamed output: {ClientId}_{RoseSuffix}_{SchoolYear}.pdf</summary>
        public string RoseSuffix { get; set; } = "suiviscolaire";

        /// <summary>Suffix for consent renamed output: {ClientId}_{ConsentSuffix}{VaccineType}_{SchoolYear}.pdf</summary>
        public string ConsentSuffix { get; set; } = "consent";

        // Processing settings
        public int PagesPerConsent { get; set; } = 1;
        public int StartPage { get; set; } = 1;
        public bool AutoDetectNames { get; set; } = true;
        public string NamingFormat { get; set; } = "{ID}_{LastName}_{FirstName}_consent";
        public bool OverwriteExisting { get; set; } = false;
        public bool MoveToArchiveAfterProcessing { get; set; } = true;
        public bool MoveErrorPdfsToErrorFolder { get; set; } = true;

        // ── Computed path helpers ──────────────────────────────────────────────
        public string GetInputBulkPath() => Path.Combine(BasePdfPath, InputBulkFolder);
        public string GetInputScannedPath() => Path.Combine(BasePdfPath, InputScannedFolder);
        public string GetOutputReadyPath() => Path.Combine(BasePdfPath, OutputReadyFolder);

        // FileRose parent + its subfolders
        public string GetFileRosePath() => Path.Combine(BasePdfPath, FileRoseFolder);

        /// <summary>Users drop {ClientId}.pdf files here.</summary>
        public string GetFileRoseScanPath() => Path.Combine(GetFileRosePath(), FileRoseScanSubFolder);

        /// <summary>Error summary text file is written here — no PDFs are moved here.</summary>
        public string GetFileRoseErrorPath() => Path.Combine(GetFileRosePath(), FileRoseErrorSubFolder);

        public string GetDuplicateClientPath() => Path.Combine(BasePdfPath, DuplicateClientFolder);
        public string GetErrorPath() => Path.Combine(BasePdfPath, ErrorFolder);
        public string GetArchivePath() => Path.Combine(BasePdfPath, ArchiveFolder);
        public string GetArchiveBulkPath() => Path.Combine(GetArchivePath(), "Bulk");
        public string GetArchiveScannedPath() => Path.Combine(GetArchivePath(), "Scanned");
        public string GetArchiveFileRosePath() => Path.Combine(GetArchivePath(), "FileRose");
    }
}