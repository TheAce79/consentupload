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

             LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
             LoggerService.LogInformation("║      PDF Processing - Automatic Detection             ║");
             LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝\n");

            try
            {
                // Ensure all directories exist
                EnsureDirectoriesExist();

                // Process Bulk PDFs (1_Input_Bulk)
                var inputBulkPath = _bulkConfig.GetInputBulkPath();
                var bulkPdfs = Directory.GetFiles(inputBulkPath, "*.pdf");

                if (bulkPdfs.Length > 0)
                {
                     LoggerService.LogInformation($"\n📂 Found {bulkPdfs.Length} PDF(s) in 1_Input_Bulk");
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
                     LoggerService.LogInformation($"\n📂 Found {scannedPdfs.Length} PDF(s) in 2_Input_Scanned");
                    foreach (var pdfPath in scannedPdfs)
                    {
                        var result = ProcessSingleFile(pdfPath, PdfSourceType.Scanned);
                        aggregateResult.Merge(result);
                    }
                }

                if (bulkPdfs.Length == 0 && scannedPdfs.Length == 0)
                {
                     LoggerService.LogInformation("⚠️  No PDFs found to process");
                     LoggerService.LogInformation($"\n💡 Place PDFs in:");
                     LoggerService.LogInformation($"   - Bulk downloads: {inputBulkPath}");
                     LoggerService.LogInformation($"   - Scanned files:  {inputScannedPath}");
                }

                aggregateResult.Success = aggregateResult.FailedExtractions == 0;

                // Display final summary
                DisplayProcessingSummary(aggregateResult);

                return aggregateResult;
            }
            catch (Exception ex)
            {
                aggregateResult.ErrorMessage = $"Processing failed: {ex.Message}";
                 LoggerService.LogInformation($"❌ {aggregateResult.ErrorMessage}");
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
                 LoggerService.LogInformation($"\n{'=',-60}");
                 LoggerService.LogInformation($"Processing: {fileName}");
                 LoggerService.LogInformation($"Source: {sourceType}");
                 LoggerService.LogInformation($"{'=',-60}");

                // Smart extraction to 3_Output_Ready
                var outputPath = _bulkConfig.GetOutputReadyPath();
                result = SmartExtractFromPdf(pdfPath, outputPath);

                // ── Archival decision ──────────────────────────────────────
                // Archive : pages were extracted AND no hard failures AND no unknown names
                //           Duplicates (5_Duplicate) do NOT block archival.
                // Error   : zero pages extracted, OR failures, OR unknown names.
                bool shouldArchive = result.TotalExtracted > 0
                                     && result.FailedExtractions == 0
                                     && result.UnknownNameCount == 0;

                if (shouldArchive)
                {
                    if (_bulkConfig.MoveToArchiveAfterProcessing)
                    {
                        MoveToArchive(pdfPath, sourceType);
                    }
                }
                else
                {
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

                 LoggerService.LogInformation($"❌ {result.ErrorMessage}");

                if (_bulkConfig.MoveErrorPdfsToErrorFolder)
                {
                    try
                    {
                        MoveToErrorFolder(pdfPath, ex.Message);
                    }
                    catch (Exception moveEx)
                    {
                         LoggerService.LogInformation($"⚠️  Could not move to error folder: {moveEx.Message}");
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
                     LoggerService.LogInformation($"   ❌ {result.ErrorMessage}");
                    return result;
                }

                // Detect PDF type by page count
                using var pdfDoc = PdfDocument.Open(pdfPath);
                int totalPages = pdfDoc.NumberOfPages;
                int pagesPerConsent = _bulkConfig.PagesPerConsent;

                 LoggerService.LogInformation($"\n📄 Smart PDF Processing...");
                 LoggerService.LogInformation($"   Source: {Path.GetFileName(pdfPath)}");
                 LoggerService.LogInformation($"   Total pages: {totalPages}");
                 LoggerService.LogInformation($"   Expected pages per consent: {pagesPerConsent}");

                // Decision logic
                if (totalPages == 1)
                {
                     LoggerService.LogInformation($"   🔍 Detected: Single-page PDF (scanned consent)");
                    result = ProcessSinglePagePdf(pdfPath, outputDirectory, pageId: 1);
                }
                else if (totalPages <= pagesPerConsent)
                {
                     LoggerService.LogInformation($"   🔍 Detected: Single consent PDF ({totalPages} page(s))");
                    result = ProcessSinglePagePdf(pdfPath, outputDirectory, pageId: 1);
                }
                else
                {
                     LoggerService.LogInformation($"   🔍 Detected: Bulk PDF ({totalPages} pages / {pagesPerConsent} per consent = ~{totalPages / pagesPerConsent} consents)");
                    result = ExtractFromBulkPdfWithNames(pdfPath, outputDirectory);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Smart extraction failed: {ex.Message}";
                 LoggerService.LogInformation($"   ❌ {result.ErrorMessage}");
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
                 LoggerService.LogInformation($"\n   📄 Processing single-page consent...");
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
                     LoggerService.LogInformation($"   ⚠️  Could not extract names - moving to 4_Error for manual review");

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

                     LoggerService.LogInformation($"   ⚠️  Moved to 4_Error: {errorFileName}");
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
                     LoggerService.LogInformation($"   ⚠️  File exists, adding suffix: _{duplicateCounter}");
                    outputFileName = FormatFileName(pageId, lastName, firstName, duplicateCounter);
                    outputPath = Path.Combine(outputDirectory, outputFileName);
                    duplicateCounter++;
                }

                // Copy file to 3_Output_Ready
                File.Copy(pdfPath, outputPath, overwrite: false);

                result.ExtractedFiles.Add(outputPath);
                result.TotalExtracted = 1;
                result.Success = true;

                 LoggerService.LogInformation($"   ✅ Saved to 3_Output_Ready: {outputFileName}");

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Single-page processing failed: {ex.Message}";
                result.FailedExtractions = 1;
                 LoggerService.LogInformation($"   ❌ {result.ErrorMessage}");
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
                 LoggerService.LogInformation($"\n📄 Extracting from bulk PDF...");

                Directory.CreateDirectory(outputDirectory);
                Directory.CreateDirectory(tempExtractionDir);

                using var pdfDocument = PdfDocument.Open(bulkPdfPath);

                int totalPages = pdfDocument.NumberOfPages;
                int pagesPerConsent = _bulkConfig.PagesPerConsent;
                int startPage = _bulkConfig.StartPage;

                 LoggerService.LogInformation($"   Total pages: {totalPages}");
                 LoggerService.LogInformation($"   Pages per consent: {pagesPerConsent}");
                 LoggerService.LogInformation($"   Starting from page: {startPage}");

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
                             LoggerService.LogInformation($"   ⚠️  Page {currentPage}: Names not detected - moving to 4_Error");

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

                             LoggerService.LogInformation($"   ⚠️  Moved to 4_Error: {errorFileName}");
                            pageIndex++;
                            currentPage += pagesPerConsent;
                            continue; // Skip to next PDF
                        }

                        firstName = CleanAndCapitalizeName(firstName);
                        lastName = CleanAndCapitalizeName(lastName);



                        // ✅ Duplicate key — NormalizedLastName_NormalizedFirstName (accent-safe)
                        string nameKey = $"{NormalizeName(lastName)}_{NormalizeName(firstName)}";

                        if (seenNames.ContainsKey(nameKey))
                        {
                            seenNames[nameKey]++;
                            int occurrence = seenNames[nameKey] + 1; // 1-based

                             LoggerService.LogInformation($"   ⚠️  DUPLICATE #{occurrence}: {lastName}_{firstName} → 5_Duplicate\\");


                            // ── Create / reuse per-student subfolder ──────────────────────
                            string duplicateSubFolder = GetOrCreateDuplicateSubFolder(lastName, firstName);
                            // ✅ No encoding — spaces are valid in filenames, '_' is the only delimiter
                            string duplicateFileName = MakeSafeFileName(
                                $"{pageIndex}_{lastName}_{firstName}_{occurrence}_consent.pdf");
                            string duplicatePath = Path.Combine(duplicateSubFolder, duplicateFileName);

                            File.Move(tempFilePath, duplicatePath, overwrite: true);
                             LoggerService.LogInformation($"   📄 Moved duplicate → {duplicateFileName}");


                            // ── On the SECOND occurrence: pull the FIRST copy out of 3_Output_Ready ──
                            if (seenNames[nameKey] == 1)
                            {
                                string normLast = NormalizeName(lastName);
                                string normFirst = NormalizeName(firstName);
                                string normKey2 = $"_{normLast}_{normFirst}_{_bulkConfig.ConsentSuffix}".ToLowerInvariant();

                                // Fast path: search the in-memory list
                                var firstCopyPath = result.ExtractedFiles.FirstOrDefault(f =>
                                    NormalizeName(Path.GetFileNameWithoutExtension(f))
                                        .ToLowerInvariant()
                                        .Contains(normKey2));

                                // Fallback: scan 3_Output_Ready on disk
                                if (firstCopyPath == null || !File.Exists(firstCopyPath))
                                {
                                    firstCopyPath = Directory
                                        .GetFiles(_bulkConfig.GetOutputReadyPath(), "*.pdf")
                                        .FirstOrDefault(f =>
                                            NormalizeName(Path.GetFileNameWithoutExtension(f))
                                                .ToLowerInvariant()
                                                .Contains(normKey2));
                                }

                                if (firstCopyPath != null && File.Exists(firstCopyPath))
                                {
                                    string origNoExt = Path.GetFileNameWithoutExtension(firstCopyPath);
                                    string firstDestName = MakeSafeFileName($"{origNoExt}_1_consent.pdf");
                                    string firstDestPath = Path.Combine(duplicateSubFolder, firstDestName);

                                    File.Move(firstCopyPath, firstDestPath, overwrite: true);
                                    result.ExtractedFiles.Remove(firstCopyPath);
                                    result.TotalExtracted--;

                                    // ✅ FIX: The first copy is now also in 5_Duplicate — count it
                                    result.DuplicatesFound++;

                                     LoggerService.LogInformation($"   ♻️  Moved original copy → {firstDestName}");
                                }
                                else
                                {
                                     LoggerService.LogInformation($"   ⚠️  Could not locate original copy in 3_Output_Ready.");
                                }

                                // HOW_TO_MERGE.txt (written once when the folder is first created)
                                string mergeReadme = Path.Combine(duplicateSubFolder, "HOW_TO_MERGE.txt");
                                if (!File.Exists(mergeReadme))
                                {
                                    File.WriteAllText(mergeReadme,
                                        $"DUPLICATE CONSENTS — {lastName}, {firstName}\n" +
                                        $"==============================================\n\n" +
                                        $"This folder groups ALL consent PDFs found for this student.\n\n" +
                                        $"What to do:\n" +
                                        $"  1. Review each file — keep the best-quality copy.\n" +
                                        $"  2. If same student submitted twice → delete extras, keep one.\n" +
                                        $"  3. If TWO DIFFERENT students share this name:\n" +
                                        $"     → Confirm with the school.\n" +
                                        $"     → Rename to distinguish, e.g.:\n" +
                                        $"          {{ID}}_{lastName}_{firstName}_consent.pdf\n" +
                                        $"          {{ID}}_{lastName}_{firstName}_2_consent.pdf\n" +
                                        $"  4. Move the final file(s) to 3_Output_Ready\\\n\n" +
                                        $"Files here will NOT be uploaded to PHIS automatically.\n");
                                }
                            }




                            result.DuplicatesFound++;
                            result.ErrorMessages.Add(
                                $"DUPLICATE #{occurrence}: {lastName}_{firstName} → {Path.GetFileName(duplicateSubFolder)}\\{duplicateFileName}");

                            pageIndex++;
                            currentPage += pagesPerConsent;
                            continue;
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

                         LoggerService.LogInformation($"   ✅ [{pageIndex}] {outputFileName}");

                        pageIndex++;


                    }
                    catch (Exception ex)
                    {
                        result.FailedExtractions++;
                        result.ErrorMessages.Add($"Page {currentPage}: {ex.Message}");
                         LoggerService.LogInformation($"   ❌ Failed page {currentPage}: {ex.Message}");
                    }

                    currentPage += pagesPerConsent;
                }

                // DuplicatesFound is NOT a failure — they are safely stored in 5_Duplicate.
                result.Success = result.FailedExtractions == 0 && result.UnknownNameCount == 0;

                 LoggerService.LogInformation($"\n   Summary: {result.TotalExtracted} extracted, {result.UnknownNameCount} unknown names, {result.DuplicatesFound} duplicates, {result.FailedExtractions} failed");

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Bulk extraction failed: {ex.Message}";
                 LoggerService.LogInformation($"   ❌ {result.ErrorMessage}");
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
                 LoggerService.LogInformation($"\n📄 Extracting PDFs from bulk file (simple numbering)...");
                 LoggerService.LogInformation($"   Source: {Path.GetFileName(bulkPdfPath)}");
                 LoggerService.LogInformation($"   Output: {outputDirectory}");

                if (!File.Exists(bulkPdfPath))
                {
                    result.ErrorMessage = $"Bulk PDF not found: {bulkPdfPath}";
                     LoggerService.LogInformation($"   ❌ {result.ErrorMessage}");
                    return result;
                }

                Directory.CreateDirectory(outputDirectory);

                using var pdfDocument = PdfDocument.Open(bulkPdfPath);

                int totalPages = pdfDocument.NumberOfPages;
                int pagesPerConsent = _bulkConfig.PagesPerConsent;
                int startPage = _bulkConfig.StartPage;

                 LoggerService.LogInformation($"   Total pages: {totalPages}");
                 LoggerService.LogInformation($"   Pages per consent: {pagesPerConsent}");

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
                             LoggerService.LogInformation($"   📄 Extracted {extractedCount} PDFs...");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailedExtractions++;
                        result.ErrorMessages.Add($"Pages {currentPage}-{endPage}: {ex.Message}");
                         LoggerService.LogInformation($"   ⚠️  Failed to extract pages {currentPage}-{endPage}: {ex.Message}");
                    }

                    currentPage += pagesPerConsent;
                }

                result.TotalExtracted = extractedCount;
                result.Success = result.FailedExtractions == 0;

                 LoggerService.LogInformation($"\n   ✅ Extraction complete:");
                 LoggerService.LogInformation($"      Successfully extracted: {result.TotalExtracted}");
                 LoggerService.LogInformation($"      Failed: {result.FailedExtractions}");

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Bulk extraction failed: {ex.Message}";
                 LoggerService.LogInformation($"   ❌ {result.ErrorMessage}");
                return result;
            }
        }





        /// <summary>
        /// Returns (or creates) the duplicate subfolder for a given student.
        /// Path: 5_Duplicate\{LastName}_{FirstName}\
        /// All occurrences for the same student land in the same folder,
        /// making it trivial to merge them later into a single upload PDF.
        /// </summary>
        private string GetOrCreateDuplicateSubFolder(string lastName, string firstName)
        {
            // Sanitize so it is always a valid folder name
            string folderName = MakeSafeFileName($"{lastName}_{firstName}");
            string path = Path.Combine(_bulkConfig.GetDuplicateClientPath(), folderName);
            Directory.CreateDirectory(path);
            return path;
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

                // 4 FileRose Extraction — parent + scan subfolder only
                // (2_Output_Ready_FileRose removed — extracted files go to Phis\1_To_Upload\2 File Rose Upload)
                Directory.CreateDirectory(_bulkConfig.GetFileRosePath());
                Directory.CreateDirectory(_bulkConfig.GetFileRoseScanPath());
                Directory.CreateDirectory(_bulkConfig.GetFileRoseErrorPath());

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
                LoggerService.LogInformation($"⚠️  Warning: Could not create folder structure: {ex.Message}");
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

                string destinationPath = Path.Combine(archivePath, fileName);

                File.Move(sourcePath, destinationPath, overwrite: true);
                LoggerService.LogInformation($"   📦 Archived to 7_Archive/{sourceType}: {fileName}");
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"   ⚠️  Could not archive file: {ex.Message}");
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
                 LoggerService.LogInformation($"   ⚠️  Moved to 4_Error: {fileName}");
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Could not move to error folder: {ex.Message}");
            }
        }



        private void DisplayProcessingSummary(BulkExtractionResult result)
        {
            // ✅ Grand total = unique (3_Output_Ready) + all duplicates (5_Duplicate)
            int grandTotal = result.TotalExtracted + result.DuplicatesFound;

             LoggerService.LogInformation("\n" + new string('═', 60));
             LoggerService.LogInformation("📊 PROCESSING SUMMARY");
             LoggerService.LogInformation(new string('═', 60));
             LoggerService.LogInformation($"   Total pages processed    : {grandTotal}");
             LoggerService.LogInformation($"   ├── 3_Output_Ready       : {result.TotalExtracted}  (unique, ready to upload)");
             LoggerService.LogInformation($"   └── 5_Duplicate          : {result.DuplicatesFound}  (all copies, needs review)");
            if (result.UnknownNameCount > 0)
                 LoggerService.LogInformation($"   4_Error (unknown names)  : {result.UnknownNameCount}  ⚠️  manual review required");
            if (result.FailedExtractions > 0)
                 LoggerService.LogInformation($"   Failed                   : {result.FailedExtractions}  ❌");
             LoggerService.LogInformation($"   Status                   : {(result.Success ? "✅ Success" : "⚠️  Needs Review")}");
             LoggerService.LogInformation(new string('═', 60));

             LoggerService.LogInformation($"\n📁 Output Locations:");
             LoggerService.LogInformation($"   3_Output_Ready : {_bulkConfig.GetOutputReadyPath()}");
             LoggerService.LogInformation($"   5_Duplicate    : {_bulkConfig.GetDuplicateClientPath()}");
             LoggerService.LogInformation($"   4_Error        : {_bulkConfig.GetErrorPath()}");

            if (result.UnknownNameCount > 0 || result.DuplicatesFound > 0)
            {
                 LoggerService.LogInformation($"\n⚠️  MANUAL REVIEW REQUIRED:");

                if (result.UnknownNameCount > 0)
                     LoggerService.LogInformation($"   • {result.UnknownNameCount} file(s) with unknown names in 4_Error → identify and rename");

                if (result.DuplicatesFound > 0)
                     LoggerService.LogInformation($"   • {result.DuplicatesFound} file(s) in 5_Duplicate → review HOW_TO_MERGE.txt in each subfolder");
            }

            if (result.Success)
                 LoggerService.LogInformation($"\n✅ All pages processed — ready for Phase 3!");
            else
                 LoggerService.LogInformation($"\n⚠️  Review required before proceeding to Phase 3.");
        }


        #endregion  File Management




        #region Helper Methods


        /// <summary>
        /// Format filename: {ID}_{LastName}_{FirstName}_consent.pdf
        /// Spaces within name parts are kept as-is — they are valid in Windows filenames
        /// and '_' is the sole field delimiter (names never contain underscores).
        /// Genuine hyphens (De-Cruz, Jean-Pierre) are preserved untouched.
        /// Examples:
        ///   Larochelle / Ève              →  205_Larochelle_Ève_consent.pdf
        ///   De Cruz / Marie Anne          →  1_De Cruz_Marie Anne_consent.pdf
        ///   De-Cruz / Jean-Pierre         →  3_De-Cruz_Jean-Pierre_consent.pdf
        ///   Hoosdally / Mohammad Jaabir   →  1_Hoosdally_Mohammad Jaabir_consent.pdf
        ///   Romo Lerma / Angel Javier     →  92_Romo Lerma_Angel Javier_consent.pdf
        /// </summary>
        private string FormatFileName(int id, string lastName, string firstName, int? duplicateSuffix = null)
        {
            string suffix = _bulkConfig.ConsentSuffix;

            // ✅ No encoding needed — spaces are valid in Windows filenames and '_' is
            //    the only field separator.  Names never contain underscores.
            string baseName = duplicateSuffix.HasValue
                ? $"{id}_{lastName}_{firstName}_{duplicateSuffix}_{suffix}"
                : $"{id}_{lastName}_{firstName}_{suffix}";

            return MakeSafeFileName(baseName + ".pdf");
        }


        /// <summary>
        /// Capitalizes a name, processing each space-separated word independently so that
        /// mixed names like "Bouibaoune-Sayf-Eddine Sayf" are NOT collapsed to
        /// "Bouibaoune-Sayf-Eddine-Sayf".  Genuine hyphens within a single token
        /// (e.g. "Jean-Pierre", "De-Cruz") are preserved.
        /// </summary>
        private string CleanAndCapitalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // Normalize whitespace
            name = Regex.Replace(name.Trim(), @"\s+", " ");

            // ✅ Split on spaces FIRST so each word is capitalized independently.
            //    Old code: split on both ' ' and '-' then re-join with '-' if ANY hyphen
            //    existed → "Bouibaoune-Sayf-Eddine Sayf" became "Bouibaoune-Sayf-Eddine-Sayf".
            var spaceParts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var processed = spaceParts.Select(word =>
            {
                // Remove characters that are invalid in names (keep letters, digits, hyphens, apostrophes)
                word = Regex.Replace(word, @"[^\w\-'']", "");
                if (string.IsNullOrEmpty(word)) return string.Empty;

                if (word.Contains('-'))
                {
                    // Capitalize each hyphenated segment, re-join with the hyphen
                    return string.Join("-",
                        word.Split('-', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Length <= 1
                                ? p.ToUpperInvariant()
                                : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
                }

                return word.Length <= 1
                    ? word.ToUpperInvariant()
                    : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
            });

            return string.Join(" ", processed.Where(w => !string.IsNullOrEmpty(w)));
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

✅ Phase 3 will use these files to upload to PHIS.
   Do not delete or move files from here manually!
");

                // ── 4 FileRose Extraction / 1_Scan_FileRose ─────────────────────────────
                var fileRoseScanReadme = Path.Combine(_bulkConfig.GetFileRoseScanPath(), "README.txt");
                if (!File.Exists(fileRoseScanReadme))
                    File.WriteAllText(fileRoseScanReadme,
@"📁 1_Scan_FileRose - Place File Rose Scans Here
================================================

Place all scanned File Rose (feuille rose) documents in this folder.

Naming convention:
  Each File Rose MUST be saved as:  <ClientID>.pdf
  Example: 106467.pdf

The system will:
✓ Read the Client ID from the filename
✓ Match it to a validated record in Validation_Results.csv
✓ Rename to: <ClientID>_suiviscolaire_<SchoolYear>.pdf
✓ Move the renamed file directly to:
    Phis\1_To_Upload\2 File Rose Upload\
✓ Set IsFileRoseDefault=True and IsFileRoseExtracted=True in Validation_Results.csv

Requirements:
  - One PDF per client
  - Filename must be exactly the Client ID (digits only), e.g. 106467.pdf
  - The ClientId must exist in Validation_Results.csv with ClientIdStatus=Found

Files with invalid names → left in this folder with an error summary written to:
    3_Error_FileRose_Extraction\_extraction_errors.txt
");

                // ── 4 FileRose Extraction / 3_Error_FileRose_Extraction ─────────────────
                var fileRoseErrorReadme = Path.Combine(_bulkConfig.GetFileRoseErrorPath(), "README.txt");
                if (!File.Exists(fileRoseErrorReadme))
                    File.WriteAllText(fileRoseErrorReadme,
@"📁 3_Error_FileRose_Extraction - FileRose Error Summary
========================================================

This folder contains only the error summary text file.
No PDFs are moved here — files with errors stay in 1_Scan_FileRose.

Review _extraction_errors.txt, fix each filename in 1_Scan_FileRose,
then click 'Generate Upload CSV' again.
");

                // ── 5_Duplicate ─────────────────────────────────────────────────────────
                var duplicateReadme = Path.Combine(_bulkConfig.GetDuplicateClientPath(), "README.txt");
                if (!File.Exists(duplicateReadme))
                    File.WriteAllText(duplicateReadme,
@"📁 5_DUPLICATE - Duplicate Client ID PDFs
==========================================

This folder contains PDFs where the same student name appeared more than once
during bulk extraction.

What to do:
1. Review each subfolder and its HOW_TO_MERGE.txt instructions.
2. Keep the best copy and move it to 3_Output_Ready.
3. Files here will NOT be uploaded to PHIS until moved to 3_Output_Ready.
");

                // ── 6_Error ─────────────────────────────────────────────────────────────
                var errorReadme = Path.Combine(_bulkConfig.GetErrorPath(), "README.txt");
                if (!File.Exists(errorReadme))
                    File.WriteAllText(errorReadme,
@"📁 6_ERROR - Failed Processing or Unknown Students
===================================================

This folder contains PDFs that failed processing or could not be identified.

What to do:
1. Review the error log files (*_ERROR_*.txt) for details.
2. Re-scan at higher resolution (300 DPI) if quality is poor.
3. Rename manually if you can identify the student, then move to the
   appropriate input folder for reprocessing.
");

                // ── 7_Archive ───────────────────────────────────────────────────────────
                var archiveReadme = Path.Combine(_bulkConfig.GetArchivePath(), "README.txt");
                if (!File.Exists(archiveReadme))
                    File.WriteAllText(archiveReadme,
@"📁 7_ARCHIVE - Successfully Processed Original Files
=====================================================

Structure:
  📂 Bulk\      - Original bulk PDF files from Vitalite
  📂 Scanned\   - Original scanned consent forms
  📂 FileRose\  - Original scanned File Rose (feuille rose) documents

You can safely delete old archives after Phase 3 is complete and verified.
Recommended: Keep for at least one school year.
");
            }
            catch (Exception ex)
            {
                // Silently fail — README files are nice-to-have, not critical
                LoggerService.LogInformation($"⚠️  Could not create README files: {ex.Message}");
            }
        }



    }
}
