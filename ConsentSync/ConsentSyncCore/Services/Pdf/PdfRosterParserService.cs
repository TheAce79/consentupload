using System.Globalization;
using System.Text.RegularExpressions;
using ConsentSyncCore.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ConsentSyncCore.Services.Pdf;

public class PdfRosterParserService
{
    private static readonly Regex NameDobRegex = new(
        @"(?<FullName>[\p{L}\s,'\-.]+?)\s*\(?\b(?<DOB>\d{4}-\d{2}-\d{2})\b\)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MedicareRegex = new(@"\b(?:\d{9}|\d{3}\s?\d{3}\s?\d{3})\b", RegexOptions.Compiled);
    private static readonly Regex AppointmentPrefixRegex = new(@"^\s*[\u2605]?\s*\d{1,2}h\d{2}\s*(?:\d/\d)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public List<ClinicPdfClientRecord> ExtractRecordsFromPdfFolder(string inputPdfDir)
    {
        if (!Directory.Exists(inputPdfDir)) return [];

        var records = new List<ClinicPdfClientRecord>();
        foreach (string pdfPath in Directory.EnumerateFiles(inputPdfDir, "*.pdf", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                using var document = PdfDocument.Open(pdfPath);
                foreach (var page in document.GetPages())
                {
                    records.AddRange(ExtractRecordsFromLines(ReconstructLines(page.GetWords())));
                }
            }
            catch (Exception ex) when (ex is not PdfRosterExtractionException)
            {
                throw new PdfRosterExtractionException($"Could not read PDF '{Path.GetFileName(pdfPath)}'.", ex);
            }
        }

        return records;
    }

    public static List<ClinicPdfClientRecord> ExtractRecordsFromLines(IEnumerable<string> lines)
    {
        var records = new List<ClinicPdfClientRecord>();
        foreach (string line in lines)
        {
            string cleanLine = AppointmentPrefixRegex.Replace(line, string.Empty).Trim();
            Match match = NameDobRegex.Match(cleanLine);
            if (!match.Success || !DateOnly.TryParseExact(match.Groups["DOB"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsedDob))
            {
                continue;
            }

            string fullName = WhitespaceRegex.Replace(match.Groups["FullName"].Value, " ").Trim();
            fullName = fullName.TrimStart('-', '=', '+', '@', ',', ' ', '\t', '–', '—', '•', '●', '▪', '◦')
                .TrimEnd(',', '-')
                .Trim();
            if (fullName.Length < 3 || fullName.Equals("CIP", StringComparison.OrdinalIgnoreCase)) continue;

            Match medicare = MedicareRegex.Match(line);
            records.Add(new ClinicPdfClientRecord
            {
                FullName = fullName,
                DateOfBirth = parsedDob.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                Medicare = medicare.Success ? medicare.Value.Replace(" ", string.Empty) : null
            });
        }

        return records;
    }

    private static IEnumerable<string> ReconstructLines(IEnumerable<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (Word word in words.OrderByDescending(word => word.BoundingBox.Bottom).ThenBy(word => word.BoundingBox.Left))
        {
            double tolerance = Math.Max(1.5, word.BoundingBox.Height * 0.5);
            List<Word>? line = lines.FirstOrDefault(existing =>
                Math.Abs(existing.Average(item => item.BoundingBox.Bottom) - word.BoundingBox.Bottom) <= tolerance);
            if (line is null)
            {
                line = [];
                lines.Add(line);
            }
            line.Add(word);
        }

        return lines.OrderByDescending(line => line.Average(word => word.BoundingBox.Bottom))
            .Select(line => string.Join(" ", line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)));
    }
}

public sealed class PdfRosterExtractionException : Exception
{
    public PdfRosterExtractionException(string message, Exception innerException) : base(message, innerException) { }
}
