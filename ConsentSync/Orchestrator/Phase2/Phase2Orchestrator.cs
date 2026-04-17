using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Matching;
using ConsentSyncCore.Services.Pdf;
using CsvHelper;
using CsvHelper.Configuration;
using CsvProcessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Text;
using ConsentSyncCore.Services.ConfigurationPoco;
using ConsentSyncCore.Services.Configuration;

namespace Orchestrator.Phase2
{
    public class Phase2Orchestrator
    {


        private readonly IConfiguration _config;
        private readonly Phase2Config _phase2Config;
        private readonly StudentCsvRepository _csvRepo;
        private readonly FuzzyMatcher _fuzzyMatcher;
        private readonly SchoolContextConfig _schoolContext;
        private readonly BulkPdfExtractionConfig _bulkPdfConfig;

        private readonly PhisWorkspaceConfig _phisWorkspace;
        private readonly ILogger<Phase2Orchestrator> _logger;

        public Phase2Orchestrator(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _phase2Config = ConfigurationService.GetPhase2Config();
            _bulkPdfConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            _phisWorkspace = ConfigurationService.GetPhisWorkspaceConfig();

            _schoolContext = ConfigurationService.GetSchoolContextConfig();
            _csvRepo = new StudentCsvRepository(_config);
            _fuzzyMatcher = new FuzzyMatcher();
            _logger = LoggerService.GetLogger<Phase2Orchestrator>();

            // ✅ Ensure workspace folders exist on startup
            EnsureWorkspaceFoldersExist();
        }


        private void EnsureWorkspaceFoldersExist()
        {
            try
            {
                Directory.CreateDirectory(_phisWorkspace.GetToUploadPath());
                Directory.CreateDirectory(_phisWorkspace.GetConsentUploadPath());
                Directory.CreateDirectory(_phisWorkspace.GetFileRoseUploadPath());
                Directory.CreateDirectory(_phisWorkspace.GetErrorPath());
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"⚠️  Warning: Could not create workspace folders: {ex.Message}");
            }
        }

        private bool ValidateFolders()
        {
            bool valid = true;

            // ✅ Use BulkPdfConfig instead of Phase2 DownloadPath
            var pdfSourcePath = _bulkPdfConfig.GetOutputReadyPath();
            if (!Directory.Exists(pdfSourcePath))
            {
                 LoggerService.LogInformation($"   ❌ PDF Source (OutputReady) not found: {pdfSourcePath}");
                valid = false;
            }
            else
            {
                 LoggerService.LogInformation($"   ✅ PDF Source (OutputReady): {pdfSourcePath}");
            }

            if (!Directory.Exists(_phase2Config.RenamedPath))
            {
                Directory.CreateDirectory(_phase2Config.RenamedPath);
                 LoggerService.LogInformation($"   ✅ Created RenamedPath: {_phase2Config.RenamedPath}");
            }
            else
            {
                 LoggerService.LogInformation($"   ✅ RenamedPath: {_phase2Config.RenamedPath}");
            }

            if (!string.IsNullOrWhiteSpace(_phase2Config.ErrorOutputDir))
            {
                if (!Directory.Exists(_phase2Config.ErrorOutputDir))
                {
                    Directory.CreateDirectory(_phase2Config.ErrorOutputDir);
                     LoggerService.LogInformation($"   ✅ Created ErrorOutputDir: {_phase2Config.ErrorOutputDir}");
                }
                else
                {
                     LoggerService.LogInformation($"   ✅ ErrorOutputDir: {_phase2Config.ErrorOutputDir}");
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
                     LoggerService.LogInformation($"         ⚠️  ErrorOutputDir not configured - file not moved");
                    return;
                }

                // Create error directory if it doesn't exist
                if (!Directory.Exists(_phase2Config.ErrorOutputDir))
                {
                    Directory.CreateDirectory(_phase2Config.ErrorOutputDir);
                     LoggerService.LogInformation($"         📁 Created error directory: {_phase2Config.ErrorOutputDir}");
                }

                // Generate new filename with reason prefix
                var originalFileName = Path.GetFileNameWithoutExtension(sourcePdfPath);
                var extension = Path.GetExtension(sourcePdfPath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var newFileName = $"{reason}_{originalFileName}_{timestamp}{extension}";
                var destinationPath = Path.Combine(_phase2Config.ErrorOutputDir, newFileName);

                // Move the file
                File.Copy(sourcePdfPath, destinationPath, overwrite: false);
                 LoggerService.LogInformation($"         📤 copy to errors: {newFileName}");
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"         ⚠️  Failed to copy file to error directory: {ex.Message}");
            }
        }



        public async Task<Phase2Result> RunAsync()
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════");
            _logger.LogInformation("ConsentSync - Phase 2: Process PDFs");
            _logger.LogInformation("═══════════════════════════════════════════════════════");

            // Or use the simpler static methods:
            LoggerService.LogInformation("Starting Phase 2...");

            // Rest of your code, replacing  LoggerService.LogInformation with:
            LoggerService.LogInformation("Your message here");
            LoggerService.LogWarning("Warning message");
            LoggerService.LogError("Error message");

            var result = new Phase2Result();

            try
            {
                // Step 1: Validate folders
                 LoggerService.LogInformation("📋 Step 1: Validating folders...");
                if (!ValidateFolders())
                {
                    result.HasErrors = true;
                    return result;
                }

                // Step 2: Load student CSV
                 LoggerService.LogInformation("\n📋 Step 2: Loading student data...");
                var students = _csvRepo.ReadAll()
                    .Where(s => !string.IsNullOrWhiteSpace(s.ClientId))
                    .ToList();

                 LoggerService.LogInformation($"   ✅ Loaded {students.Count} students with Client IDs");

                // ✅ Step 3: Create validation records from ALL students
                 LoggerService.LogInformation($"\n📋 Step 3: Creating validation records...");
                var validationRecords = students.Select(s => new ValidationRecord
                {
                    // Copy all student fields
                    LastName = s.LastName,
                    FirstName = s.FirstName,
                    School = s.School,
                    Grade = s.Grade,
                    DateOfBirth = s.DateOfBirth,
                    MedicareNumber = s.MedicareNumber,
                    ConsentStatus = s.ConsentStatus,
                    Tdap = s.Tdap,
                    HPV = s.HPV,
                    ClientId = s.ClientId,
                    IsFileRoseDefault = s.IsFileRoseDefault,
                    ClientIdStatus = (int)s.ClientIdStatus,
                    BestMatch = s.BestMatch,

                    // Initialize validation fields
                    FileFound = false,
                    IsMatch = false,
                    ExtractedName = string.Empty,
                    NormalizedPDF = string.Empty,
                    NormalizedCSV = NormalizeName(s.FirstName, s.LastName),
                    IsPdfSave = false,
                    MatchScore = 0.0,
                    ValidationNotes = string.Empty
                }).ToList();

                 LoggerService.LogInformation($"   ✅ Created {validationRecords.Count} validation records");

                // Step 4: Process PDFs
                // Step 4: Process PDFs from BulkPdfExtraction OutputReady folder
                var pdfSourcePath = _bulkPdfConfig.GetOutputReadyPath(); // ✅ Changed

                 LoggerService.LogInformation($"\n📋 Step 4: Processing PDFs from: {pdfSourcePath}");

                var pdfFiles = Directory.GetFiles(pdfSourcePath, "*.pdf"); // ✅ Changed
                result.TotalPdfs = pdfFiles.Length;

                 LoggerService.LogInformation($"   Found {pdfFiles.Length} PDF files to process");

                if (pdfFiles.Length == 0)
                {
                     LoggerService.LogInformation("   ⚠️  No PDFs found in DownloadPath.");
                     LoggerService.LogInformation("   💡 User will need to manually download PDFs before running Pre-Phase 3");
                }


                foreach (var pdfPath in pdfFiles)
                {
                    var fileName = Path.GetFileName(pdfPath);
                     LoggerService.LogInformation($"\n   Processing: {fileName}");

                    try
                    {
                        string firstName, lastName;

                        // ✅ Check configuration flag
                        if (_phase2Config.ReadNamesFromFilename)
                        {
                            // Extract from filename
                            var (fnameFromFile, lnameFromFile, idFromFile) = ExtractNamesFromFilename(fileName);

                            if (!string.IsNullOrWhiteSpace(fnameFromFile) && !string.IsNullOrWhiteSpace(lnameFromFile))
                            {
                                firstName = fnameFromFile;
                                lastName = lnameFromFile;
                                 LoggerService.LogInformation($"      ✅ Extracted from filename: {firstName} {lastName}");
                            }
                            else
                            {
                                // Filename parsing failed, fallback to PDF extraction
                                 LoggerService.LogInformation($"      ⚠️  Failed to parse filename, falling back to PDF extraction...");
                                var (fn, ln, pageCount) = PdfProcessor.ProcessSinglePdf(
                                    pdfPath,
                                    _phase2Config.DebugMode,
                                    _phase2Config.DebugOutputDir);
                                firstName = fn;
                                lastName = ln;
                                 LoggerService.LogInformation($"      ✅ Extracted from PDF: {firstName} {lastName}");
                            }
                        }
                        else {

                            // Extract from PDF content
                             LoggerService.LogInformation($"      📄 Reading PDF content...");
                            var (fn, ln, pageCount) = PdfProcessor.ProcessSinglePdf(
                                pdfPath,
                                _phase2Config.DebugMode,
                                _phase2Config.DebugOutputDir);
                            firstName = fn;
                            lastName = ln;
                             LoggerService.LogInformation($"      ✅ Extracted from PDF: {firstName} {lastName}");

                        }

                        if (firstName == "Unknown" || lastName == "Unknown" ||
                            firstName == "Error" || lastName == "Error")
                        {
                             LoggerService.LogInformation($"      ❌ Failed to extract names from PDF");
                            CopyToErrorDirectory(pdfPath, "NameExtractionFailed");
                            result.FailedToMatch++;
                            result.ErrorMessages.Add($"{fileName}: Name extraction failed");

                            // ✅ IMPORTANT: Don't continue - we can't match if we can't extract names
                            continue;
                        }

                         LoggerService.LogInformation($"      Extracted: {firstName} {lastName}");

                        // ✅ Find matching validation record
                        var (matchedRecord, matchScore) = FindBestMatchingValidationRecord(
                            firstName,
                            lastName,
                            validationRecords);

                        // ✅ CRITICAL FIX: Update FileFound regardless of match quality
                        if (matchedRecord != null)
                        {
                            // File exists AND we found a potential match in CSV
                            matchedRecord.FileFound = true;
                            matchedRecord.ExtractedName = $"{firstName} {lastName}";
                            matchedRecord.NormalizedPDF = NormalizeName(firstName, lastName);
                            matchedRecord.MatchScore = matchScore;

                            // ✅ IsMatch depends on the match score threshold (85%)
                            matchedRecord.IsMatch = matchScore >= 85.0;

                            if (matchScore >= 100)
                            {
                                matchedRecord.ValidationNotes = "Exact match";
                            }
                            else if (matchScore >= 85.0)
                            {
                                matchedRecord.ValidationNotes = $"Good match ({matchScore:F1}%)";
                            }
                            else
                            {
                                matchedRecord.ValidationNotes = $"Weak match ({matchScore:F1}%) - needs review";
                            }

                             LoggerService.LogInformation($"      ✅ Matched to Client ID: {matchedRecord.ClientId} (Score: {matchScore:F1}%)");
                             LoggerService.LogInformation($"         FileFound: {matchedRecord.FileFound}");
                             LoggerService.LogInformation($"         IsMatch: {matchedRecord.IsMatch}");

                            result.SuccessfullyProcessed++;
                        }
                        else
                        {
                            // ✅ File exists but NO match found in CSV at all
                             LoggerService.LogInformation($"      ⚠️  No matching student found in CSV");
                             LoggerService.LogInformation($"      💡 This PDF exists but doesn't match any student record");

                            // ✅ Optional: Create an "orphan" validation record for this PDF
                            var orphanRecord = new ValidationRecord
                            {
                                FileFound = true,
                                IsMatch = false,
                                ExtractedName = $"{firstName} {lastName}",
                                NormalizedPDF = NormalizeName(firstName, lastName),
                                NormalizedCSV = "",
                                MatchScore = 0.0,
                                ValidationNotes = "PDF found but no matching student in CSV",
                                ClientId = "",
                                FirstName = firstName,
                                LastName = lastName,
                                // Other fields remain empty
                            };

                            validationRecords.Add(orphanRecord);

                            CopyToErrorDirectory(pdfPath, $"NoMatch_{firstName}_{lastName}");
                            result.FailedToMatch++;
                            result.ErrorMessages.Add($"{fileName}: No match for {firstName} {lastName}");
                        }
                    }
                    catch (Exception ex)
                    {
                         LoggerService.LogInformation($"      ❌ Error: {ex.Message}");
                        CopyToErrorDirectory(pdfPath, "ProcessingError");
                        result.FailedToMatch++;
                        result.ErrorMessages.Add($"{fileName}: {ex.Message}");
                    }
                }



                // Step 5: Generate Validation CSV
                 LoggerService.LogInformation($"\n📋 Step 5: Generating Validation_Results.csv...");
                GenerateValidationCsv(validationRecords);

                // Step 6: Display summary
                DisplaySummary(result, validationRecords);

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
        /// ✅ Find best matching validation record using fuzzy matching
        /// Returns the best match even if score is below threshold
        /// </summary>
        private (ValidationRecord? record, double score) FindBestMatchingValidationRecord(
            string pdfFirstName,
            string pdfLastName,
            List<ValidationRecord> records)
        {
            // Try exact match first
            var exactMatch = records.FirstOrDefault(r =>
                r.FirstName.Equals(pdfFirstName, StringComparison.OrdinalIgnoreCase) &&
                r.LastName.Equals(pdfLastName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                 LoggerService.LogInformation($"         Exact match found");
                return (exactMatch, 100.0);
            }

            // Use fuzzy matching
             LoggerService.LogInformation($"         No exact match - using fuzzy matching...");

            var matches = records
                .Select(r =>
                {
                    var score = _fuzzyMatcher.CalculateNameMatchScore(
                        pdfFirstName, pdfLastName,
                        r.FirstName, r.LastName);
                    return new { Record = r, Score = score };
                })
                .Where(m => m.Score >= 60.0)  // ✅ Lower threshold to 60% to catch weak matches
                .OrderByDescending(m => m.Score)
                .ToList();

            if (matches.Count == 0)
            {
                 LoggerService.LogInformation($"         No matches found (even at 60% threshold)");
                return (null, 0.0);
            }

            var bestMatch = matches.First();
             LoggerService.LogInformation($"         Fuzzy match: {bestMatch.Record.FirstName} {bestMatch.Record.LastName} (score: {bestMatch.Score:F1}%)");

            // ✅ Return the best match regardless of score
            // The caller will decide if IsMatch should be true based on threshold
            return (bestMatch.Record, bestMatch.Score);
        }



        private void DisplaySummary(Phase2Result result, List<ValidationRecord> validationRecords)
        {
             LoggerService.LogInformation("\n" + new string('═', 60));
             LoggerService.LogInformation("📊 PHASE 2 COMPLETE - Final Summary");
             LoggerService.LogInformation(new string('═', 60));
             LoggerService.LogInformation($"Total PDFs found: {result.TotalPdfs}");
             LoggerService.LogInformation($"✅ Successfully processed: {result.SuccessfullyProcessed}");
             LoggerService.LogInformation($"❌ Failed to match: {result.FailedToMatch}");
             LoggerService.LogInformation($"\n📋 Validation CSV Statistics:");
             LoggerService.LogInformation($"   Total students: {validationRecords.Count}");
             LoggerService.LogInformation($"   Files found: {validationRecords.Count(r => r.FileFound)}");
             LoggerService.LogInformation($"   Files missing: {validationRecords.Count(r => !r.FileFound)}");
             LoggerService.LogInformation($"   Matched: {validationRecords.Count(r => r.IsMatch)}");
             LoggerService.LogInformation($"   Needs review: {validationRecords.Count(r => !r.FileFound || !r.IsMatch)}");
             LoggerService.LogInformation(new string('═', 60));

            if (result.ErrorMessages.Count > 0)
            {
                 LoggerService.LogInformation($"\n⚠️  Errors:");
                foreach (var error in result.ErrorMessages.Take(10))
                {
                     LoggerService.LogInformation($"   - {error}");
                }
            }

             LoggerService.LogInformation($"\n✅ Next Step: Review Validation_Results.csv");
             LoggerService.LogInformation($"   - Fix records where FileFound=false or IsMatch=false");
             LoggerService.LogInformation($"   - Then run Pre-Phase 3 to process validated records");
        }


        /// <summary>
        /// ✅ Normalize name for comparison (removes accents, uppercases)
        /// </summary>
        private string NormalizeName(string firstName, string lastName)
        {
            var normalizedFirst = RemoveAccents(firstName.Trim().ToUpperInvariant());
            var normalizedLast = RemoveAccents(lastName.Trim().ToUpperInvariant());
            return $"{normalizedFirst} {normalizedLast}";
        }

        private string RemoveAccents(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// ✅ Generate Validation_Results.csv for manual review
        /// </summary>
        private void GenerateValidationCsv(List<ValidationRecord> records)
        {
            var outputPath = Path.Combine(_phase2Config.RenamedPath, _phase2Config.ValidationResultsCsv);

            using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                csv.Context.RegisterClassMap<ValidationRecordMap>();
                csv.WriteRecords(records);
            }

             LoggerService.LogInformation($"   ✅ Generated: {outputPath}");
             LoggerService.LogInformation($"   📊 Total records: {records.Count}");

            var needsReview = records.Count(r => !r.FileFound || !r.IsMatch);
            if (needsReview > 0)
            {
                 LoggerService.LogInformation($"   ⚠️  {needsReview} records need manual review (FileFound=false or IsMatch=false)");
            }
        }


        /// <summary>
        /// Extract names from filename format: {ID}_{LastName}_{FirstName}_consent.pdf
        /// Returns empty strings if parsing fails
        /// </summary>
        private (string firstName, string lastName, string id) ExtractNamesFromFilename(string fileName)
        {
            try
            {
                // Remove extension
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

                // Expected format: {ID}_{LastName}_{FirstName}_consent
                // Remove "_consent" suffix if present
                if (nameWithoutExt.EndsWith("_consent", StringComparison.OrdinalIgnoreCase))
                {
                    nameWithoutExt = nameWithoutExt.Substring(0, nameWithoutExt.Length - 8);
                }

                var parts = nameWithoutExt.Split('_');

                if (parts.Length >= 3)
                {
                    var id = parts[0];
                    var lastName = parts[1];
                    var firstName = parts[2];

                    // Validate that we got meaningful values
                    if (!string.IsNullOrWhiteSpace(id) &&
                        !string.IsNullOrWhiteSpace(lastName) &&
                        !string.IsNullOrWhiteSpace(firstName))
                    {
                        return (firstName, lastName, id);
                    }
                }

                 LoggerService.LogInformation($"         ⚠️  Filename doesn't match expected format: {fileName}");
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"         ⚠️  Error parsing filename: {ex.Message}");
            }

            return (string.Empty, string.Empty, string.Empty);
        }

    }
}
