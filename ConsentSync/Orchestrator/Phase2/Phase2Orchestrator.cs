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
        /// Copies an unmatched PDF into <paramref name="sessionErrorDir"/> keeping
        /// the original filename.  The session folder is created once per
        /// <see cref="RunAsync"/> call so re-running never overwrites previous errors.
        /// </summary>
        private void CopyToErrorDirectory(string sourcePdfPath, string sessionErrorDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionErrorDir))
                {
                    LoggerService.LogWarning("         ⚠️  Error folder not configured — file not copied");
                    return;
                }

                // Folder is already created by the caller — just ensure it exists
                Directory.CreateDirectory(sessionErrorDir);

                var fileName = Path.GetFileName(sourcePdfPath);
                var destinationPath = Path.Combine(sessionErrorDir, fileName);

                File.Copy(sourcePdfPath, destinationPath, overwrite: true);
                LoggerService.LogWarning($"         📤 Copied to error folder: {fileName}");
            }
            catch (Exception ex)
            {
                LoggerService.LogWarning(
                    $"         ⚠️  Failed to copy to error directory: {ex.Message}\n" +
                    $"             Source: {sourcePdfPath}");
            }
        }

        /// <summary>
        /// Resolves (and lazily creates) the session-scoped error subfolder.
        /// Format: <c>6_Error\Error_yyyyMMdd_HHmmss</c>
        /// The same folder path is reused for every error in one <see cref="RunAsync"/> run.
        /// </summary>
        private string ResolveSessionErrorDir(ref string? sessionErrorDir)
        {
            if (sessionErrorDir is not null)
                return sessionErrorDir;

            var baseErrorDir = _bulkPdfConfig.GetErrorPath();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            sessionErrorDir = Path.Combine(baseErrorDir, $"Error_{timestamp}");

            Directory.CreateDirectory(sessionErrorDir);
            LoggerService.LogWarning($"   📁 Error subfolder created: {sessionErrorDir}");

            return sessionErrorDir;
        }

        public async Task<Phase2Result> RunAsync()
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════");
            _logger.LogInformation("ConsentSync - Phase 2: Process PDFs");
            _logger.LogInformation("═══════════════════════════════════════════════════════");

            LoggerService.LogInformation("Starting Phase 2...");

            var result = new Phase2Result();

            // ── Session-scoped error subfolder (created lazily on first error) ──
            string? sessionErrorDir = null;

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

                // Step 3: Create validation records from ALL students
                LoggerService.LogInformation("\n📋 Step 3: Creating validation records...");
                var validationRecords = students.Select(s => new ValidationRecord
                {
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

                // Step 4: Process PDFs from BulkPdfExtraction OutputReady folder
                var pdfSourcePath = _bulkPdfConfig.GetOutputReadyPath();
                LoggerService.LogInformation($"\n📋 Step 4: Processing PDFs from: {pdfSourcePath}");

                var pdfFiles = Directory.GetFiles(pdfSourcePath, "*.pdf");
                result.TotalPdfs = pdfFiles.Length;

                LoggerService.LogInformation($"   Found {pdfFiles.Length} PDF file(s) to process");

                if (pdfFiles.Length == 0)
                {
                    LoggerService.LogInformation("   ⚠️  No PDFs found in OutputReady folder.");
                    LoggerService.LogInformation("   💡 Place PDFs in the OutputReady folder before running Phase 2.");
                }

                foreach (var pdfPath in pdfFiles)
                {
                    var fileName = Path.GetFileName(pdfPath);
                    LoggerService.LogInformation($"\n   Processing: {fileName}");

                    try
                    {
                        // ── Pass 0: ClientId-only filename ────────────────────────────
                        // A user-corrected PDF is renamed to just "{ClientId}.pdf"
                        // (no underscores, not purely numeric).
                        if (IsClientIdOnlyFilename(fileName))
                        {
                            var clientId = Path.GetFileNameWithoutExtension(fileName).Trim();

                            var clientIdMatch = validationRecords.FirstOrDefault(r =>
                                !string.IsNullOrWhiteSpace(r.ClientId) &&
                                r.ClientId.Trim().Equals(clientId, StringComparison.OrdinalIgnoreCase));

                            if (clientIdMatch != null)
                            {
                                LoggerService.LogInformation(
                                    $"      ✅ ClientId match: {clientId} " +
                                    $"→ {clientIdMatch.FirstName} {clientIdMatch.LastName}");

                                clientIdMatch.FileFound = true;
                                clientIdMatch.IsMatch = true;
                                clientIdMatch.ExtractedName = clientId;
                                clientIdMatch.NormalizedPDF = NormalizeName(clientIdMatch.FirstName, clientIdMatch.LastName);
                                clientIdMatch.MatchScore = 100.0;
                                clientIdMatch.ValidationNotes = "Matched by ClientId (manually corrected)";

                                LoggerService.LogInformation("         FileFound : true");
                                LoggerService.LogInformation("         IsMatch   : true");

                                result.SuccessfullyProcessed++;
                                continue;
                            }

                            // ClientId not found in CSV — move to session error folder
                            LoggerService.LogInformation(
                                $"      ⚠️  ClientId '{clientId}' not found in CSV");
                            CopyToErrorDirectory(pdfPath, ResolveSessionErrorDir(ref sessionErrorDir));
                            result.FailedToMatch++;
                            result.ErrorMessages.Add($"{fileName}: ClientId '{clientId}' not found in student CSV");
                            continue;
                        }

                        // ── Normal bulk PDF: extract names then fuzzy-match ────────────
                        string firstName, lastName;

                        if (_phase2Config.ReadNamesFromFilename)
                        {
                            var (fnameFromFile, lnameFromFile) = ExtractNamesFromBulkFilename(fileName);

                            if (!string.IsNullOrWhiteSpace(fnameFromFile) &&
                                !string.IsNullOrWhiteSpace(lnameFromFile))
                            {
                                firstName = fnameFromFile;
                                lastName = lnameFromFile;
                                LoggerService.LogInformation(
                                    $"      ✅ Extracted from filename: {firstName} {lastName}");
                            }
                            else
                            {
                                LoggerService.LogInformation(
                                    "      ⚠️  Failed to parse filename, falling back to PDF content...");
                                var (fn, ln, _) = PdfProcessor.ProcessSinglePdf(
                                    pdfPath, _phase2Config.DebugMode, _phase2Config.DebugOutputDir);
                                firstName = fn;
                                lastName = ln;
                                LoggerService.LogInformation(
                                    $"      ✅ Extracted from PDF: {firstName} {lastName}");
                            }
                        }
                        else
                        {
                            LoggerService.LogInformation("      📄 Reading PDF content...");
                            var (fn, ln, _) = PdfProcessor.ProcessSinglePdf(
                                pdfPath, _phase2Config.DebugMode, _phase2Config.DebugOutputDir);
                            firstName = fn;
                            lastName = ln;
                            LoggerService.LogInformation(
                                $"      ✅ Extracted from PDF: {firstName} {lastName}");
                        }

                        if (firstName == "Unknown" || lastName == "Unknown" ||
                            firstName == "Error" || lastName == "Error")
                        {
                            LoggerService.LogInformation("      ❌ Failed to extract names from PDF");
                            CopyToErrorDirectory(pdfPath, ResolveSessionErrorDir(ref sessionErrorDir));
                            result.FailedToMatch++;
                            result.ErrorMessages.Add($"{fileName}: Name extraction failed");
                            continue;
                        }

                        LoggerService.LogInformation($"      Extracted: {firstName} {lastName}");

                        var (matchedRecord, matchScore) =
                            FindBestMatchingValidationRecord(firstName, lastName, validationRecords);

                        if (matchedRecord != null)
                        {
                            matchedRecord.FileFound = true;
                            matchedRecord.ExtractedName = $"{firstName} {lastName}";
                            matchedRecord.NormalizedPDF = NormalizeName(firstName, lastName);
                            matchedRecord.MatchScore = matchScore;
                            matchedRecord.IsMatch = matchScore >= 85.0;

                            matchedRecord.ValidationNotes = matchScore >= 100
                                ? "Exact match"
                                : matchScore >= 85.0
                                    ? $"Good match ({matchScore:F1}%)"
                                    : $"Weak match ({matchScore:F1}%) - needs review";

                            LoggerService.LogInformation(
                                $"      ✅ Matched to Client ID: {matchedRecord.ClientId} " +
                                $"(Score: {matchScore:F1}%)");
                            LoggerService.LogInformation($"         FileFound: {matchedRecord.FileFound}");
                            LoggerService.LogInformation($"         IsMatch  : {matchedRecord.IsMatch}");

                            result.SuccessfullyProcessed++;
                        }
                        else
                        {
                            LoggerService.LogInformation("      ⚠️  No matching student found in CSV");
                            LoggerService.LogInformation(
                                "      💡 This PDF exists but doesn't match any student record");

                            var orphanRecord = new ValidationRecord
                            {
                                FileFound = true,
                                IsMatch = false,
                                ExtractedName = $"{firstName} {lastName}",
                                NormalizedPDF = NormalizeName(firstName, lastName),
                                NormalizedCSV = string.Empty,
                                MatchScore = 0.0,
                                ValidationNotes = "PDF found but no matching student in CSV",
                                ClientId = string.Empty,
                                FirstName = firstName,
                                LastName = lastName,
                            };

                            validationRecords.Add(orphanRecord);

                            CopyToErrorDirectory(pdfPath, ResolveSessionErrorDir(ref sessionErrorDir));
                            result.FailedToMatch++;
                            result.ErrorMessages.Add($"{fileName}: No match for {firstName} {lastName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogInformation($"      ❌ Error: {ex.Message}");
                        CopyToErrorDirectory(pdfPath, ResolveSessionErrorDir(ref sessionErrorDir));
                        result.FailedToMatch++;
                        result.ErrorMessages.Add($"{fileName}: {ex.Message}");
                    }
                }

                // Step 5: Generate Validation CSV
                LoggerService.LogInformation("\n📋 Step 5: Generating Validation_Results.csv...");
                GenerateValidationCsv(validationRecords);

                // Step 6: Display summary
                result.SessionErrorDir = sessionErrorDir;   // ← expose to UI
                DisplaySummary(result, validationRecords, sessionErrorDir);

                return result; ;
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
        // Filename helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns <c>true</c> when the filename is a user-corrected PDF renamed to
        /// just <c>{ClientId}.pdf</c> — no underscores AND the stem is not purely numeric.
        /// </summary>
        private static bool IsClientIdOnlyFilename(string fileName)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            return !stem.Contains('_') && !long.TryParse(stem, out _);
        }

        /// <summary>
        /// Parses a normal bulk-PDF filename: <c>{index}_{LastName}_{FirstName}[_...].pdf</c>
        /// The leading index segment is discarded — it is a page number, not a ClientId.
        /// </summary>
        private (string firstName, string lastName) ExtractNamesFromBulkFilename(string fileName)
        {
            try
            {
                var stem = Path.GetFileNameWithoutExtension(fileName);

                if (stem.EndsWith("_consent", StringComparison.OrdinalIgnoreCase))
                    stem = stem[..^8];

                var parts = stem.Split('_');

                if (parts.Length >= 3)
                {
                    var lastName = parts[1];
                    var firstName = parts[2];

                    if (!string.IsNullOrWhiteSpace(lastName) &&
                        !string.IsNullOrWhiteSpace(firstName))
                        return (firstName, lastName);
                }

                LoggerService.LogInformation(
                    $"         ⚠️  Filename doesn't match expected format: {fileName}");
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"         ⚠️  Error parsing filename: {ex.Message}");
            }

            return (string.Empty, string.Empty);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Name matching
        // ─────────────────────────────────────────────────────────────────────

        private (ValidationRecord? record, double score) FindBestMatchingValidationRecord(
            string pdfFirstName,
            string pdfLastName,
            List<ValidationRecord> records)
        {
            // Pass 1: exact match — normal order
            var exactMatch = records.FirstOrDefault(r =>
                r.FirstName.Equals(pdfFirstName, StringComparison.OrdinalIgnoreCase) &&
                r.LastName.Equals(pdfLastName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                LoggerService.LogInformation("         Exact match found");
                return (exactMatch, 100.0);
            }

            // Pass 2: exact match — swapped order
            var swappedExact = records.FirstOrDefault(r =>
                r.FirstName.Equals(pdfLastName, StringComparison.OrdinalIgnoreCase) &&
                r.LastName.Equals(pdfFirstName, StringComparison.OrdinalIgnoreCase));

            if (swappedExact != null)
            {
                LoggerService.LogInformation("         Exact match found (names were reversed in filename)");
                return (swappedExact, 100.0);
            }

            // Pass 3: fuzzy — both orderings
            LoggerService.LogInformation("         No exact match — using fuzzy matching (both orderings)...");

            var candidates = records.Select(r =>
            {
                double scoreNormal = _fuzzyMatcher.CalculateNameMatchScore(
                    pdfFirstName, pdfLastName, r.FirstName, r.LastName);
                double scoreSwapped = _fuzzyMatcher.CalculateNameMatchScore(
                    pdfLastName, pdfFirstName, r.FirstName, r.LastName);

                double best = Math.Max(scoreNormal, scoreSwapped);
                bool wasSwapped = scoreSwapped > scoreNormal;

                return new { Record = r, Score = best, WasSwapped = wasSwapped };
            })
            .Where(m => m.Score >= 60.0)
            .OrderByDescending(m => m.Score)
            .ToList();

            if (candidates.Count == 0)
            {
                LoggerService.LogInformation(
                    "         No match found (even at 60% threshold, both orderings tried)");
                return (null, 0.0);
            }

            var best = candidates.First();
            LoggerService.LogInformation(
                $"         Fuzzy match: {best.Record.FirstName} {best.Record.LastName} " +
                $"(score: {best.Score:F1}%{(best.WasSwapped ? ", names were reversed" : "")})");

            return (best.Record, best.Score);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Output
        // ─────────────────────────────────────────────────────────────────────

        private void DisplaySummary(Phase2Result result, List<ValidationRecord> validationRecords,
            string? sessionErrorDir)
        {
            LoggerService.LogInformation("\n" + new string('═', 60));
            LoggerService.LogInformation("📊 PHASE 2 COMPLETE - Final Summary");
            LoggerService.LogInformation(new string('═', 60));
            LoggerService.LogInformation($"Total PDFs found          : {result.TotalPdfs}");
            LoggerService.LogInformation($"✅ Successfully processed : {result.SuccessfullyProcessed}");
            LoggerService.LogInformation($"❌ Failed to match        : {result.FailedToMatch}");
            LoggerService.LogInformation("\n📋 Validation CSV Statistics:");
            LoggerService.LogInformation($"   Total students  : {validationRecords.Count}");
            LoggerService.LogInformation($"   Files found     : {validationRecords.Count(r => r.FileFound)}");
            LoggerService.LogInformation($"   Files missing   : {validationRecords.Count(r => !r.FileFound)}");
            LoggerService.LogInformation($"   Matched         : {validationRecords.Count(r => r.IsMatch)}");
            LoggerService.LogInformation($"   Needs review    : {validationRecords.Count(r => !r.FileFound || !r.IsMatch)}");

            if (sessionErrorDir is not null)
                LoggerService.LogWarning($"\n   📁 Error folder  : {sessionErrorDir}");

            LoggerService.LogInformation(new string('═', 60));

            if (result.ErrorMessages.Count > 0)
            {
                LoggerService.LogInformation("\n⚠️  Errors:");
                foreach (var error in result.ErrorMessages.Take(10))
                    LoggerService.LogInformation($"   - {error}");
            }

            LoggerService.LogInformation("\n✅ Next Step: Review Validation_Results.csv");
            LoggerService.LogInformation("   - Fix records where FileFound=false or IsMatch=false");
            LoggerService.LogInformation("   - Then run Pre-Phase 3 to process validated records");
        }

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
                LoggerService.LogInformation(
                    $"   ⚠️  {needsReview} record(s) need manual review (FileFound=false or IsMatch=false)");
        }

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
    }
}