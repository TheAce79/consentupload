using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Matching;
using ConsentSyncCore.Services.Pdf;
using CsvHelper;
using CsvHelper.Configuration;
using CsvProcessing;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Phase2
{
    public class Phase2Orchestrator
    {


        private readonly IConfiguration _config;
        private readonly Phase2Config _phase2Config;
        private readonly StudentCsvRepository _csvRepo;
        private readonly FuzzyMatcher _fuzzyMatcher;  

        public Phase2Orchestrator(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _phase2Config = ConfigurationService.GetPhase2Config();
            _csvRepo = new StudentCsvRepository(_config);
            _fuzzyMatcher = new FuzzyMatcher(); 
        }






        /// <summary>
        /// Validate required folders exist
        /// </summary>
        private bool ValidateFolders()
        {
            bool valid = true;

            if (!Directory.Exists(_phase2Config.DownloadPath))
            {
                Console.WriteLine($"   ❌ DownloadPath not found: {_phase2Config.DownloadPath}");
                valid = false;
            }
            else
            {
                Console.WriteLine($"   ✅ DownloadPath: {_phase2Config.DownloadPath}");
            }

            if (!Directory.Exists(_phase2Config.RenamedPath))
            {
                Directory.CreateDirectory(_phase2Config.RenamedPath);
                Console.WriteLine($"   ✅ Created RenamedPath: {_phase2Config.RenamedPath}");
            }
            else
            {
                Console.WriteLine($"   ✅ RenamedPath: {_phase2Config.RenamedPath}");
            }

            // ✅ Add error directory validation
            if (!string.IsNullOrWhiteSpace(_phase2Config.ErrorOutputDir))
            {
                if (!Directory.Exists(_phase2Config.ErrorOutputDir))
                {
                    Directory.CreateDirectory(_phase2Config.ErrorOutputDir);
                    Console.WriteLine($"   ✅ Created ErrorOutputDir: {_phase2Config.ErrorOutputDir}");
                }
                else
                {
                    Console.WriteLine($"   ✅ ErrorOutputDir: {_phase2Config.ErrorOutputDir}");
                }
            }

            return valid;
        }



        /// <summary>
        /// Move PDF to error output directory for manual review
        /// </summary>
        private void CopyToErrorDirectory(string sourcePdfPath, string reason)
        {
            try
            {
                // Check if ErrorOutputDir is configured
                if (string.IsNullOrWhiteSpace(_phase2Config.ErrorOutputDir))
                {
                    Console.WriteLine($"         ⚠️  ErrorOutputDir not configured - file not moved");
                    return;
                }

                // Create error directory if it doesn't exist
                if (!Directory.Exists(_phase2Config.ErrorOutputDir))
                {
                    Directory.CreateDirectory(_phase2Config.ErrorOutputDir);
                    Console.WriteLine($"         📁 Created error directory: {_phase2Config.ErrorOutputDir}");
                }

                // Generate new filename with reason prefix
                var originalFileName = Path.GetFileNameWithoutExtension(sourcePdfPath);
                var extension = Path.GetExtension(sourcePdfPath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var newFileName = $"{reason}_{originalFileName}_{timestamp}{extension}";
                var destinationPath = Path.Combine(_phase2Config.ErrorOutputDir, newFileName);

                // Move the file
                File.Copy(sourcePdfPath, destinationPath, overwrite: false);
                Console.WriteLine($"         📤 copy to errors: {newFileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"         ⚠️  Failed to copy file to error directory: {ex.Message}");
            }
        }


        /// <summary>
        /// Run Phase 2 workflow
        /// </summary>
        public async Task<Phase2Result> RunAsync()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════╗");
            Console.WriteLine("║         ConsentSync - Phase 2: Process PDFs            ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

            var result = new Phase2Result();

            try
            {
                // Step 1: Validate folders
                Console.WriteLine("📋 Step 1: Validating folders...");
                if (!ValidateFolders())
                {
                    result.HasErrors = true;
                    return result;
                }

                // Step 2: Load student CSV
                Console.WriteLine("\n📋 Step 2: Loading student data...");
                var students = _csvRepo.ReadAll()
                    .Where(s => !string.IsNullOrWhiteSpace(s.ClientId))
                    .ToList();

                Console.WriteLine($"   ✅ Loaded {students.Count} students with Client IDs");

                // Step 3: Process PDFs
                Console.WriteLine($"\n📋 Step 3: Processing PDFs from: {_phase2Config.DownloadPath}");
                var pdfFiles = Directory.GetFiles(_phase2Config.DownloadPath, "*.pdf");
                result.TotalPdfs = pdfFiles.Length;

                Console.WriteLine($"   Found {pdfFiles.Length} PDF files to process");

                if (pdfFiles.Length == 0)
                {
                    Console.WriteLine("   ⚠️  No PDFs found. Please download PDFs to the DownloadPath folder.");
                    return result;
                }

                var uploadRecords = new List<UploadRecord>();

                foreach (var pdfPath in pdfFiles)
                {
                    var fileName = Path.GetFileName(pdfPath);
                    Console.WriteLine($"\n   Processing: {fileName}");

                    try
                    {
                        // Extract names from PDF
                        var (firstName, lastName, pageCount) = PdfProcessor.ProcessSinglePdf(
                            pdfPath,
                            _phase2Config.DebugMode,
                            _phase2Config.DebugOutputDir);

                        if (firstName == "Unknown" || lastName == "Unknown" ||
                            firstName == "Error" || lastName == "Error")
                        {
                            Console.WriteLine($"      ❌ Failed to extract names from PDF");

                            // ✅ Move to error directory
                            CopyToErrorDirectory(pdfPath, "NameExtractionFailed");

                            result.FailedToMatch++;
                            result.ErrorMessages.Add($"{fileName}: Name extraction failed");
                            continue;
                        }

                        Console.WriteLine($"      Extracted: {firstName} {lastName}");


                        StudentRecord? matchedStudent = null;
                        if (_phase2Config.UseFuzzyMatching)
                        {

                            // ✅ ENHANCED: Use fuzzy matching instead of exact match
                            matchedStudent = FindBestMatchingStudent(
                               firstName,
                               lastName,
                               students);
                        }
                        else
                        {
                            // Find matching student using exact name
                            matchedStudent = students.FirstOrDefault(s =>
                            s.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase) &&
                            s.LastName.Equals(lastName, StringComparison.OrdinalIgnoreCase));

                        }


                        if (matchedStudent == null)
                        {
                            Console.WriteLine($"      ⚠️  No matching student found in CSV");

                            // ✅ Move to error directory
                            CopyToErrorDirectory(pdfPath, "NameExtractionFailed");

                            result.FailedToMatch++;
                            result.ErrorMessages.Add($"{fileName}: No match for {firstName} {lastName}");
                            continue;
                        }

                        Console.WriteLine($"      ✅ Matched to Client ID: {matchedStudent.ClientId}");

                        // Process based on grade
                        var generated = await ProcessPdfForGrade(
                            pdfPath,
                            matchedStudent,
                            pageCount,
                            uploadRecords);

                        result.FilesGenerated += generated;
                        result.SuccessfullyProcessed++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      ❌ Error: {ex.Message}");
                        result.FailedToMatch++;
                        result.ErrorMessages.Add($"{fileName}: {ex.Message}");
                    }
                }

                // Step 4: Generate Upload CSV
                Console.WriteLine($"\n📋 Step 4: Generating Upload_to_PHIS.csv...");
                GenerateUploadCsv(uploadRecords);

                // Step 5: Display summary
                DisplaySummary(result, uploadRecords.Count);

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
        /// ✅ NEW: Find best matching student using fuzzy matching
        /// Handles accents (Félix vs F lix) and minor spelling variations
        /// </summary>
        private StudentRecord? FindBestMatchingStudent(
            string pdfFirstName,
            string pdfLastName,
            List<StudentRecord> students)
        {
            // First try exact match (fastest)
            var exactMatch = students.FirstOrDefault(s =>
                s.FirstName.Equals(pdfFirstName, StringComparison.OrdinalIgnoreCase) &&
                s.LastName.Equals(pdfLastName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                Console.WriteLine($"         Exact match found");
                return exactMatch;
            }

            // Use fuzzy matching for accented names
            Console.WriteLine($"         No exact match - using fuzzy matching...");

            var matches = students
                .Select(s =>
                {
                    var nameScore = _fuzzyMatcher.CalculateNameMatchScore(
                        pdfFirstName,
                        pdfLastName,
                        s.FirstName,
                        s.LastName);

                    return new { Student = s, Score = nameScore };
                })
                .Where(m => m.Score >= 80.0)  // 80% threshold for Phase 2
                .OrderByDescending(m => m.Score)
                .ToList();

            if (matches.Count == 0)
            {
                Console.WriteLine($"         No fuzzy matches found (threshold: 80%)");
                return null;
            }

            var bestMatch = matches.First();
            Console.WriteLine($"         Fuzzy match: {bestMatch.Student.FirstName} {bestMatch.Student.LastName} (score: {bestMatch.Score:F1}%)");

            // If score is very high (>= 95%), auto-accept
            if (bestMatch.Score >= 95.0)
            {
                Console.WriteLine($"         ✅ High confidence match - auto-accepted");
                return bestMatch.Student;
            }

            // If multiple similar scores, be cautious
            if (matches.Count > 1)
            {
                var secondBest = matches[1];
                var scoreDiff = bestMatch.Score - secondBest.Score;

                if (scoreDiff < 5.0)
                {
                    Console.WriteLine($"         ⚠️  Ambiguous: {matches.Count} similar matches (diff: {scoreDiff:F1}%)");
                    Console.WriteLine($"            1. {bestMatch.Student.FirstName} {bestMatch.Student.LastName} ({bestMatch.Score:F1}%)");
                    Console.WriteLine($"            2. {secondBest.Student.FirstName} {secondBest.Student.LastName} ({secondBest.Score:F1}%)");
                    return null;  // Too ambiguous - require manual review
                }
            }

            // Accept best match if score is good
            if (bestMatch.Score >= 85.0)
            {
                Console.WriteLine($"         ✅ Good match - accepted");
                return bestMatch.Student;
            }

            Console.WriteLine($"         ⚠️  Score too low ({bestMatch.Score:F1}% < 85%)");
            return null;
        }



        /// <summary>
        /// Process PDF based on student's grade
        /// </summary>
        private async Task<int> ProcessPdfForGrade(
            string sourcePdfPath,
            StudentRecord student,
            int pageCount,
            List<UploadRecord> uploadRecords)
        {
            int filesGenerated = 0;
            var schoolYear = _phase2Config.CurrentYear;
            var grade = student.Grade.Trim();

            // Determine vaccine types based on grade
            string[] vaccineTypes;

            if (grade == "7" || grade.Contains("Grade 7", StringComparison.OrdinalIgnoreCase))
            {
                vaccineTypes = new[] { "HPV9", "Tdap" };
                Console.WriteLine($"      Grade 7 detected → Generating 2 files (HPV9, Tdap)");
            }
            else if (grade == "9" || grade.Contains("Grade 9", StringComparison.OrdinalIgnoreCase))
            {
                vaccineTypes = new[] { "MenCACYW135" };
                Console.WriteLine($"      Grade 9 detected → Generating 1 file (MenCACYW135)");
            }
            else
            {
                Console.WriteLine($"      ⚠️  Unknown grade: {grade} - defaulting to no vaccines");
                return 0;
            }

            // Generate file for each vaccine type
            foreach (var vaccineType in vaccineTypes)
            {
                var documentTitle = $"{student.ClientId}_consent{vaccineType}_{schoolYear}";
                var newFileName = $"{documentTitle}.pdf";
                var destinationPath = Path.Combine(_phase2Config.RenamedPath, newFileName);

                // Copy and rename file
                File.Copy(sourcePdfPath, destinationPath, overwrite: true);
                Console.WriteLine($"         → Created: {newFileName}");

                // Add to upload records
                uploadRecords.Add(new UploadRecord
                {
                    ClientID = student.ClientId,
                    LastName = student.LastName,
                    FirstName = student.FirstName,
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
        /// Generate Upload_to_PHIS.csv
        /// </summary>
        private void GenerateUploadCsv(List<UploadRecord> records)
        {
            var outputPath = Path.Combine(_phase2Config.RenamedPath, _phase2Config.UploadCsv);

            using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                csv.Context.RegisterClassMap<UploadRecordMap>();
                csv.WriteRecords(records);
            }

            Console.WriteLine($"   ✅ Generated: {outputPath}");
            Console.WriteLine($"   📊 Total records: {records.Count}");
        }



        /// <summary>
        /// Display final summary
        /// </summary>
        private void DisplaySummary(Phase2Result result, int uploadRecordCount)
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("📊 PHASE 2 COMPLETE - Final Summary");
            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"Total PDFs found: {result.TotalPdfs}");
            Console.WriteLine($"✅ Successfully processed: {result.SuccessfullyProcessed}");
            Console.WriteLine($"❌ Failed to match: {result.FailedToMatch}");
            Console.WriteLine($"📄 Files generated: {result.FilesGenerated}");
            Console.WriteLine($"📋 Upload records created: {uploadRecordCount}");
            Console.WriteLine(new string('═', 60));

            if (result.ErrorMessages.Count > 0)
            {
                Console.WriteLine($"\n⚠️  Errors encountered:");
                foreach (var error in result.ErrorMessages.Take(10))
                {
                    Console.WriteLine($"   - {error}");
                }
                if (result.ErrorMessages.Count > 10)
                {
                    Console.WriteLine($"   ... and {result.ErrorMessages.Count - 10} more");
                }
            }

            if (result.SuccessfullyProcessed > 0)
            {
                Console.WriteLine($"\n✅ Ready for Phase 3: Upload to PHIS");
            }
        }



    }
}
