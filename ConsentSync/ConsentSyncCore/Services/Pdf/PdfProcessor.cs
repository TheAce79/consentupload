using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tesseract;
using UglyToad.PdfPig;

namespace ConsentSyncCore.Services.Pdf
{
    public class PdfProcessor
    {
        private readonly PdfExtractionConfig _config;


        // ✅ Register Windows-1252 and other legacy encodings for PdfPig
        static PdfProcessor()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public PdfProcessor(IConfiguration? config = null)
        {
            var configuration = config ?? ConfigurationService.GetConfiguration();
            _config = ConfigurationService.GetPdfExtractionConfig();
        }



        #region Public API



        /// <summary>
        /// Process a single PDF and extract first name, last name, and page count
        /// Returns: (firstName, lastName, pageCount)
        /// </summary>
        public static (string firstName, string lastName, int pageCount) ProcessSinglePdf(
            string pdfFilePath,
            bool debugOcr = true,
            string? debugOutputDir = null)
        {
            if (string.IsNullOrEmpty(pdfFilePath))
            {
                throw new ArgumentException("PDF file path cannot be null or empty.", nameof(pdfFilePath));
            }

            if (!File.Exists(pdfFilePath))
            {
                throw new FileNotFoundException($"PDF file not found: {pdfFilePath}");
            }

            string fileName = Path.GetFileNameWithoutExtension(pdfFilePath);
            var config = ConfigurationService.GetPdfExtractionConfig();

            // Use provided debug directory or create temp folder
            string debugFolder = debugOutputDir ?? Path.Combine(Path.GetTempPath(), "PdfProcessor_Debug");
            if (debugOcr && !Directory.Exists(debugFolder))
            {
                Directory.CreateDirectory(debugFolder);
            }

            try
            {
                LoggerService.LogInformation($"\n--- Processing: {fileName} ---");

                using var document = PdfDocument.Open(pdfFilePath);

                if (document.NumberOfPages < 1)
                {
                    LoggerService.LogInformation("  No pages found in PDF");
                    return ("Unknown", "Unknown", 0);
                }

                var page = document.GetPage(1);
                var words = page.GetWords().ToList();
                int pageCount = document.NumberOfPages;

                LoggerService.LogInformation($"  Total words found via PdfPig: {words.Count}");

                string? firstName = null;
                string? lastName = null;

                // Try text extraction first (faster)
                if (words.Count == 0)
                {
                    LoggerService.LogInformation("  PDF is scanned - using OCR extraction...");

                    // ── Pass 1: Full Page OCR ──
                    var ocrText = ExtractTextWithOCR(pdfFilePath, 1, fileName, debugOcr, debugFolder, config, false);

                    if (!string.IsNullOrEmpty(ocrText))
                    {
                        LoggerService.LogInformation($"  OCR extracted {ocrText.Length} characters");
                        if (debugOcr) File.WriteAllText(Path.Combine(debugFolder, $"OCR_DEBUG_{fileName}.txt"), ocrText);

                        (firstName, lastName) = ExtractNamesFromOCRText(ocrText, config);
                    }

                    // ── Pass 2: Fallback (Crop to Name Area) ──
                    if (firstName == null || lastName == null)
                    {
                        LoggerService.LogInformation("  Names not found on full page. Retrying with cropped name area...");

                        var croppedOcrText = ExtractTextWithOCR(pdfFilePath, 1, fileName, debugOcr, debugFolder, config, true);

                        if (!string.IsNullOrEmpty(croppedOcrText))
                        {
                            LoggerService.LogInformation($"  Cropped OCR extracted {croppedOcrText.Length} characters");
                            if (debugOcr) File.WriteAllText(Path.Combine(debugFolder, $"OCR_DEBUG_{fileName}_CROPPED.txt"), croppedOcrText);

                            var (cropFirst, cropLast) = ExtractNamesFromOCRText(croppedOcrText, config);

                            // Only overwrite if it actually found something
                            if (cropLast != null) lastName = cropLast;
                            if (cropFirst != null) firstName = cropFirst;
                        }
                    }
                }
                else
                {
                    LoggerService.LogInformation("  PDF has extractable text - using direct extraction");
                    (firstName, lastName) = ExtractNamesFromWords(words, config);
                }

                LoggerService.LogInformation($"  Final Result: {firstName ?? "Unknown"} {lastName ?? "Unknown"} | Pages: {pageCount}");

                return (firstName ?? "Unknown", lastName ?? "Unknown", pageCount);
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"  ERROR processing {fileName}: {ex.Message}");
                return ("Error", "Error", 0);
            }
        }



        /// <summary>
        /// Process a single scanned PDF using OCR and extract first name, last name, date of birth, and page count
        /// Returns: (firstName, lastName, dateOfBirth, pageCount)
        /// </summary>
        public static (string firstName, string lastName, string dateOfBirth, int pageCount) ProcessSingleScannedPdf(
            string pdfFilePath,
            bool debugOcr = true,
            string? debugOutputDir = null)
        {
            if (string.IsNullOrEmpty(pdfFilePath))
            {
                throw new ArgumentException("PDF file path cannot be null or empty.", nameof(pdfFilePath));
            }

            if (!File.Exists(pdfFilePath))
            {
                throw new FileNotFoundException($"PDF file not found: {pdfFilePath}");
            }

            string fileName = Path.GetFileNameWithoutExtension(pdfFilePath);
            var config = ConfigurationService.GetPdfExtractionConfig();

            // Use provided debug directory or create temp folder
            string debugFolder = debugOutputDir ?? Path.Combine(Path.GetTempPath(), "PdfProcessor_Debug");
            if (debugOcr && !Directory.Exists(debugFolder))
            {
                Directory.CreateDirectory(debugFolder);
            }

            try
            {
                LoggerService.LogInformation($"\n--- Processing Scanned PDF: {fileName} ---");

                using var document = PdfDocument.Open(pdfFilePath);

                int pageCount = document.NumberOfPages;
                if (pageCount < 1)
                {
                    LoggerService.LogInformation("  No pages found in PDF");
                    return ("Unknown", "Unknown", "Unknown", 0);
                }

                LoggerService.LogInformation("  Starting OCR extraction...");

                string? firstName = null;
                string? lastName = null;
                string? dateOfBirth = null;

                // ── Full Page OCR ──
                var ocrText = ExtractTextWithOCR(pdfFilePath, 1, fileName, debugOcr, debugFolder, config, false);

                if (!string.IsNullOrEmpty(ocrText))
                {
                    LoggerService.LogInformation($"  OCR extracted {ocrText.Length} characters");
                    if (debugOcr) File.WriteAllText(Path.Combine(debugFolder, $"OCR_DEBUG_{fileName}.txt"), ocrText);

                    // Extract DOB using the specific Regex/Encoding handler
                    (_, _, dateOfBirth) = ExtractDetailsFromOCRText(ocrText);

                    // Extract Names using pattern matching logic
                    (firstName, lastName) = ExtractNamesFromOCRText(ocrText, config);
                }

                // ── Pass 2: Fallback (Crop to Name Area) ──
                if (firstName == null || lastName == null)
                {
                    LoggerService.LogInformation("  Names not found on full page. Retrying with cropped name area...");

                    var croppedOcrText = ExtractTextWithOCR(pdfFilePath, 1, fileName, debugOcr, debugFolder, config, true);

                    if (!string.IsNullOrEmpty(croppedOcrText))
                    {
                        LoggerService.LogInformation($"  Cropped OCR extracted {croppedOcrText.Length} characters");
                        if (debugOcr) File.WriteAllText(Path.Combine(debugFolder, $"OCR_DEBUG_{fileName}_CROPPED.txt"), croppedOcrText);

                        var (_, _, cropDob) = ExtractDetailsFromOCRText(croppedOcrText);
                        var (cropFirst, cropLast) = ExtractNamesFromOCRText(croppedOcrText, config);

                        // Only overwrite if it actually found something
                        if (cropLast != null) lastName = cropLast;
                        if (cropFirst != null) firstName = cropFirst;
                        if (dateOfBirth == null) dateOfBirth = cropDob;
                    }
                }

                LoggerService.LogInformation($"  Final Result: {firstName ?? "Unknown"} {lastName ?? "Unknown"} | DOB: {dateOfBirth ?? "Unknown"} | Pages: {pageCount}");

                return (firstName ?? "Unknown", lastName ?? "Unknown", dateOfBirth ?? "Unknown", pageCount);
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"  ERROR processing {fileName}: {ex.Message}");
                return ("Error", "Error", "Error", 0);
            }
        }



        /// <summary>
        /// Process multiple PDFs in a directory
        /// Returns dictionary: filename -> "FirstName LastName|PageCount"
        /// </summary>
        public static Dictionary<string, string> GetDirectoryPdfInfo(
            string sourceDir,
            bool debugOcr = false,
            string? debugOutputDir = null)
        {
            var results = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                 LoggerService.LogInformation("Source directory is invalid or doesn't exist.");
                return results;
            }

            var files = Directory.GetFiles(sourceDir, "*.pdf");
             LoggerService.LogInformation($"Found {files.Length} PDF files to process.\n");

            foreach (var filePath in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                try
                {
                    var (firstName, lastName, pageCount) = ProcessSinglePdf(filePath, debugOcr, debugOutputDir);
                    string nameInfo = $"{firstName} {lastName}|{pageCount}";
                    results.Add(fileName, nameInfo);
                }
                catch (Exception ex)
                {
                     LoggerService.LogInformation($"  ERROR processing {fileName}: {ex.Message}");
                    results.Add(fileName, $"Error Error|0");
                }
            }

            return results;
        }

        #endregion

        #region Text Extraction from PDF Words

        /// <summary>
        /// Extract names from PDF text words using pattern matching
        /// </summary>
        /// 




        private static (string? firstName, string? lastName) ExtractNamesFromWords(
              List<UglyToad.PdfPig.Content.Word> words,
              PdfExtractionConfig config)
        {
            string? firstName = null;
            string? lastName = null;
            int lastNameEndIndex = -1;
            int firstNameEndIndex = -1;

            try
            {
                LoggerService.LogInformation($"  Starting word-based extraction with {words.Count} words");

                var lastNamePatterns = config.LastNamePatterns;
                var firstNamePatterns = config.FirstNamePatterns;
                var preferredNamePatterns = config.PreferredNamePatterns;

                // ── STRATEGY 1: Pattern matching ──────────────────────────────
                for (int i = 0; i < words.Count; i++)
                {
                    if (lastName == null)
                    {
                        foreach (var pattern in lastNamePatterns)
                        {
                            if (MatchesPattern(words, i, pattern.Words))
                            {
                                LoggerService.LogInformation($"  Found last name pattern ({pattern.Language}) at index {i}");
                                int startIndex = i + pattern.Words.Length;
                                int maxIndex = startIndex + config.SearchRange - 1;

                                for (int j = startIndex; j < words.Count && j <= maxIndex; j++)
                                {
                                    string candidateWord = words[j].Text
                                        .Normalize(NormalizationForm.FormC)
                                        .Trim();

                                    LoggerService.LogInformation($"    Checking word at [{j}]: '{candidateWord}'");

                                    if (IsValidNameCandidate(candidateWord, config))
                                    {
                                        var (name, lastIdx) = CollectNameWords(words, j, maxIndex, -1, config);
                                        lastName = name;
                                        lastNameEndIndex = lastIdx;
                                        LoggerService.LogInformation($"  ✅ Last Name: {lastName} (indices {j}–{lastIdx})");
                                        break;
                                    }
                                }

                                if (lastName != null) break;
                            }
                        }
                    }

                    if (firstName == null)
                    {
                        bool isPreferredName = preferredNamePatterns.Any(p => MatchesPattern(words, i, p.Words));

                        if (!isPreferredName)
                        {
                            foreach (var pattern in firstNamePatterns)
                            {
                                if (MatchesPattern(words, i, pattern.Words))
                                {
                                    LoggerService.LogInformation($"  Found first name pattern ({pattern.Language}) at index {i}");
                                    int patternStart = i + pattern.Words.Length;
                                    int maxIndex = patternStart + config.SearchRange - 1;

                                    // ✅ Always start AFTER the full lastName range so we never
                                    //    re-collect lastName words as firstName.
                                    //    On the Vitalité form the value row is:
                                    //      [lastName words] [firstName words] [preferred words]
                                    //    Without this, firstName search restarts from the same
                                    //    value row position and duplicates the lastName.
                                    int fnSearchFrom = lastNameEndIndex >= 0
                                        ? Math.Max(patternStart, lastNameEndIndex + 1)
                                        : patternStart;

                                    for (int j = fnSearchFrom; j < words.Count && j <= maxIndex; j++)
                                    {
                                        string candidateWord = words[j].Text
                                            .Normalize(NormalizationForm.FormC)
                                            .Trim();

                                        LoggerService.LogInformation($"    Checking word at [{j}]: '{candidateWord}'");

                                        if (IsValidNameCandidate(candidateWord, config))
                                        {
                                            var (name, lastIdx) = CollectNameWords(words, j, maxIndex, lastNameEndIndex, config);
                                            firstName = name;
                                            firstNameEndIndex = lastIdx;
                                            LoggerService.LogInformation($"  ✅ First Name: {firstName} (indices {j}–{lastIdx})");
                                            break;
                                        }
                                    }

                                    if (firstName != null) break;
                                }
                            }
                        }
                    }

                    if (lastName != null && firstName != null)
                    {
                        LoggerService.LogInformation($"  Both names found - stopping search");
                        break;
                    }
                }

                // ── STRATEGY 2: Fallback keyword matching ─────────────────────
                if (lastName == null || firstName == null)
                {
                    LoggerService.LogInformation($"  Pattern matching incomplete, trying keyword fallback...");

                    for (int i = 0; i < words.Count - 1; i++)
                    {
                        string currentWord = words[i].Text.Normalize(NormalizationForm.FormC);

                        if (lastName == null && ContainsAnyKeyword(currentWord, config.LastNameKeywords))
                        {
                            int maxIndex = i + config.SearchRange;
                            for (int j = i + 1; j < words.Count && j <= maxIndex; j++)
                            {
                                string candidateWord = words[j].Text
                                    .Normalize(NormalizationForm.FormC)
                                    .Trim();

                                LoggerService.LogInformation($"    Checking word at [{j}]: '{candidateWord}'");

                                if (IsValidNameCandidate(candidateWord, config))
                                {
                                    var (name, lastIdx) = CollectNameWords(words, j, maxIndex, -1, config);
                                    lastName = name;
                                    lastNameEndIndex = lastIdx;
                                    LoggerService.LogInformation($"  -> Last Name: {lastName} (indices {j}–{lastIdx})");
                                    break;
                                }
                            }
                        }

                        if (firstName == null && ContainsAnyKeyword(currentWord, config.FirstNameKeywords))
                        {
                            if (i + 1 < words.Count && ContainsAnyKeyword(
                                    words[i + 1].Text.Normalize(NormalizationForm.FormC),
                                    new[] { "PRÉFÉRÉ", "PREFERRED" }))
                            {
                                continue;
                            }

                            int maxIndex = i + config.SearchRange;

                            // ✅ Start AFTER lastName range here too
                            int fnSearchFrom = lastNameEndIndex >= 0
                                ? Math.Max(i + 1, lastNameEndIndex + 1)
                                : i + 1;

                            for (int j = fnSearchFrom; j < words.Count && j <= maxIndex; j++)
                            {
                                if (j == lastNameEndIndex) continue;

                                string candidateWord = words[j].Text
                                    .Normalize(NormalizationForm.FormC)
                                    .Trim();

                                LoggerService.LogInformation($"    Checking word at [{j}]: '{candidateWord}'");

                                if (IsValidNameCandidate(candidateWord, config))
                                {
                                    var (name, lastIdx) = CollectNameWords(words, j, maxIndex, lastNameEndIndex, config);
                                    firstName = name;
                                    firstNameEndIndex = lastIdx;
                                    LoggerService.LogInformation($"  -> First Name: {firstName} (indices {j}–{lastIdx})");
                                    break;
                                }
                            }
                        }

                        if (lastName != null && firstName != null) break;
                    }
                }

                LoggerService.LogInformation($"  Extraction complete: FirstName={firstName ?? "NULL"}, LastName={lastName ?? "NULL"}");
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"  ❌ ERROR: {ex.Message}");
            }

            return (firstName, lastName);
        }






        #endregion

        #region OCR Extraction

        /// <summary>
        /// Extract text using OCR for scanned PDFs
        /// </summary>
        private static string ExtractTextWithOCRPSM4(
            string pdfPath,
            int pageNumber,
            string fileName,
            bool saveDebugImage,
            string debugOutputDir,
            PdfExtractionConfig config)
        {
            string tempImagePath = string.Empty;

            try
            {
                var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

                if (!Directory.Exists(tessDataPath))
                {
                     LoggerService.LogInformation($"    ERROR: tessdata not found at: {tessDataPath}");
                    return string.Empty;
                }

                 LoggerService.LogInformation($"    Converting PDF page {pageNumber} to image...");
                tempImagePath = ConvertPdfPageToImage(pdfPath, pageNumber, fileName, saveDebugImage, true, debugOutputDir);

                if (string.IsNullOrEmpty(tempImagePath) || !File.Exists(tempImagePath))
                {
                     LoggerService.LogInformation("    Failed to convert PDF to image");
                    return string.Empty;
                }

                 LoggerService.LogInformation($"    Running OCR...");

                using var engine = new TesseractEngine(tessDataPath, "fra+eng", EngineMode.Default);
                using var img = Pix.LoadFromFile(tempImagePath);
                using var result = engine.Process(img);

                var text = result.GetText();
                var confidence = result.GetMeanConfidence();

                 LoggerService.LogInformation($"    OCR Confidence: {confidence:P}");

                return text;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"    OCR Error: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                if (!saveDebugImage && !string.IsNullOrEmpty(tempImagePath) && File.Exists(tempImagePath))
                {
                    try { File.Delete(tempImagePath); } catch { }
                }
            }
        }







        /// <summary>
        /// Extract text using OCR for scanned PDFs
        /// </summary>
        private static string ExtractTextWithOCR(
            string pdfPath,
            int pageNumber,
            string fileName,
            bool saveDebugImage,
            string debugOutputDir,
            PdfExtractionConfig config,
            bool cropNameArea = false)
        {
            string tempImagePath = string.Empty;

            try
            {
                var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

                if (!Directory.Exists(tessDataPath))
                {
                    LoggerService.LogInformation($"    ERROR: tessdata not found at: {tessDataPath}");
                    return string.Empty;
                }

                LoggerService.LogInformation($"    Converting PDF page {pageNumber} to image...");
                tempImagePath = ConvertPdfPageToImage(pdfPath, pageNumber, fileName, saveDebugImage, true, debugOutputDir, cropNameArea);

                if (string.IsNullOrEmpty(tempImagePath) || !File.Exists(tempImagePath))
                {
                    LoggerService.LogInformation("    Failed to convert PDF to image");
                    return string.Empty;
                }

                LoggerService.LogInformation($"    Running OCR...");

                using var engine = new TesseractEngine(tessDataPath, "fra+eng", EngineMode.Default);

                // When cropped, PSM 4 (Assume a single column of text of variable sizes) 
                // or PSM 6 often does better at reading trapped table text.
                engine.SetVariable("tessedit_pageseg_mode", cropNameArea ? "4" : "6");

                using var img = Pix.LoadFromFile(tempImagePath);
                using var result = engine.Process(img);

                var text = result.GetText();
                var confidence = result.GetMeanConfidence();

                LoggerService.LogInformation($"    OCR Confidence: {confidence:P}");

                return text;
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"    OCR Error: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                if (!saveDebugImage && !string.IsNullOrEmpty(tempImagePath) && File.Exists(tempImagePath))
                {
                    try { File.Delete(tempImagePath); } catch { }
                }
            }
        }



        /// <summary>
        /// Convert PDF page to image for OCR
        /// </summary>
        private static string ConvertPdfPageToImage(
            string pdfPath,
            int pageNumber,
            string fileName,
            bool saveForDebug,
            bool detectOrientation,
            string? debugOutputDir,
            bool cropNameArea = false)
        {
            try
            {
                using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(2160, 3840));

                if (docReader.GetPageCount() < pageNumber)
                {
                    return string.Empty;
                }

                using var pageReader = docReader.GetPageReader(pageNumber - 1);
                var rawBytes = pageReader.GetImage();
                var width = pageReader.GetPageWidth();
                var height = pageReader.GetPageHeight();

                using var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height);

                if (detectOrientation)
                {
                    int rotationNeeded = DetectOrientation(image);
                    if (rotationNeeded > 0)
                    {
                        LoggerService.LogInformation($"    Rotating {rotationNeeded}°");
                        image.Mutate(x => x.Rotate(rotationNeeded));
                    }
                }

                // ✅ Fallback: crop to the top horizontal band where the name table is
                if (cropNameArea)
                {
                    // Crop the specific horizontal region containing the "REINSEIGNEMENTS PERSONNELS" block.
                    // Assuming the student name is roughly between 10% and 35% down the page height.
                    int startY = (int)(image.Height * 0.10);
                    int cropHeight = (int)(image.Height * 0.25);
                    image.Mutate(x => x.Crop(new Rectangle(0, startY, image.Width, cropHeight)));
                    LoggerService.LogInformation($"    Cropping image to name area (Y: {startY} to {startY + cropHeight})");
                }

                string tempPath;
                if (saveForDebug)
                {
                    var outputDir = debugOutputDir ?? Path.GetDirectoryName(pdfPath)!;
                    string suffix = cropNameArea ? "_CROPPED" : "";
                    tempPath = Path.Combine(outputDir, $"DEBUG_IMAGE_{fileName}{suffix}.png");
                    LoggerService.LogInformation($"    Debug image: {tempPath}");
                }
                else
                {
                    tempPath = Path.Combine(Path.GetTempPath(), $"pdf_page_{Guid.NewGuid()}.png");
                }

                image.SaveAsPng(tempPath);
                return tempPath;
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"    Error converting PDF: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Detect PDF orientation using OCR
        /// </summary>
        private static int DetectOrientation(Image<Bgra32> image)
        {
            try
            {
                var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

                using var engine = new TesseractEngine(tessDataPath, "fra+eng", EngineMode.Default);

                int[] rotations = { 0, 90, 180, 270 };
                int bestRotation = 0;
                int bestScore = 0;

                foreach (int rotation in rotations)
                {
                    using var testImage = image.Clone();

                    if (rotation > 0)
                    {
                        testImage.Mutate(x => x.Rotate(rotation));
                    }

                    int cropHeight = (int)(testImage.Height * 0.3);
                    using var croppedImage = testImage.Clone(ctx => ctx.Crop(new Rectangle(0, 0, testImage.Width, cropHeight)));

                    var testPath = Path.Combine(Path.GetTempPath(), $"orientation_test_{Guid.NewGuid()}.png");
                    croppedImage.SaveAsPng(testPath);

                    try
                    {
                        using var img = Pix.LoadFromFile(testPath);
                        using var result = engine.Process(img);

                        var text = result.GetText().ToLower();
                        var confidence = result.GetMeanConfidence();

                        int score = 0;
                        if (text.Contains("vitalit")) score += 50;
                        if (text.Contains("réseau") || text.Contains("reseau")) score += 30;
                        if (text.Contains("santé") || text.Contains("sante")) score += 30;
                        if (text.Contains("section")) score += 40;
                        score += (int)(confidence * 10);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestRotation = rotation;
                        }
                    }
                    finally
                    {
                        try { File.Delete(testPath); } catch { }
                    }
                }

                return bestRotation;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"    Orientation detection failed: {ex.Message}");
                return 180; // Default fallback
            }
        }


        /// <summary>
        /// Extract names from OCR text
        /// </summary>
        private static (string? firstName, string? lastName) ExtractNamesFromOCRText(
            string ocrText,
            PdfExtractionConfig config)
        {
            string? firstName = null;
            string? lastName = null;

            var lines = ocrText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            LoggerService.LogInformation($"    Parsing {lines.Length} lines from OCR...");

            string[] skipWords = config.FieldLabelWords
                .Concat(new[] { "STUDENT", "STUDENTS", "ÉLÈVE", "ELEVE", "SIGNATURE" })
                .ToArray();

            // ── Priority 1: "NOM DE L'ÉLÈVE" / "STUDENT'S NAME" combined header ──────
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                bool hasFrenchPattern = line.Contains("NOM DE L", StringComparison.OrdinalIgnoreCase) &&
                                        line.Contains("ÉLÈVE", StringComparison.OrdinalIgnoreCase);
                bool hasEnglishPattern = line.Contains("STUDENT", StringComparison.OrdinalIgnoreCase) &&
                                         line.Contains("NAME", StringComparison.OrdinalIgnoreCase);

                if ((hasFrenchPattern || hasEnglishPattern) &&
                    !line.Contains("PRÉFÉRÉ", StringComparison.OrdinalIgnoreCase))
                {
                    LoggerService.LogInformation($"    Found student name header in line {i}");

                    string cleanedLine = System.Text.RegularExpressions.Regex.Replace(
                        line, @"NOM\s+DE\s+L.?[ÉE]L[ÈE]VE|STUDENT.?S?\s+NAME", "",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    var validNames = cleanedLine
                        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => IsValidNameCandidate(w, config) && !IsSkipWord(w, skipWords))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (validNames.Count >= 2)
                    {
                        firstName = validNames[0];
                        lastName = validNames[1];
                        LoggerService.LogInformation($"      ✅ Found (P1): {firstName} {lastName}");
                        return (firstName, lastName);
                    }
                }
            }

            // ── Priority 2: Look after "NOM DE FAMILLE" label using leading-token extraction ──
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                bool hasLastNameLabel =
                    line.Contains("NOM DE FAMILLE", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("LAST NAME", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("SURNAME", StringComparison.OrdinalIgnoreCase);

                if (!hasLastNameLabel) continue;

                LoggerService.LogInformation($"    Found NOM DE FAMILLE label in line {i}: \"{line}\"");

                for (int j = i + 1; j < lines.Length && j <= i + 15; j++)
                {
                    string valueLine = lines[j].Trim();
                    if (string.IsNullOrWhiteSpace(valueLine)) continue;

                    var leading = ExtractLeadingNameTokens(valueLine, config, skipWords);

                    // ✅ GUARD against OCR noise: a valid name sequence MUST contain 
                    //    at least one token longer than 2 characters.
                    if (leading.Count > 0 && !leading.Any(t => t.Length > 2))
                    {
                        LoggerService.LogInformation($"    Skipping noise tokens '{string.Join(" ", leading)}' on line {j}");
                        continue;
                    }

                    if (leading.Count >= 1)
                    {
                        lastName = leading[0];
                        firstName = leading.Count >= 2 ? leading[1] : null;
                        LoggerService.LogInformation($"      ✅ Last name  (P2): {lastName}  ← line {j}: \"{valueLine}\"");
                        if (firstName != null)
                            LoggerService.LogInformation($"      ✅ First name (P2): {firstName}");
                        break;
                    }

                    LoggerService.LogInformation($"    Skipping line {j} (no leading names): \"{valueLine}\"");
                }

                if (lastName != null) break;
            }

            // ── Priority 2.5: Pure-name-line scan over the entire document ────────────
            if (lastName == null)
            {
                LoggerService.LogInformation($"    P2 failed — scanning all lines for pure name line...");

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (IsContactOrDataRow(line)) continue;

                    var tokens = line.Split(new[] { ' ', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length < 1 || tokens.Length > 3) continue;

                    var nameTokens = tokens
                        .Where(t => IsValidNameCandidate(t, config) && !IsSkipWord(t, skipWords))
                        .ToList();

                    if (nameTokens.Count != tokens.Length) continue;

                    // ✅ A pure name line must contain at least one real word (> 2 chars)
                    if (!nameTokens.Any(t => t.Length > 2)) continue;

                    bool hasFormIndicator = nameTokens.Any(t =>
                        _formLabelIndicators.Any(kw =>
                            t.ToUpperInvariant().Contains(kw, StringComparison.OrdinalIgnoreCase)));

                    if (hasFormIndicator) continue;

                    lastName = nameTokens[0];
                    firstName = nameTokens.Count >= 2 ? nameTokens[1] : null;

                    LoggerService.LogInformation($"      ✅ Last name  (P2.5, pure line): {lastName}  ← line {i}: \"{line}\"");
                    if (firstName != null)
                        LoggerService.LogInformation($"      ✅ First name (P2.5, pure line): {firstName}");
                    break;
                }
            }

            // ── Priority 3: Dedicated PRÉNOM label scan (fills missing first name) ────
            if (firstName == null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();

                    bool hasFirstNameLabel =
                        (line.Contains("PRÉNOM", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("PRENOM", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("FIRST NAME", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("GIVEN NAME", StringComparison.OrdinalIgnoreCase)) &&
                        !line.Contains("PRÉFÉRÉ", StringComparison.OrdinalIgnoreCase) &&
                        !line.Contains("PREFERRED", StringComparison.OrdinalIgnoreCase);

                    if (!hasFirstNameLabel) continue;

                    LoggerService.LogInformation($"    Found PRÉNOM label in line {i}: \"{line}\"");

                    string inlineValue = System.Text.RegularExpressions.Regex.Replace(
                        line,
                        @"PR[ÉE]NOM(\s+PR[ÉE]F[ÉE]R[ÉE])?|FIRST\s+NAME|GIVEN\s+NAME|NOM\s+DE\s+FAMILLE|LAST\s+NAME",
                        " ",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

                    var inlineTokens = inlineValue
                        .Split(new[] { ' ', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => IsValidNameCandidate(w, config) && !IsSkipWord(w, skipWords))
                        .Where(t => lastName == null || !t.Equals(lastName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (inlineTokens.Count >= 1)
                    {
                        firstName = inlineTokens[0];
                        LoggerService.LogInformation($"      ✅ First name (P3, inline): {firstName}");
                        break;
                    }

                    for (int j = i + 1; j < lines.Length && j <= i + 5; j++)
                    {
                        string valueLine = lines[j].Trim();
                        if (string.IsNullOrWhiteSpace(valueLine)) continue;

                        var leading = ExtractLeadingNameTokens(valueLine, config, skipWords);
                        var candidate = leading
                            .Where(t => lastName == null || !t.Equals(lastName, StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();

                        if (candidate != null)
                        {
                            firstName = candidate;
                            LoggerService.LogInformation($"      ✅ First name (P3, next line): {firstName}");
                            break;
                        }
                    }

                    if (firstName != null) break;
                }
            }

            LoggerService.LogInformation($"    OCR name extraction complete: lastName={lastName ?? "NULL"} firstName={firstName ?? "NULL"}");
            return (firstName, lastName);
        }



        #endregion

        #region Helper Methods

        private static List<string[]> LoadNamePatterns(string configKey)
        {
            var config = ConfigurationService.GetConfiguration();
            var patterns = config.GetSection(configKey)
                .Get<List<Dictionary<string, object>>>() ?? new();

            return patterns
                .Where(p => p.ContainsKey("Words"))
                .Select(p => ((System.Text.Json.JsonElement)p["Words"]).EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .ToArray())
                .ToList();
        }


        private static bool MatchesPattern(List<UglyToad.PdfPig.Content.Word> words, int startIndex, string[] pattern)
        {
            if (startIndex + pattern.Length > words.Count)
                return false;

            for (int i = 0; i < pattern.Length; i++)
            {
                // Normalize both to FormC to handle accented characters correctly
                // e.g. "PRÉNOM" or "NOM DE FAMILLE" from PDFs like Antonine-Maillet
                string pdfWord = words[startIndex + i].Text
                    .Normalize(NormalizationForm.FormC)
                    .TrimEnd(':')
                    .Trim();

                string patternWord = pattern[i].Normalize(NormalizationForm.FormC);

                if (!pdfWord.Equals(patternWord, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }



        private static bool ContainsAnyKeyword(string text, string[] keywords)
        {
            return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }


        private static bool IsValidNameCandidate(string word, PdfExtractionConfig config)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < config.MinNameLength)
                return false;

            string upperWord = word.ToUpper();

            // ✅ Reject purely OCR noise like "OF", "OX", "O", "X", "M", "F", "CE", "LL" 
            //    that often come from checkboxes.
            string[] exactNoiseWords = { "OF", "OX", "OM", "CE", "LL", "O", "X", "M", "F", "A", "Y", "N", "OUI", "NON", "OUL", "GOUL", "OOUL" };
            if (exactNoiseWords.Contains(upperWord))
                return false;

            // Reject checkbox noise that ends in X (e.g. "Ox")
            if (upperWord.Length == 2 && upperWord.EndsWith("X"))
                return false;

            if (config.FieldLabelWords.Any(label => upperWord.Equals(label, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (ContainsAnyKeyword(word, config.ExcludeKeywords))
                return false;

            if (word.All(char.IsDigit))
                return false;

            int letterCount = word.Count(char.IsLetter);
            if (letterCount < word.Length * 0.7)
                return false;

            return true;
        }


        private static bool IsSkipWord(string word, string[] skipWords)
        {
            string cleanWord = word.Trim().ToUpper();

            if (skipWords.Any(skipWord => cleanWord.Equals(skipWord, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(cleanWord, @"^STUDENT.?S$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;

            return false;
        }


        /// <summary>
        /// Collects consecutive valid name words starting at <paramref name="startIndex"/>,
        /// stopping when:
        ///   • a word fails <see cref="IsValidNameCandidate"/>, OR
        ///   • the horizontal gap to the next word exceeds <see cref="MaxIntraCellGapPts"/>
        ///     (indicates a new table cell — e.g. the gap between the LastName column and
        ///      the FirstName column on the Vitalité consent form).
        /// Words within the same cell (e.g. "Marie Anne", "De Cruz", "Romo Lerma") are
        /// spaced only a few points apart; column gaps are typically 50+ points.
        /// </summary>
        private const double MaxIntraCellGapPts = 40.0;

        private static (string name, int lastIndex) CollectNameWords(
            List<UglyToad.PdfPig.Content.Word> words,
            int startIndex,
            int maxIndex,
            int skipIndex,
            PdfExtractionConfig config)
        {
            var parts = new List<string>();
            int lastIndex = startIndex - 1;
            double lastWordRight = double.MinValue;

            for (int j = startIndex; j < words.Count && j <= maxIndex; j++)
            {
                if (j == skipIndex) break;

                string candidate = words[j].Text
                    .Normalize(NormalizationForm.FormC)
                    .Trim();

                if (!IsValidNameCandidate(candidate, config))
                    break;

                // ✅ Cell-boundary detection: stop when horizontal gap between words
                //    exceeds the intra-cell threshold.
                //    Same-cell words ("Marie Anne"):  gap ≈ 5–15 pt  → continue
                //    Adjacent cells ("Donnelly|Rhys"): gap ≈ 50–150 pt → stop
                if (parts.Count > 0)
                {
                    double gap = words[j].BoundingBox.Left - lastWordRight;
                    if (gap > MaxIntraCellGapPts)
                    {
                        LoggerService.LogInformation(
                            $"    Cell boundary at [{j}] '{candidate}': " +
                            $"X gap {gap:F0}pt > {MaxIntraCellGapPts}pt — stopping collection");
                        break;
                    }
                }

                parts.Add(candidate);
                lastWordRight = words[j].BoundingBox.Right;
                lastIndex = j;
            }

            return (string.Join(" ", parts), lastIndex);
        }



        /// <summary>
        /// Returns true when a line looks like a form field-label row rather than a value row.
        /// Used in OCR extraction to skip secondary header rows (e.g. "S'IDENTIFIE COMME
        /// NO D'ASSURANCE-MALADIE") that sit between the primary label row and the value row.
        /// </summary>
        /// 
        private static readonly string[] _formLabelIndicators = new[]
       {
            // French form labels (Vitalité / Horizon)
            "S'IDENTIFIE", "IDENTIFIE", "ASSURANCE", "MALADIE", "NAISSANCE",
            "TUTEUR", "TUTRICE", "PARENT", "LEGAL", "LÉGAL",
            "TELEPHONE", "TÉLÉPHONE", "COURRIEL",
            "ALLERGIES", "MEDICAMENTS", "MÉDICAMENTS", "SANTE", "SANTÉ",
            "PROFESSEUR", "TITULAIRE", "CLASSE", "FOYER", "ANNÉE", "ANNEE",
            "SECTION", "CONSENTEMENT", "VACCIN", "DIPHTERIE", "TÉTANOS",
            "COQUELUCHE", "PAPILLOME", "HUMAIN", "ALERTE", "SIGNATURE",
            // Question-sentence openers on Vitalité/Horizon alert rows
            "VOTRE", "ENFANT", "PREND", "PROBLÈME", "PROBLEME",
            // Header artifacts
            "VITALITÉ", "VITALITE", "HORIZON", "RÉSEAU", "RESEAU",
            // English equivalents
            "IDENTIFY", "IDENTIFIES", "INSURANCE", "BIRTH", "GUARDIAN",
            "TELEPHONE", "EMAIL", "ALLERGIES", "MEDICATIONS", "HEALTH",
            "TEACHER", "HOMEROOM", "YEAR", "SECTION", "CONSENT", "VACCINE"
        };

        private static bool IsFormLabelRow(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            string upper = line.ToUpperInvariant();

            // ✅ Single-token lines: one matching indicator is enough.
            //    A genuine name ("Landry", "Lucas") will never appear in _formLabelIndicators.
            //    A lone section header ("ALERTE", "SECTION", "VACCIN") will.
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 1)
            {
                return _formLabelIndicators.Any(kw =>
                    upper.Contains(kw, StringComparison.OrdinalIgnoreCase));
            }

            // Multi-token lines: require 2+ hits to avoid false positives on
            // lines like "Tina Landry" that might contain one incidental keyword.
            int hits = _formLabelIndicators.Count(kw =>
                upper.Contains(kw, StringComparison.OrdinalIgnoreCase));

            return hits >= 2;
        }



        /// <summary>
        /// Returns true when a line contains phone numbers, emails, dates, or checkbox
        /// markers — i.e. it is a data/contact row, never a student name row.
        /// </summary>
        private static bool IsContactOrDataRow(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            // Phone number pattern  e.g. (506)874-9641
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\(?\d{3}\)?[\s\-]\d{3}[\s\-]\d{4}"))
                return true;

            // Any standalone digit cluster ≥ 4  e.g. dates, health-card numbers
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b\d{4,}\b"))
                return true;

            // Email address
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}"))
                return true;

            // Checkbox markers produced by OCR  e.g. "ceELL", "ceLL", "O cell", "M cell"
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\bce[EL]{2,}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;

            // ISO date  e.g. 2013-09-25
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b\d{4}-\d{2}-\d{2}\b"))
                return true;

            return false;
        }





        /// <summary>
        /// Reads tokens from the START of a line until a form-label indicator or
        /// contact/data marker is encountered.  Returns the leading name tokens found
        /// before that stopping point.
        ///
        /// Example: "Landry Lucas S'IDENTIFIE COMME …"
        ///   → ["Landry", "Lucas"]   (stops at S'IDENTIFIE)
        ///
        /// Example: "NOM DU PARENT/TUTEUR LEGAL Tina Landry"
        ///   → []   (first token "NOM" is in FieldLabelWords → nothing before a stopper)
        ///
        /// Example: "S'IDENTIFIE COMME …"
        ///   → []   (first token hits a form-label indicator immediately)
        /// </summary>
        private static List<string> ExtractLeadingNameTokens(
            string line,
            PdfExtractionConfig config,
            string[] skipWords)
        {
            var result = new List<string>();
            var tokens = line.Split(new[] { ' ', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in tokens)
            {
                string token = raw.Trim();
                string upper = token.ToUpperInvariant();

                // Stop as soon as we hit a known form-label indicator
                bool isFormIndicator = _formLabelIndicators.Any(kw =>
                    upper.Contains(kw, StringComparison.OrdinalIgnoreCase));
                if (isFormIndicator) break;

                // Stop at contact/data markers (email, phone, digits …)
                if (IsContactOrDataRow(token)) break;

                // Stop at a field-label word from config (e.g. NOM, PRÉNOM, DE, FAMILLE …)
                bool isConfigLabel = config.FieldLabelWords.Any(lw =>
                    upper.Equals(lw, StringComparison.OrdinalIgnoreCase));
                if (isConfigLabel) break;

                if (IsValidNameCandidate(token, config) && !IsSkipWord(token, skipWords))
                    result.Add(token);
                else if (result.Count > 0)
                    break; // first non-name after we already have names → stop
            }

            return result;
        }


        ////private static (string? firstName, string? lastName, string? dateOfBirth) ExtractDetailsFromOCRText(string ocrResult)
        ////{
        ////    // 1. Ensure support for Windows-1252 in .NET Core/.NET 9 (if not already registered in Program.cs/Startup)
        ////    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ////    // 2. Define encodings
        ////    Encoding utf8 = Encoding.UTF8;
        ////    Encoding win1252 = Encoding.GetEncoding(1252); // "Western European (Windows)"

        ////    // 3. Convert the string to Windows-1252
        ////    byte[] utf8Bytes = utf8.GetBytes(ocrResult);
        ////    byte[] win1252Bytes = Encoding.Convert(utf8, win1252, utf8Bytes);

        ////    // 4. Decoded string for extraction
        ////    string decodedText = win1252.GetString(win1252Bytes);

        ////    string? firstName = null;
        ////    string? lastName = null;
        ////    string? dateOfBirth = null;

        ////    // --- Extractions ---

        ////    // Extract Date of Birth - matches formats like 2013-11-27 or 2013/11/27
        ////    Match dobMatch = Regex.Match(decodedText, @"\b(\d{4}[-/]\d{2}[-/]\d{2})\b");
        ////    if (dobMatch.Success)
        ////    {
        ////        dateOfBirth = dobMatch.Groups[1].Value;
        ////    }

        ////    // Extract Last Name (NOM DE FAMILLE). Looks for the label and captures the next likely line.
        ////    // NOTE: OCR newline patterns vary, adjust the spacing (\s) as needed for your specific OCR output.
        ////    Match lastNameMatch = Regex.Match(decodedText, @"NOM DE FAMILLE[:\s]*\r?\n([A-Za-zÀ-ÿ\-]+)", RegexOptions.IgnoreCase);
        ////    if (lastNameMatch.Success)
        ////    {
        ////        lastName = lastNameMatch.Groups[1].Value.Trim();
        ////    }

        ////    // Extract First Name (PRÉNOM). Looks for PRÉNOM but avoids capturing PRÉNOM PRÉFÉRÉ on the same line.
        ////    Match firstNameMatch = Regex.Match(decodedText, @"PRÉNOM(?![ ]+PRÉFÉRÉ)[:\s]*\r?\n([A-Za-zÀ-ÿ\-\s]+)", RegexOptions.IgnoreCase);
        ////    if (firstNameMatch.Success)
        ////    {
        ////        // Often OCR might lump first name and last name together like "Malik Perry", split if necessary
        ////        firstName = firstNameMatch.Groups[1].Value.Trim();
        ////    }

        ////    return (firstName, lastName, dateOfBirth);
        ////}


        private static (string? firstName, string? lastName, string? dateOfBirth) ExtractDetailsFromOCRText(string ocrResult)
        {
            // No need to register provider here if you use EncodingConfigurationService elsewhere,
            // but it doesn't hurt.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Use the raw ocrResult. C# strings handle 'È' and 'ë' natively.
            string text = ocrResult;

            string? firstName = null;
            string? lastName = null;
            string? dateOfBirth = null;

            // --- Date of Birth ---
            // Matches YYYY-MM-DD or YYYY/MM/DD
            Match dobMatch = Regex.Match(text, @"\b(\d{4}[-/]\d{2}[-/]\d{2})\b");
            if (dobMatch.Success) dateOfBirth = dobMatch.Groups[1].Value;

            // --- Last Name (NOM DE FAMILLE) ---
            // Look for the label, allow optional colon/spaces, and capture the next word(s)
            // Supports names on the same line OR the next line.
            var lastNameMatch = Regex.Match(text, @"NOM DE FAMILLE[:\s]+(?:\r?\n)?([A-ZÀ-ÿ][A-Za-zÀ-ÿ\-\s']+)", RegexOptions.IgnoreCase);
            if (lastNameMatch.Success)
            {
                lastName = lastNameMatch.Groups[1].Value.Trim().Split('\n')[0].Trim();
            }

            // --- First Name (PRÉNOM) ---
            // Negative lookahead for "PRÉFÉRÉ" is smart! 
            // Added support for names on same line or next line.
            var firstNameMatch = Regex.Match(text, @"PRÉNOM(?![ ]+PRÉFÉRÉ)[:\s]+(?:\r?\n)?([A-ZÀ-ÿ][A-Za-zÀ-ÿ\-\s']+)", RegexOptions.IgnoreCase);
            if (firstNameMatch.Success)
            {
                firstName = firstNameMatch.Groups[1].Value.Trim().Split('\n')[0].Trim();
            }

            return (firstName, lastName, dateOfBirth);
        }



        #endregion


    }
}
