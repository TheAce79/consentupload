using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;

public class CsvValidator
{
    private readonly IConfiguration _config;
    private readonly string _sourceDir;
    private readonly string _errorDir;
    private readonly string _csvPath;
    private readonly string _outputDir;

    /// <summary>
    /// ctor for CsvValidator class, initializes configuration and validates required settings for source directory, error directory, and CSV file path.
    /// </summary>
    /// <param name="config"></param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public CsvValidator(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _sourceDir = _config["AppSettings:SourceDir"]
            ?? throw new InvalidOperationException("AppSettings:SourceDir is not configured");

        _errorDir = _config["AppSettings:ErrorDir"]
            ?? throw new InvalidOperationException("AppSettings:ErrorDir is not configured");

        _outputDir = _config["AppSettings:OutputDir"] ?? "C:\\Default\\Path";

        // Get and validate CSV configuration values
        string csvFilePath = _config["AppSettings:csvFilePath"]
            ?? throw new InvalidOperationException("AppSettings:csvFilePath is not configured");

        string csvFileName = _config["AppSettings:csvFileName"]
            ?? throw new InvalidOperationException("AppSettings:csvFileName is not configured");

        // FullfilePath of CSV = "csvFilePath" + "csvFileName"
        _csvPath = Path.Combine(csvFilePath, csvFileName);

        // Ensure error directory exists
        if (!Directory.Exists(_errorDir))
        {
            Directory.CreateDirectory(_errorDir);
        }

        // Ensure output directory exists
        if (!Directory.Exists(_outputDir))
        {
            Directory.CreateDirectory(_outputDir);
            Console.WriteLine($"Created output directory: {_outputDir}");
        }
    }

    public void ProcessAndValidate()
    {
        if (!File.Exists(_csvPath))
        {
            Console.WriteLine($"CSV file not found: {_csvPath}");
            return;
        }

        // DEBUG: Show raw bytes for troubleshooting
        DebugShowBytes(_csvPath, 1);

        // ENHANCED: Try reading with multiple encodings until we find one that works
        var lines = ReadCsvWithBestEncoding(_csvPath);
        if (lines.Count == 0) return;

        var header = lines[0];
        var newLines = new List<string>();
        // ADDED: PageCount column to the header
        newLines.Add($"{header},FileFound,IsMatch,ExtractedName,PageCount,NormalizedCSV,NormalizedPDF");

        var columnMap = header.Split(',')
                              .Select((name, index) => new { name, index })
                              .ToDictionary(x => x.name.Trim(), x => x.index);

        for (int i = 1; i < lines.Count; i++)
        {
            var columns = lines[i].Split(',');
            string clientId = columns[columnMap["ClientID"]].Trim();
            string csvLastName = columns[columnMap["Last Name"]].Trim();
            string csvFirstName = columns[columnMap["First Name"]].Trim();

            string pdfPath = Path.Combine(_sourceDir, $"{clientId}.pdf");
            bool fileFound = File.Exists(pdfPath);
            bool isMatch = false;
            string extractedFullName = "N/A";
            int pageCount = 0; // ADDED: Initialize page count
            string normalizedCSV = "N/A";
            string normalizedPDF = "N/A";

            if (fileFound)
            {
                // CHANGED: Capture the page count from the tuple
                var (pdfFirstName, pdfLastName, pdfPageCount) = ProcessSinglePdfWrapper(pdfPath);
                extractedFullName = $"{pdfFirstName} {pdfLastName}";
                pageCount = pdfPageCount; // ADDED: Store the page count

                // Create normalized versions for debugging
                string csvFullNameNormalized = $"{SimplifyString(csvFirstName)} {SimplifyString(csvLastName)}";
                string pdfFullNameNormalized = $"{SimplifyString(pdfFirstName)} {SimplifyString(pdfLastName)}";

                normalizedCSV = csvFullNameNormalized;
                normalizedPDF = pdfFullNameNormalized;

                // FIX 2: Compare using a normalization helper to ignore accents and casing
                bool firstNameMatch = CompareNames(csvFirstName, pdfFirstName);
                bool lastNameMatch = CompareNames(csvLastName, pdfLastName);
                isMatch = firstNameMatch && lastNameMatch;

                // ENHANCED LOGGING
                Console.WriteLine($"\n[ClientID: {clientId}]");
                Console.WriteLine($"  CSV Name: {csvFirstName} {csvLastName}");
                Console.WriteLine($"  PDF Name: {pdfFirstName} {pdfLastName}");
                Console.WriteLine($"  Page Count: {pageCount}"); // ADDED: Log page count
                Console.WriteLine($"  Normalized CSV: {csvFullNameNormalized}");
                Console.WriteLine($"  Normalized PDF: {pdfFullNameNormalized}");
                Console.WriteLine($"  First Name Match: {firstNameMatch}");
                Console.WriteLine($"  Last Name Match: {lastNameMatch}");
                Console.WriteLine($"  Overall Match: {isMatch}");

                if (!isMatch)
                {
                    string destPath = Path.Combine(_errorDir, Path.GetFileName(pdfPath));
                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Move(pdfPath, destPath);
                    Console.WriteLine($"  ❌ Moved to error folder: {destPath}");
                }
                else
                {
                    Console.WriteLine($"  ✅ Match confirmed!");
                }
            }

            // CHANGED: Added PageCount to the output line
            newLines.Add($"{lines[i]},{fileFound},{isMatch},{extractedFullName},{pageCount},{normalizedCSV},{normalizedPDF}");
        }

        string outputCsvPath = Path.Combine(_outputDir, "Validation_Results.csv");
        // Ensure we save back in UTF8 so Excel/Text editors see the accents correctly
        File.WriteAllLines(outputCsvPath, newLines, System.Text.Encoding.UTF8);

        Console.WriteLine($"\n✅ Validation complete! Results saved to: {outputCsvPath}");
    }


    /// <summary>
    /// Tries multiple encodings to read the CSV file correctly, handling French accents and special characters.
    /// </summary>
    private List<string> ReadCsvWithBestEncoding(string csvPath)
    {
        Console.WriteLine($"\n🔍 Attempting to read CSV with proper encoding...");
        Console.WriteLine($"   File: {csvPath}");

        // ENHANCED: More robust encoding detection with better validation
        var encodingsToTry = new List<(System.Text.Encoding Encoding, string Name)>
    {
        (System.Text.Encoding.UTF8, "UTF-8"),
        (new System.Text.UTF8Encoding(true), "UTF-8 with BOM"),
        (System.Text.Encoding.GetEncoding(1252), "Windows-1252 (ANSI)"),
        (System.Text.Encoding.GetEncoding("ISO-8859-1"), "Latin-1"),
        (System.Text.Encoding.Default, "System Default")
    };

        List<string>? bestLines = null;
        string bestEncodingName = "UTF-8 (fallback)";
        int bestScore = -1;

        foreach (var (encoding, name) in encodingsToTry)
        {
            try
            {
                var lines = File.ReadAllLines(csvPath, encoding).ToList();

                if (lines.Count == 0) continue;

                // Score this encoding based on various factors
                int score = 0;
                string firstLine = lines[0];
                string allText = string.Join(" ", lines.Take(Math.Min(10, lines.Count))); // Check first 10 lines

                // CRITICAL: Check for encoding error indicators (negative points)
                if (firstLine.Contains('?')) score -= 100;
                if (firstLine.Contains('�')) score -= 100;
                if (allText.Contains("Ã©")) score -= 50;  // UTF-8 read as Windows-1252
                if (allText.Contains("Ã¨")) score -= 50;
                if (allText.Contains("Ã ")) score -= 50;

                // Positive points for valid French characters
                if (allText.Contains('é')) score += 10;
                if (allText.Contains('è')) score += 10;
                if (allText.Contains('à')) score += 10;
                if (allText.Contains('ê')) score += 5;
                if (allText.Contains('ô')) score += 5;
                if (allText.Contains('û')) score += 5;
                if (allText.Contains('ç')) score += 5;

                // Bonus for common French words
                if (allText.Contains("Prénom", StringComparison.OrdinalIgnoreCase)) score += 20;
                if (allText.Contains("Théo", StringComparison.OrdinalIgnoreCase)) score += 20;

                Console.WriteLine($"   Tried {name}: Score={score}");

                // Debug: Show first line for troubleshooting
                if (lines.Count > 0)
                {
                    Console.WriteLine($"      First line preview: {lines[0].Substring(0, Math.Min(80, lines[0].Length))}...");
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestLines = lines;
                    bestEncodingName = name;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠ Failed to read with {name}: {ex.Message}");
            }
        }

        if (bestLines != null && bestScore >= 0)
        {
            Console.WriteLine($"✅ Selected {bestEncodingName} (score: {bestScore})");

            // Additional validation: Show a sample of French characters found
            var sampleLine = bestLines.FirstOrDefault(line =>
                line.Contains('é') || line.Contains('è') || line.Contains('à'));
            if (sampleLine != null)
            {
                Console.WriteLine($"   ✓ Sample with French chars: {sampleLine.Substring(0, Math.Min(100, sampleLine.Length))}");
            }

            return bestLines;
        }

        // Last resort: Try to read as raw bytes and detect BOM
        Console.WriteLine("⚠ All encodings failed, trying BOM detection...");
        var detectedEncoding = DetectEncodingFromBom(csvPath);
        Console.WriteLine($"   Detected from BOM: {detectedEncoding.EncodingName}");

        return File.ReadAllLines(csvPath, detectedEncoding).ToList();
    }


    /// <summary>
    /// Detects encoding from BOM (Byte Order Mark) if present
    /// </summary>
    private System.Text.Encoding DetectEncodingFromBom(string csvPath)
    {
        // Read first few bytes to check for BOM
        var bom = new byte[4];
        using (var file = new FileStream(csvPath, FileMode.Open, FileAccess.Read))
        {
            file.Read(bom, 0, 4);
        }

        // Check for UTF-8 BOM (EF BB BF)
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            Console.WriteLine("   Found UTF-8 BOM");
            return new System.Text.UTF8Encoding(true);
        }

        // Check for UTF-16 LE BOM (FF FE)
        if (bom[0] == 0xFF && bom[1] == 0xFE)
        {
            Console.WriteLine("   Found UTF-16 LE BOM");
            return System.Text.Encoding.Unicode;
        }

        // Check for UTF-16 BE BOM (FE FF)
        if (bom[0] == 0xFE && bom[1] == 0xFF)
        {
            Console.WriteLine("   Found UTF-16 BE BOM");
            return System.Text.Encoding.BigEndianUnicode;
        }

        // No BOM detected - likely ANSI/Windows-1252
        Console.WriteLine("   No BOM found - assuming Windows-1252");
        return System.Text.Encoding.GetEncoding(1252);
    }


    private (string firstName, string lastName, int pageCount) ProcessSinglePdfWrapper(string filePath)
    {
        // Create a debug folder if needed
        string debugDir = Path.Combine(_sourceDir, "_debug");
        if (!Directory.Exists(debugDir))
        {
            Directory.CreateDirectory(debugDir);
        }

        // Directly call the static refactored method
        // Note: Use 'true' for debugOcr if you want the images/text files saved during validation
        return PdfProcessor.ProcessSinglePdf(filePath, debugOcr: true, debugOutputDir: debugDir);
    }

    private bool CompareNames(string nameA, string nameB)
    {
        if (string.IsNullOrEmpty(nameA) || string.IsNullOrEmpty(nameB)) return false;

        // Exact match after normalization
        string normalizedA = SimplifyString(nameA);
        string normalizedB = SimplifyString(nameB);

        if (normalizedA == normalizedB)
            return true;

        // Fuzzy match: Allow up to 2 character differences (for OCR errors)
        int distance = LevenshteinDistance(normalizedA, normalizedB);
        int maxAllowedDistance = Math.Max(normalizedA.Length, normalizedB.Length) / 5; // 20% difference allowed

        bool fuzzyMatch = distance <= maxAllowedDistance;

        if (fuzzyMatch && distance > 0)
        {
            Console.WriteLine($"    ⚠ Fuzzy match: '{nameA}' ≈ '{nameB}' (distance: {distance})");
        }

        return fuzzyMatch;
    }

    /// <summary>
    /// Levenshtein distance algorithm for fuzzy string matching
    /// </summary>
    private int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        // FIXED: Added missing 'i' in the for loop condition
        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    /// <summary>
    /// Normalizes a string by removing accents, converting to lowercase, and removing special characters.
    /// Essential for comparing names with French accents.
    /// </summary>
    private string SimplifyString(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Step 1: Normalize to Form D (decomposes characters like 'é' into 'e' + '´')
        string normalizedString = text.Trim().ToLower().Normalize(System.Text.NormalizationForm.FormD);

        // Step 2: Filter out the non-spacing mark (the accents)
        var stringBuilder = new System.Text.StringBuilder();
        foreach (char c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        // Step 3: Normalize back to Form C and remove any remaining oddities (like hyphens)
        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC).Replace("-", "");
    }


    /// <summary>
    /// DEBUG: Shows the raw bytes of a specific character in the file
    /// </summary>
    private void DebugShowBytes(string csvPath, int lineNumber = 1)
    {
        Console.WriteLine($"\n🔬 DEBUG: Raw byte analysis of line {lineNumber}:");

        // Read as raw bytes
        var allBytes = File.ReadAllBytes(csvPath);
        var lines = File.ReadAllLines(csvPath, System.Text.Encoding.GetEncoding(1252));

        if (lineNumber < lines.Length)
        {
            string line = lines[lineNumber];
            Console.WriteLine($"   Line content (Windows-1252): {line}");

            // Find "Th?o" or similar patterns
            int thIndex = line.IndexOf("Th", StringComparison.OrdinalIgnoreCase);
            if (thIndex >= 0 && thIndex + 4 < line.Length)
            {
                string suspect = line.Substring(thIndex, Math.Min(6, line.Length - thIndex));
                Console.WriteLine($"   Suspect word: '{suspect}'");

                byte[] bytes = System.Text.Encoding.GetEncoding(1252).GetBytes(suspect);
                Console.WriteLine($"   Bytes (1252): {BitConverter.ToString(bytes)}");

                bytes = System.Text.Encoding.UTF8.GetBytes(suspect);
                Console.WriteLine($"   Bytes (UTF-8): {BitConverter.ToString(bytes)}");
            }
        }
    }

}