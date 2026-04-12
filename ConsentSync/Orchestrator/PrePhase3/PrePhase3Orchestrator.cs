using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
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

        public PrePhase3Orchestrator(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _prePhase3Config = ConfigurationService.GetPrePhase3Config();
            _phase2Config = ConfigurationService.GetPhase2Config();
            _schoolContext = ConfigurationService.GetSchoolContextConfig();
        }


        public async Task<PrePhase3Result> RunAsync()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════╗");
            Console.WriteLine("║      ConsentSync - Pre-Phase 3: Prepare for Upload     ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

            var result = new PrePhase3Result();

            try
            {
                // Step 1: Load Validation CSV
                Console.WriteLine("📋 Step 1: Loading Validation_Results.csv...");
                var validationRecords = LoadValidationCsv();
                result.TotalRecords = validationRecords.Count;

                Console.WriteLine($"   ✅ Loaded {validationRecords.Count} validation records");

                // Step 2: Filter validated records
                Console.WriteLine("\n📋 Step 2: Filtering validated records...");
                var validatedRecords = validationRecords
                    .Where(r =>
                        r.FileFound == true &&
                        r.IsMatch == true &&
                        !string.IsNullOrWhiteSpace(r.ClientId))
                    .ToList();

                result.ValidatedRecords = validatedRecords.Count;
                result.SkippedNotValidated = validationRecords.Count - validatedRecords.Count;

                Console.WriteLine($"   ✅ Validated records: {validatedRecords.Count}");
                Console.WriteLine($"   ⏭️  Skipped (not validated): {result.SkippedNotValidated}");

                if (validatedRecords.Count == 0)
                {
                    Console.WriteLine("\n⚠️  No validated records to process!");
                    Console.WriteLine("   💡 Please review Validation_Results.csv and fix FileFound/IsMatch flags");
                    return result;
                }

                // Step 3: Process each validated record
                Console.WriteLine($"\n📋 Step 3: Processing {validatedRecords.Count} validated PDFs...");
                var uploadRecords = new List<UploadRecord>();

                foreach (var record in validatedRecords)
                {
                    Console.WriteLine($"\n   Processing: {record.FirstName} {record.LastName} (Client ID: {record.ClientId})");

                    try
                    {
                        // Find the original PDF
                        var pdfPath = FindPdfForRecord(record);

                        if (string.IsNullOrEmpty(pdfPath))
                        {
                            Console.WriteLine($"      ⚠️  PDF not found for {record.ClientId}");
                            result.SkippedMissingPdf++;
                            result.ErrorMessages.Add($"{record.ClientId}: PDF file not found");
                            continue;
                        }

                        Console.WriteLine($"      Found PDF: {Path.GetFileName(pdfPath)}");

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
                        Console.WriteLine($"      ❌ Error: {ex.Message}");
                        result.ErrorMessages.Add($"{record.ClientId}: {ex.Message}");
                    }
                }

                // Step 4: Generate Upload_to_PHIS.csv
                Console.WriteLine($"\n📋 Step 4: Generating Upload_to_PHIS.csv...");
                GenerateUploadCsv(uploadRecords);
                result.UploadRecordsCreated = uploadRecords.Count;

                // Step 5: Update Validation CSV with IsPdfSave flags
                Console.WriteLine($"\n📋 Step 5: Updating Validation_Results.csv...");
                SaveValidationCsv(validationRecords);

                // Step 6: Display summary
                DisplaySummary(result);

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ FATAL ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
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
                Console.WriteLine($"      Grade 7 → Generating 2 files (HPV9, Tdap)");
            }
            else if (grade == "9" || grade.Contains("Grade 9", StringComparison.OrdinalIgnoreCase))
            {
                vaccineTypes = new[] { "MenCACYW135" };
                Console.WriteLine($"      Grade 9 → Generating 1 file (MenCACYW135)");
            }
            else
            {
                Console.WriteLine($"      ⚠️  Unknown grade: {grade} - skipping");
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
                Console.WriteLine($"         → Created: {newFileName}");

                // Add to upload records
                uploadRecords.Add(new UploadRecord
                {
                    ClientID = record.ClientId,
                    LastName = record.LastName,
                    FirstName = record.FirstName,
                    DocumentTitle = documentTitle,
                    Description = $"Consent{vaccineType}",
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
        /// Find PDF file for a validation record
        /// </summary>
        private string? FindPdfForRecord(ValidationRecord record)
        {
            // Try to find by extracted name first
            if (!string.IsNullOrEmpty(record.ExtractedName))
            {
                var extractedParts = record.ExtractedName.Split(' ', 2);
                if (extractedParts.Length == 2)
                {
                    var possibleFileName = $"{extractedParts[0]}_{extractedParts[1]}.pdf";
                    var possiblePath = Path.Combine(_prePhase3Config.PdfSourcePath, possibleFileName);

                    if (File.Exists(possiblePath))
                        return possiblePath;
                }
            }

            // Try by CSV name
            var csvFileName = $"{record.FirstName}_{record.LastName}.pdf";
            var csvPath = Path.Combine(_prePhase3Config.PdfSourcePath, csvFileName);

            if (File.Exists(csvPath))
                return csvPath;

            // Try by ClientID
            var clientIdFileName = $"{record.ClientId}.pdf";
            var clientIdPath = Path.Combine(_prePhase3Config.PdfSourcePath, clientIdFileName);

            if (File.Exists(clientIdPath))
                return clientIdPath;

            // Search all PDFs in directory
            var allPdfs = Directory.GetFiles(_prePhase3Config.PdfSourcePath, "*.pdf");
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

            Console.WriteLine($"   ✅ Updated: {csvPath}");
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

            Console.WriteLine($"   ✅ Generated: {outputPath}");
            Console.WriteLine($"   📊 Total upload records: {records.Count}");
        }

        private void DisplaySummary(PrePhase3Result result)
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("📊 PRE-PHASE 3 COMPLETE - Final Summary");
            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"Total validation records: {result.TotalRecords}");
            Console.WriteLine($"✅ Validated records: {result.ValidatedRecords}");
            Console.WriteLine($"⏭️  Skipped (not validated): {result.SkippedNotValidated}");
            Console.WriteLine($"📄 PDFs processed: {result.PdfsProcessed}");
            Console.WriteLine($"📄 Files generated: {result.FilesGenerated}");
            Console.WriteLine($"📋 Upload records created: {result.UploadRecordsCreated}");
            Console.WriteLine($"⚠️  Missing PDFs: {result.SkippedMissingPdf}");
            Console.WriteLine(new string('═', 60));

            if (result.ErrorMessages.Count > 0)
            {
                Console.WriteLine($"\n⚠️  Errors:");
                foreach (var error in result.ErrorMessages.Take(10))
                {
                    Console.WriteLine($"   - {error}");
                }
            }

            if (result.UploadRecordsCreated > 0)
            {
                Console.WriteLine($"\n✅ Ready for Phase 3: Upload to PHIS");
                Console.WriteLine($"   Upload CSV: Upload_to_PHIS.csv");
                Console.WriteLine($"   Renamed PDFs ready in: {_prePhase3Config.OutputPath}");
            }
        }


    }







}
