using System.Globalization;
using ConsentSyncCore.Models;
using CsvHelper;
using CsvHelper.Configuration;

namespace ConsentSyncCore.Services.Csv;

public static class CsvImporterService
{
    private static readonly string[] RequiredCanonicalHeaders = ["ClientId", "FullName", "DateOfBirth", "Medicare", "ClientIdStatus", "FirstName", "LastName", "MiddleName"];

    // Add French AbleAssess header aliases here when an actual French export is available.
    // Keep aliases grouped by canonical field so detection and extraction stay in sync.
    private static readonly IReadOnlyDictionary<string, string[]> AbleAssessHeaders = new Dictionary<string, string[]>
    {
        ["BookingId"] = ["Booking ID", "BookingID"], ["ClinicName"] = ["Clinic Name"], ["ClinicDate"] = ["Clinic Date"],
        ["AppointmentType"] = ["Appointment Type"], ["CatalogItem"] = ["Catalog Item"], ["FullName"] = ["Enrolled Person Name"],
        ["Medicare"] = ["Medicare Number"], ["Email"] = ["Email"], ["DateOfBirth"] = ["Date of Birth"],
        ["Phone"] = ["Phone"], ["Timeslot"] = ["Timeslot"], ["Comment"] = ["Comment"],
        ["SdcId"] = ["SDC Id"], ["PreferredLanguage"] = ["Preferred Language"]
    };

    public static List<ClinicPdfClientRecord> ReadFromCsv(string csvFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvFilePath);
        if (!File.Exists(csvFilePath)) throw new FileNotFoundException("CSV input file was not found.", csvFilePath);

        using var reader = new StreamReader(csvFilePath);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = args => throw new FormatException($"Malformed CSV data at row {args.Context?.Parser?.Row ?? 0}."),
            MissingFieldFound = args => throw new FormatException($"Missing field at row {args.Context?.Parser?.Row ?? 0}."),
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim
        });

        if (!csv.Read() || !csv.ReadHeader()) return [];
        var headers = (csv.HeaderRecord ?? []).ToDictionary(NormalizeHeader, header => header, StringComparer.OrdinalIgnoreCase);
        InputSourceType source = HasAllCanonicalHeaders(headers) ? InputSourceType.PdfRoster : DetectSource(headers);
        if (source == InputSourceType.PdfRoster) ValidateCanonicalHeaders(headers);
        else ValidateAbleAssessHeaders(headers);

        var records = new List<ClinicPdfClientRecord>();
        while (csv.Read())
        {
            try { records.Add(source == InputSourceType.AbleAssess ? ReadAbleAssess(csv, headers) : ReadCanonical(csv)); }
            catch (Exception ex) when (ex is not FormatException || !ex.Message.Contains("row", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"CSV row {csv.Parser.Row}: {ex.Message}", ex);
            }
        }
        return records;
    }

    private static InputSourceType DetectSource(IReadOnlyDictionary<string, string> headers) => FindHeader(headers, AbleAssessHeaders["BookingId"]) is not null ? InputSourceType.AbleAssess : InputSourceType.PdfRoster;
    private static bool HasAllCanonicalHeaders(IReadOnlyDictionary<string, string> headers) => RequiredCanonicalHeaders.All(header => headers.ContainsKey(NormalizeHeader(header)));
    private static void ValidateCanonicalHeaders(IReadOnlyDictionary<string, string> headers)
    {
        foreach (string header in RequiredCanonicalHeaders)
            if (!headers.ContainsKey(NormalizeHeader(header))) throw new FormatException($"CSV header is missing required column '{header}'.");
    }
    private static void ValidateAbleAssessHeaders(IReadOnlyDictionary<string, string> headers)
    {
        foreach (string field in new[] { "FullName", "DateOfBirth" })
            if (FindHeader(headers, AbleAssessHeaders[field]) is null) throw new FormatException($"AbleAssess CSV header is missing required column '{AbleAssessHeaders[field][0]}'.");
    }
    private static ClinicPdfClientRecord ReadCanonical(CsvReader csv) => new()
    {
        ClientId = NullIfEmpty(Get(csv, "ClientId")), FullName = Get(csv, "FullName"), DateOfBirth = Get(csv, "DateOfBirth"),
        Medicare = NullIfEmpty(Get(csv, "Medicare")), VaccineType = NullIfEmpty(Get(csv, "VaccineType")) ?? "Autre", ClientIdStatus = ParseStatus(Get(csv, "ClientIdStatus")),
        FirstName = NullIfEmpty(Get(csv, "FirstName")), LastName = NullIfEmpty(Get(csv, "LastName")), MiddleName = NullIfEmpty(Get(csv, "MiddleName")),
        ErrorDetails = NullIfEmpty(Get(csv, "ErrorDetails")), BestMatch = NullIfEmpty(Get(csv, "BestMatch")), Email = FirstNonBlank(csv, "Email", "Email Address"), Phone = FirstNonBlank(csv, "Phone", "Phone Number", "Telephone Number"),
        BookingId = NullIfEmpty(Get(csv, "Booking ID")), ClinicName = NullIfEmpty(Get(csv, "Clinic Name")), ClinicDate = NullIfEmpty(Get(csv, "Clinic Date")), AppointmentType = NullIfEmpty(Get(csv, "Appointment Type")), CatalogItem = NullIfEmpty(Get(csv, "Catalog Item")), Timeslot = NullIfEmpty(Get(csv, "Timeslot")), Comment = NullIfEmpty(Get(csv, "Comment")), SdcId = NullIfEmpty(Get(csv, "SDC Id")), PreferredLanguage = NullIfEmpty(Get(csv, "Preferred Language"))
    };
    private static ClinicPdfClientRecord ReadAbleAssess(CsvReader csv, IReadOnlyDictionary<string, string> headers)
    {
        string GetAble(string field) => Get(csv, FindHeader(headers, AbleAssessHeaders[field]));
        string dob = GetAble("DateOfBirth");
        DateOnly date = default;
        if (!string.IsNullOrWhiteSpace(dob) && !DateOnly.TryParseExact(dob.Trim(), ["M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) throw new FormatException($"Invalid Date of Birth '{dob}'.");
        return new ClinicPdfClientRecord
        {
            ClientId = null, FullName = GetAble("FullName").Trim(), DateOfBirth = string.IsNullOrWhiteSpace(dob) ? string.Empty : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Medicare = NullIfEmptyOrNaN(GetAble("Medicare")), ClientIdStatus = ClientIdStatus.NeedsManualReview, ErrorDetails = string.Empty, BestMatch = string.Empty,
            Phone = NullIfEmpty(GetAble("Phone")), Email = NullIfEmpty(GetAble("Email")), VaccineType = NullIfEmpty(GetAble("CatalogItem")) ?? NullIfEmpty(GetAble("AppointmentType")) ?? "Autre",
            BookingId = NullIfEmpty(GetAble("BookingId")), ClinicName = NullIfEmpty(GetAble("ClinicName")), ClinicDate = NullIfEmpty(GetAble("ClinicDate")), AppointmentType = NullIfEmpty(GetAble("AppointmentType")), CatalogItem = NullIfEmpty(GetAble("CatalogItem")), Timeslot = NullIfEmpty(GetAble("Timeslot")), Comment = NullIfEmpty(GetAble("Comment")), SdcId = NullIfEmpty(GetAble("SdcId")), PreferredLanguage = NullIfEmpty(GetAble("PreferredLanguage"))
        };
    }
    private static ClientIdStatus ParseStatus(string status)
    {
        if (!int.TryParse(status, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || !Enum.IsDefined(typeof(ClientIdStatus), value)) throw new FormatException($"Invalid ClientIdStatus '{status}'.");
        return (ClientIdStatus)value;
    }
    private static string? FindHeader(IReadOnlyDictionary<string, string> headers, IEnumerable<string> aliases) => aliases.Select(alias => headers.GetValueOrDefault(NormalizeHeader(alias))).FirstOrDefault(value => value is not null);
    private static string Get(CsvReader csv, string? header) => header is not null && csv.TryGetField(header, out string? value) ? value ?? string.Empty : string.Empty;
    private static string NormalizeHeader(string header) => header.Trim();
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NullIfEmptyOrNaN(string value) => string.Equals(value.Trim(), "NaN", StringComparison.OrdinalIgnoreCase) ? null : NullIfEmpty(value);
    private static string? FirstNonBlank(CsvReader csv, params string[] headers) => headers.Select(header => NullIfEmpty(Get(csv, header))).FirstOrDefault(value => value is not null);
}
