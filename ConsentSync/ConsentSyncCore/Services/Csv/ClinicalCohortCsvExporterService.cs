using System.Globalization;
using System.Text;
using ConsentSyncCore.Models;
using CsvHelper;
using CsvHelper.Configuration;

namespace ConsentSyncCore.Services.Csv;

/// <summary>Writes the recipient-facing clinical cohort CSV from eligibility preview rows.</summary>
public static class ClinicalCohortCsvExporterService
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy", "dd/MM/yyyy"];

    public static void SaveToCsv(IEnumerable<ClinicalCohortExportRow> rows, string targetCsvPath)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCsvPath);
        string destinationDirectory = Path.GetDirectoryName(targetCsvPath)
            ?? throw new ArgumentException("The target path must include a directory.", nameof(targetCsvPath));
        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = Path.Combine(destinationDirectory, $".{Path.GetFileName(targetCsvPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { ShouldQuote = _ => true }))
            {
                csv.WriteField("Client_ID");
                csv.WriteField("Full Name");
                csv.WriteField("Clinic Date");
                csv.WriteField("Date of birth");
                csv.WriteField("Time Slot");
                csv.WriteField("Vaccine type");
                csv.WriteField("Status");
                csv.WriteField("Evaluation Reason");
                csv.NextRecord();

                foreach (ClinicalCohortExportRow row in rows)
                {
                    csv.WriteField(row.ClientId);
                    csv.WriteField(row.FullName);
                    csv.WriteField(FormatDate(row.ClinicDate));
                    csv.WriteField(FormatDate(row.DateOfBirth));
                    csv.WriteField(FormatTimeslot(row.Timeslot));
                    csv.WriteField(row.VaccineType);
                    csv.WriteField(row.Status?.ToString() ?? string.Empty);
                    csv.WriteField(row.EvaluationReason);
                    csv.NextRecord();
                }
            }

            if (File.Exists(targetCsvPath)) File.Replace(temporaryPath, targetCsvPath, null);
            else File.Move(temporaryPath, targetCsvPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string FormatDate(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out DateTime parsed)
            ? parsed.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
            : string.Empty;

    private static string FormatTimeslot(string? value) =>
        AppointmentTime.TryParse(value, out TimeOnly time)
            ? time.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : string.Empty;
}
