using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Linq;
using Tesseract;
using UglyToad.PdfPig;
using static System.Runtime.InteropServices.JavaScript.JSType;

public class PdfProcessor
{
    private static readonly IConfiguration _config;
    private static readonly string[] _lastNameKeywords;
    private static readonly string[] _firstNameKeywords;
    private static readonly string[] _excludeKeywords;
    private static readonly int _searchRange;
    private static readonly int _minNameLength;
    private static readonly List<NamePattern> _lastNamePatterns;
    private static readonly List<NamePattern> _firstNamePatterns;
    private static readonly List<NamePattern> _preferredNamePatterns;
    private static readonly string[] _fieldLabelWords;

    // Helper class to represent name patterns
    private class NamePattern
    {
        public string[] Words { get; set; } = Array.Empty<string>();
        public string Language { get; set; } = "Unknown";
    }


    static PdfProcessor()
    {
        // Load the config file once with better path resolution
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _config = new ConfigurationBuilder()
            .AddJsonFile(configPath, optional: false, reloadOnChange: false)
            .Build();

        // Load PDF extraction configuration
        _lastNameKeywords = _config.GetSection("PdfExtraction:LastNameKeywords").Get<string[]>()
            ?? new[] { "FAMILLE", "NOM", "SURNAME" };
        _firstNameKeywords = _config.GetSection("PdfExtraction:FirstNameKeywords").Get<string[]>()
            ?? new[] { "PRÉNOM", "PRENOM", "GIVEN" };
        _excludeKeywords = _config.GetSection("PdfExtraction:ExcludeKeywords").Get<string[]>()
            ?? new[] { "PRÉFÉRÉ", "PREFERRED", "DATE" };
        _searchRange = _config.GetValue<int>("PdfExtraction:SearchRange", 15);
        _minNameLength = _config.GetValue<int>("PdfExtraction:MinNameLength", 2);

        // Load multi-word patterns
        _lastNamePatterns = _config.GetSection("PdfExtraction:LastNamePatterns").Get<List<NamePattern>>()
            ?? new List<NamePattern>
            {
                new NamePattern { Words = new[] { "LAST", "NAME" }, Language = "English" },
                new NamePattern { Words = new[] { "NOM", "DE", "FAMILLE" }, Language = "French" },
                new NamePattern { Words = new[] { "SURNAME" }, Language = "English" },
                new NamePattern { Words = new[] { "NOM" }, Language = "French" }
            };

        _firstNamePatterns = _config.GetSection("PdfExtraction:FirstNamePatterns").Get<List<NamePattern>>()
            ?? new List<NamePattern>
            {
                new NamePattern { Words = new[] { "FIRST", "NAME" }, Language = "English" },
                new NamePattern { Words = new[] { "PRÉNOM" }, Language = "French" },
                new NamePattern { Words = new[] { "PRENOM" }, Language = "French" },
                new NamePattern { Words = new[] { "GIVEN", "NAME" }, Language = "English" }
            };

        _preferredNamePatterns = _config.GetSection("PdfExtraction:PreferredNamePatterns").Get<List<NamePattern>>()
            ?? new List<NamePattern>
            {
                new NamePattern { Words = new[] { "PREFERRED", "FIRST", "NAME" }, Language = "English" },
                new NamePattern { Words = new[] { "PRÉNOM", "PRÉFÉRÉ" }, Language = "French" }
            };

        // Load field label words to exclude
        _fieldLabelWords = _config.GetSection("PdfExtraction:FieldLabelWords").Get<string[]>()
            ?? new[] { "FIRST", "LAST", "NAME", "PREFERRED", "NOM", "PRÉNOM", "PRENOM", "DE", "FAMILLE", "GIVEN", "SURNAME" };

        Console.WriteLine("PDF Extraction Configuration Loaded:");
        Console.WriteLine($"  Last Name Keywords: {string.Join(", ", _lastNameKeywords)}");
        Console.WriteLine($"  First Name Keywords: {string.Join(", ", _firstNameKeywords)}");
        Console.WriteLine($"  Last Name Patterns: {_lastNamePatterns.Count}");
        Console.WriteLine($"  First Name Patterns: {_firstNamePatterns.Count}");
        Console.WriteLine($"  Field Label Words (excluded): {string.Join(", ", _fieldLabelWords)}");
        Console.WriteLine($"  Exclude Keywords: {string.Join(", ", _excludeKeywords)}");
        Console.WriteLine($"  Search Range: {_searchRange}");
        Console.WriteLine($"  Min Name Length: {_minNameLength}\n");
    }



    public string GetConfigOutputDir()
    {
        return _config["AppSettings:OutputDir"] ?? "C:\\Default\\Path";
    }

    public string GetConfigSourceDir()
    {
        return _config["AppSettings:SourceDir"] ?? "C:\\Default\\Path";
    }



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

        // Use provided debug directory or create temp folder
        string debugFolder = debugOutputDir ?? Path.Combine(Path.GetTempPath(), "PdfProcessor_Debug");
        if (debugOcr && !Directory.Exists(debugFolder))
        {
            Directory.CreateDirectory(debugFolder);
        }

        // Skip "Scanned_" files as per your workflow
        if (fileName.StartsWith("Scanned_", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Skipping {fileName} (already scanned)");
            return ("Unknown", "Unknown", 0);
        }

        try
        {
            Console.WriteLine($"\n--- Processing: {fileName} ---");

            using (var pdf = PdfDocument.Open(pdfFilePath))
            {
                if (pdf.NumberOfPages < 1)
                {
                    Console.WriteLine("  No pages found in PDF");
                    return ("Unknown", "Unknown", 0);
                }

                var page = pdf.GetPage(1);
                var words = page.GetWords().ToList();

                Console.WriteLine($"  Total words found via PdfPig: {words.Count}");

                string? firstName = null;
                string? lastName = null;
                int pageCount = pdf.NumberOfPages;

                // If no words found, PDF is scanned - use OCR
                if (words.Count == 0)
                {
                    Console.WriteLine("  PDF is scanned - using OCR extraction...");

                    // Pass debug folder to OCR method
                    var ocrText = ExtractTextWithOCR(pdfFilePath, 1, fileName, debugOcr, debugFolder);

                    if (!string.IsNullOrEmpty(ocrText))
                    {
                        Console.WriteLine($"  OCR extracted {ocrText.Length} characters");

                        if (debugOcr)
                        {
                            // Save OCR output to debug folder
                            var debugPath = Path.Combine(debugFolder, $"OCR_DEBUG_{fileName}.txt");
                            File.WriteAllText(debugPath, ocrText);
                            Console.WriteLine($"  OCR text saved to: {debugPath}");
                        }

                        // Parse OCR text for names
                        (firstName, lastName) = ExtractNamesFromOCRText(ocrText);
                    }
                    else
                    {
                        Console.WriteLine("  OCR extraction failed or returned empty text");
                    }
                }
                else
                {
                    // Use configurable extraction for text-based PDFs
                    Console.WriteLine("  PDF has extractable text - using direct extraction");
                    (firstName, lastName) = ExtractNamesFromWords(words);
                }

                Console.WriteLine($"  Final Result: {firstName ?? "Unknown"} {lastName ?? "Unknown"} | Pages: {pageCount}");

                return (firstName ?? "Unknown", lastName ?? "Unknown", pageCount);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ERROR processing {fileName}: {ex.Message}");
            return ("Error", "Error", 0);
        }
    }





    // Refactored: Keep the original method but use the new single-file method internally
    public static Dictionary<string, string> GetDirectoryPdfInfo(string sourceDir, bool debugOcr = false, string? debugOutputDir = null)
    {
        var results = new Dictionary<string, string>();

        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
        {
            Console.WriteLine("Source directory is invalid or doesn't exist.");
            return results;
        }

        var files = Directory.GetFiles(sourceDir, "*.pdf");
        Console.WriteLine($"Found {files.Length} PDF files to process.\n");

        foreach (var filePath in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            try
            {
                // Use the new single-file processor
                var (firstName, lastName, pageCount) = ProcessSinglePdf(filePath, debugOcr, debugOutputDir);

                string nameInfo = $"{firstName} {lastName}|{pageCount}";
                results.Add(fileName, nameInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR processing {fileName}: {ex.Message}");
                results.Add(fileName, $"Error Error|0");
            }
        }

        return results;
    }


    private static string ExtractTextWithOCR(string pdfPath, int pageNumber, string fileName, bool saveDebugImage = false, string? debugOutputDir = null)
    {
        string tempImagePath = string.Empty;

        try
        {
            // Path to tessdata folder
            var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

            if (!Directory.Exists(tessDataPath))
            {
                Console.WriteLine($"    ERROR: tessdata folder not found at: {tessDataPath}");
                return string.Empty;
            }

            var tessdataFile = Path.Combine(tessDataPath, "fra.traineddata");
            if (!File.Exists(tessdataFile))
            {
                Console.WriteLine($"    ERROR: fra.traineddata not found at: {tessdataFile}");
                return string.Empty;
            }

            Console.WriteLine($"    Converting PDF page {pageNumber} to image...");

            // Convert PDF page to image with higher resolution
            tempImagePath = ConvertPdfPageToImage(pdfPath, pageNumber, fileName, saveDebugImage, detectOrientation: true, debugOutputDir);

            if (string.IsNullOrEmpty(tempImagePath) || !File.Exists(tempImagePath))
            {
                Console.WriteLine("    Failed to convert PDF to image");
                return string.Empty;
            }

            Console.WriteLine($"    Running OCR on image...");

            // Perform OCR with both French and English
            using (var engine = new TesseractEngine(tessDataPath, "fra+eng", EngineMode.Default))
            {
                using (var img = Pix.LoadFromFile(tempImagePath))
                {
                    using (var result = engine.Process(img))
                    {
                        var text = result.GetText();
                        var confidence = result.GetMeanConfidence();

                        Console.WriteLine($"    OCR Confidence: {confidence:P}");

                        return text;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    OCR Error: {ex.Message}");
            return string.Empty;
        }
        finally
        {
            // Clean up temp image only if not in debug mode
            if (!saveDebugImage && !string.IsNullOrEmpty(tempImagePath) && File.Exists(tempImagePath))
            {
                try { File.Delete(tempImagePath); } catch { }
            }
        }
    }





    private static string ConvertPdfPageToImage(string pdfPath, int pageNumber, string fileName, bool saveForDebug = false, bool detectOrientation = false, string? debugOutputDir = null)
    {
        try
        {
            // Increase resolution for better OCR
            using (var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(2160, 3840)))
            {
                if (docReader.GetPageCount() < pageNumber)
                {
                    return string.Empty;
                }

                using (var pageReader = docReader.GetPageReader(pageNumber - 1)) // 0-based index
                {
                    var rawBytes = pageReader.GetImage();
                    var width = pageReader.GetPageWidth();
                    var height = pageReader.GetPageHeight();

                    using (var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height))
                    {
                        // Auto-detect orientation if requested
                        if (detectOrientation)
                        {
                            int rotationNeeded = DetectOrientation(image);
                            if (rotationNeeded > 0)
                            {
                                Console.WriteLine($"    Auto-detected orientation: rotating {rotationNeeded}°");
                                image.Mutate(x => x.Rotate(rotationNeeded));
                            }
                            else
                            {
                                Console.WriteLine($"    Image orientation is correct (no rotation needed)");
                            }
                        }

                        // Save as PNG file
                        string tempPath;
                        if (saveForDebug)
                        {
                            // Use debug output directory if provided, otherwise use source directory
                            var outputDir = debugOutputDir ?? Path.GetDirectoryName(pdfPath)!;
                            tempPath = Path.Combine(outputDir, $"DEBUG_IMAGE_{fileName}.png");
                            Console.WriteLine($"    Debug image saved to: {tempPath}");
                        }
                        else
                        {
                            tempPath = Path.Combine(Path.GetTempPath(), $"pdf_page_{Guid.NewGuid()}.png");
                        }

                        image.SaveAsPng(tempPath);
                        return tempPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Error converting PDF to image: {ex.Message}");
            return string.Empty;
        }
    }


    private static int DetectOrientation(Image<Bgra32> image)
    {
        try
        {
            var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

            // Test each rotation and look for "vitalité" or "SECTION" at the top of the page
            using (var engine = new TesseractEngine(tessDataPath, "fra+eng", EngineMode.Default))
            {
                int[] rotations = { 0, 90, 180, 270 };
                int bestRotation = 0;
                int bestScore = 0;

                foreach (int rotation in rotations)
                {
                    using (var testImage = image.Clone())
                    {
                        if (rotation > 0)
                        {
                            testImage.Mutate(x => x.Rotate(rotation));
                        }

                        // Crop to top 30% using ImageSharp before converting to Pix
                        int cropHeight = (int)(testImage.Height * 0.3);
                        using (var croppedImage = testImage.Clone(ctx => ctx.Crop(new Rectangle(0, 0, testImage.Width, cropHeight))))
                        {
                            var testPath = Path.Combine(Path.GetTempPath(), $"orientation_test_{Guid.NewGuid()}.png");
                            croppedImage.SaveAsPng(testPath);

                            try
                            {
                                using (var img = Pix.LoadFromFile(testPath))
                                {
                                    using (var result = engine.Process(img))
                                    {
                                        var text = result.GetText().ToLower();
                                        var confidence = result.GetMeanConfidence();

                                        // Score based on finding key markers at the top
                                        int score = 0;
                                        if (text.Contains("vitalit")) score += 50;  // vitalité (with or without accent)
                                        if (text.Contains("réseau")) score += 30;
                                        if (text.Contains("reseau")) score += 25;  // without accent
                                        if (text.Contains("santé")) score += 30;
                                        if (text.Contains("sante")) score += 25;   // without accent
                                        if (text.Contains("section")) score += 40;
                                        if (text.Contains("renseignement")) score += 40;

                                        // Bonus for high confidence
                                        score += (int)(confidence * 10);

                                        Console.WriteLine($"      Testing {rotation}°: Score={score}, Confidence={confidence:P}");

                                        if (score > bestScore)
                                        {
                                            bestScore = score;
                                            bestRotation = rotation;
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                try { File.Delete(testPath); } catch { }
                            }
                        }
                    }
                }

                Console.WriteLine($"    Best orientation detected: {bestRotation}° (score: {bestScore})");
                return bestRotation;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Orientation detection failed: {ex.Message}");
            Console.WriteLine($"    Stack trace: {ex.StackTrace}");
            // Default to 180° rotation if detection fails (based on your earlier tests)
            return 180;
        }
    }


    private static (string? firstName, string? lastName) ExtractNamesFromWords(List<UglyToad.PdfPig.Content.Word> words)
    {
        string? firstName = null;
        string? lastName = null;
        int lastNameEndIndex = -1;  // Track where we found the last name
        int firstNameEndIndex = -1; // Track where we found the first name

        try
        {
            Console.WriteLine($"  Starting word-based extraction with {words.Count} words");

            // STRATEGY 1: Pattern-based matching (handles multi-word field labels)
            for (int i = 0; i < words.Count; i++)
            {
                // Check for last name patterns
                if (lastName == null)
                {
                    foreach (var pattern in _lastNamePatterns)
                    {
                        if (MatchesPattern(words, i, pattern.Words))
                        {
                            Console.WriteLine($"  Found last name pattern '{string.Join(" ", pattern.Words)}' ({pattern.Language}) at index {i}");

                            // Skip the pattern words and look for the actual name value
                            int startIndex = i + pattern.Words.Length;
                            for (int j = startIndex; j < words.Count && j < startIndex + _searchRange; j++)
                            {
                                string candidateWord = words[j].Text.Trim();
                                Console.WriteLine($"    Checking word at [{j}]: '{candidateWord}'");

                                if (IsValidNameCandidate(candidateWord))
                                {
                                    lastName = candidateWord;
                                    lastNameEndIndex = j;  // Remember where we found it
                                    Console.WriteLine($"  ✅ Last Name extracted: {lastName} at index {j}");
                                    break;
                                }
                            }

                            if (lastName != null) break;
                        }
                    }
                }

                // Check for first name patterns (excluding preferred name patterns)
                if (firstName == null)
                {
                    // First check if this is a preferred name pattern (skip if so)
                    bool isPreferredName = _preferredNamePatterns.Any(p => MatchesPattern(words, i, p.Words));

                    if (!isPreferredName)
                    {
                        foreach (var pattern in _firstNamePatterns)
                        {
                            if (MatchesPattern(words, i, pattern.Words))
                            {
                                Console.WriteLine($"  Found first name pattern '{string.Join(" ", pattern.Words)}' ({pattern.Language}) at index {i}");

                                // Skip the pattern words and look for the actual name value
                                int startIndex = i + pattern.Words.Length;
                                for (int j = startIndex; j < words.Count && j < startIndex + _searchRange; j++)
                                {
                                    // CRITICAL FIX: Skip if this is the same word we used for last name
                                    if (j == lastNameEndIndex)
                                    {
                                        Console.WriteLine($"    Skipping word at [{j}]: '{words[j].Text}' - already used as last name");
                                        continue;
                                    }

                                    string candidateWord = words[j].Text.Trim();
                                    Console.WriteLine($"    Checking word at [{j}]: '{candidateWord}'");

                                    if (IsValidNameCandidate(candidateWord))
                                    {
                                        firstName = candidateWord;
                                        firstNameEndIndex = j;  // Remember where we found it
                                        Console.WriteLine($"  ✅ First Name extracted: {firstName} at index {j}");
                                        break;
                                    }
                                }

                                if (firstName != null) break;
                            }
                        }
                    }
                }

                // Early exit if both found
                if (lastName != null && firstName != null)
                {
                    Console.WriteLine($"  Both names found - stopping search");
                    break;
                }
            }

            // STRATEGY 2: Fallback to single keyword matching (for edge cases)
            if (lastName == null || firstName == null)
            {
                Console.WriteLine($"  Pattern matching incomplete, trying fallback keyword matching...");

                for (int i = 0; i < words.Count - 1; i++)
                {
                    string currentWord = words[i].Text;

                    // Check for last name keyword
                    if (lastName == null && ContainsAnyKeyword(currentWord, _lastNameKeywords))
                    {
                        Console.WriteLine($"  Found last name keyword '{currentWord}' at index {i}");

                        for (int j = i + 1; j < words.Count && j < i + _searchRange; j++)
                        {
                            string candidateWord = words[j].Text.Trim();
                            if (IsValidNameCandidate(candidateWord))
                            {
                                lastName = candidateWord;
                                lastNameEndIndex = j;
                                Console.WriteLine($"  -> Last Name extracted: {lastName} at index {j}");
                                break;
                            }
                        }
                    }

                    // Check for first name keyword
                    if (firstName == null && ContainsAnyKeyword(currentWord, _firstNameKeywords))
                    {
                        Console.WriteLine($"  Found first name keyword '{currentWord}' at index {i}");

                        if (i + 1 < words.Count && ContainsAnyKeyword(words[i + 1].Text, new[] { "PRÉFÉRÉ", "PREFERRED" }))
                        {
                            Console.WriteLine($"    Skipping - this is a 'preferred name' field");
                            continue;
                        }

                        for (int j = i + 1; j < words.Count && j < i + _searchRange; j++)
                        {
                            // Skip if this is the same word we used for last name
                            if (j == lastNameEndIndex)
                            {
                                Console.WriteLine($"    Skipping word at [{j}] - already used as last name");
                                continue;
                            }

                            string candidateWord = words[j].Text.Trim();
                            if (IsValidNameCandidate(candidateWord))
                            {
                                firstName = candidateWord;
                                firstNameEndIndex = j;
                                Console.WriteLine($"  -> First Name extracted: {firstName} at index {j}");
                                break;
                            }
                        }
                    }

                    if (lastName != null && firstName != null) break;
                }
            }

            Console.WriteLine($"  Word extraction complete: FirstName={firstName ?? "NULL"}, LastName={lastName ?? "NULL"}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ ERROR in ExtractNamesFromWords: {ex.Message}");
            Console.WriteLine($"  Stack trace: {ex.StackTrace}");
        }

        return (firstName, lastName);
    }


    // Helper method to check if words match a pattern starting at a given index
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


    private static bool IsValidNameCandidate(string word)
    {
        // Must not be empty or too short
        if (string.IsNullOrWhiteSpace(word) || word.Length < _minNameLength)
            return false;

        // CRITICAL: Reject field label words
        string upperWord = word.ToUpper();
        if (_fieldLabelWords.Any(label => upperWord.Equals(label, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"        Rejected '{word}' - it's a field label word");
            return false;
        }

        // Must not contain any exclude keywords
        if (ContainsAnyKeyword(word, _excludeKeywords))
            return false;

        // Must not contain any first/last name keywords (to skip headers)
        if (ContainsAnyKeyword(word, _firstNameKeywords) || ContainsAnyKeyword(word, _lastNameKeywords))
            return false;

        // Must not be all digits
        if (word.All(char.IsDigit))
            return false;

        // Should mostly be letters (at least 70%)
        int letterCount = word.Count(char.IsLetter);
        if (letterCount < word.Length * 0.7)
            return false;

        // Exclude common non-name patterns
        if (word.Contains("--") || word.Contains("__") || (word.StartsWith("O") && word.Length == 1))
            return false;

        return true;
    }


    private static (string? firstName, string? lastName) ExtractNamesFromOCRText(string ocrText)
    {
        string? firstName = null;
        string? lastName = null;

        var lines = ocrText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        Console.WriteLine($"    Parsing {lines.Length} lines from OCR text...");

        // Common connector words to skip - EXPANDED LIST
        string[] skipWords = {
        "DE", "DU", "DA", "L", "LE", "LA",
        "PRENOM", "PRÉNOM", "PREFERE", "PRÉFÉRÉ",
        "NOM", "FAMILLE",
        "LAST", "FIRST", "NAME", "GIVEN",
        "COMME", "SEXE",
        "LEGAL", "PARENT", "TUTEUR",
        "ÉLÈVE", "ELEVE", "LELEVE",
        "STUDENT", "STUDENTS",
        "SIGNATURE", "INFIRMIERE", "INFIRMIÈRE", "SNATURE",
        "ceLL", "ceELL", "CELL", "CELU",
        "HEURE", "VACCIN", "ADACEL", "BOOSTRIX", "GARDASIL",
        "GM", "OR", "OX", "F", "M", "X", "H", "O",
        "NAISSANCE", "BIRTH", "OF", "DATE", "AAAA", "MM", "JJ", "YYYY",
        "ASSURANCE", "MALADIE", "NO", "NUM", "NUMERO",
        "PROFESSEUR", "TITULAIRE", "CLASSE", "FOYER", "ÉCOLE",
        "TELEPHONE", "COURRIEL", "EMAIL", "TEL"
    };

        // PRIORITY 1: Look for "NOM DE L'ÉLÈVE" or "STUDENT'S NAME" followed by names (Section 4)
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            // Match French variations: "NOM DE L'ÉLÈVE"
            bool hasFrenchPattern = line.Contains("NOM DE L", StringComparison.OrdinalIgnoreCase) &&
                                    (line.Contains("ÉLÈVE", StringComparison.OrdinalIgnoreCase) ||
                                     line.Contains("ELEVE", StringComparison.OrdinalIgnoreCase));

            // Match English variations: "STUDENT'S NAME" or "STUDENTS NAME"
            bool hasEnglishPattern = line.Contains("STUDENT", StringComparison.OrdinalIgnoreCase) &&
                                     line.Contains("NAME", StringComparison.OrdinalIgnoreCase);

            if ((hasFrenchPattern || hasEnglishPattern) &&
                !line.Contains("PRÉFÉRÉ", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("PREFERRED", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    Found student name header in line {i}: {line}");

                // AGGRESSIVE FIX: Use regex to remove the entire header pattern
                string cleanedLine = line;

                // Remove "NOM DE L'ÉLÈVE" pattern (including any character for apostrophe)
                cleanedLine = System.Text.RegularExpressions.Regex.Replace(
                    cleanedLine,
                    @"NOM\s+DE\s+L.?[ÉE]L[ÈE]VE",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Remove "STUDENT'S NAME" pattern (including any character for apostrophe)
                cleanedLine = System.Text.RegularExpressions.Regex.Replace(
                    cleanedLine,
                    @"STUDENT.?S?\s+NAME",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Split by whitespace
                var parts = cleanedLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                Console.WriteLine($"      Cleaned line: {cleanedLine}");
                Console.WriteLine($"      Parts after split: {string.Join(" | ", parts)}");

                // Extract valid names - be very strict
                var validNames = parts
                    .Where(w =>
                    {
                        bool isValid = IsValidNameCandidate(w) && !IsSkipWord(w, skipWords);
                        if (isValid)
                        {
                            Console.WriteLine($"        ✓ Valid name candidate: {w}");
                        }
                        else
                        {
                            Console.WriteLine($"        ✗ Rejected: {w}");
                        }
                        return isValid;
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (validNames.Count >= 2)
                {
                    firstName = validNames[0];
                    lastName = validNames[1];
                    Console.WriteLine($"      ✅ Found from Section 4 header: {firstName} {lastName}");
                    return (firstName, lastName);
                }
                else
                {
                    Console.WriteLine($"      ⚠ Only found {validNames.Count} valid name(s): {string.Join(", ", validNames)}");
                }
            }
        }

        // PRIORITY 1.5: Look for "SECTION 4" then find the first line with two valid names
        bool inSection4 = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            // Mark when we enter Section 4
            if (line.Contains("SECTION 4", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("FICHE D'IMMUNISATION", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("FICHE DIMMUNISATION", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("PERSONAL IMMUNIZATION", StringComparison.OrdinalIgnoreCase))
            {
                inSection4 = true;
                Console.WriteLine($"    Entered Section 4 at line {i}");
                continue;
            }

            // Stop searching when we reach next section or signature area
            if (inSection4 && (line.Contains("SIGNATURE", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("SECTION 5", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"    Exiting Section 4 at line {i} (found signature/next section)");
                break;
            }

            // If we're in Section 4, look for names
            if (inSection4)
            {
                // Skip obvious non-name lines
                if (line.Contains("VACCIN", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("VACCINE", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("DOSE", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("DATE DE NAISSANCE", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("DATE OF BIRTH", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("ASSURANCE", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("MEDICARE", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("MALADIE", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("NOM DU VACCIN", StringComparison.OrdinalIgnoreCase) ||
                    line.Length < 5)
                {
                    continue;
                }

                var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var validNames = words.Where(w =>
                    IsValidNameCandidate(w) &&
                    !IsSkipWord(w, skipWords)
                ).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

                if (validNames.Count >= 2)
                {
                    firstName = validNames[0];
                    lastName = validNames[1];
                    Console.WriteLine($"      Found in Section 4 line [{i}]: {line}");
                    Console.WriteLine($"      Extracted: {firstName} {lastName}");
                    return (firstName, lastName);
                }
            }
        }

        // PRIORITY 2: Look for "LAST NAME" / "FIRST NAME" (English forms)
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (line.Contains("LAST NAME", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("FIRST NAME", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    Found English name headers in line {i}: {line}");

                // Check next line for names
                if (i + 1 < lines.Length)
                {
                    string nextLine = lines[i + 1].Trim();
                    Console.WriteLine($"      Checking line [{i + 1}]: {nextLine}");

                    var validNames = nextLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => IsValidNameCandidate(w) && !IsSkipWord(w, skipWords))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (validNames.Count >= 2)
                    {
                        lastName = validNames[0];
                        firstName = validNames[1];
                        Console.WriteLine($"      Found from English headers: {firstName} {lastName}");
                        return (firstName, lastName);
                    }
                }
            }
        }

        // PRIORITY 3: Look for "NOM DE FAMILLE PRENOM" headers in Section 1 (fallback)
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            bool hasLastNameKeyword = ContainsAnyKeyword(line, _lastNameKeywords);
            bool hasFirstNameKeyword = ContainsAnyKeyword(line, _firstNameKeywords);

            if (hasLastNameKeyword && hasFirstNameKeyword)
            {
                Console.WriteLine($"    Found BOTH name keywords in line {i}: {line}");

                // Check next lines for the actual names
                for (int j = i + 1; j < Math.Min(i + 5, lines.Length); j++)
                {
                    string candidate = lines[j].Trim();
                    Console.WriteLine($"      Checking line [{j}]: {candidate}");

                    // Skip lines that are obviously field labels, not names
                    if (candidate.Contains("S'IDENTIFIE", StringComparison.OrdinalIgnoreCase) ||
                        candidate.Contains("NAISSANCE", StringComparison.OrdinalIgnoreCase) ||
                        candidate.Contains("ASSURANCE", StringComparison.OrdinalIgnoreCase) ||
                        candidate.Contains("D'ASSURANCE-MALADIE", StringComparison.OrdinalIgnoreCase) ||
                        candidate.Contains("PARENT", StringComparison.OrdinalIgnoreCase) ||
                        candidate.Contains("TUTEUR", StringComparison.OrdinalIgnoreCase) ||
                        candidate.Contains("LEGAL", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"        Skipping - this is a field label line");
                        continue;
                    }

                    var words = candidate.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    var validNames = words.Where(w =>
                        IsValidNameCandidate(w) &&
                        !IsSkipWord(w, skipWords)
                    ).Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                    if (validNames.Count >= 2)
                    {
                        lastName = validNames[0];
                        firstName = validNames[1];
                        Console.WriteLine($"      Found last name: {lastName}");
                        Console.WriteLine($"      Found first name: {firstName}");
                        break;
                    }
                }

                if (firstName != null && lastName != null)
                {
                    break;
                }
            }
        }

        return (firstName, lastName);
    }

    // New helper method to check skip words more thoroughly
    private static bool IsSkipWord(string word, string[] skipWords)
    {
        string cleanWord = word.Trim().ToUpper();

        // Match exact words
        if (skipWords.Any(skipWord => cleanWord.Equals(skipWord, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Filter out "STUDENT'S" variations (with any character between STUDENT and S)
        if (System.Text.RegularExpressions.Regex.IsMatch(cleanWord, @"^STUDENT.?S$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return true;

        // Filter out "L'ÉLÈVE" variations (L followed by any character, then ÉLÈVE/ELEVE)
        if (System.Text.RegularExpressions.Regex.IsMatch(cleanWord, @"^L.?[ÉE]L[ÈE]VE$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return true;

        return false;
    }



}