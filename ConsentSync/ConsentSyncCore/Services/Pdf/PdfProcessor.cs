using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract;
using UglyToad.PdfPig;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using ConsentSyncCore.Services.ConfigurationPoco;
using ConsentSyncCore.Services.Configuration;

namespace ConsentSyncCore.Services.Pdf
{
    public class PdfProcessor
    {
        private readonly PdfExtractionConfig _config;

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
            bool debugOcr = false,
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

            // Skip "Scanned_" files
            if (fileName.StartsWith("Scanned_", StringComparison.OrdinalIgnoreCase))
            {
                 LoggerService.LogInformation($"Skipping {fileName} (already scanned)");
                return ("Unknown", "Unknown", 0);
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
                    var ocrText = ExtractTextWithOCR(pdfFilePath, 1, fileName, debugOcr, debugFolder, config);

                    if (!string.IsNullOrEmpty(ocrText))
                    {
                         LoggerService.LogInformation($"  OCR extracted {ocrText.Length} characters");

                        if (debugOcr)
                        {
                            var debugPath = Path.Combine(debugFolder, $"OCR_DEBUG_{fileName}.txt");
                            File.WriteAllText(debugPath, ocrText);
                             LoggerService.LogInformation($"  OCR text saved to: {debugPath}");
                        }

                        (firstName, lastName) = ExtractNamesFromOCRText(ocrText, config);
                    }
                    else
                    {
                         LoggerService.LogInformation("  OCR extraction failed or returned empty text");
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

                // ✅ Use patterns directly from config (no need for LoadNamePatterns)
                var lastNamePatterns = config.LastNamePatterns;
                var firstNamePatterns = config.FirstNamePatterns;
                var preferredNamePatterns = config.PreferredNamePatterns;


                for (int i = 0; i < words.Count; i++)
                {
                    // Check for last name patterns
                    if (lastName == null)
                    {
                        foreach (var pattern in lastNamePatterns)
                        {
                            if (MatchesPattern(words, i, pattern.Words))
                            {
                                 LoggerService.LogInformation($"  Found last name pattern ({pattern.Language}) at index {i}");
                                int startIndex = i + pattern.Words.Length;

                                for (int j = startIndex; j < words.Count && j < startIndex + config.SearchRange; j++)
                                {
                                    string candidateWord = words[j].Text.Trim();

                                    if (IsValidNameCandidate(candidateWord, config))
                                    {
                                        lastName = candidateWord;
                                        lastNameEndIndex = j;
                                         LoggerService.LogInformation($"  ✅ Last Name: {lastName} at index {j}");
                                        break;
                                    }
                                }

                                if (lastName != null) break;
                            }
                        }
                    }

                    // Check for first name patterns (exclude preferred name)
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
                                    int startIndex = i + pattern.Words.Length;

                                    for (int j = startIndex; j < words.Count && j < startIndex + config.SearchRange; j++)
                                    {
                                        if (j == lastNameEndIndex)
                                        {
                                             LoggerService.LogInformation($"    Skipping index {j} - already used as last name");
                                            continue;
                                        }

                                        string candidateWord = words[j].Text.Trim();

                                        if (IsValidNameCandidate(candidateWord, config))
                                        {
                                            firstName = candidateWord;
                                            firstNameEndIndex = j;
                                             LoggerService.LogInformation($"  ✅ First Name: {firstName} at index {j}");
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




                // STRATEGY 2: Fallback keyword matching
                if (lastName == null || firstName == null)
                {
                     LoggerService.LogInformation($"  Pattern matching incomplete, trying keyword fallback...");

                    for (int i = 0; i < words.Count - 1; i++)
                    {
                        string currentWord = words[i].Text;

                        if (lastName == null && ContainsAnyKeyword(currentWord, config.LastNameKeywords))
                        {
                            for (int j = i + 1; j < words.Count && j < i + config.SearchRange; j++)
                            {
                                string candidateWord = words[j].Text.Trim();
                                if (IsValidNameCandidate(candidateWord, config))
                                {
                                    lastName = candidateWord;
                                    lastNameEndIndex = j;
                                     LoggerService.LogInformation($"  -> Last Name: {lastName} at index {j}");
                                    break;
                                }
                            }
                        }

                        if (firstName == null && ContainsAnyKeyword(currentWord, config.FirstNameKeywords))
                        {
                            if (i + 1 < words.Count && ContainsAnyKeyword(words[i + 1].Text, new[] { "PRÉFÉRÉ", "PREFERRED" }))
                            {
                                continue;
                            }

                            for (int j = i + 1; j < words.Count && j < i + config.SearchRange; j++)
                            {
                                if (j == lastNameEndIndex) continue;

                                string candidateWord = words[j].Text.Trim();
                                if (IsValidNameCandidate(candidateWord, config))
                                {
                                    firstName = candidateWord;
                                     LoggerService.LogInformation($"  -> First Name: {firstName} at index {j}");
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
        private static string ExtractTextWithOCR(
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
        /// Convert PDF page to image for OCR
        /// </summary>
        private static string ConvertPdfPageToImage(
            string pdfPath,
            int pageNumber,
            string fileName,
            bool saveForDebug,
            bool detectOrientation,
            string? debugOutputDir)
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

                string tempPath;
                if (saveForDebug)
                {
                    var outputDir = debugOutputDir ?? Path.GetDirectoryName(pdfPath)!;
                    tempPath = Path.Combine(outputDir, $"DEBUG_IMAGE_{fileName}.png");
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

            // Priority 1: Look for "NOM DE L'ÉLÈVE" or "STUDENT'S NAME"
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

                    var validNames = cleanedLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => IsValidNameCandidate(w, config) && !IsSkipWord(w, skipWords))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (validNames.Count >= 2)
                    {
                        firstName = validNames[0];
                        lastName = validNames[1];
                         LoggerService.LogInformation($"      ✅ Found: {firstName} {lastName}");
                        return (firstName, lastName);
                    }
                }
            }

            // Additional extraction strategies...
            // (Keeping your other strategies for brevity)

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
                if (!words[startIndex + i].Text.Equals(pattern[i], StringComparison.OrdinalIgnoreCase))
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

        #endregion


    }
}
