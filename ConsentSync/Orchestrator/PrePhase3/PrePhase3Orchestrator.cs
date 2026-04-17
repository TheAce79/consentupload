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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

                // ── Step 0: Merge resolved duplicate PDFs into 3_Output_Ready ─
                // Must run BEFORE loading Validation_Results.csv so the merged
                // PDF is visible to FindPdfForRecord() in Step 3.
                LoggerService.LogInformation("📋 Step 0: Processing resolved duplicate PDFs...");
                var mergeService = new DuplicateMergeService(_config);
                int merged = mergeService.MergeResolvedDuplicates();
                result.DuplicatesMerged = merged;
                LoggerService.LogInformation(merged > 0
                    ? $"   ✅ {merged} duplicate group(s) merged and moved to 3_Output_Ready"
                    : "   ℹ️  No resolved duplicates to merge");


                // Step 1: Load Validation CSV
                LoggerService.LogInformation("📋 Step 1: Loading Validation_Results.csv...");
                var validationRecords = LoadValidationCsv();
                result.TotalRecords = validationRecords.Count;

                 LoggerService.LogInformation($"   ✅ Loaded {validationRecords.Count} validation records");

                // Step 2: Filter validated records
                 LoggerService.LogInformation("\n📋 Step 2: Filtering validated records...");
                var validatedRecords = validationRecords
                    .Where(r =>
                        r.FileFound == true &&
                        r.IsMatch == true &&
                        !string.IsNullOrWhiteSpace(r.ClientId))
                    .ToList();

                result.ValidatedRecords = validatedRecords.Count;
                result.SkippedNotValidated = validationRecords.Count - validatedRecords.Count;

                 LoggerService.LogInformation($"   ✅ Validated records: {validatedRecords.Count}");
                 LoggerService.LogInformation($"   ⏭️  Skipped (not validated): {result.SkippedNotValidated}");

                if (validatedRecords.Count == 0)
                {
                     LoggerService.LogInformation("\n⚠️  No validated records to process!");
                     LoggerService.LogInformation("   💡 Please review Validation_Results.csv and fix FileFound/IsMatch flags");
                    return result;
                }

                // Step 3: Process each validated record
                 LoggerService.LogInformation($"\n📋 Step 3: Processing {validatedRecords.Count} validated PDFs...");
                var uploadRecords = new List<UploadRecord>();

                foreach (var record in validatedRecords)
                {
                     LoggerService.LogInformation($"\n   Processing: {record.FirstName} {record.LastName} (Client ID: {record.ClientId})");

                    try
                    {
                        // Find the original PDF
                        var pdfPath = FindPdfForRecord(record);

                        if (string.IsNullOrEmpty(pdfPath))
                        {
                             LoggerService.LogInformation($"      ⚠️  PDF not found for {record.ClientId}");
                            result.SkippedMissingPdf++;
                            result.ErrorMessages.Add($"{record.ClientId}: PDF file not found");
                            continue;
                        }

                         LoggerService.LogInformation($"      Found PDF: {Path.GetFileName(pdfPath)}");

                        // Process based on grade
                        var generated = await ProcessPdfForGrade(
                            pdfPath,
                            record,
                            uploadRecords);

                        result.FilesGenerated += generated;
                        result.PdfsProcessed++;

                        // Mark as processed
                        record.IsPdfSave = true;
                    }
                    catch (Exception ex)
                    {
                         LoggerService.LogInformation($"      ❌ Error: {ex.Message}");
                        result.ErrorMessages.Add($"{record.ClientId}: {ex.Message}");
                    }
                }

                // Step 4: Generate Upload_to_PHIS.csv
                 LoggerService.LogInformation($"\n📋 Step 4: Generating Upload_to_PHIS.csv...");
                GenerateUploadCsv(uploadRecords);
                result.UploadRecordsCreated = uploadRecords.Count;

                // Step 5: Update Validation CSV with IsPdfSave flags
                 LoggerService.LogInformation($"\n📋 Step 5: Updating Validation_Results.csv...");
                SaveValidationCsv(validationRecords);

                // Step 6: Display summary
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



        /// <summary>
        /// ✅ Process PDF based on student's grade (extracted from Phase2Orchestrator)
        /// </summary>
        private async Task<int> ProcessPdfForGrade(
            string sourcePdfPath,
            ValidationRecord record,
            List<UploadRecord> uploadRecords)
        {
            int filesGenerated = 0;
            var schoolYear = _schoolContext.SchoolYear;
            var grade = record.Grade.Trim();

            // Determine vaccine types based on grade
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
                 LoggerService.LogInformation($"      ⚠️  Unknown grade: {grade} - skipping");
                return 0;
            }

            // Generate file for each vaccine type
            foreach (var vaccineType in vaccineTypes)
            {
                var documentTitle = $"{record.ClientId}_consent{vaccineType}_{schoolYear}";
                var newFileName = $"{documentTitle}.pdf";
                var destinationPath = Path.Combine(_prePhase3Config.OutputPath, newFileName);

                // Copy and rename file
                File.Copy(sourcePdfPath, destinationPath, overwrite: true);
                 LoggerService.LogInformation($"         → Created: {newFileName}");

                // ✅ Map Description to PhisAntigen
                var description = $"Consent{vaccineType}";
                var phisAntigen = MapDescriptionToPhisAntigen(description);

                 LoggerService.LogInformation($"         → PhisAntigen: {phisAntigen}");

                // Add to upload records
                uploadRecords.Add(new UploadRecord
                {
                    ClientID = record.ClientId,
                    LastName = record.LastName,
                    FirstName = record.FirstName,
                    DocumentTitle = documentTitle,
                    Description = description,
                    PhisAntigen = phisAntigen, // ✅ NEW
                    IsFeuilleRose = false,
                    Status = "",
                    IsFeuilleRoseUpload = false
                });

                filesGenerated++;
            }

            await Task.CompletedTask;
            return filesGenerated;
        }

        /// <summary>
        /// ✅ NEW: Map Description to PHIS Antigen name using configuration
        /// </summary>
        private string MapDescriptionToPhisAntigen(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return string.Empty;

            // Check if mapping exists in configuration
            if (_prePhase3Config.AntigenMapping.TryGetValue(description, out var phisAntigen))
            {
                return phisAntigen;
            }

            // Fallback: log warning and return empty
             LoggerService.LogInformation($"         ⚠️  WARNING: No antigen mapping found for '{description}'");
            return string.Empty;
        }

        /// <summary>
        /// Find PDF file for a validation record using BulkPdfExtraction OutputReadyFolder
        /// </summary>
        private string? FindPdfForRecord(ValidationRecord record)
        {
            // ✅ Use BulkPdfExtraction.OutputReadyFolder instead of PrePhase3.PdfSourcePath
            var pdfSourcePath = _bulkPdfConfig.GetOutputReadyPath();

            // Try to find by extracted name first
            if (!string.IsNullOrEmpty(record.ExtractedName))
            {
                var extractedParts = record.ExtractedName.Split(' ', 2);
                if (extractedParts.Length == 2)
                {
                    var possibleFileName = $"{extractedParts[0]}_{extractedParts[1]}.pdf";
                    var possiblePath = Path.Combine(pdfSourcePath, possibleFileName);

                    if (File.Exists(possiblePath))
                        return possiblePath;
                }
            }

            // Try by CSV name
            var csvFileName = $"{record.FirstName}_{record.LastName}.pdf";
            var csvPath = Path.Combine(pdfSourcePath, csvFileName);

            if (File.Exists(csvPath))
                return csvPath;

            // Try by ClientID
            var clientIdFileName = $"{record.ClientId}.pdf";
            var clientIdPath = Path.Combine(pdfSourcePath, clientIdFileName);

            if (File.Exists(clientIdPath))
                return clientIdPath;

            // Search all PDFs in directory
            if (!Directory.Exists(pdfSourcePath))
            {
                 LoggerService.LogInformation($"      ⚠️  PDF source directory does not exist: {pdfSourcePath}");
                return null;
            }

            var allPdfs = Directory.GetFiles(pdfSourcePath, "*.pdf");
            foreach (var pdf in allPdfs)
            {
                var fileName = Path.GetFileNameWithoutExtension(pdf);
                if (fileName.Contains(record.FirstName, StringComparison.OrdinalIgnoreCase) &&
                    fileName.Contains(record.LastName, StringComparison.OrdinalIgnoreCase))
                {
                    return pdf;
                }
            }

            return null;
        }

        /// <summary>
        /// Load Validation_Results.csv
        /// </summary>
        private List<ValidationRecord> LoadValidationCsv()
        {
            var csvPath = Path.Combine(_prePhase3Config.ValidationCsvPath, _prePhase3Config.ValidationCsvFileName);

            if (!File.Exists(csvPath))
            {
                throw new FileNotFoundException($"Validation CSV not found: {csvPath}");
            }

            using var reader = new StreamReader(csvPath, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));

            csv.Context.RegisterClassMap<ValidationRecordMap>();
            return csv.GetRecords<ValidationRecord>().ToList();
        }

        /// <summary>
        /// Save updated Validation_Results.csv
        /// </summary>
        private void SaveValidationCsv(List<ValidationRecord> records)
        {
            var csvPath = Path.Combine(_prePhase3Config.ValidationCsvPath, _prePhase3Config.ValidationCsvFileName);

            using var writer = new StreamWriter(csvPath, false, Encoding.UTF8);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

            csv.Context.RegisterClassMap<ValidationRecordMap>();
            csv.WriteRecords(records);

             LoggerService.LogInformation($"   ✅ Updated: {csvPath}");
        }

        /// <summary>
        /// Generate Upload_to_PHIS.csv
        /// </summary>
        private void GenerateUploadCsv(List<UploadRecord> records)
        {
            var outputPath = Path.Combine(_prePhase3Config.OutputPath, _phase2Config.UploadCsv);

            using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

            csv.Context.RegisterClassMap<UploadRecordMap>();
            csv.WriteRecords(records);

             LoggerService.LogInformation($"   ✅ Generated: {outputPath}");
             LoggerService.LogInformation($"   📊 Total upload records: {records.Count}");
        }

        private void DisplaySummary(PrePhase3Result result)
        {
             LoggerService.LogInformation("\n" + new string('═', 60));
             LoggerService.LogInformation("📊 PRE-PHASE 3 COMPLETE - Final Summary");
             LoggerService.LogInformation(new string('═', 60));
             LoggerService.LogInformation($"Total validation records: {result.TotalRecords}");
            LoggerService.LogInformation($"🔀 Duplicate groups merged: {result.DuplicatesMerged}");
            LoggerService.LogInformation($"✅ Validated records: {result.ValidatedRecords}");
            LoggerService.LogInformation($"⏭️  Skipped (not validated): {result.SkippedNotValidated}");
             LoggerService.LogInformation($"📄 PDFs processed: {result.PdfsProcessed}");
             LoggerService.LogInformation($"📄 Files generated: {result.FilesGenerated}");
             LoggerService.LogInformation($"📋 Upload records created: {result.UploadRecordsCreated}");
             LoggerService.LogInformation($"⚠️  Missing PDFs: {result.SkippedMissingPdf}");
             LoggerService.LogInformation(new string('═', 60));

            if (result.ErrorMessages.Count > 0)
            {
                 LoggerService.LogInformation($"\n⚠️  Errors:");
                foreach (var error in result.ErrorMessages.Take(10))
                {
                     LoggerService.LogInformation($"   - {error}");
                }
            }

            if (result.UploadRecordsCreated > 0)
            {
                 LoggerService.LogInformation($"\n✅ Ready for Phase 3: Upload to PHIS");
                 LoggerService.LogInformation($"   Upload CSV: Upload_to_PHIS.csv");
                 LoggerService.LogInformation($"   Renamed PDFs ready in: {_prePhase3Config.OutputPath}");
            }

            // ✅ Display the PDF source path being used
             LoggerService.LogInformation($"\n📁 PDF Source: {_bulkPdfConfig.GetOutputReadyPath()}");
        }


    }







}