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
using ConsentSyncCore.Services.ConfigurationPoco;
using ConsentSyncCore.Services.Configuration;

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
        /// Files with Unknown names are moved to 4_Error for manual review
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

                //// Track if names were detected
                //bool namesDetected = true;

                if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName) ||
                    firstName == "Unknown" || lastName == "Unknown" ||
                    firstName == "Error" || lastName == "Error")
                {
                    Console.WriteLine($"   ⚠️  Could not extract names - moving to 4_Error for manual review");

                    // ✅ Move to error folder instead of processing
                    string errorMessage = "Unable to extract student names from PDF. Manual identification required.";

                    // Copy to error folder with descriptive name
                    string errorFileName = $"UNKNOWN_{pageId}_{Path.GetFileNameWithoutExtension(pdfPath)}.pdf";
                    string errorPath = Path.Combine(_bulkConfig.GetErrorPath(), errorFileName);

                    File.Copy(pdfPath, errorPath, overwrite: true);

                    // Create error log
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string errorLogPath = Path.Combine(_bulkConfig.GetErrorPath(),
                        $"UNKNOWN_{pageId}_{Path.GetFileNameWithoutExtension(pdfPath)}_ERROR_{timestamp}.txt");

                    File.WriteAllText(errorLogPath,
                        $"Error Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                        $"Original File: {Path.GetFileName(pdfPath)}\n" +
                        $"Reason: {errorMessage}\n" +
                        $"Action Required: Manually identify student and rename file to: {{ID}}_{{LastName}}_{{FirstName}}_consent.pdf\n" +
                        $"Then move to 3_Output_Ready folder");

                    result.UnknownNameCount = 1;
                    result.ErrorMessages.Add($"{errorFileName}: {errorMessage}");
                    result.Success = false; // Mark as unsuccessful since it needs manual review

                    Console.WriteLine($"   ⚠️  Moved to 4_Error: {errorFileName}");
                    return result;
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
                result.Success = true;

                Console.WriteLine($"   ✅ Saved to 3_Output_Ready: {outputFileName}");

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
        /// Files with Unknown names or duplicates are moved to 4_Error for manual review
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
                        bool isDuplicate = false;

                        // ✅ Check for unknown names
                        if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName) ||
                            firstName == "Unknown" || lastName == "Unknown" ||
                            firstName == "Error" || lastName == "Error")
                        {
                            Console.WriteLine($"   ⚠️  Page {currentPage}: Names not detected - moving to 4_Error");

                            // Move to error folder
                            string errorFileName = $"UNKNOWN_{pageIndex}_Page{currentPage}.pdf";
                            string errorPath = Path.Combine(_bulkConfig.GetErrorPath(), errorFileName);
                            File.Move(tempFilePath, errorPath, overwrite: true);

                            // Create error log
                            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            string errorLogPath = Path.Combine(_bulkConfig.GetErrorPath(),
                                $"UNKNOWN_{pageIndex}_Page{currentPage}_ERROR_{timestamp}.txt");

                            File.WriteAllText(errorLogPath,
                                $"Error Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                $"Source: {Path.GetFileName(bulkPdfPath)}, Page {currentPage}\n" +
                                $"Reason: Unable to extract student names from PDF\n" +
                                $"Action Required: Manually identify student and rename file to: {{ID}}_{{LastName}}_{{FirstName}}_consent.pdf\n" +
                                $"Then move to 3_Output_Ready folder");

                            result.UnknownNameCount++;
                            result.ErrorMessages.Add($"{errorFileName}: Names not detected");

                            Console.WriteLine($"   ⚠️  Moved to 4_Error: {errorFileName}");
                            pageIndex++;
                            currentPage += pagesPerConsent;
                            continue; // Skip to next PDF
                        }

                        firstName = CleanAndCapitalizeName(firstName);
                        lastName = CleanAndCapitalizeName(lastName);

                        // ✅ Check for duplicates
                        string nameKey = $"{NormalizeName(lastName)}_{NormalizeName(firstName)}";

                        if (seenNames.ContainsKey(nameKey))
                        {
                            seenNames[nameKey]++;
                            int duplicateCount = seenNames[nameKey];

                            Console.WriteLine($"   ⚠️  DUPLICATE: {firstName} {lastName} (occurrence #{duplicateCount + 1}) - moving to 4_Error");

                            // Move duplicate to error folder
                            string errorFileName = $"DUPLICATE_{pageIndex}_{lastName}_{firstName}_{duplicateCount + 1}.pdf";
                            string errorPath = Path.Combine(_bulkConfig.GetErrorPath(), errorFileName);
                            File.Move(tempFilePath, errorPath, overwrite: true);

                            // Create error log
                            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            string errorLogPath = Path.Combine(_bulkConfig.GetErrorPath(),
                                $"DUPLICATE_{pageIndex}_{lastName}_{firstName}_{duplicateCount + 1}_ERROR_{timestamp}.txt");

                            File.WriteAllText(errorLogPath,
                                $"Error Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                $"Source: {Path.GetFileName(bulkPdfPath)}, Page {currentPage}\n" +
                                $"Student: {firstName} {lastName}\n" +
                                $"Reason: Duplicate name detected (occurrence #{duplicateCount + 1})\n" +
                                $"Action Required: \n" +
                                $"  1. Verify if this is the same student (form submitted twice)\n" +
                                $"  2. If duplicate, delete this file\n" +
                                $"  3. If different student with same name, add middle initial or identifier\n" +
                                $"     Example: {pageIndex}_{lastName}_{firstName}_2_consent.pdf\n" +
                                $"  4. Move corrected file to 3_Output_Ready");

                            result.DuplicatesFound++;
                            result.ErrorMessages.Add($"{errorFileName}: Duplicate name");

                            Console.WriteLine($"   ⚠️  Moved to 4_Error: {errorFileName}");
                            pageIndex++;
                            currentPage += pagesPerConsent;
                            continue; // Skip to next PDF
                        }
                        else
                        {
                            seenNames[nameKey] = 0;
                        }

                        // ✅ Names detected and no duplicate - save to 3_Output_Ready
                        string outputFileName = FormatFileName(pageIndex, lastName, firstName);
                        string finalOutputPath = Path.Combine(outputDirectory, outputFileName);

                        File.Move(tempFilePath, finalOutputPath, overwrite: true);

                        result.ExtractedFiles.Add(finalOutputPath);
                        result.TotalExtracted++;

                        Console.WriteLine($"   ✅ [{pageIndex}] {outputFileName}");

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

                result.Success = result.FailedExtractions == 0 && result.UnknownNameCount == 0 && result.DuplicatesFound == 0;

                Console.WriteLine($"\n   Summary: {result.TotalExtracted} extracted, {result.UnknownNameCount} unknown names, {result.DuplicatesFound} duplicates, {result.FailedExtractions} failed");

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
        /// Ensure all required directories exist
        /// </summary>
        private void EnsureDirectoriesExist()
        {
            try
            {
                Directory.CreateDirectory(_bulkConfig.BasePdfPath);

                // Top-level folders
                Directory.CreateDirectory(_bulkConfig.GetInputBulkPath());
                Directory.CreateDirectory(_bulkConfig.GetInputScannedPath());
                Directory.CreateDirectory(_bulkConfig.GetOutputReadyPath());

                // 4 FileRose Extraction — parent + two subfolders
                Directory.CreateDirectory(_bulkConfig.GetFileRosePath());
                Directory.CreateDirectory(_bulkConfig.GetFileRoseScanPath());
                Directory.CreateDirectory(_bulkConfig.GetFileRoseOutputReadyPath());

                Directory.CreateDirectory(_bulkConfig.GetDuplicateClientPath());
                Directory.CreateDirectory(_bulkConfig.GetErrorPath());

                // 7_Archive — parent + three subfolders
                Directory.CreateDirectory(_bulkConfig.GetArchivePath());
                Directory.CreateDirectory(_bulkConfig.GetArchiveBulkPath());
                Directory.CreateDirectory(_bulkConfig.GetArchiveScannedPath());
                Directory.CreateDirectory(_bulkConfig.GetArchiveFileRosePath());

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


        private void DisplayProcessingSummary(BulkExtractionResult result)
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("📊 PROCESSING SUMMARY");
            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"Total PDFs Processed:    {result.TotalExtracted}");
            Console.WriteLine($"Unknown Names:           {result.UnknownNameCount} ⚠️");
            Console.WriteLine($"Duplicates Detected:     {result.DuplicatesFound} ⚠️");
            Console.WriteLine($"Failed:                  {result.FailedExtractions}");
            Console.WriteLine($"Status:                  {(result.Success ? "✅ Success" : "⚠️ Needs Review")}");
            Console.WriteLine(new string('═', 60));

            Console.WriteLine($"\n📁 Output Locations:");
            Console.WriteLine($"   3_Output_Ready: {_bulkConfig.GetOutputReadyPath()}");
            Console.WriteLine($"   4_Error:        {_bulkConfig.GetErrorPath()}");

            if (result.UnknownNameCount > 0 || result.DuplicatesFound > 0)
            {
                Console.WriteLine($"\n⚠️  MANUAL REVIEW REQUIRED:");

                if (result.UnknownNameCount > 0)
                {
                    Console.WriteLine($"   • {result.UnknownNameCount} file(s) with unknown names in 4_Error");
                    Console.WriteLine($"     → Identify students and rename manually");
                }

                if (result.DuplicatesFound > 0)
                {
                    Console.WriteLine($"   • {result.DuplicatesFound} duplicate name(s) in 4_Error");
                    Console.WriteLine($"     → Verify if same student or add identifier");
                }

                Console.WriteLine($"\n   📋 Check error log files (*_ERROR_*.txt) for details");
                Console.WriteLine($"   ✅ After fixing, move corrected files to 3_Output_Ready");
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
                Console.WriteLine($"\n✅ All PDFs successfully processed!");
                Console.WriteLine($"   Ready for Phase 3! Use files from 3_Output_Ready");
            }
            else
            {
                Console.WriteLine($"\n⚠️  Review required before proceeding to Phase 3");
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
                // ── 1_Input_Bulk ────────────────────────────────────────────────────────
                var bulkReadme = Path.Combine(_bulkConfig.GetInputBulkPath(), "README.txt");
                if (!File.Exists(bulkReadme))
                    File.WriteAllText(bulkReadme,
@"📁 1_INPUT_BULK - Drop Bulk PDF Files Here
==========================================

Place your bulk consent PDF files here (e.g., BulkConsent.pdf downloaded from Vitalite).

The system will:
✓ Automatically detect this is a multi-page bulk file
✓ Split into individual consent PDFs
✓ Extract student names from each page
✓ Save to 3_Output_Ready  →  {ID}_{LastName}_{FirstName}_consent.pdf
✓ Move original bulk file to 7_Archive\Bulk after successful processing

Example: BulkConsent.pdf (50 pages, 1 page per student)
Result:  50 individual PDFs in 3_Output_Ready

Note: Only PDF files are processed. Other file types will be ignored.
");

                // ── 2_Input_Scanned ─────────────────────────────────────────────────────
                var scannedReadme = Path.Combine(_bulkConfig.GetInputScannedPath(), "README.txt");
                if (!File.Exists(scannedReadme))
                    File.WriteAllText(scannedReadme,
@"📁 2_INPUT_SCANNED - Drop Scanned PDF Files Here
=================================================

Place individual scanned consent forms here (typically 1-page PDFs from nursing office).

The system will:
✓ Automatically detect this is a single-page scan
✓ Extract student name from the PDF
✓ Rename to: {ID}_{LastName}_{FirstName}_consent.pdf
✓ Move to 3_Output_Ready
✓ Move original scan to 7_Archive\Scanned after successful processing

Example: scan001.pdf, scan002.pdf
Result:  Individual renamed PDFs in 3_Output_Ready

Note: Each file should contain only ONE student's consent form.
");

                // ── 3_Output_Ready ──────────────────────────────────────────────────────
                var outputReadme = Path.Combine(_bulkConfig.GetOutputReadyPath(), "README.txt");
                if (!File.Exists(outputReadme))
                    File.WriteAllText(outputReadme,
@"📁 3_OUTPUT_READY - Processed Consent PDFs Ready for Phase 3
=============================================================

This folder contains processed consent PDFs ready for upload to PHIS.

File naming format: {ID}_{LastName}_{FirstName}_consent.pdf
Examples:
  - 1_Smith_John_consent.pdf
  - 2_Leblanc_Marie_consent.pdf

⚠️  Files with 'Unknown' in the name require manual identification:
   - Rename manually if you know the student
   - Or move to 6_Error if the student cannot be identified

✅ Phase 3 will use these files to upload to PHIS.
   Do not delete or move files from here manually!
");

                // ── 4 FileRose Extraction / 1 Scan File Rose ────────────────────────────
                var fileRoseScanReadme = Path.Combine(_bulkConfig.GetFileRoseScanPath(), "README.txt");
                if (!File.Exists(fileRoseScanReadme))
                    File.WriteAllText(fileRoseScanReadme,
@"📁 1 SCAN FILE ROSE - Place File Rose Scans Here
=================================================

Place all scanned File Rose (feuille rose) documents in this folder.

Naming convention:
  Each File Rose MUST be saved as:  <ClientID>.pdf
  Example: 106467.pdf

The system will:
✓ Read the Client ID from the filename
✓ Extract and validate the File Rose content
✓ Output the processed file to:  2_Output_Ready_FileRose\<ClientID>.pdf
✓ Move the original scan to 7_Archive\FileRose after successful processing

Requirements:
  - One PDF per client
  - Filename must be exactly the Client ID (digits only), e.g. 106467.pdf
  - PDF should be a clear scan (300 DPI recommended)

Note: Files whose names are not valid Client IDs will be skipped.
");

                // ── 4 FileRose Extraction / 2_Output_Ready_FileRose ─────────────────────
                var fileRoseOutputReadme = Path.Combine(_bulkConfig.GetFileRoseOutputReadyPath(), "README.txt");
                if (!File.Exists(fileRoseOutputReadme))
                    File.WriteAllText(fileRoseOutputReadme,
@"📁 2_OUTPUT_READY_FILEROSE - Extracted File Rose PDFs
======================================================

File Rose documents will be extracted here after processing scans from
the '1 Scan File Rose' folder.

File naming format: <ClientID>.pdf
Example: 106467.pdf

✅ These files are ready to be attached to the corresponding PHIS client record.
   Do not rename or move files from here manually!
");

                // ── 5_Duplicate ─────────────────────────────────────────────────────────
                var duplicateReadme = Path.Combine(_bulkConfig.GetDuplicateClientPath(), "README.txt");
                if (!File.Exists(duplicateReadme))
                    File.WriteAllText(duplicateReadme,
@"📁 5_DUPLICATE - Duplicate Client ID PDFs
==========================================

This folder contains PDFs where the same Client ID appeared more than once
during bulk extraction.

Common reasons:
⚠️  Student submitted the consent form multiple times
⚠️  Two students share the same extracted name (rare)
⚠️  Data-entry error in the source bulk PDF

What to do:
1. Review each file and its matching *_ERROR_*.txt log
2. If it is a true duplicate (same student, submitted twice):
   - Keep the better-quality copy in 3_Output_Ready
   - Delete this duplicate
3. If two DIFFERENT students share the same name:
   - Add a middle initial or number suffix to distinguish them
     Example: {ID}_Smith_John_2_consent.pdf
   - Move the corrected file to 3_Output_Ready
4. Files here will NOT be uploaded to PHIS until moved to 3_Output_Ready.
");

                // ── 6_Error ─────────────────────────────────────────────────────────────
                var errorReadme = Path.Combine(_bulkConfig.GetErrorPath(), "README.txt");
                if (!File.Exists(errorReadme))
                    File.WriteAllText(errorReadme,
@"📁 6_ERROR - Failed Processing or Unknown Students
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
   - Move back to the appropriate input folder for reprocessing

Note: Files here will NOT be processed in Phase 3 until moved elsewhere.
");

                // ── 7_Archive ───────────────────────────────────────────────────────────
                var archiveReadme = Path.Combine(_bulkConfig.GetArchivePath(), "README.txt");
                if (!File.Exists(archiveReadme))
                    File.WriteAllText(archiveReadme,
@"📁 7_ARCHIVE - Successfully Processed Original Files
=====================================================

This folder contains the original source files after successful processing.

Structure:
  📂 Bulk\      - Original bulk PDF files from Vitalite
  📂 Scanned\   - Original scanned consent forms
  📂 FileRose\  - Original scanned File Rose (feuille rose) documents

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
            catch (Exception ex)
            {
                // Silently fail — README files are nice-to-have, not critical
                Console.WriteLine($"⚠️  Could not create README files: {ex.Message}");
            }
        }




    }
}
