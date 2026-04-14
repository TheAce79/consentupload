using ConsentSyncCore.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace ConsentSyncCore.Services.Pdf
{
    public class BulkPdfExtractor
    {
   

        private readonly IConfiguration _config;
        private readonly BulkPdfExtractionConfig _bulkConfig;
        private readonly PdfExtractionConfig _pdfConfig;

        public BulkPdfExtractor(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            _pdfConfig = ConfigurationService.GetPdfExtractionConfig();

            // ✅ Automatically create folder structure on initialization
            EnsureDirectoriesExist();
        }





        /// <summary>
        /// Process all PDFs in the input folders (1_Input_Bulk and 2_Input_Scanned)
        /// Automatically detects type and processes accordingly
        /// </summary>
        public BulkExtractionResult ProcessAllPdfs()
        {
            var aggregateResult = new BulkExtractionResult();

            Console.WriteLine("╔════════════════════════════════════════════════════════╗");
            Console.WriteLine("║      PDF Processing - Automatic Detection             ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

            try
            {
                // Ensure all directories exist
                EnsureDirectoriesExist();

                // Process Bulk PDFs (1_Input_Bulk)
                var inputBulkPath = _bulkConfig.GetInputBulkPath();
                var bulkPdfs = Directory.GetFiles(inputBulkPath, "*.pdf");

                if (bulkPdfs.Length > 0)
                {
                    Console.WriteLine($"\n📂 Found {bulkPdfs.Length} PDF(s) in 1_Input_Bulk");
                    foreach (var pdfPath in bulkPdfs)
                    {
                        var result = ProcessSingleFile(pdfPath, PdfSourceType.Bulk);
                        aggregateResult.Merge(result);
                    }
                }

                // Process Scanned PDFs (2_Input_Scanned)
                var inputScannedPath = _bulkConfig.GetInputScannedPath();
                var scannedPdfs = Directory.GetFiles(inputScannedPath, "*.pdf");

                if (scannedPdfs.Length > 0)
                {
                    Console.WriteLine($"\n📂 Found {scannedPdfs.Length} PDF(s) in 2_Input_Scanned");
                    foreach (var pdfPath in scannedPdfs)
                    {
                        var result = ProcessSingleFile(pdfPath, PdfSourceType.Scanned);
                        aggregateResult.Merge(result);
                    }
                }

                if (bulkPdfs.Length == 0 && scannedPdfs.Length == 0)
                {
                    Console.WriteLine("⚠️  No PDFs found to process");
                    Console.WriteLine($"\n💡 Place PDFs in:");
                    Console.WriteLine($"   - Bulk downloads: {inputBulkPath}");
                    Console.WriteLine($"   - Scanned files:  {inputScannedPath}");
                }

                aggregateResult.Success = aggregateResult.FailedExtractions == 0;

                // Display final summary
                DisplayProcessingSummary(aggregateResult);

                return aggregateResult;
            }
            catch (Exception ex)
            {
                aggregateResult.ErrorMessage = $"Processing failed: {ex.Message}";
                Console.WriteLine($"❌ {aggregateResult.ErrorMessage}");
                return aggregateResult;
            }
        }




        /// <summary>
        /// Process a single PDF file with error handling and archival
        /// </summary>
        private BulkExtractionResult ProcessSingleFile(string pdfPath, PdfSourceType sourceType)
        {
            var result = new BulkExtractionResult();
            string fileName = Path.GetFileName(pdfPath);

            try
            {
                Console.WriteLine($"\n{'=',-60}");
                Console.WriteLine($"Processing: {fileName}");
                Console.WriteLine($"Source: {sourceType}");
                Console.WriteLine($"{'=',-60}");

                // Smart extraction to 3_Output_Ready
                var outputPath = _bulkConfig.GetOutputReadyPath();
                result = SmartExtractFromPdf(pdfPath, outputPath);

                // Handle success/failure
                if (result.Success && result.TotalExtracted > 0)
                {
                    // Move source to 5_Archive if enabled
                    if (_bulkConfig.MoveToArchiveAfterProcessing)
                    {
                        MoveToArchive(pdfPath, sourceType);
                    }
                }
                else
                {
                    // Move to 4_Error if enabled
                    if (_bulkConfig.MoveErrorPdfsToErrorFolder)
                    {
                        MoveToErrorFolder(pdfPath, result.ErrorMessage ?? "Unknown error");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Failed to process {fileName}: {ex.Message}";
                result.FailedExtractions++;

                Console.WriteLine($"❌ {result.ErrorMessage}");

                // Move to 4_Error
                if (_bulkConfig.MoveErrorPdfsToErrorFolder)
                {
                    try
                    {
                        MoveToErrorFolder(pdfPath, ex.Message);
                    }
                    catch (Exception moveEx)
                    {
                        Console.WriteLine($"⚠️  Could not move to error folder: {moveEx.Message}");
                    }
                }

                return result;
            }
        }




        /// <summary>
        /// Smart extraction: Detects if PDF is single-page (scanned) or multi-page (bulk)
        /// Outputs to 3_Output_Ready with format: {ID}_{LastName}_{FirstName}_consent.pdf
        /// </summary>
        public BulkExtractionResult SmartExtractFromPdf(string pdfPath, string outputDirectory)
        {
            var result = new BulkExtractionResult();

            try
            {
                if (!File.Exists(pdfPath))
                {
                    result.ErrorMessage = $"PDF not found: {pdfPath}";
                    Console.WriteLine($"   ❌ {result.ErrorMessage}");
                    return result;
                }

                // Detect PDF type by page count
                using var pdfDoc = PdfDocument.Open(pdfPath);
                int totalPages = pdfDoc.NumberOfPages;
                int pagesPerConsent = _bulkConfig.PagesPerConsent;

                Console.WriteLine($"\n📄 Smart PDF Processing...");
                Console.WriteLine($"   Source: {Path.GetFileName(pdfPath)}");
                Console.WriteLine($"   Total pages: {totalPages}");
                Console.WriteLine($"   Expected pages per consent: {pagesPerConsent}");

                // Decision logic
                if (totalPages == 1)
                {
                    Console.WriteLine($"   🔍 Detected: Single-page PDF (scanned consent)");
                    result = ProcessSinglePagePdf(pdfPath, outputDirectory, pageId: 1);
                }
                else if (totalPages <= pagesPerConsent)
                {
                    Console.WriteLine($"   🔍 Detected: Single consent PDF ({totalPages} page(s))");
                    result = ProcessSinglePagePdf(pdfPath, outputDirectory, pageId: 1);
                }
                else
                {
                    Console.WriteLine($"   🔍 Detected: Bulk PDF ({totalPages} pages / {pagesPerConsent} per consent = ~{totalPages / pagesPerConsent} consents)");
                    result = ExtractFromBulkPdfWithNames(pdfPath, outputDirectory);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Smart extraction failed: {ex.Message}";
                Console.WriteLine($"   ❌ {result.ErrorMessage}");
                return result;
            }
        }





        /// <summary>
        /// Process a single-page scanned PDF
        /// Output format: {ID}_{LastName}_{FirstName}_consent.pdf
        /// </summary>
        private BulkExtractionResult ProcessSinglePagePdf(string pdfPath, string outputDirectory, int pageId)
        {
            var result = new BulkExtractionResult();

            try
            {
                Console.WriteLine($"\n   📄 Processing single-page consent...");
                Directory.CreateDirectory(outputDirectory);

                // Extract names
                var (firstName, lastName, pageCount) = PdfProcessor.ProcessSinglePdf(
                    pdfPath, debugOcr: false, debugOutputDir: null);

                // Track if names were detected
                bool namesDetected = true;

                if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName) ||
                    firstName == "Unknown" || lastName == "Unknown" ||
                    firstName == "Error" || lastName == "Error")
                {
                    Console.WriteLine($"   ⚠️  Could not extract names - using 'Unknown'");
                    firstName = "Unknown";
                    lastName = "Unknown";
                    namesDetected = false;
                }

                firstName = CleanAndCapitalizeName(firstName);
                lastName = CleanAndCapitalizeName(lastName);

                // Format: {ID}_{LastName}_{FirstName}_consent.pdf
                string outputFileName = FormatFileName(pageId, lastName, firstName);
                string outputPath = Path.Combine(outputDirectory, outputFileName);

                // Handle duplicates
                int duplicateCounter = 2;
                while (File.Exists(outputPath))
                {
                    Console.WriteLine($"   ⚠️  File exists, adding suffix: _{duplicateCounter}");
                    outputFileName = FormatFileName(pageId, lastName, firstName, duplicateCounter);
                    outputPath = Path.Combine(outputDirectory, outputFileName);
                    duplicateCounter++;
                }

                // Copy file to 3_Output_Ready
                File.Copy(pdfPath, outputPath, overwrite: false);

                result.ExtractedFiles.Add(outputPath);
                result.TotalExtracted = 1;
                result.Success = namesDetected; // Success only if names detected

                if (!namesDetected)
                {
                    result.UnknownNameCount = 1;
                    result.ErrorMessages.Add($"{outputFileName}: Names not detected - requires manual review");
                }

                Console.WriteLine($"   ✅ Saved to 3_Output_Ready: {outputFileName}");

                if (!namesDetected)
                {
                    Console.WriteLine($"   ⚠️  WARNING: File contains 'Unknown' - may need to move to 4_Error");
                }

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Single-page processing failed: {ex.Message}";
                result.FailedExtractions = 1;
                Console.WriteLine($"   ❌ {result.ErrorMessage}");
                return result;
            }
        }





        /// <summary>
        /// Extract from bulk PDF with name detection
        /// Output format: {ID}_{LastName}_{FirstName}_consent.pdf
        /// </summary>
        public BulkExtractionResult ExtractFromBulkPdfWithNames(string bulkPdfPath, string outputDirectory)
        {
            var result = new BulkExtractionResult();
            var seenNames = new Dictionary<string, int>();
            var tempExtractionDir = Path.Combine(Path.GetTempPath(), $"BulkExtract_{Guid.NewGuid()}");

            try
            {
                Console.WriteLine($"\n📄 Extracting from bulk PDF...");

                Directory.CreateDirectory(outputDirectory);
                Directory.CreateDirectory(tempExtractionDir);

                using var pdfDocument = PdfDocument.Open(bulkPdfPath);

                int totalPages = pdfDocument.NumberOfPages;
                int pagesPerConsent = _bulkConfig.PagesPerConsent;
                int startPage = _bulkConfig.StartPage;

                Console.WriteLine($"   Total pages: {totalPages}");
                Console.WriteLine($"   Pages per consent: {pagesPerConsent}");
                Console.WriteLine($"   Starting from page: {startPage}");

                int currentPage = startPage;
                int pageIndex = 1;

                while (currentPage <= totalPages)
                {
                    int endPage = Math.Min(currentPage + pagesPerConsent - 1, totalPages);

                    try
                    {
                        // Extract to temp file
                        string tempFileName = $"Temp_Consent_{pageIndex:D3}.pdf";
                        string tempFilePath = Path.Combine(tempExtractionDir, tempFileName);

                        var builder = new PdfDocumentBuilder();
                        for (int pageNum = currentPage; pageNum <= endPage; pageNum++)
                        {
                            builder.AddPage(pdfDocument, pageNum);
                        }

                        byte[] pdfBytes = builder.Build();
                        File.WriteAllBytes(tempFilePath, pdfBytes);

                        // Extract names
                        var (firstName, lastName, pageCount) = PdfProcessor.ProcessSinglePdf(
                            tempFilePath, debugOcr: false, debugOutputDir: null);

                        bool namesDetected = true;

                        if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName) ||
                            firstName == "Unknown" || lastName == "Unknown" ||
                            firstName == "Error" || lastName == "Error")
                        {
                            Console.WriteLine($"   ⚠️  Page {currentPage}: Names not detected");
                            firstName = "Unknown";
                            lastName = "Unknown";
                            namesDetected = false;
                            result.UnknownNameCount++;
                        }

                        firstName = CleanAndCapitalizeName(firstName);
                        lastName = CleanAndCapitalizeName(lastName);

                        // Track duplicates
                        string nameKey = $"{NormalizeName(lastName)}_{NormalizeName(firstName)}";
                        int duplicateCount = 0;

                        if (seenNames.ContainsKey(nameKey))
                        {
                            seenNames[nameKey]++;
                            duplicateCount = seenNames[nameKey];
                            Console.WriteLine($"   ⚠️  DUPLICATE: {firstName} {lastName} (#{duplicateCount + 1})");
                        }
                        else
                        {
                            seenNames[nameKey] = 0;
                        }

                        // Format: {ID}_{LastName}_{FirstName}_consent.pdf
                        string outputFileName = FormatFileName(pageIndex, lastName, firstName, duplicateCount > 0 ? duplicateCount + 1 : null);
                        string finalOutputPath = Path.Combine(outputDirectory, outputFileName);

                        // Move to 3_Output_Ready
                        File.Move(tempFilePath, finalOutputPath, overwrite: true);

                        result.ExtractedFiles.Add(finalOutputPath);
                        result.TotalExtracted++;

                        string statusIcon = namesDetected ? "✅" : "⚠️";
                        Console.WriteLine($"   {statusIcon} [{pageIndex}] {outputFileName}");

                        pageIndex++;
                    }
                    catch (Exception ex)
                    {
                        result.FailedExtractions++;
                        result.ErrorMessages.Add($"Page {currentPage}: {ex.Message}");
                        Console.WriteLine($"   ❌ Failed page {currentPage}: {ex.Message}");
                    }

                    currentPage += pagesPerConsent;
                }

                result.Success = result.FailedExtractions == 0 && result.UnknownNameCount == 0;
                result.DuplicatesFound = seenNames.Count(kvp => kvp.Value > 0);

                Console.WriteLine($"\n   Summary: {result.TotalExtracted} extracted, {result.UnknownNameCount} unknown names, {result.FailedExtractions} failed");

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Bulk extraction failed: {ex.Message}";
                Console.WriteLine($"   ❌ {result.ErrorMessage}");
                return result;
            }
            finally
            {
                if (Directory.Exists(tempExtractionDir))
                {
                    try { Directory.Delete(tempExtractionDir, recursive: true); } catch { }
                }
            }
        }





        /// <summary>
        /// Extract with simple numbering (no name detection)
        /// Fallback when AutoDetectNames is disabled
        /// </summary>
        public BulkExtractionResult ExtractFromBulkPdf(string bulkPdfPath, string outputDirectory)
        {
            var result = new BulkExtractionResult();

            try
            {
                Console.WriteLine($"\n📄 Extracting PDFs from bulk file (simple numbering)...");
                Console.WriteLine($"   Source: {Path.GetFileName(bulkPdfPath)}");
                Console.WriteLine($"   Output: {outputDirectory}");

                if (!File.Exists(bulkPdfPath))
                {
                    result.ErrorMessage = $"Bulk PDF not found: {bulkPdfPath}";
                    Console.WriteLine($"   ❌ {result.ErrorMessage}");
                    return result;
                }

                Directory.CreateDirectory(outputDirectory);

                using var pdfDocument = PdfDocument.Open(bulkPdfPath);

                int totalPages = pdfDocument.NumberOfPages;
                int pagesPerConsent = _bulkConfig.PagesPerConsent;
                int startPage = _bulkConfig.StartPage;

                Console.WriteLine($"   Total pages: {totalPages}");
                Console.WriteLine($"   Pages per consent: {pagesPerConsent}");

                int currentPage = startPage;
                int extractedCount = 0;

                while (currentPage <= totalPages)
                {
                    int endPage = Math.Min(currentPage + pagesPerConsent - 1, totalPages);

                    try
                    {
                        string outputFileName = $"Consent_{extractedCount + 1:D3}.pdf";
                        string outputPath = Path.Combine(outputDirectory, outputFileName);

                        var builder = new PdfDocumentBuilder();
                        for (int pageNum = currentPage; pageNum <= endPage; pageNum++)
                        {
                            builder.AddPage(pdfDocument, pageNum);
                        }

                        byte[] pdfBytes = builder.Build();
                        File.WriteAllBytes(outputPath, pdfBytes);

                        extractedCount++;
                        result.ExtractedFiles.Add(outputPath);

                        if (extractedCount % 10 == 0)
                        {
                            Console.WriteLine($"   📄 Extracted {extractedCount} PDFs...");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailedExtractions++;
                        result.ErrorMessages.Add($"Pages {currentPage}-{endPage}: {ex.Message}");
                        Console.WriteLine($"   ⚠️  Failed to extract pages {currentPage}-{endPage}: {ex.Message}");
                    }

                    currentPage += pagesPerConsent;
                }

                result.TotalExtracted = extractedCount;
                result.Success = result.FailedExtractions == 0;

                Console.WriteLine($"\n   ✅ Extraction complete:");
                Console.WriteLine($"      Successfully extracted: {result.TotalExtracted}");
                Console.WriteLine($"      Failed: {result.FailedExtractions}");

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Bulk extraction failed: {ex.Message}";
                Console.WriteLine($"   ❌ {result.ErrorMessage}");
                return result;
            }
        }




        #region File Management


        /// <summary>
        /// Ensure all required directories exist and display structure
        /// </summary>
        private void EnsureDirectoriesExist()
        {
            try
            {
                // Create base path
                Directory.CreateDirectory(_bulkConfig.BasePdfPath);

                // Create all subfolders
                Directory.CreateDirectory(_bulkConfig.GetInputBulkPath());
                Directory.CreateDirectory(_bulkConfig.GetInputScannedPath());
                Directory.CreateDirectory(_bulkConfig.GetOutputReadyPath());
                Directory.CreateDirectory(_bulkConfig.GetErrorPath());
                Directory.CreateDirectory(_bulkConfig.GetArchivePath());
                Directory.CreateDirectory(_bulkConfig.GetArchiveBulkPath());
                Directory.CreateDirectory(_bulkConfig.GetArchiveScannedPath());

                // ✅ Create README files to guide users
                CreateReadmeFiles();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Warning: Could not create folder structure: {ex.Message}");
            }
        }


        /// <summary>
        /// Move successfully processed PDF to 5_Archive
        /// </summary>
        private void MoveToArchive(string sourcePath, PdfSourceType sourceType)
        {
            try
            {
                string fileName = Path.GetFileName(sourcePath);
                string archivePath = sourceType == PdfSourceType.Bulk
                    ? _bulkConfig.GetArchiveBulkPath()
                    : _bulkConfig.GetArchiveScannedPath();

                // Add timestamp to avoid conflicts
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                string archivedFileName = $"{fileNameWithoutExt}_{timestamp}{extension}";

                string destinationPath = Path.Combine(archivePath, archivedFileName);

                File.Move(sourcePath, destinationPath, overwrite: true);
                Console.WriteLine($"   📦 Archived to 5_Archive/{sourceType}: {archivedFileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Could not archive file: {ex.Message}");
            }
        }



        /// <summary>
        /// Move failed PDF to 4_Error with error details
        /// </summary>
        private void MoveToErrorFolder(string sourcePath, string errorMessage)
        {
            try
            {
                string fileName = Path.GetFileName(sourcePath);
                string errorPath = _bulkConfig.GetErrorPath();
                Directory.CreateDirectory(errorPath);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string destinationPath = Path.Combine(errorPath, fileName);

                // Add error log
                string errorLogPath = Path.Combine(errorPath, $"{Path.GetFileNameWithoutExtension(fileName)}_ERROR_{timestamp}.txt");
                File.WriteAllText(errorLogPath, $"Error Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nFile: {fileName}\nError: {errorMessage}");

                File.Move(sourcePath, destinationPath, overwrite: true);
                Console.WriteLine($"   ⚠️  Moved to 4_Error: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Could not move to error folder: {ex.Message}");
            }
        }


        /// <summary>
        /// Display final processing summary
        /// </summary>
        private void DisplayProcessingSummary(BulkExtractionResult result)
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("📊 PROCESSING SUMMARY");
            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"Total PDFs Processed:    {result.TotalExtracted}");
            Console.WriteLine($"Unknown Names:           {result.UnknownNameCount} ⚠️");
            Console.WriteLine($"Duplicates Detected:     {result.DuplicatesFound}");
            Console.WriteLine($"Failed:                  {result.FailedExtractions}");
            Console.WriteLine($"Status:                  {(result.Success ? "✅ Success" : "⚠️ Needs Review")}");
            Console.WriteLine(new string('═', 60));

            Console.WriteLine($"\n📁 Output Location:");
            Console.WriteLine($"   3_Output_Ready: {_bulkConfig.GetOutputReadyPath()}");

            if (result.UnknownNameCount > 0)
            {
                Console.WriteLine($"\n⚠️  {result.UnknownNameCount} file(s) with 'Unknown' names");
                Console.WriteLine($"   Review files in 3_Output_Ready and move to 4_Error if needed");
            }

            if (result.ErrorMessages.Count > 0)
            {
                Console.WriteLine($"\n⚠️  Errors ({result.ErrorMessages.Count}):");
                foreach (var error in result.ErrorMessages.Take(10))
                {
                    Console.WriteLine($"   - {error}");
                }
                if (result.ErrorMessages.Count > 10)
                {
                    Console.WriteLine($"   ... and {result.ErrorMessages.Count - 10} more");
                }
            }

            if (result.Success)
            {
                Console.WriteLine($"\n✅ Ready for Phase 3! Use files from 3_Output_Ready");
            }
        }





        #endregion  File Management




        #region Helper Methods

        /// <summary>
        /// Format filename according to naming convention: {ID}_{LastName}_{FirstName}_consent.pdf
        /// </summary>
        private string FormatFileName(int id, string lastName, string firstName, int? duplicateSuffix = null)
        {
            string baseName = $"{id}_{lastName}_{firstName}_consent";

            if (duplicateSuffix.HasValue)
            {
                baseName = $"{id}_{lastName}_{firstName}_{duplicateSuffix}_consent";
            }

            return MakeSafeFileName(baseName + ".pdf");
        }

        private string CleanAndCapitalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var cleaned = Regex.Replace(name, @"[^\w\s\-'']", "");
            cleaned = Regex.Replace(cleaned.Trim(), @"\s+", " ");
            var parts = cleaned.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var capitalized = parts.Select(part =>
                part.Length <= 1 ? part.ToUpperInvariant() :
                char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant());
            return cleaned.Contains('-') ? string.Join("-", capitalized) : string.Join(" ", capitalized);
        }

        private string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var normalized = RemoveAccents(name.ToUpperInvariant());
            return normalized.Replace(" ", "").Replace("-", "").Replace("'", "");
        }

        private string RemoveAccents(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();
            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                    stringBuilder.Append(c);
            }
            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        private string MakeSafeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string safe = fileName;
            foreach (char c in invalidChars) safe = safe.Replace(c, '_');
            return safe.Replace(":", "_").Replace("*", "_").Replace("?", "_")
                       .Replace("\"", "_").Replace("<", "_").Replace(">", "_")
                       .Replace("|", "_").Trim();
        }

        #endregion





        /// <summary>
        /// Create helpful README files in each folder
        /// </summary>
        private void CreateReadmeFiles()
        {
            try
            {
                // 1_Input_Bulk README
                var bulkReadmePath = Path.Combine(_bulkConfig.GetInputBulkPath(), "README.txt");
                if (!File.Exists(bulkReadmePath))
                {
                    File.WriteAllText(bulkReadmePath,
    @"📁 1_INPUT_BULK - Drop Bulk PDF Files Here
==========================================

Place your bulk consent PDF files here (e.g., BulkConsent.pdf downloaded from Vitalite).

The system will:
✓ Automatically detect this is a multi-page bulk file
✓ Split into individual consent PDFs
✓ Extract student names from each page
✓ Save to 3_Output_Ready with naming: {ID}_{LastName}_{FirstName}_consent.pdf
✓ Move original bulk file to 5_Archive/Bulk after successful processing

Example: BulkConsent.pdf (50 pages, 1 page per student)
Result: 50 individual PDFs in 3_Output_Ready

Note: Only PDF files are processed. Other file types will be ignored.
");
                }

                // 2_Input_Scanned README
                var scannedReadmePath = Path.Combine(_bulkConfig.GetInputScannedPath(), "README.txt");
                if (!File.Exists(scannedReadmePath))
                {
                    File.WriteAllText(scannedReadmePath,
    @"📁 2_INPUT_SCANNED - Drop Scanned PDF Files Here
=================================================

Place individual scanned consent forms here (typically 1-page PDFs from nursing office).

The system will:
✓ Automatically detect this is a single-page scan
✓ Extract student name from the PDF
✓ Rename to: 1_{LastName}_{FirstName}_consent.pdf
✓ Move to 3_Output_Ready
✓ Move original scan to 5_Archive/Scanned after successful processing

Example: scan001.pdf, scan002.pdf
Result: Individual renamed PDFs in 3_Output_Ready

Note: Each file should contain only ONE student's consent form.
");
                }

                // 3_Output_Ready README
                var outputReadmePath = Path.Combine(_bulkConfig.GetOutputReadyPath(), "README.txt");
                if (!File.Exists(outputReadmePath))
                {
                    File.WriteAllText(outputReadmePath,
    @"📁 3_OUTPUT_READY - Processed PDFs Ready for Phase 3
=====================================================

This folder contains processed consent PDFs ready for upload to PHIS.

File naming format: {ID}_{LastName}_{FirstName}_consent.pdf
Examples:
  - 1_Smith_John_consent.pdf
  - 2_Leblanc_Marie_consent.pdf
  - 3_Unknown_Unknown_consent.pdf ⚠️ (needs manual review)

⚠️ IMPORTANT: Review files with 'Unknown' in the name
   - These require manual identification
   - Move to 4_Error if you cannot identify the student
   - Or rename manually if you know the student's name

✅ Phase 3 will use these files to upload to PHIS
   Do not delete or move files from here manually!
");
                }

                // 4_Error README
                var errorReadmePath = Path.Combine(_bulkConfig.GetErrorPath(), "README.txt");
                if (!File.Exists(errorReadmePath))
                {
                    File.WriteAllText(errorReadmePath,
    @"📁 4_ERROR - Failed Processing or Unknown Students
===================================================

This folder contains PDFs that failed processing or could not be identified.

Common reasons:
❌ Could not extract student names (scanned quality too poor)
❌ PDF format not supported
❌ File corruption
❌ Processing error

What to do:
1. Review the error log files (*_ERROR_*.txt) for details
2. Try to identify students manually
3. For scanned PDFs with poor quality:
   - Re-scan at higher resolution (300 DPI minimum)
   - Ensure the form is properly aligned
   - Drop the new scan in 2_Input_Scanned

4. For unidentifiable students:
   - Contact school/nursing office for clarification
   - Rename manually if you can identify them
   - Move back to appropriate input folder for reprocessing

Note: Files here will NOT be processed in Phase 3 until moved elsewhere.
");
                }

                // 5_Archive README
                var archiveReadmePath = Path.Combine(_bulkConfig.GetArchivePath(), "README.txt");
                if (!File.Exists(archiveReadmePath))
                {
                    File.WriteAllText(archiveReadmePath,
    @"📁 5_ARCHIVE - Successfully Processed Original Files
=====================================================

This folder contains the original source files after successful processing.

Structure:
  📂 Bulk/    - Original bulk PDF files from Vitalite
  📂 Scanned/ - Original scanned consent forms

Files are timestamped to prevent conflicts:
Example: BulkConsent_20250114_143022.pdf

Why keep archives?
✓ Backup in case reprocessing is needed
✓ Audit trail for compliance
✓ Reference if questions arise about specific students

You can safely delete old archives after Phase 3 is complete and verified.
Recommended: Keep for at least one school year.
");
                }
            }
            catch (Exception ex)
            {
                // Silently fail - README files are nice-to-have, not critical
                Console.WriteLine($"⚠️  Could not create README files: {ex.Message}");
            }
        }



    }
}
