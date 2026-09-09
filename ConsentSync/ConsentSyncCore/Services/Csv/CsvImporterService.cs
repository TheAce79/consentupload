using System.Globalization;
using ConsentSyncCore.Models;
using CsvHelper;
using CsvHelper.Configuration;

namespace ConsentSyncCore.Services.Csv;

public static class CsvImporterService
{
    private static readonly string[] RequiredHeaders = ["ClientId", "FullName", "DateOfBirth", "Medicare", "ClientIdStatus", "FirstName", "LastName", "MiddleName"];

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
        var headers = csv.HeaderRecord ?? [];
        foreach (string header in RequiredHeaders)
            if (!headers.Contains(header, StringComparer.OrdinalIgnoreCase))
                throw new FormatException($"CSV header is missing required column '{header}'.");

        var records = new List<ClinicPdfClientRecord>();
        while (csv.Read())
        {
            try
            {
                string status = Get(csv, "ClientIdStatus");
                if (!int.TryParse(status, NumberStyles.Integer, CultureInfo.InvariantCulture, out int statusValue) ||
                    !Enum.IsDefined(typeof(ClientIdStatus), statusValue))
                    throw new FormatException($"Invalid ClientIdStatus '{status}'.");

                records.Add(new ClinicPdfClientRecord
                {
                    ClientId = NullIfEmpty(Get(csv, "ClientId")), FullName = Get(csv, "FullName"), DateOfBirth = Get(csv, "DateOfBirth"),
                    Medicare = NullIfEmpty(Get(csv, "Medicare")), ClientIdStatus = (ClientIdStatus)statusValue,
                    FirstName = NullIfEmpty(Get(csv, "FirstName")), LastName = NullIfEmpty(Get(csv, "LastName")),
                    MiddleName = NullIfEmpty(Get(csv, "MiddleName")), ErrorDetails = NullIfEmpty(Get(csv, "ErrorDetails")),
                    BestMatch = NullIfEmpty(Get(csv, "BestMatch"))
                });
            }
            catch (Exception ex) when (ex is not FormatException || !ex.Message.Contains("row", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"CSV row {csv.Parser.Row}: {ex.Message}", ex);
            }
        }
        return records;
    }

    private static string Get(CsvReader csv, string header) => csv.TryGetField(header, out string? value) ? value ?? string.Empty : string.Empty;
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
