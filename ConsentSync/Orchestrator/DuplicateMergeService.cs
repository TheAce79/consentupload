using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using CsvHelper;
using CsvHelper.Configuration;
using CsvProcessing;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace Orchestrator.Services
{
    /// <summary>
    /// Resolves duplicate consent PDFs before Pre-Phase 3.
    ///
    /// Workflow:
    ///   1. User reviews PDFs in  5_Duplicate\{LastName}_{FirstName}\
    ///      and deletes the copies they do NOT want to keep.
    ///   2. User sets  DuplicateResolved = true  in immunizations_processed.csv
    ///      on ALL rows for that student (every row in the duplicate group).
    ///   3. This service runs automatically before Pre-Phase 3:
    ///      - Merges all remaining PDFs in the subfolder into one PDF.
    ///      - Moves the merged PDF to 3_Output_Ready.
    ///      - Updates Validation_Results.csv so Pre-Phase 3 picks up the merged PDF.
    ///      - Skips groups where ANY row still has DuplicateResolved = false.
    /// </summary>
    public class DuplicateMergeService
    {
        private readonly StudentCsvRepository _csvRepo;
        private readonly BulkPdfExtractionConfig _bulkConfig;
        private readonly PrePhase3Config _prePhase3Config;

        public DuplicateMergeService(IConfiguration? config = null)
        {
            var cfg = config ?? ConfigurationService.GetConfiguration();
            _csvRepo = new StudentCsvRepository(cfg);
            _bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            _prePhase3Config = ConfigurationService.GetPrePhase3Config();
        }

        // ── Public entry point ────────────────────────────────────────────────


        public int MergeResolvedDuplicates()
        {
            LoggerService.LogInformation("\n╔════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║   Duplicate PDF Merge — Pre-Phase 3 preparation        ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝");

            var allStudents = _csvRepo.ReadAll();

            var groups = allStudents
                .GroupBy(s => BuildKey(s))
                .Where(g => g.Any(s => s.IsDuplicate))
                .ToList();

            if (groups.Count == 0)
            {
                LoggerService.LogInformation("   ℹ️  No duplicate groups found — nothing to merge.");
                return 0;
            }

            LoggerService.LogInformation($"\n   📊 {groups.Count} duplicate group(s) found");

            var validationRecords = LoadValidationCsv();

            int mergedCount = 0;
            int pendingCount = 0;

            foreach (var group in groups)
            {
                var members = group.ToList();

                // ── ALL rows in the group must have DuplicateResolved = true ──
                // The user must explicitly acknowledge every row before merge proceeds.
                bool isResolved = members.All(s => s.DuplicateResolved);

                var rep = members.First();
                string lastName = rep.LastName;
                string firstName = rep.FirstName;

                int duplicateCount = members.Count(s => s.IsDuplicate);
                int resolvedCount = members.Count(s => s.DuplicateResolved);

                if (!isResolved)
                {
                    pendingCount++;
                    LoggerService.LogInformation($"   ⏳ PENDING — {lastName} {firstName} " +
                        $"({resolvedCount}/{members.Count} row(s) resolved): set DuplicateResolved = true on ALL rows when ready");
                    continue;
                }

                LoggerService.LogInformation($"\n   🔀 Merging — {lastName} {firstName} ({duplicateCount} duplicate(s))");

                var mergeResult = MergeGroupPdfs(lastName, firstName, rep.ClientId);

                if (mergeResult.Success)
                {
                    mergedCount++;
                    LoggerService.LogInformation($"      ✅ Merged PDF → {mergeResult.OutputFileName}");

                    UpdateValidationRecord(
                        validationRecords,
                        rep.ClientId,
                        lastName,
                        firstName,
                        mergeResult.OutputFileName);
                }
                else
                {
                    LoggerService.LogInformation($"      ❌ Merge failed: {mergeResult.ErrorMessage}");
                }
            }

            if (mergedCount > 0)
            {
                SaveValidationCsv(validationRecords);
                LoggerService.LogInformation($"\n   💾 Validation_Results.csv updated for {mergedCount} merged group(s)");
            }

            LoggerService.LogInformation($"\n   ✅ Merged  : {mergedCount}");
            LoggerService.LogInformation($"   ⏳ Pending : {pendingCount}");

            if (pendingCount > 0)
            {
                LoggerService.LogInformation($"\n   💡 To resolve pending duplicates:");
                LoggerService.LogInformation($"      1. Review PDFs in: {_bulkConfig.GetDuplicateClientPath()}\\{{LastName}}_{{FirstName}}\\");
                LoggerService.LogInformation($"      2. Delete the copies you do NOT want to keep.");
                LoggerService.LogInformation($"      3. Set DuplicateResolved = true on ALL rows for that student.");
                LoggerService.LogInformation($"      4. Re-run Pre-Phase 3.");
            }

            return mergedCount;
        }


        // ── Validation CSV helpers ────────────────────────────────────────────

        /// <summary>
        /// Updates the PRIMARY student row in Validation_Results.csv so that
        /// Pre-Phase 3 Step 2 filter (FileFound=true AND IsMatch=true) picks it up.
        /// Also clears any duplicate rows so the merged PDF is not processed twice.
        /// </summary>
        private void UpdateValidationRecord(
            List<ValidationRecord> records,
            string clientId,
            string lastName,
            string firstName,
            string mergedFileName)
        {
            // Match by ClientId first (most reliable), fall back to name
            var matches = records
                .Where(r => (!string.IsNullOrWhiteSpace(clientId) &&
                             r.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase))
                         || (r.LastName.Equals(lastName, StringComparison.OrdinalIgnoreCase) &&
                             r.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matches.Count == 0)
            {
                LoggerService.LogInformation($"      ⚠️  No validation record found for {lastName} {firstName} — adding new row");

                // Insert a new validation row so Pre-Phase 3 processes the merged PDF
                records.Add(new ValidationRecord
                {
                    LastName = lastName,
                    FirstName = firstName,
                    ClientId = clientId,
                    FileFound = true,
                    IsMatch = true,
                    ExtractedName = $"{lastName} {firstName}",
                    NormalizedPDF = Normalize($"{lastName}{firstName}"),
                    NormalizedCSV = Normalize($"{lastName}{firstName}"),
                    MatchScore = 100.0,
                    MergedFromDuplicate = mergedFileName,
                    ValidationNotes = "Resolved duplicate — PDF merged from 5_Duplicate"
                });
                return;
            }

            // Update the FIRST match as the primary — mark it ready for upload
            var primary = matches.First();
            primary.FileFound = true;
            primary.IsMatch = true;
            primary.ExtractedName = $"{lastName} {firstName}";
            primary.NormalizedPDF = Normalize($"{lastName}{firstName}");
            primary.NormalizedCSV = Normalize($"{lastName}{firstName}");
            primary.MatchScore = 100.0;
            primary.MergedFromDuplicate = mergedFileName;
            primary.ValidationNotes = "Resolved duplicate — PDF merged from 5_Duplicate";
            primary.IsPdfSave = false; // reset so Pre-Phase 3 re-processes it

            LoggerService.LogInformation($"      📝 Validation record updated: {lastName} {firstName} → FileFound=true, IsMatch=true");

            // Any extra matches (true duplicates in validation CSV) → disable so
            // the merged PDF is not copied to upload folder twice
            foreach (var extra in matches.Skip(1))
            {
                extra.FileFound = false;
                extra.ValidationNotes = "Suppressed — covered by merged PDF row";
                LoggerService.LogInformation($"      🚫 Suppressed duplicate validation row for {extra.FirstName} {extra.LastName}");
            }
        }

        private List<ValidationRecord> LoadValidationCsv()
        {
            var path = Path.Combine(_prePhase3Config.ValidationCsvPath, _prePhase3Config.ValidationCsvFileName);

            if (!File.Exists(path))
            {
                LoggerService.LogInformation($"   ⚠️  Validation_Results.csv not found at {path} — will be created if merges succeed");
                return new List<ValidationRecord>();
            }

            using var reader = new StreamReader(path, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
                HeaderValidated = null
            });
            csv.Context.RegisterClassMap<ValidationRecordMap>();
            return csv.GetRecords<ValidationRecord>().ToList();
        }

        private void SaveValidationCsv(List<ValidationRecord> records)
        {
            var path = Path.Combine(_prePhase3Config.ValidationCsvPath, _prePhase3Config.ValidationCsvFileName);
            var tmpPath = path + ".tmp";

            using (var writer = new StreamWriter(tmpPath, false, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                csv.Context.RegisterClassMap<ValidationRecordMap>();
                csv.WriteRecords(records);
            }

            File.Move(tmpPath, path, overwrite: true);
            LoggerService.LogInformation($"      💾 Saved: {path}");
        }

        // ── Core merge logic ──────────────────────────────────────────────────

        private MergeResult MergeGroupPdfs(string lastName, string firstName, string clientId)
        {
            var result = new MergeResult();

            try
            {
                string folderName = MakeSafeFileName($"{lastName}_{firstName}");
                string duplicateDir = Path.Combine(_bulkConfig.GetDuplicateClientPath(), folderName);

                if (!Directory.Exists(duplicateDir))
                {
                    result.ErrorMessage = $"Duplicate folder not found: {duplicateDir}";
                    return result;
                }

                var pdfFiles = Directory.GetFiles(duplicateDir, "*.pdf")
                                        .OrderBy(f => f)
                                        .ToList();

                if (pdfFiles.Count == 0)
                {
                    result.ErrorMessage = $"No PDFs found in {duplicateDir}";
                    return result;
                }

                LoggerService.LogInformation($"      📄 {pdfFiles.Count} PDF(s) to merge:");
                foreach (var f in pdfFiles)
                    LoggerService.LogInformation($"         • {Path.GetFileName(f)}");

                string baseName = !string.IsNullOrWhiteSpace(clientId)
                    ? MakeSafeFileName($"{clientId}_{lastName}_{firstName}_consent")
                    : MakeSafeFileName($"{lastName}_{firstName}_merged_consent");

                string outputPath = Path.Combine(_bulkConfig.GetOutputReadyPath(), baseName + ".pdf");

                if (File.Exists(outputPath))
                {
                    string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    outputPath = Path.Combine(_bulkConfig.GetOutputReadyPath(), $"{baseName}_{ts}.pdf");
                }

                if (pdfFiles.Count == 1)
                {
                    File.Move(pdfFiles[0], outputPath, overwrite: true);
                    LoggerService.LogInformation($"      ℹ️  Single PDF — moved directly");
                }
                else
                {
                    var builder = new PdfDocumentBuilder();
                    foreach (var pdfPath in pdfFiles)
                    {
                        using var srcDoc = PdfDocument.Open(pdfPath);
                        for (int p = 1; p <= srcDoc.NumberOfPages; p++)
                            builder.AddPage(srcDoc, p);
                    }
                    File.WriteAllBytes(outputPath, builder.Build());

                    // Clean up originals only after successful write
                    foreach (var pdfPath in pdfFiles)
                        try { File.Delete(pdfPath); } catch { /* non-fatal */ }
                }

                result.Success = true;
                result.OutputFileName = Path.GetFileName(outputPath);
                result.OutputPath = outputPath;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string BuildKey(StudentRecord s)
        {
            static string Norm(string v)
            {
                if (string.IsNullOrWhiteSpace(v)) return string.Empty;
                var sb = new System.Text.StringBuilder();
                foreach (var c in v.Normalize(System.Text.NormalizationForm.FormD))
                    if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                        System.Globalization.UnicodeCategory.NonSpacingMark)
                        sb.Append(c);
                return sb.ToString()
                         .Normalize(System.Text.NormalizationForm.FormC)
                         .ToUpperInvariant()
                         .Replace(" ", "").Replace("-", "").Replace("'", "");
            }
            return $"{Norm(s.LastName)}_{Norm(s.FirstName)}_{s.DateOfBirth.Trim()}";
        }

        private static string Normalize(string v) =>
            string.IsNullOrWhiteSpace(v) ? string.Empty
            : new string(v.Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                            System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray())
              .Normalize(System.Text.NormalizationForm.FormC)
              .ToUpperInvariant().Replace(" ", "").Replace("-", "");

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private sealed class MergeResult
        {
            public bool Success { get; set; }
            public string OutputFileName { get; set; } = string.Empty;
            public string OutputPath { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
        }
    }
}