using System.Globalization;
using System.Text.RegularExpressions;
using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ConsentSyncCore.Services.Pdf;

public class PdfRosterParserService
{
    public List<string> LastPageWarnings { get; } = [];
    private static readonly Regex DobRegex = new(@"\b(?<dob>\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MedicareRegex = new(@"\b(?:\d{9}|\d{3}\s?\d{3}\s?\d{3})\b", RegexOptions.Compiled);
    private static readonly Regex TimeRangePrefixRegex = new(@"^\s*(?:\d{1,2}:\d{2}\s*(?:AM|PM)?\s*(?:[-–—]\s*|\s+)\d{1,2}:\d{2}\s*(?:AM|PM)?|\d{1,2}:\d{2}\s*-\s*\d{1,2}:\d{2})\b\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AppointmentPrefixRegex = new(@"^\s*[\u2605]?\s*\d{1,2}h\d{2}\s*(?:\d+\s*/\s*\d+)?\s*(?:[-–—]\s*)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex MilestoneRegex = new(@"\b\d+\s*(?:mois|m|months?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CategoryRegex = new(@"\b(?:autres?|PS|Mpox|rattrapage|initiale)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public List<ClinicPdfClientRecord> ExtractRecordsFromPdfFolder(string folder) => !Directory.Exists(folder) ? [] : ExtractRecordsFromPdfFiles(Directory.EnumerateFiles(folder, "*.pdf").OrderBy(x => x, StringComparer.Ordinal));

    public static void AssignClinicDate(IEnumerable<ClinicPdfClientRecord> records, DateTime cohortDate, bool overwriteExisting = false)
    {
        ArgumentNullException.ThrowIfNull(records);
        string clinicDate = cohortDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (ClinicPdfClientRecord record in records)
        {
            if (overwriteExisting || string.IsNullOrWhiteSpace(record.ClinicDate)) record.ClinicDate = clinicDate;
        }
    }

    public List<ClinicPdfClientRecord> ExtractRecordsFromPdfFiles(IEnumerable<string> paths, Action<string>? diagnostics = null)
    {
        LastPageWarnings.Clear();
        var files = paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        diagnostics?.Invoke($"PDF schedule import: {files.Count} file(s): {string.Join(", ", files.Select(Path.GetFileName))}");
        var records = new List<ClinicPdfClientRecord>();
        foreach (var path in files)
        {
            try
            {
                using var document = PdfDocument.Open(path); int pageNumber = 0;
                foreach (var page in document.GetPages())
                {
                    pageNumber++; var extracted = ExtractRecordsFromLines(ReconstructLines(page.GetWords())); records.AddRange(extracted);
                    if (extracted.Count == 0) { var warning = $"{Path.GetFileName(path)}, page {pageNumber} returned no client records."; LastPageWarnings.Add(warning); diagnostics?.Invoke($"Warning: {warning}"); }
                }
            }
            catch (Exception ex) when (ex is not PdfRosterExtractionException) { throw new PdfRosterExtractionException($"Could not read PDF '{Path.GetFileName(path)}'.", ex); }
        }
        return records;
    }

    public static List<ClinicPdfClientRecord> ExtractRecordsFromLines(IEnumerable<string> lines)
    {
        var pageLines = lines.Select(x => WhitespaceRegex.Replace(x.Replace('|', ' '), " ").Trim()).Where(x => x.Length > 0).ToArray();
        var records = new List<ClinicPdfClientRecord>();
        for (var i = 0; i < pageLines.Length; i++)
        {
            if (!StartsAppointmentBlock(pageLines[i])) continue;
            var details = RemoveAppointmentPrefixes(pageLines[i]);
            if (string.IsNullOrWhiteSpace(details) && i + 1 < pageLines.Length && !StartsAppointmentBlock(pageLines[i + 1])) details = pageLines[++i];
            var dob = DobRegex.Match(details);
            if (!dob.Success || !DateOnly.TryParseExact(dob.Groups["dob"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            var name = details[..dob.Index].Trim().Trim('-', ',', '=', '+', '@', ' ', '\t', '–', '—', '•', '●', '▪', '◦');
            name = WhitespaceRegex.Replace(name, " ").Trim().TrimEnd('(', ',', '-').Trim();
            if (name.Length < 3 || name.Equals("CIP", StringComparison.OrdinalIgnoreCase)) continue;
            var medicare = MedicareRegex.Match(details); var vaccine = MilestoneRegex.Match(details); if (!vaccine.Success) vaccine = CategoryRegex.Match(details);
            records.Add(new() { FullName = name, DateOfBirth = date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture), Medicare = medicare.Success ? medicare.Value.Replace(" ", "") : null, VaccineType = VaccineTypeNormalizer.Normalize(vaccine.Success ? WhitespaceRegex.Replace(vaccine.Value, " ") : null) });
        }
        return records;
    }

    private static bool StartsAppointmentBlock(string line) => TimeRangePrefixRegex.IsMatch(line) || AppointmentPrefixRegex.IsMatch(line);
    private static string RemoveAppointmentPrefixes(string line) => AppointmentPrefixRegex.Replace(TimeRangePrefixRegex.Replace(line, ""), "").Trim();
    private static IEnumerable<string> ReconstructLines(IEnumerable<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(x => x.BoundingBox.Bottom).ThenBy(x => x.BoundingBox.Left))
        { var tolerance = Math.Max(1.5, word.BoundingBox.Height * .5); var line = lines.FirstOrDefault(x => Math.Abs(x.Average(y => y.BoundingBox.Bottom) - word.BoundingBox.Bottom) <= tolerance); if (line is null) { line = []; lines.Add(line); } line.Add(word); }
        return lines.OrderByDescending(x => x.Average(y => y.BoundingBox.Bottom)).Select(x => string.Join(" ", x.OrderBy(y => y.BoundingBox.Left).Select(y => y.Text)));
    }
}

public sealed class PdfRosterExtractionException : Exception { public PdfRosterExtractionException(string message, Exception innerException) : base(message, innerException) { } }
