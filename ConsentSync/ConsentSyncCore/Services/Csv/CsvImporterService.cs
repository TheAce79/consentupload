using System.Globalization;
using System.Text;
using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using CsvHelper;
using CsvHelper.Configuration;

namespace ConsentSyncCore.Services.Csv;

public static class CsvImporterService
{
    private static readonly string[] SupportedAbleAssessDateFormats = ["M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "d/M/yyyy", "dd/MM/yyyy"];
    private static readonly string[] RequiredCanonicalFields = ["ClientId", "FullName", "DateOfBirth", "Medicare", "ClientIdStatus", "FirstName", "LastName", "MiddleName"];
    private static readonly IReadOnlyDictionary<string, string[]> CanonicalHeaders = new Dictionary<string, string[]>
    {
        ["ClientId"] = ["ClientId"], ["FullName"] = ["FullName"], ["DateOfBirth"] = ["DateOfBirth"], ["Medicare"] = ["Medicare"],
        ["ClientIdStatus"] = ["ClientIdStatus"], ["FirstName"] = ["FirstName"], ["LastName"] = ["LastName"], ["MiddleName"] = ["MiddleName"],
        ["ErrorDetails"] = ["ErrorDetails"], ["BestMatch"] = ["BestMatch"], ["Phone"] = ["Phone", "Phone Number", "Telephone Number"],
        ["Email"] = ["Email", "Email Address"], ["VaccineType"] = ["VaccineType"],
        ["BookingId"] = ["Booking ID"], ["ClinicName"] = ["Clinic Name"], ["ClinicDate"] = ["Clinic Date", "ClinicDate"],
        ["AppointmentType"] = ["Appointment Type"], ["CatalogItem"] = ["Catalog Item"], ["Timeslot"] = ["Timeslot"],
        ["Comment"] = ["Comment"], ["SdcId"] = ["SDC Id"], ["PreferredLanguage"] = ["Preferred Language"]
    };

    // Provisional aliases pending a real French AbleAssess export. Add verified aliases here.
    private static readonly IReadOnlyDictionary<string, string[]> AbleAssessHeaders = new Dictionary<string, string[]>
    {
        ["BookingId"] = ["Booking ID", "BookingID", "ID de réservation", "No de réservation", "ID réservation"],
        ["ClinicName"] = ["Clinic Name"], ["ClinicDate"] = ["Clinic Date", "Date de la clinique"],
        ["AppointmentType"] = ["Appointment Type", "Type de rendez-vous"], ["CatalogItem"] = ["Catalog Item", "Article du catalogue"],
        ["FullName"] = ["Enrolled Person Name", "Nom de la personne inscrite", "Nom"],
        ["Medicare"] = ["Medicare Number", "Numéro d'assurance-maladie", "Assurance-maladie"],
        ["Email"] = ["Email", "Courriel", "Adresse courriel"], ["DateOfBirth"] = ["Date of Birth", "Date de naissance"],
        ["Phone"] = ["Phone", "Téléphone", "No de téléphone"], ["Timeslot"] = ["Timeslot", "Plage horaire", "Heure"],
        ["Comment"] = ["Comment"], ["SdcId"] = ["SDC Id"], ["PreferredLanguage"] = ["Preferred Language", "Langue préférée"]
    };

    public static List<ClinicPdfClientRecord> ReadFromCsv(string csvFilePath, string? ableAssessDateFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvFilePath);
        if (!File.Exists(csvFilePath)) throw new FileNotFoundException("CSV input file was not found.", csvFilePath);

        using var reader = new StreamReader(csvFilePath);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = args => throw new FormatException($"Malformed CSV data at row {args.Context?.Parser?.Row ?? 0}."),
            MissingFieldFound = args => throw new FormatException($"Missing field at row {args.Context?.Parser?.Row ?? 0}."),
            HeaderValidated = null, TrimOptions = TrimOptions.Trim
        });

        if (!csv.Read() || !csv.ReadHeader()) return [];
        string[] headers = csv.HeaderRecord ?? [];
        bool isCanonical = RequiredCanonicalFields.All(field => FindMatchingIndexes(headers, CanonicalHeaders[field]).Count > 0);
        var fieldIndexes = ResolveHeaders(headers, isCanonical ? CanonicalHeaders : AbleAssessHeaders);
        InputSourceType source = isCanonical ? InputSourceType.PdfRoster : DetectSource(fieldIndexes);
        if (source == InputSourceType.PdfRoster) ValidateCanonicalHeaders(fieldIndexes);
        else ValidateAbleAssessHeaders(fieldIndexes);
        string[] ableAssessDateFormats = source == InputSourceType.AbleAssess
            ? GetAbleAssessDateFormats(ableAssessDateFormat)
            : [];

        var records = new List<ClinicPdfClientRecord>();
        while (csv.Read())
        {
            try { records.Add(source == InputSourceType.AbleAssess ? ReadAbleAssess(csv, fieldIndexes, ableAssessDateFormats) : ReadCanonical(csv, fieldIndexes)); }
            catch (Exception ex) when (ex is not FormatException || !ex.Message.Contains("row", StringComparison.OrdinalIgnoreCase))
            { throw new FormatException($"CSV row {csv.Parser.Row}: {ex.Message}", ex); }
        }
        return records;
    }

    /// <summary>Adds an empty Clinic Date column to a legacy canonical cohort CSV without changing its source values.</summary>
    public static bool AppendMissingClinicDateColumn(string csvFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvFilePath);
        if (!File.Exists(csvFilePath)) throw new FileNotFoundException("CSV input file was not found.", csvFilePath);

        string[] headers;
        var rows = new List<string[]>();
        using (var reader = new StreamReader(csvFilePath))
        using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = args => throw new FormatException($"Malformed CSV data at row {args.Context?.Parser?.Row ?? 0}."),
            MissingFieldFound = args => throw new FormatException($"Missing field at row {args.Context?.Parser?.Row ?? 0}."),
            HeaderValidated = null, TrimOptions = TrimOptions.Trim
        }))
        {
            if (!csv.Read() || !csv.ReadHeader()) return false;
            headers = csv.HeaderRecord ?? [];
            bool isCanonical = RequiredCanonicalFields.All(field => FindMatchingIndexes(headers, CanonicalHeaders[field]).Count > 0);
            bool hasClinicDate = FindMatchingIndexes(headers, CanonicalHeaders["ClinicDate"]).Count > 0;
            if (!isCanonical || hasClinicDate) return false;

            while (csv.Read())
                rows.Add(Enumerable.Range(0, headers.Length).Select(index => csv.GetField(index) ?? string.Empty).ToArray());
        }

        string directory = Path.GetDirectoryName(csvFilePath) ?? throw new ArgumentException("The CSV path must include a directory.", nameof(csvFilePath));
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(csvFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { ShouldQuote = _ => true }))
            {
                foreach (string header in headers) csv.WriteField(header);
                csv.WriteField("Clinic Date");
                csv.NextRecord();
                foreach (string[] row in rows)
                {
                    foreach (string value in row) csv.WriteField(value);
                    csv.WriteField(string.Empty);
                    csv.NextRecord();
                }
            }

            File.Replace(temporaryPath, csvFilePath, null);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static Dictionary<string, int> ResolveHeaders(string[] headers, IReadOnlyDictionary<string, string[]> aliases)
    {
        var resolved = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string field, string[] fieldAliases) in aliases)
        {
            List<int> matches = FindMatchingIndexes(headers, fieldAliases);
            if (matches.Count > 1) throw new FormatException($"CSV header maps multiple columns to '{field}': {string.Join(", ", matches.Select(index => $"'{headers[index]}'"))}.");
            if (matches.Count == 1) resolved[field] = matches[0];
        }
        return resolved;
    }

    private static List<int> FindMatchingIndexes(string[] headers, IEnumerable<string> aliases) => headers.Select((header, index) => new { header, index })
        .Where(item => HeaderMatchesAliases(item.header, aliases)).Select(item => item.index).ToList();

    private static bool HeaderMatchesAliases(string header, IEnumerable<string> aliases)
    {
        string normalizedHeader = NormalizeHeader(header);
        string[] normalizedAliases = aliases.Select(NormalizeHeader).ToArray();
        if (normalizedAliases.Any(alias => string.Equals(normalizedHeader, alias, StringComparison.OrdinalIgnoreCase))) return true;
        string[] segments = normalizedHeader.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 && segments.All(segment => normalizedAliases.Any(alias => string.Equals(segment, alias, StringComparison.OrdinalIgnoreCase)));
    }

    private static InputSourceType DetectSource(IReadOnlyDictionary<string, int> indexes) => indexes.ContainsKey("BookingId") ? InputSourceType.AbleAssess : InputSourceType.PdfRoster;
    private static void ValidateCanonicalHeaders(IReadOnlyDictionary<string, int> indexes)
    {
        foreach (string field in RequiredCanonicalFields)
            if (!indexes.ContainsKey(field)) throw new FormatException($"CSV header is missing required column '{CanonicalHeaders[field][0]}'.");
    }
    private static void ValidateAbleAssessHeaders(IReadOnlyDictionary<string, int> indexes)
    {
        foreach (string field in new[] { "FullName", "DateOfBirth" })
            if (!indexes.ContainsKey(field)) throw new FormatException($"AbleAssess CSV header is missing required column '{AbleAssessHeaders[field][0]}'.");
    }

    private static ClinicPdfClientRecord ReadCanonical(CsvReader csv, IReadOnlyDictionary<string, int> fields) => new()
    {
        ClientId = NullIfEmpty(Get(csv, fields, "ClientId")), FullName = Get(csv, fields, "FullName"), DateOfBirth = Get(csv, fields, "DateOfBirth"),
        Medicare = NullIfEmpty(Get(csv, fields, "Medicare")), VaccineType = VaccineTypeNormalizer.Normalize(NullIfEmpty(Get(csv, fields, "VaccineType"))), ClientIdStatus = ParseStatus(Get(csv, fields, "ClientIdStatus")),
        FirstName = NullIfEmpty(Get(csv, fields, "FirstName")), LastName = NullIfEmpty(Get(csv, fields, "LastName")), MiddleName = NullIfEmpty(Get(csv, fields, "MiddleName")),
        ErrorDetails = NullIfEmpty(Get(csv, fields, "ErrorDetails")), BestMatch = NullIfEmpty(Get(csv, fields, "BestMatch")), Email = NullIfEmpty(Get(csv, fields, "Email")), Phone = NullIfEmpty(Get(csv, fields, "Phone")),
        BookingId = NullIfEmpty(Get(csv, fields, "BookingId")), ClinicName = NullIfEmpty(Get(csv, fields, "ClinicName")), ClinicDate = NullIfEmpty(Get(csv, fields, "ClinicDate")), AppointmentType = NullIfEmpty(Get(csv, fields, "AppointmentType")), CatalogItem = NullIfEmpty(Get(csv, fields, "CatalogItem")), Timeslot = NullIfEmpty(Get(csv, fields, "Timeslot")), Comment = NullIfEmpty(Get(csv, fields, "Comment")), SdcId = NullIfEmpty(Get(csv, fields, "SdcId")), PreferredLanguage = NullIfEmpty(Get(csv, fields, "PreferredLanguage"))
    };

    public static bool IsSupportedAbleAssessDateFormat(string? format) =>
        !string.IsNullOrWhiteSpace(format) && SupportedAbleAssessDateFormats.Contains(format.Trim(), StringComparer.Ordinal);

    private static string[] GetAbleAssessDateFormats(string? preferredFormat)
    {
        string format = string.IsNullOrWhiteSpace(preferredFormat) ? "M/d/yyyy" : preferredFormat.Trim();
        if (!IsSupportedAbleAssessDateFormat(format))
            throw new FormatException($"Unsupported AbleAssess date format '{format}'. Use one of: {string.Join(", ", SupportedAbleAssessDateFormats)}.");
        return [format, .. SupportedAbleAssessDateFormats.Where(candidate => !string.Equals(candidate, format, StringComparison.Ordinal))];
    }

    private static ClinicPdfClientRecord ReadAbleAssess(CsvReader csv, IReadOnlyDictionary<string, int> fields, string[] dateFormats)
    {
        string dob = Get(csv, fields, "DateOfBirth");
        DateOnly? dateOfBirth = ParseAbleAssessDate(dob, "Date of Birth", dateFormats);
        string clinicDate = Get(csv, fields, "ClinicDate");
        DateOnly? parsedClinicDate = ParseAbleAssessDate(clinicDate, "Clinic Date", dateFormats);
        return new ClinicPdfClientRecord
        {
            ClientId = null, FullName = Get(csv, fields, "FullName").Trim(), DateOfBirth = dateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            Medicare = NullIfEmptyOrNaN(Get(csv, fields, "Medicare")), ClientIdStatus = ClientIdStatus.NeedsManualReview, ErrorDetails = string.Empty, BestMatch = string.Empty,
            Phone = NullIfEmptyOrNaN(Get(csv, fields, "Phone")), Email = NullIfEmpty(Get(csv, fields, "Email")), VaccineType = VaccineTypeNormalizer.Normalize(NullIfEmpty(Get(csv, fields, "CatalogItem")) ?? NullIfEmpty(Get(csv, fields, "AppointmentType"))),
            BookingId = NullIfEmpty(Get(csv, fields, "BookingId")), ClinicName = NullIfEmpty(Get(csv, fields, "ClinicName")), ClinicDate = parsedClinicDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), AppointmentType = NullIfEmpty(Get(csv, fields, "AppointmentType")), CatalogItem = NullIfEmpty(Get(csv, fields, "CatalogItem")), Timeslot = NullIfEmpty(Get(csv, fields, "Timeslot")), Comment = NullIfEmpty(Get(csv, fields, "Comment")), SdcId = NullIfEmpty(Get(csv, fields, "SdcId")), PreferredLanguage = NullIfEmpty(Get(csv, fields, "PreferredLanguage"))
        };
    }

    private static DateOnly? ParseAbleAssessDate(string value, string fieldName, string[] dateFormats)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateOnly.TryParseExact(value.Trim(), dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
            throw new FormatException($"Invalid {fieldName} '{value}'.");
        return parsed;
    }

    private static ClientIdStatus ParseStatus(string status)
    {
        if (!int.TryParse(status, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || !Enum.IsDefined(typeof(ClientIdStatus), value)) throw new FormatException($"Invalid ClientIdStatus '{status}'.");
        return (ClientIdStatus)value;
    }
    private static string Get(CsvReader csv, IReadOnlyDictionary<string, int> fields, string field) => fields.TryGetValue(field, out int index) ? csv.GetField(index) ?? string.Empty : string.Empty;
    private static string NormalizeHeader(string header) => string.Join(' ', header.Trim().Replace('\u2019', '\'').Replace('\u2018', '\'').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NullIfEmptyOrNaN(string value) => string.Equals(value.Trim(), "NaN", StringComparison.OrdinalIgnoreCase) ? null : NullIfEmpty(value);
}
