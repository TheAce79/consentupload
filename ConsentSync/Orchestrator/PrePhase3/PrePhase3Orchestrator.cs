
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

                // ── Step 1b: Re-scan 3_Output_Ready for {ClientId}.pdf renames ─
                LoggerService.LogInformation("\n📋 Step 1b: Re-scanning 3_Output_Ready for manually corrected PDFs...");
                int rescanned = RescanForClientIdRenames(validationRecords);
                LoggerService.LogInformation(rescanned > 0
                    ? $"   ✅ {rescanned} record(s) updated from ClientId-renamed PDFs"
                    : "   ℹ️  No ClientId-renamed PDFs found");

                // ── Early-exit: nothing left to do ────────────────────────────
                var pdfSourcePath = _bulkPdfConfig.GetOutputReadyPath();
                var uploadCsvPath = Path.Combine(_prePhase3Config.OutputPath, _phase2Config.UploadCsv);
                bool outputReadyIsEmpty = !Directory.Exists(pdfSourcePath) ||
                                          Directory.GetFiles(pdfSourcePath, "*.pdf").Length == 0;
                bool uploadCsvExists = File.Exists(uploadCsvPath);

                if (outputReadyIsEmpty && uploadCsvExists && rescanned == 0)
                {
                    LoggerService.LogInformation(
                        "\n   ℹ️  3_Output_Ready is empty and Upload CSV already exists — nothing to do.");
                    LoggerService.LogInformation(
                        "   💡 To process more PDFs: rename unmatched files to {ClientId}.pdf,");
                    LoggerService.LogInformation(
                        "      drop them into 3_Output_Ready, then click Generate Upload CSV again.");
                    result.AlreadyProcessed = true;
                    return result;
                }

                // ── Step 2: Filter validated consent records ──────────────────
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

                if (result.SkippedNotValidated > 0)
                {
                    LoggerService.LogWarning($"\n   ⚠️  {result.SkippedNotValidated} record(s) have no matching PDF.");
                    LoggerService.LogWarning("   💡 To fix: rename the PDF to {ClientId}.pdf, place it in");
                    LoggerService.LogWarning($"      {pdfSourcePath}");
                    LoggerService.LogWarning("      then click Generate Upload CSV again.");
                }

                // ── Early-exit when nothing new to process ────────────────────
                if (toProcess.Count == 0 &&
                    !validationRecords.Any(r => r.IsFileRoseDefault && r.IsFileRoseExtracted &&
                                               !string.IsNullOrWhiteSpace(r.ClientId)))
                {
                    LoggerService.LogInformation(
                        "\n   ℹ️  No new records to process — Upload CSV is already up to date.");

                    // Save if rescanned records were promoted
                    if (rescanned > 0)
                        SaveValidationCsv(validationRecords);

                    // ✅ Report remaining BEFORE returning so UI shows unmatched files
                    ReportRemainingPdfs(pdfSourcePath, result);
                    DisplaySummary(result);
                    return result;
                }

                // ── Step 3: Process NEW consent PDFs only ─────────────────────
                LoggerService.LogInformation($"\n📋 Step 3: Processing {toProcess.Count} new consent PDF(s)...");
                var newUploadRecords = new List<UploadRecord>();

                foreach (var record in toProcess)
                {
                    LoggerService.LogInformation(
                        $"\n   Processing: {record.FirstName} {record.LastName} ({record.ClientId})");
                    try
                    {
                        var pdfPath = FindPdfForRecord(record);
                        if (string.IsNullOrEmpty(pdfPath))
                        {
                            LoggerService.LogWarning($"      ⚠️  PDF not found for {record.ClientId}");
                            result.SkippedMissingPdf++;
                            result.ErrorMessages.Add(
                                $"{record.ClientId}: PDF not found in 3_Output_Ready" +
                                $" — rename the file to {record.ClientId}.pdf and re-run");
                            continue;
                        }

                        LoggerService.LogInformation($"      Found PDF: {Path.GetFileName(pdfPath)}");

                        var generated = await ProcessPdfForGrade(pdfPath, record, newUploadRecords);
                        if (generated > 0)
                        {
                            result.FilesGenerated += generated;
                            result.PdfsProcessed++;
                            record.IsPdfSave = true;

                            // ✅ Archive source PDF from 3_Output_Ready AFTER
                            //    successful copy to 1 Consent Upload
                            ArchiveSourcePdf(pdfPath);
                        }
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
                LoggerService.LogInformation("\n📋 Step 4: Merging new rows into Upload_to_PHIS.csv...");
                int appended = MergeUploadCsv(newUploadRecords);
                result.UploadRecordsCreated = appended;

                // ── Step 5: Update Validation CSV ─────────────────────────────
                LoggerService.LogInformation("\n📋 Step 5: Updating Validation_Results.csv...");
                SaveValidationCsv(validationRecords);

                // ── Step 6: Report remaining AFTER archiving ──────────────────
                // ✅ Only truly unmatched files remain at this point —
                //    all processed ones have been moved to 7_Archive\Consent\
                ReportRemainingPdfs(pdfSourcePath, result);

                // ── Step 7: Summary ───────────────────────────────────────────
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


        // ─────────────────────────────────────────────────────────────────────
        // Step 1b — Re-scan for {ClientId}.pdf renames
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds every <c>{ClientId}.pdf</c> in <c>3_Output_Ready</c> dropped by the
        /// user to manually resolve an unmatched PDF and updates the corresponding
        /// <see cref="ValidationRecord"/> so it is picked up by Step 2.
        ///
        /// Accepted as a ClientId filename when:
        ///   - No underscores  (bulk-extracted files always have underscores), AND
        ///   - If purely numeric, length ≥ 5 digits
        ///     (PHIS ClientIds are 5-7 digits; page-index stubs like "1", "108" are ≤ 4)
        /// </summary>
        private int RescanForClientIdRenames(List<ValidationRecord> validationRecords)
        {
            var pdfSourcePath = _bulkPdfConfig.GetOutputReadyPath();
            if (!Directory.Exists(pdfSourcePath)) return 0;

            int updated = 0;

            foreach (var file in Directory.GetFiles(pdfSourcePath, "*.pdf"))
            {
                var stem = Path.GetFileNameWithoutExtension(file).Trim();

                // Bulk-extracted files always contain underscores — skip them
                if (stem.Contains('_'))
                    continue;

                // Purely numeric: accept only if ≥ 4 digits.
                // Bulk page-index stubs (1, 13, 108 …) are at most 3 digits.
                // PHIS ClientIds start at 4 digits (e.g. 3678).
                if (long.TryParse(stem, out _) && stem.Length < 4)
                    continue;

                var clientId = stem;
                var record = validationRecords.FirstOrDefault(r =>
                    !string.IsNullOrWhiteSpace(r.ClientId) &&
                    r.ClientId.Trim().Equals(clientId, StringComparison.OrdinalIgnoreCase));

                if (record == null)
                {
                    LoggerService.LogWarning(
                        $"   ⚠️  ClientId-renamed PDF has no matching record: {Path.GetFileName(file)}");
                    continue;
                }

                if (record.IsPdfSave)
                {
                    LoggerService.LogInformation($"   ⏭️  Already processed: {clientId} — skipping");
                    continue;
                }

                record.FileFound = true;
                record.IsMatch = true;
                record.ExtractedName = $"{record.FirstName} {record.LastName}";
                record.NormalizedPDF = NormalizeName(record.FirstName, record.LastName);
                record.MatchScore = 100.0;
                record.ValidationNotes = "Matched by ClientId (manually corrected — re-scan)";

                LoggerService.LogInformation(
                    $"   ✅ Re-scanned: {clientId} → {record.FirstName} {record.LastName}");
                updated++;
            }

            return updated;
        }



        // ─────────────────────────────────────────────────────────────────────
        // FindPdfForRecord — compound-surname aware, corruption-safe
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Locates the source consent PDF for a validation record in <c>3_Output_Ready</c>.
        ///
        /// Search order:
        ///   Pass 1 — Exact <c>{ClientId}.pdf</c> (manually corrected rename)
        ///   Pass 2 — <c>ExtractedName</c> tokens, but ONLY when <c>MatchScore ≥ 85</c>
        ///             (guards against corrupted ExtractedName left by old weak-match runs)
        ///   Pass 3 — All CSV name tokens (LastName + FirstName, every word part)
        ///   Pass 4 — LastName tokens only (≥ 2 tokens) for compound/double-barrel surnames
        ///             where the first name is absent from the PDF filename
        /// </summary>
        private string? FindPdfForRecord(ValidationRecord record)
        {
            var pdfSourcePath = _bulkPdfConfig.GetOutputReadyPath();
            if (!Directory.Exists(pdfSourcePath)) return null;

            string NormToken(string s) =>
                RemoveAccents(s.Trim().Replace(' ', '_').Replace('-', '_').ToUpperInvariant());

            var allPdfs = Directory.GetFiles(pdfSourcePath, "*.pdf");

            // ── Pass 1: exact {ClientId}.pdf ──────────────────────────────────
            var clientPath = Path.Combine(pdfSourcePath, $"{record.ClientId}.pdf");
            if (File.Exists(clientPath)) return clientPath;

            // ── Pass 2: ExtractedName tokens (strong matches only) ────────────
            // ExtractedName is only trustworthy when MatchScore ≥ 85 — below that
            // threshold Phase 2 may have written a different student's name into this
            // field (e.g. "Félix Comeau" written into Erik Comeau's row at 76%).
            if (!string.IsNullOrWhiteSpace(record.ExtractedName) && record.MatchScore >= 85.0)
            {
                var extractedTokens = record.ExtractedName
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormToken)
                    .Where(t => t.Length > 1)
                    .ToArray();

                if (extractedTokens.Length > 0)
                {
                    var match = allPdfs.FirstOrDefault(f =>
                    {
                        var stemNorm = NormToken(Path.GetFileNameWithoutExtension(f));
                        return extractedTokens.All(t => stemNorm.Contains(t));
                    });
                    if (match != null)
                    {
                        LoggerService.LogInformation(
                            $"      🔍 Found by ExtractedName tokens: {Path.GetFileName(match)}");
                        return match;
                    }
                }
            }

            // ── Pass 3: all CSV name tokens (LastName + FirstName) ────────────
            var lastTokens = record.LastName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormToken).Where(t => t.Length > 1).ToArray();

            var firstTokens = record.FirstName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormToken).Where(t => t.Length > 1).ToArray();

            var allTokens = lastTokens.Concat(firstTokens).ToArray();

            if (allTokens.Length > 0)
            {
                var match = allPdfs.FirstOrDefault(f =>
                {
                    var stemNorm = NormToken(Path.GetFileNameWithoutExtension(f));
                    return allTokens.All(t => stemNorm.Contains(t));
                });
                if (match != null)
                {
                    LoggerService.LogInformation(
                        $"      🔍 Found by full name tokens: {Path.GetFileName(match)}");
                    return match;
                }
            }

            // ── Pass 4: LastName tokens only (compound surnames) ──────────────
            // For e.g. LastName="Puente Delgadillo", FirstName="José", the PDF filename
            // only encodes the compound surname — the first name is never embedded.
            if (lastTokens.Length >= 2)
            {
                var match = allPdfs.FirstOrDefault(f =>
                {
                    var stemNorm = NormToken(Path.GetFileNameWithoutExtension(f));
                    return lastTokens.All(t => stemNorm.Contains(t));
                });
                if (match != null)
                {
                    LoggerService.LogInformation(
                        $"      🔍 Found by LastName tokens (compound surname): {Path.GetFileName(match)}");
                    return match;
                }
            }

            LoggerService.LogWarning(
                $"      ❌ PDF not found for {record.FirstName} {record.LastName} ({record.ClientId})");
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Archive helper
        // ─────────────────────────────────────────────────────────────────────


        private void ArchiveSourcePdf(string sourcePdfPath)
        {
            try
            {
                var archiveConsentDir = Path.Combine(_bulkPdfConfig.GetArchivePath(), "Consent");
                Directory.CreateDirectory(archiveConsentDir);

                var fileName = Path.GetFileName(sourcePdfPath);
                var dest = Path.Combine(archiveConsentDir, fileName);

                File.Move(sourcePdfPath, dest, overwrite: true);
                LoggerService.LogInformation($"      📦 Archived: {fileName} → 7_Archive\\Consent\\");
            }
            catch (Exception ex)
            {
                LoggerService.LogWarning($"      ⚠️  Could not archive PDF: {ex.Message}");
            }
        }




        // ─────────────────────────────────────────────────────────────────────
        // Report remaining unmatched PDFs
        // ─────────────────────────────────────────────────────────────────────

      

        private void ReportRemainingPdfs(string pdfSourcePath, PrePhase3Result result)
        {
            if (!Directory.Exists(pdfSourcePath)) return;

            var remaining = Directory.GetFiles(pdfSourcePath, "*.pdf");
            if (remaining.Length == 0)
            {
                LoggerService.LogInformation(
                    "\n   ✅ 3_Output_Ready is now empty — all PDFs have been processed.");
                return;
            }

            // Store in result so the UI can display them prominently
            result.RemainingUnmatchedPdfs.AddRange(remaining.Select(Path.GetFileName)!);

            LoggerService.LogWarning(
                $"\n   ⚠️  {remaining.Length} unmatched PDF(s) still in 3_Output_Ready:");
            LoggerService.LogWarning(
                "   💡 Rename each to {ClientId}.pdf, then click Generate Upload CSV again:");
            foreach (var f in remaining)
                LoggerService.LogWarning($"      • {Path.GetFileName(f)}");
        }



        // ─────────────────────────────────────────────────────────────────────
        // Upload CSV — safe merge (never overwrites)
        // ─────────────────────────────────────────────────────────────────────

        private int MergeUploadCsv(List<UploadRecord> newRecords)
        {
            var outputPath = Path.Combine(_prePhase3Config.OutputPath, _phase2Config.UploadCsv);

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
                    LoggerService.LogInformation(
                        $"   📂 Loaded {existingRecords.Count} existing row(s) from Upload CSV");
                }
                catch (Exception ex)
                {
                    LoggerService.LogWarning(
                        $"   ⚠️  Could not read existing Upload CSV: {ex.Message} — appending to empty base");
                }
            }
            else
            {
                LoggerService.LogInformation("   📂 Upload CSV does not exist yet — will be created");
            }

            var existingKeys = existingRecords
                .Select(r => r.DocumentTitle.Trim().ToUpperInvariant())
                .ToHashSet();

            var toAppend = newRecords
                .Where(r => !existingKeys.Contains(r.DocumentTitle.Trim().ToUpperInvariant()))
                .ToList();

            int skippedAlready = newRecords.Count - toAppend.Count;
            if (skippedAlready > 0)
                LoggerService.LogInformation(
                    $"   ⏭️  {skippedAlready} row(s) already in Upload CSV — skipped");

            if (toAppend.Count == 0)
            {
                LoggerService.LogInformation("   ℹ️  No new rows to append — Upload CSV unchanged");
                return 0;
            }

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
            LoggerService.LogInformation(
                $"   📊 Upload CSV total: {allRecords.Count} row(s) " +
                $"({existingRecords.Count} existing + {toAppend.Count} new)");
            LoggerService.LogInformation($"      Consent rows  : {allRecords.Count(r => !r.IsFeuilleRose)}");
            LoggerService.LogInformation($"      FileRose rows : {allRecords.Count(r => r.IsFeuilleRose)}");

            return toAppend.Count;
        }

        // ─────────────────────────────────────────────────────────────────────
        // FileRose rows
        // ─────────────────────────────────────────────────────────────────────

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

        // ─────────────────────────────────────────────────────────────────────
        // Consent PDF processing
        // ─────────────────────────────────────────────────────────────────────

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

            Directory.CreateDirectory(_prePhase3Config.ConsentPdfOutputPath);

            foreach (var vaccineType in vaccineTypes)
            {
                var documentTitle = $"{record.ClientId}_consent{vaccineType}_{schoolYear}";
                var newFileName = $"{documentTitle}.pdf";
                var destinationPath = Path.Combine(
                    _prePhase3Config.ConsentPdfOutputPath, newFileName);

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
            LoggerService.LogInformation(
                $"         ⚠️  No antigen mapping found for '{description}'");
            return string.Empty;
        }

        // ─────────────────────────────────────────────────────────────────────
        // CSV helpers
        // ─────────────────────────────────────────────────────────────────────

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

        // ─────────────────────────────────────────────────────────────────────
        // Name helpers
        // ─────────────────────────────────────────────────────────────────────

        private string NormalizeName(string firstName, string lastName)
        {
            var normalizedFirst = RemoveAccents(firstName.Trim().ToUpperInvariant());
            var normalizedLast = RemoveAccents(lastName.Trim().ToUpperInvariant());
            return $"{normalizedFirst} {normalizedLast}";
        }

        private string RemoveAccents(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalizedString)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                    System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Summary
        // ─────────────────────────────────────────────────────────────────────

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
                LoggerService.LogWarning("\n⚠️  Unmatched / missing PDFs:");
                foreach (var error in result.ErrorMessages)
                    LoggerService.LogWarning($"   - {error}");
            }

            if (result.UploadRecordsCreated > 0 || result.ValidatedRecords > 0)
            {
                LoggerService.LogInformation($"\n✅ Ready for Phase 3: Upload to PHIS");
                LoggerService.LogInformation(
                    $"   Upload CSV   : {_prePhase3Config.OutputPath}\\{_phase2Config.UploadCsv}");
                LoggerService.LogInformation(
                    $"   Consent PDFs : {_prePhase3Config.ConsentPdfOutputPath}");
                LoggerService.LogInformation(
                    $"   FileRose PDFs: {_bulkPdfConfig.GetFileRoseOutputReadyPath()}");
            }
        }
    }
}