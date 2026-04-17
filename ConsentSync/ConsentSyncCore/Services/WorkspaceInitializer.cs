using ConsentSyncCore.Services.Configuration;

namespace ConsentSyncCore.Services
{
    /// <summary>
    /// Creates ALL required folders on startup based on BaseDirectory + SchoolContext.
    /// Call once at the very beginning of Main() — before any phase runs.
    ///
    /// Full structure created:
    ///   {BaseDirectory}\{SchoolName}\Grade {Grade}\
    ///     ├── Csv\
    ///     │     ├── 1_Input Csv\
    ///     │     │     └── 1 Consent Csv\
    ///     │     └── 2_Output Csv\
    ///     │           ├── 1 Processed Csv\
    ///     │           └── 2 Upload Csv\
    ///     ├── Pdf\
    ///     │     ├── 1_Input_Bulk\
    ///     │     ├── 2_Input_Scanned\
    ///     │     ├── 3_Output_Ready\
    ///     │     ├── 4 FileRose Extraction\
    ///     │     │     ├── 1 Scan File Rose\
    ///     │     │     └── 2_Output_Ready_FileRose\
    ///     │     ├── 5_Duplicate\
    ///     │     ├── 6_Error\
    ///     │     └── 7_Archive\
    ///     │           ├── Bulk\
    ///     │           ├── Scanned\
    ///     │           └── FileRose\
    ///     └── Phis\
    ///           ├── 1_To_Upload\
    ///           │     ├── 1 Consent Upload\
    ///           │     └── 2 File Rose Upload\
    ///           └── 2_Error\
    /// </summary>
    public static class WorkspaceInitializer
    {
        public static void EnsureAllFoldersExist()
        {
            LoggerService.LogInformation("\n📂 Initializing workspace folders...");

            var errors = new List<string>();

            // ── CSV workspace ──────────────────────────────────────────────────
            try
            {
                var csv = ConfigurationService.GetCsvWorkspaceConfig();

                CreateFolder(csv.GetConsentCsvPath(), "Csv › 1_Input Csv › 1 Consent Csv", errors);
                CreateFolder(csv.GetProcessedCsvPath(), "Csv › 2_Output Csv › 1 Processed Csv", errors);
                CreateFolder(csv.GetUploadCsvPath(), "Csv › 2_Output Csv › 2 Upload Csv", errors);
            }
            catch (Exception ex)
            {
                errors.Add($"CsvWorkspace: {ex.Message}");
            }

            // ── PDF workspace (BulkPdfExtraction) ─────────────────────────────
            try
            {
                var pdf = ConfigurationService.GetBulkPdfExtractionConfig();

                CreateFolder(pdf.GetInputBulkPath(), "Pdf › 1_Input_Bulk", errors);
                CreateFolder(pdf.GetInputScannedPath(), "Pdf › 2_Input_Scanned", errors);
                CreateFolder(pdf.GetOutputReadyPath(), "Pdf › 3_Output_Ready", errors);
                CreateFolder(pdf.GetFileRoseScanPath(), "Pdf › 4 FileRose Extraction › 1 Scan", errors);
                CreateFolder(pdf.GetFileRoseOutputReadyPath(), "Pdf › 4 FileRose Extraction › 2 Output Ready", errors);
                CreateFolder(pdf.GetDuplicateClientPath(), "Pdf › 5_Duplicate", errors);
                CreateFolder(pdf.GetErrorPath(), "Pdf › 6_Error", errors);
                CreateFolder(pdf.GetArchiveBulkPath(), "Pdf › 7_Archive › Bulk", errors);
                CreateFolder(pdf.GetArchiveScannedPath(), "Pdf › 7_Archive › Scanned", errors);
                CreateFolder(pdf.GetArchiveFileRosePath(), "Pdf › 7_Archive › FileRose", errors);
            }
            catch (Exception ex)
            {
                errors.Add($"PdfWorkspace: {ex.Message}");
            }

            // ── Phis workspace ─────────────────────────────────────────────────
            try
            {
                var phis = ConfigurationService.GetPhisWorkspaceConfig();

                CreateFolder(phis.GetConsentUploadPath(), "Phis › 1_To_Upload › 1 Consent Upload", errors);
                CreateFolder(phis.GetFileRoseUploadPath(), "Phis › 1_To_Upload › 2 File Rose Upload", errors);
                CreateFolder(phis.GetErrorPath(), "Phis › 2_Error", errors);
            }
            catch (Exception ex)
            {
                errors.Add($"PhisWorkspace: {ex.Message}");
            }

            // ── Summary ────────────────────────────────────────────────────────
            if (errors.Count == 0)
            {
                LoggerService.LogInformation("   ✅ All workspace folders ready\n");
            }
            else
            {
                LoggerService.LogWarning($"   ⚠️  {errors.Count} folder(s) could not be created:");
                foreach (var e in errors)
                    LoggerService.LogWarning($"      - {e}");
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static void CreateFolder(string path, string label, List<string> errors)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    LoggerService.LogInformation($"   ✅ Created : {label}");
                    LoggerService.LogInformation($"             → {path}");
                }
                else
                {
                    LoggerService.LogInformation($"   ✔  Exists  : {label}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{label}: {ex.Message}");
                LoggerService.LogWarning($"   ❌ Failed  : {label} → {ex.Message}");
            }
        }
    }
}