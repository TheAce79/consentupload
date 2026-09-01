using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{

    /// <summary>
    /// Phase 2 configuration
    /// </summary>
    public class Phase2Config
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;

        // Vitalite Website
        public string VitaliteLoginUrl { get; set; } = string.Empty;
        public string VitaliteSearchUrl { get; set; } = string.Empty;
        public string VitaliteUsername { get; set; } = string.Empty;
        public string VitalitePassword { get; set; } = string.Empty;
        public int WaitAfterLoginSeconds { get; set; }
        public int DownloadTimeoutSeconds { get; set; }

        // Download
        public string RenamedPath { get; set; } = string.Empty;
        public string TempPath { get; set; } = string.Empty;
        public int MaxDownloadRetries { get; set; }
        public int DelayBetweenDownloadsMs { get; set; }

        // PDF Processing
        public bool ValidateNamesBeforeRename { get; set; }
        public bool SplitMultiPagePdfs { get; set; }
        public int FileRosePageThreshold { get; set; }
        public bool DebugMode { get; set; }
        public string DebugOutputDir { get; set; } = string.Empty;

        /// <summary>
        /// by default, we want to use fuzzy matching for Phase 2 to maximize 
        /// the number of records we can automatically process, but this can be disabled for testing or if you want to be more strict with matching criteria
        /// </summary>
        public bool UseFuzzyMatching { get; set; } = true;

        /// <summary>
        /// If true, extract names from filename format: {ID}_{LastName}_{FirstName}_consent.pdf
        /// If false, extract names by reading PDF content (slower but works with any filename)
        /// </summary>
        public bool ReadNamesFromFilename { get; set; } = true;

        /// <summary>
        /// copy any files that fail processing to a separate directory 
        /// for easier troubleshooting and reprocessing after issues are resolved
        /// </summary>
        public string ErrorOutputDir { get; set; } = string.Empty;

       
        // ── Output ────────────────────────────────────────────────────────────
        public string ValidationResultsCsv { get; set; } = string.Empty;
        public string UploadCsv { get; set; } = string.Empty;

        /// <summary>
        /// Canonical folder for both Validation_Results.csv and Upload_to_PHIS.csv.
        /// Resolved from CsvWorkspace → 2_Output Csv → 2 Upload Csv.
        /// </summary>
        public string ValidationCsvPath { get; set; } = string.Empty;

        /// <summary>Configured vaccine types keyed by grade, for example Grade7.</summary>
        public Dictionary<string, List<string>> VaccineTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

}
