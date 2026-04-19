using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using ConsentSyncCore.Services.Pdf;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orchestrator.Services;
using System.Globalization;
using System.Text;

namespace Orchestrator.PrePhase3
{
    public class PrePhase3Orchestrator
    {
        private readonly IConfiguration _config;
        private readonly PrePhase3Config _prePhase3Config;
        private readonly Phase2Config _phase2Config;
        private readonly SchoolContextConfig _schoolContext;
        private readonly BulkPdfExtractionConfig _bulkPdfConfig;
        private readonly ILogger<PrePhase3Orchestrator> _logger;

        public PrePhase3Orchestrator(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _prePhase3Config = ConfigurationService.GetPrePhase3Config();
            _phase2Config = ConfigurationService.GetPhase2Config();
            _schoolContext = ConfigurationService.GetSchoolContextConfig();
            _bulkPdfConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            _logger = LoggerService.GetLogger<PrePhase3Orchestrator>();
        }

        public async Task<PrePhase3Result> RunAsync()
        {
            LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║      ConsentSync - Pre-Phase 3: Prepare for Upload     ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝\n");

            var result = new PrePhase3Result();

            try
            {
                // ── Step 0: Merge resolved duplicate PDFs ─────────────────────
                LoggerService.LogInformation("📋 Step 0: Processing resolved duplicate PDFs...");
                var mergeService = new DuplicateMergeService(_config);
                int merged = mergeService.MergeResolvedDuplicates();
                result.DuplicatesMerged = merged;
                LoggerService.LogInformation(merged > 0
                    ? $"   ✅ {merged} duplicate group(s) merged"
                    : "   ℹ️  No resolved duplicates to merge");

                // ── Step 1: Load Validation CSV ───────────────────────────────
                LoggerService.LogInformation("\n📋 Step 1: Loading Validation_Results.csv...");
                var validationRecords = LoadValidationCsv();
                result.TotalRecords = validationRecords.Count;
                LoggerService.LogInformation($"   ✅ Loaded {validationRecords.Count} validation records");

                // ── Step 2: Filter validated consent records ──────────────────
                //    Skip records already saved (IsPdfSave = true) so re-runs only
                //    process new/resolved rows and never duplicate work.
                LoggerService.LogInformation("\n📋 Step 2: Filtering validated records...");
                var validatedRecords = validationRecords
                    .Where(r => r.FileFound &&
                                r.IsMatch &&
                                !string.IsNullOrWhiteSpace(r.ClientId))
                    .ToList();

                var toProcess = validatedRecords.Where(r => !r.IsPdfSave).ToList();
                var alreadyDone = validatedRecords.Count - toProcess.Count;

                result.ValidatedRecords = validatedRecords.Count;
                result.SkippedNotValidated = validationRecords.Count - validatedRecords.Count;

                LoggerService.LogInformation($"   ✅ Validated records      : {validatedRecords.Count}");
                LoggerService.LogInformation($"   ♻️  Already processed     : {alreadyDone} (skipped — already in Upload CSV)");
                LoggerService.LogInformation($"   🆕 New records to process : {toProcess.Count}");
                LoggerService.LogInformation($"   ⏭️  Skipped (not validated): {result.SkippedNotValidated}");

                if (toProcess.Count == 0 &&
                    !validationRecords.Any(r => r.IsFileRoseDefault && r.IsFileRoseExtracted &&
                                               !string.IsNullOrWhiteSpace(r.ClientId)))
                {
                    LoggerService.LogInformation("\n   ℹ️  No new records to process — Upload CSV is already up to date.");
                    return result;
                }

                // ── Step 3: Process NEW consent PDFs only ─────────────────────
                LoggerService.LogInformation($"\n📋 Step 3: Processing {toProcess.Count} new consent PDF(s)...");
                var newUploadRecords = new List<UploadRecord>();

                foreach (var record in toProcess)
                {
                    LoggerService.LogInformation($"\n   Processing: {record.FirstName} {record.LastName} ({record.ClientId})");
                    try
                    {
                        var pdfPath = FindPdfForRecord(record);
                        if (string.IsNullOrEmpty(pdfPath))
                        {
                            LoggerService.LogInformation($"      ⚠️  PDF not found for {record.ClientId}");
                            result.SkippedMissingPdf++;
                            result.ErrorMessages.Add($"{record.ClientId}: PDF file not found");
                            continue;
                        }

                        LoggerService.LogInformation($"      Found PDF: {Path.GetFileName(pdfPath)}");

                        var generated = await ProcessPdfForGrade(pdfPath, record, newUploadRecords);
                        result.FilesGenerated += generated;
                        result.PdfsProcessed++;
                        record.IsPdfSave = true;   // mark so next run skips this record
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogInformation($"      ❌ Error: {ex.Message}");
                        result.ErrorMessages.Add($"{record.ClientId}: {ex.Message}");
                    }
                }

                // ── Step 4b: Append FileRose rows ─────────────────────────────
                LoggerService.LogInformation("\n📋 Step 4b: Appending FileRose rows to upload CSV...");
                int fileRoseRowsAdded = AppendFileRoseUploadRows(validationRecords, newUploadRecords);
                result.FileRoseRecordsCreated = fileRoseRowsAdded;
                LoggerService.LogInformation(fileRoseRowsAdded > 0
                    ? $"   ✅ {fileRoseRowsAdded} FileRose row(s) added"
                    : "   ℹ️  No eligible FileRose records");

                // ── Step 4: Merge new rows into Upload_to_PHIS.csv ────────────
                //    NEVER overwrites — loads existing rows, deduplicates by
                //    DocumentTitle, and appends only genuinely new rows.
                LoggerService.LogInformation("\n📋 Step 4: Merging new rows into Upload_to_PHIS.csv...");
                int appended = MergeUploadCsv(newUploadRecords);
                result.UploadRecordsCreated = appended;

                // ── Step 5: Update Validation CSV ─────────────────────────────
                LoggerService.LogInformation("\n📋 Step 5: Updating Validation_Results.csv...");
                SaveValidationCsv(validationRecords);

                // ── Step 6: Summary ───────────────────────────────────────────
                DisplaySummary(result);
                return result;
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"\n❌ FATAL ERROR: {ex.Message}");
                LoggerService.LogInformation($"Stack trace: {ex.StackTrace}");
                result.HasErrors = true;
                return result;
            }
        }

        // ── Upload CSV — safe merge (never overwrites) ────────────────────────

        /// <summary>
        /// Loads the existing Upload_to_PHIS.csv (if present), deduplicates the
        /// incoming <paramref name="newRecords"/> by <c>DocumentTitle</c>, appends
        /// only rows that are not already present, and saves.
        /// Returns the number of rows actually appended.
        /// </summary>
        private int MergeUploadCsv(List<UploadRecord> newRecords)
        {
            var outputPath = Path.Combine(_prePhase3Config.OutputPath, _phase2Config.UploadCsv);

            // ── Load existing rows ────────────────────────────────────────────
            var existingRecords = new List<UploadRecord>();

            if (File.Exists(outputPath))
            {
                try
                {
                    using var reader = new StreamReader(outputPath, Encoding.UTF8);
                    using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        MissingFieldFound = null,
                        HeaderValidated = null
                    });
                    csv.Context.RegisterClassMap<UploadRecordMap>();
                    existingRecords = csv.GetRecords<UploadRecord>().ToList();
                    LoggerService.LogInformation($"   📂 Loaded {existingRecords.Count} existing row(s) from Upload CSV");
                }
                catch (Exception ex)
                {
                    LoggerService.LogWarning($"   ⚠️  Could not read existing Upload CSV: {ex.Message} — will append to empty base");
                }
            }
            else
            {
                LoggerService.LogInformation("   📂 Upload CSV does not exist yet — will be created");
            }

            // ── Deduplicate: only keep new rows not already present ───────────
            var existingKeys = existingRecords
                .Select(r => r.DocumentTitle.Trim().ToUpperInvariant())
                .ToHashSet();

            var toAppend = newRecords
                .Where(r => !existingKeys.Contains(r.DocumentTitle.Trim().ToUpperInvariant()))
                .ToList();

            int skippedAlready = newRecords.Count - toAppend.Count;

            if (skippedAlready > 0)
                LoggerService.LogInformation($"   ⏭️  {skippedAlready} row(s) already in Upload CSV — skipped");

            if (toAppend.Count == 0)
            {
                LoggerService.LogInformation("   ℹ️  No new rows to append — Upload CSV unchanged");
                return 0;
            }

            // ── Write existing + new rows atomically ──────────────────────────
            var allRecords = existingRecords.Concat(toAppend).ToList();
            var tmpPath = outputPath + ".tmp";

            using (var writer = new StreamWriter(tmpPath, false, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                csv.Context.RegisterClassMap<UploadRecordMap>();
                csv.WriteRecords(allRecords);
            }

            File.Move(tmpPath, outputPath, overwrite: true);

            LoggerService.LogInformation($"   ✅ Appended {toAppend.Count} new row(s) → {outputPath}");
            LoggerService.LogInformation($"   📊 Upload CSV total: {allRecords.Count} row(s) " +
                $"({existingRecords.Count} existing + {toAppend.Count} new)");

            var consentCount = allRecords.Count(r => !r.IsFeuilleRose);
            var fileRoseCount = allRecords.Count(r => r.IsFeuilleRose);
            LoggerService.LogInformation($"      Consent rows  : {consentCount}");
            LoggerService.LogInformation($"      FileRose rows : {fileRoseCount}");

            return toAppend.Count;
        }

        // ── Step 4b helper ────────────────────────────────────────────────────

        private int AppendFileRoseUploadRows(
            IEnumerable<ValidationRecord> validationRecords,
            List<UploadRecord> uploadRecords)
        {
            var fileRoseOutputPath = _bulkPdfConfig.GetFileRoseOutputReadyPath();
            var schoolYear = _schoolContext.SchoolYear;
            var suffix = _bulkPdfConfig.RoseSuffix;
            int added = 0;

            foreach (var row in validationRecords)
            {
                if (!row.IsFileRoseDefault || !row.IsFileRoseExtracted ||
                    string.IsNullOrWhiteSpace(row.ClientId))
                    continue;

                var documentTitle = $"{row.ClientId}_{suffix}_{schoolYear}";
                var expectedPdfPath = Path.Combine(fileRoseOutputPath, $"{documentTitle}.pdf");

                if (!File.Exists(expectedPdfPath))
                {
                    LoggerService.LogWarning(
                        $"   ⚠️  FileRose PDF not found for {row.ClientId}: {expectedPdfPath}");
                    continue;
                }

                uploadRecords.Add(new UploadRecord
                {
                    ClientID = row.ClientId,
                    LastName = row.LastName,
                    FirstName = row.FirstName,
                    DocumentTitle = documentTitle,
                    Description = "Suivi scolaire",
                    PhisAntigen = string.Empty,
                    IsFeuilleRose = true,
                    VerifStatus = UploadVerificationStatus.NotProcessed,
                    FailureReason = string.Empty
                });

                added++;
                LoggerService.LogInformation(
                    $"   🌹 FileRose row added: {row.ClientId} → {documentTitle}");
            }

            return added;
        }

        // ── Consent processing ────────────────────────────────────────────────

        private async Task<int> ProcessPdfForGrade(
            string sourcePdfPath,
            ValidationRecord record,
            List<UploadRecord> uploadRecords)
        {
            int filesGenerated = 0;
            var schoolYear = _schoolContext.SchoolYear;
            var grade = record.Grade.Trim();

            string[] vaccineTypes;

            if (grade == "7" || grade.Contains("Grade 7", StringComparison.OrdinalIgnoreCase))
            {
                vaccineTypes = new[] { "HPV9", "Tdap" };
                LoggerService.LogInformation($"      Grade 7 → Generating 2 files (HPV9, Tdap)");
            }
            else if (grade == "9" || grade.Contains("Grade 9", StringComparison.OrdinalIgnoreCase))
            {
                vaccineTypes = new[] { "MenCACYW135" };
                LoggerService.LogInformation($"      Grade 9 → Generating 1 file (MenCACYW135)");
            }
            else
            {
                LoggerService.LogInformation($"      ⚠️  Unknown grade: {grade} — skipping");
                return 0;
            }

            foreach (var vaccineType in vaccineTypes)
            {
                var documentTitle = $"{record.ClientId}_consent{vaccineType}_{schoolYear}";
                var newFileName = $"{documentTitle}.pdf";
                var destinationPath = Path.Combine(_prePhase3Config.OutputPath, newFileName);

                File.Copy(sourcePdfPath, destinationPath, overwrite: true);
                LoggerService.LogInformation($"         → Created: {newFileName}");

                var description = $"Consent{vaccineType}";
                var phisAntigen = MapDescriptionToPhisAntigen(description);
                LoggerService.LogInformation($"         → PhisAntigen: {phisAntigen}");

                uploadRecords.Add(new UploadRecord
                {
                    ClientID = record.ClientId,
                    LastName = record.LastName,
                    FirstName = record.FirstName,
                    DocumentTitle = documentTitle,
                    Description = description,
                    PhisAntigen = phisAntigen,
                    IsFeuilleRose = false,
                    VerifStatus = UploadVerificationStatus.NotProcessed,
                    FailureReason = string.Empty
                });

                filesGenerated++;
            }

            await Task.CompletedTask;
            return filesGenerated;
        }

        private string MapDescriptionToPhisAntigen(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            if (_prePhase3Config.AntigenMapping.TryGetValue(description, out var phisAntigen))
                return phisAntigen;
            LoggerService.LogInformation($"         ⚠️  No antigen mapping found for '{description}'");
            return string.Empty;
        }

        private string? FindPdfForRecord(ValidationRecord record)
        {
            var pdfSourcePath = _bulkPdfConfig.GetOutputReadyPath();

            if (!string.IsNullOrEmpty(record.ExtractedName))
            {
                var parts = record.ExtractedName.Split(' ', 2);
                if (parts.Length == 2)
                {
                    var path = Path.Combine(pdfSourcePath, $"{parts[0]}_{parts[1]}.pdf");
                    if (File.Exists(path)) return path;
                }
            }

            var csvPath = Path.Combine(pdfSourcePath, $"{record.FirstName}_{record.LastName}.pdf");
            if (File.Exists(csvPath)) return csvPath;

            var clientPath = Path.Combine(pdfSourcePath, $"{record.ClientId}.pdf");
            if (File.Exists(clientPath)) return clientPath;

            if (!Directory.Exists(pdfSourcePath))
            {
                LoggerService.LogInformation($"      ⚠️  PDF source directory not found: {pdfSourcePath}");
                return null;
            }

            return Directory.GetFiles(pdfSourcePath, "*.pdf")
                .FirstOrDefault(f =>
                {
                    var n = Path.GetFileNameWithoutExtension(f);
                    return n.Contains(record.FirstName, StringComparison.OrdinalIgnoreCase) &&
                           n.Contains(record.LastName, StringComparison.OrdinalIgnoreCase);
                });
        }

        private List<ValidationRecord> LoadValidationCsv()
        {
            var csvPath = Path.Combine(
                _prePhase3Config.ValidationCsvPath,
                _prePhase3Config.ValidationCsvFileName);

            if (!File.Exists(csvPath))
                throw new FileNotFoundException($"Validation CSV not found: {csvPath}");

            using var reader = new StreamReader(csvPath, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));
            csv.Context.RegisterClassMap<ValidationRecordMap>();
            return csv.GetRecords<ValidationRecord>().ToList();
        }

        private void SaveValidationCsv(List<ValidationRecord> records)
        {
            var csvPath = Path.Combine(
                _prePhase3Config.ValidationCsvPath,
                _prePhase3Config.ValidationCsvFileName);

            using var writer = new StreamWriter(csvPath, false, Encoding.UTF8);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
            csv.Context.RegisterClassMap<ValidationRecordMap>();
            csv.WriteRecords(records);
            LoggerService.LogInformation($"   ✅ Updated: {csvPath}");
        }

        private void DisplaySummary(PrePhase3Result result)
        {
            LoggerService.LogInformation("\n" + new string('═', 60));
            LoggerService.LogInformation("📊 PRE-PHASE 3 COMPLETE - Final Summary");
            LoggerService.LogInformation(new string('═', 60));
            LoggerService.LogInformation($"Total validation records  : {result.TotalRecords}");
            LoggerService.LogInformation($"🔀 Duplicates merged      : {result.DuplicatesMerged}");
            LoggerService.LogInformation($"✅ Validated records      : {result.ValidatedRecords}");
            LoggerService.LogInformation($"⏭️  Skipped               : {result.SkippedNotValidated}");
            LoggerService.LogInformation($"📄 PDFs processed         : {result.PdfsProcessed}");
            LoggerService.LogInformation($"📄 Files generated        : {result.FilesGenerated}");
            LoggerService.LogInformation($"🌹 FileRose rows added    : {result.FileRoseRecordsCreated}");
            LoggerService.LogInformation($"📋 New rows appended      : {result.UploadRecordsCreated}");
            LoggerService.LogInformation($"⚠️  Missing PDFs          : {result.SkippedMissingPdf}");
            LoggerService.LogInformation(new string('═', 60));

            if (result.ErrorMessages.Count > 0)
            {
                LoggerService.LogInformation("\n⚠️  Errors:");
                foreach (var error in result.ErrorMessages.Take(10))
                    LoggerService.LogInformation($"   - {error}");
            }

            if (result.UploadRecordsCreated > 0 || result.ValidatedRecords > 0)
            {
                LoggerService.LogInformation($"\n✅ Ready for Phase 3: Upload to PHIS");
                LoggerService.LogInformation($"   Upload CSV   : {_phase2Config.UploadCsv}");
                LoggerService.LogInformation($"   Consent PDFs : {_prePhase3Config.OutputPath}");
                LoggerService.LogInformation($"   FileRose PDFs: {_bulkPdfConfig.GetFileRoseOutputReadyPath()}");
            }
        }
    }
}