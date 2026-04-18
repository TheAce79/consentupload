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
                LoggerService.LogInformation("\n📋 Step 2: Filtering validated records...");
                var validatedRecords = validationRecords
                    .Where(r => r.FileFound &&
                                r.IsMatch &&
                                !string.IsNullOrWhiteSpace(r.ClientId))
                    .ToList();

                result.ValidatedRecords = validatedRecords.Count;
                result.SkippedNotValidated = validationRecords.Count - validatedRecords.Count;

                LoggerService.LogInformation($"   ✅ Validated records : {validatedRecords.Count}");
                LoggerService.LogInformation($"   ⏭️  Skipped           : {result.SkippedNotValidated}");

                if (validatedRecords.Count == 0 &&
                    !validationRecords.Any(r => r.IsFileRoseDefault && r.IsFileRoseExtracted &&
                                               !string.IsNullOrWhiteSpace(r.ClientId)))
                {
                    LoggerService.LogInformation("\n⚠️  No validated records to process!");
                    return result;
                }

                // ── Step 3: Process consent PDFs ──────────────────────────────
                LoggerService.LogInformation($"\n📋 Step 3: Processing {validatedRecords.Count} consent PDFs...");
                var uploadRecords = new List<UploadRecord>();

                foreach (var record in validatedRecords)
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

                        var generated = await ProcessPdfForGrade(pdfPath, record, uploadRecords);
                        result.FilesGenerated += generated;
                        result.PdfsProcessed++;
                        record.IsPdfSave = true;
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogInformation($"      ❌ Error: {ex.Message}");
                        result.ErrorMessages.Add($"{record.ClientId}: {ex.Message}");
                    }
                }

                // ── Step 4b: Append FileRose rows ─────────────────────────────
                LoggerService.LogInformation("\n📋 Step 4b: Appending FileRose rows to upload CSV...");
                int fileRoseRowsAdded = AppendFileRoseUploadRows(validationRecords, uploadRecords);
                result.FileRoseRecordsCreated = fileRoseRowsAdded;
                LoggerService.LogInformation(fileRoseRowsAdded > 0
                    ? $"   ✅ {fileRoseRowsAdded} FileRose row(s) added"
                    : "   ℹ️  No eligible FileRose records");

                // ── Step 4: Generate Upload_to_PHIS.csv ───────────────────────
                LoggerService.LogInformation("\n📋 Step 4: Generating Upload_to_PHIS.csv...");
                GenerateUploadCsv(uploadRecords);
                result.UploadRecordsCreated = uploadRecords.Count;

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

        // ── Step 4b helper ────────────────────────────────────────────────────

        /// <summary>
        /// For every validation row where
        ///   • IsFileRoseDefault  = true
        ///   • IsFileRoseExtracted = true
        ///   • ClientId is not empty
        /// verifies the renamed PDF exists in <c>2_Output_Ready_FileRose</c> and,
        /// if so, adds one <see cref="UploadRecord"/> (IsFeuilleRose = true) to
        /// <paramref name="uploadRecords"/>.
        /// </summary>
        private int AppendFileRoseUploadRows(
            IEnumerable<ValidationRecord> validationRecords,
            List<UploadRecord> uploadRecords)
        {
            var fileRoseOutputPath = _bulkPdfConfig.GetFileRoseOutputReadyPath();
            var schoolYear = _schoolContext.SchoolYear;
            var suffix = _bulkPdfConfig.RoseSuffix;   // e.g. "suiviscolaire"
            int added = 0;

            foreach (var row in validationRecords)
            {
                // Eligibility gate
                if (!row.IsFileRoseDefault || !row.IsFileRoseExtracted ||
                    string.IsNullOrWhiteSpace(row.ClientId))
                    continue;

                // Build expected file name: {ClientId}_{suffix}_{schoolYear}.pdf
                var documentTitle = $"{row.ClientId}_{suffix}_{schoolYear}"; // no extension
                var expectedPdfName = $"{documentTitle}.pdf";
                var expectedPdfPath = Path.Combine(fileRoseOutputPath, expectedPdfName);

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
                    DocumentTitle = documentTitle,          // without .pdf
                    Description = "Suivi scolaire",
                    PhisAntigen = string.Empty,           // not used for FileRose
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

        // ── Consent processing (unchanged logic) ──────────────────────────────

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

        private void GenerateUploadCsv(List<UploadRecord> records)
        {
            var outputPath = Path.Combine(_prePhase3Config.OutputPath, _phase2Config.UploadCsv);

            using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
            csv.Context.RegisterClassMap<UploadRecordMap>();
            csv.WriteRecords(records);

            LoggerService.LogInformation($"   ✅ Generated: {outputPath}");
            LoggerService.LogInformation($"   📊 Total upload records: {records.Count}");

            var consentCount = records.Count(r => !r.IsFeuilleRose);
            var fileRoseCount = records.Count(r => r.IsFeuilleRose);
            LoggerService.LogInformation($"      Consent rows  : {consentCount}");
            LoggerService.LogInformation($"      FileRose rows : {fileRoseCount}");
        }

        private void DisplaySummary(PrePhase3Result result)
        {
            LoggerService.LogInformation("\n" + new string('═', 60));
            LoggerService.LogInformation("📊 PRE-PHASE 3 COMPLETE - Final Summary");
            LoggerService.LogInformation(new string('═', 60));
            LoggerService.LogInformation($"Total validation records : {result.TotalRecords}");
            LoggerService.LogInformation($"🔀 Duplicates merged     : {result.DuplicatesMerged}");
            LoggerService.LogInformation($"✅ Validated records     : {result.ValidatedRecords}");
            LoggerService.LogInformation($"⏭️  Skipped              : {result.SkippedNotValidated}");
            LoggerService.LogInformation($"📄 PDFs processed        : {result.PdfsProcessed}");
            LoggerService.LogInformation($"📄 Files generated       : {result.FilesGenerated}");
            LoggerService.LogInformation($"🌹 FileRose rows added   : {result.FileRoseRecordsCreated}");
            LoggerService.LogInformation($"📋 Upload records total  : {result.UploadRecordsCreated}");
            LoggerService.LogInformation($"⚠️  Missing PDFs         : {result.SkippedMissingPdf}");
            LoggerService.LogInformation(new string('═', 60));

            if (result.ErrorMessages.Count > 0)
            {
                LoggerService.LogInformation("\n⚠️  Errors:");
                foreach (var error in result.ErrorMessages.Take(10))
                    LoggerService.LogInformation($"   - {error}");
            }

            if (result.UploadRecordsCreated > 0)
            {
                LoggerService.LogInformation($"\n✅ Ready for Phase 3: Upload to PHIS");
                LoggerService.LogInformation($"   Upload CSV: {_phase2Config.UploadCsv}");
                LoggerService.LogInformation($"   Consent PDFs : {_prePhase3Config.OutputPath}");
                LoggerService.LogInformation($"   FileRose PDFs: {_bulkPdfConfig.GetFileRoseOutputReadyPath()}");
            }
        }
    }
}