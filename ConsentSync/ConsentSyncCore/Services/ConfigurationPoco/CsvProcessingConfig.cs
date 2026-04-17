using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{
    /// <summary>
    /// CSV folder structure — one set per grade, easy for data-entry staff.
    ///   Csv\
    ///     ├── 1_Input Csv\
    ///     │     └── 1 Consent Csv\    ← drop immunizations.csv here
    ///     └── 2_Output Csv\
    ///           ├── 1 Processed Csv\ ← immunizations_processed.csv (Phase pre-1)
    ///           └── 2 Upload Csv\    ← Validation_Results.csv + Upload_to_PHIS.csv (Phase 2 → 3)
    /// </summary>
    public class CsvWorkspaceConfig
    {
        public string BaseCsvPath { get; set; } = string.Empty;

        // Top-level folder names
        public string InputFolder { get; set; } = "1_Input Csv";
        public string OutputFolder { get; set; } = "2_Output Csv";

        // Subfolders
        public string ConsentCsvSubFolder { get; set; } = "1 Consent Csv";
        public string ProcessedCsvSubFolder { get; set; } = "1 Processed Csv";
        public string UploadCsvSubFolder { get; set; } = "2 Upload Csv";

        // ── Computed path helpers ──────────────────────────────────────────────
        public string GetInputPath() => Path.Combine(BaseCsvPath, InputFolder);
        public string GetOutputPath() => Path.Combine(BaseCsvPath, OutputFolder);

        /// <summary>User drops immunizations.csv here</summary>
        public string GetConsentCsvPath() => Path.Combine(GetInputPath(), ConsentCsvSubFolder);

        /// <summary>immunizations_processed.csv is written here</summary>
        public string GetProcessedCsvPath() => Path.Combine(GetOutputPath(), ProcessedCsvSubFolder);

        /// <summary>Validation_Results.csv and Upload_to_PHIS.csv are written here</summary>
        public string GetUploadCsvPath() => Path.Combine(GetOutputPath(), UploadCsvSubFolder);
    }


    /// <summary>
    /// CSV Processing configuration — filenames + formatting only (no paths)
    /// </summary>
    public class CsvProcessingConfig
    {
        // ── Paths resolved from CsvWorkspaceConfig at load time ───────────────
        public string InputCsvPath { get; set; } = string.Empty;  // → 1_Input Csv\1 Consent Csv
        public string OutputCsvPath { get; set; } = string.Empty;  // → 2_Output Csv\1 Processed Csv

        // ── Filenames ─────────────────────────────────────────────────────────
        public string InputCsvFileName { get; set; } = "immunizations.csv";
        public string OutputCsvFileName { get; set; } = "immunizations_processed.csv";

        // ── Processing settings ───────────────────────────────────────────────
        public int SaveProgressEveryNRecords { get; set; } = 5;
        public string DateOfBirthColumn { get; set; } = "Date of Birth";
        public string DateFormat { get; set; } = "yyyy-MM-dd";
        public string[] InputDateFormats { get; set; } = Array.Empty<string>();
        public string LastNameColumn { get; set; } = "Last Name";
        public string FirstNameColumn { get; set; } = "First Name";
        public string ClientIdColumn { get; set; } = "ClientId";
    }
}
