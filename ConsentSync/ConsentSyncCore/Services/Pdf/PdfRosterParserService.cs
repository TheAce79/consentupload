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
    private static readonly Regex TimeRangeStartRegex = new(@"^\s*(?<start>\d{1,2}:\d{2}\s*(?:AM|PM)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StandaloneTimeRangeRegex = new(@"^\s*(?<start>\d{1,2}:\d{2}\s*(?:AM|PM)?)\s*[-–—]\s*\d{1,2}:\d{2}\s*(?:AM|PM)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AppointmentPrefixRegex = new(@"^\s*[\u2605]?\s*(?<time>\d{1,2}h\d{2})\s*(?:\d+\s*/\s*\d+)?\s*(?:[-–—]\s*)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex MilestoneRegex = new(@"\b\d+\s*(?:mois|m|months?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CategoryRegex = new(@"\b(?:autres?|PS|preschool|Mpox|rattrapage|initiale)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EtsScheduleHeaderRegex = new(@"^\s*ETS\b.*\b(?:inf\.?|infirm)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EtsPlusRegex = new(@"\bMMRV\b|\bETS\s*-\s*Vaccination\b|\bETS\s*\+(?!\w)|\bETS\s+Plus\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EtsEvaluationRegex = new(@"\bevaluation\s+only\b|\bassessment\s+only\b|\bETS\s+seulement\b|\bETS\s+uniquement\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EmailRegex = new(@"\b[^\s@]+@[^\s@]+\.[^\s@]+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ContactPhoneRegex = new(@"\b(?:[\p{L}'-]+\s+){0,3}(?:\+?1[\s.-]?)?(?:\(?\d{3}\)?[\s.-]?)\d{3}[\s.-]\d{4}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ParentheticalRegex = new(@"\([^)]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AdministrativeCodeRegex = new(@"\b[A-Za-z]{1,3}(?:-[A-Za-z]{1,3})*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RoutinePunctuationRegex = new(@"[\s\d*\-/–—,;:.()]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                using var document = PdfDocument.Open(path);
                var pages = document.GetPages().Select(page => ReconstructLines(page.GetWords()).ToArray()).ToList();
                bool isEtsClinic = IsEtsClinicSchedule(Path.GetFileName(path), pages.SelectMany(page => page));
                int pageNumber = 0;
                foreach (string[] pageLines in pages)
                {
                    pageNumber++; var extracted = ExtractRecordsFromLines(pageLines, isEtsClinic); records.AddRange(extracted);
                    if (extracted.Count == 0) { var warning = $"{Path.GetFileName(path)}, page {pageNumber} returned no client records."; LastPageWarnings.Add(warning); diagnostics?.Invoke($"Warning: {warning}"); }
                }
            }
            catch (Exception ex) when (ex is not PdfRosterExtractionException) { throw new PdfRosterExtractionException($"Could not read PDF '{Path.GetFileName(path)}'.", ex); }
        }
        return records;
    }

    /// <summary>Extracts a page of roster lines. Set <paramref name="isEtsClinic"/> when its containing PDF is an ETS schedule.</summary>
    public static List<ClinicPdfClientRecord> ExtractRecordsFromLines(IEnumerable<string> lines, bool isEtsClinic = false)
    {
        var pageLines = lines.Select(x => WhitespaceRegex.Replace(x.Replace('|', ' '), " ").Trim()).Where(x => x.Length > 0).ToArray();
        var records = new List<ClinicPdfClientRecord>();
        string? pendingSectionTimeslot = null;
        for (var i = 0; i < pageLines.Length; i++)
        {
            Match sectionHeader = StandaloneTimeRangeRegex.Match(pageLines[i]);
            if (sectionHeader.Success && !DobRegex.IsMatch(pageLines[i]))
            {
                pendingSectionTimeslot = sectionHeader.Groups["start"].Value.Trim();
                continue;
            }
            if (!StartsAppointmentBlock(pageLines[i])) continue;
            string? timeslot = pendingSectionTimeslot ?? ExtractTimeslot(pageLines[i]);
            pendingSectionTimeslot = null;
            var details = RemoveAppointmentPrefixes(pageLines[i]);
            if (string.IsNullOrWhiteSpace(details) && i + 1 < pageLines.Length && !StartsAppointmentBlock(pageLines[i + 1])) details = pageLines[++i];
            var dob = DobRegex.Match(details);
            if (!dob.Success || !DateOnly.TryParseExact(dob.Groups["dob"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            var name = details[..dob.Index].Trim().Trim('-', ',', '=', '+', '@', ' ', '\t', '–', '—', '•', '●', '▪', '◦');
            name = WhitespaceRegex.Replace(name, " ").Trim().TrimEnd('(', ',', '-').Trim();
            if (name.Length < 3 || name.Equals("CIP", StringComparison.OrdinalIgnoreCase)) continue;
            var medicare = MedicareRegex.Match(details); var vaccine = MilestoneRegex.Match(details); if (!vaccine.Success) vaccine = CategoryRegex.Match(details);
            int continuationIndex = i + 1;
            var appointmentBlock = new List<string> { details };
            while (continuationIndex < pageLines.Length && !StartsAppointmentBlock(pageLines[continuationIndex]))
                appointmentBlock.Add(pageLines[continuationIndex++]);
            i = continuationIndex - 1;

            records.Add(new()
            {
                FullName = name,
                DateOfBirth = date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                Medicare = medicare.Success ? medicare.Value.Replace(" ", "") : null,
                Timeslot = timeslot,
                VaccineType = isEtsClinic
                    ? ClassifyEtsAppointment(appointmentBlock, dob)
                    : VaccineTypeNormalizer.Normalize(vaccine.Success ? WhitespaceRegex.Replace(vaccine.Value, " ") : null)
            });
        }
        return records;
    }

    /// <summary>Determines whether a PDF is an ETS clinic schedule from its name or schedule header.</summary>
    public static bool IsEtsClinicSchedule(string? sourceFileName, IEnumerable<string> documentLines)
    {
        ArgumentNullException.ThrowIfNull(documentLines);
        if (!string.IsNullOrWhiteSpace(sourceFileName) && sourceFileName.Contains("ETS_", StringComparison.OrdinalIgnoreCase)) return true;
        return documentLines.Any(line =>
        {
            string header = RemoveAppointmentPrefixes(WhitespaceRegex.Replace(line.Replace('|', ' '), " ").Trim());
            return !DobRegex.IsMatch(header) && EtsScheduleHeaderRegex.IsMatch(header);
        });
    }

    private static string ClassifyEtsAppointment(IEnumerable<string> appointmentBlock, Match dob)
    {
        string[] lines = appointmentBlock.ToArray();
        string block = string.Join(" ", lines);
        if (EtsPlusRegex.IsMatch(block)) return "ETS+";
        if (EtsEvaluationRegex.IsMatch(block)) return "ETS";

        string afterDob = lines[0][(dob.Index + dob.Length)..];
        if (lines.Length > 1) afterDob = string.Join(" ", new[] { afterDob }.Concat(lines.Skip(1)));
        return string.IsNullOrWhiteSpace(RemoveRoutineRosterMetadata(afterDob)) ? "ETS" : "ETS Unknown";
    }

    private static string RemoveRoutineRosterMetadata(string text)
    {
        string remaining = ParentheticalRegex.Replace(text, " ");
        remaining = EmailRegex.Replace(remaining, " ");
        remaining = ContactPhoneRegex.Replace(remaining, " ");
        remaining = MedicareRegex.Replace(remaining, " ");
        remaining = AdministrativeCodeRegex.Replace(remaining, " ");
        return RoutinePunctuationRegex.Replace(remaining, string.Empty);
    }

    private static bool StartsAppointmentBlock(string line) => TimeRangePrefixRegex.IsMatch(line) || AppointmentPrefixRegex.IsMatch(line);
    private static string? ExtractTimeslot(string line)
    {
        if (TimeRangePrefixRegex.IsMatch(line)) return TimeRangeStartRegex.Match(line).Groups["start"].Value.Trim();
        Match appointment = AppointmentPrefixRegex.Match(line);
        return appointment.Success ? appointment.Groups["time"].Value.Trim() : null;
    }
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
