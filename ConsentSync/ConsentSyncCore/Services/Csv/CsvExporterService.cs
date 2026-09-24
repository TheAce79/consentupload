using System.Globalization;
using System.Text;
using ConsentSyncCore.Models;
using CsvHelper;
using CsvHelper.Configuration;

namespace ConsentSyncCore.Services.Csv;

public static class CsvExporterService
{
    private static readonly (string Header, Func<ClinicPdfClientRecord, string?> Value)[] AbleAssessMetadata =
    [
        ("Booking ID", r => r.BookingId), ("Clinic Name", r => r.ClinicName), ("Clinic Date", r => r.ClinicDate),
        ("Appointment Type", r => r.AppointmentType), ("Catalog Item", r => r.CatalogItem), ("Timeslot", r => r.Timeslot),
        ("Comment", r => r.Comment), ("SDC Id", r => r.SdcId), ("Preferred Language", r => r.PreferredLanguage)
    ];

    public static void SaveToCsv(IEnumerable<ClinicPdfClientRecord> records, string targetCsvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCsvPath);
        string destinationDirectory = Path.GetDirectoryName(targetCsvPath) ?? throw new ArgumentException("The target path must include a directory.", nameof(targetCsvPath));
        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = Path.Combine(destinationDirectory, $".{Path.GetFileName(targetCsvPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            List<ClinicPdfClientRecord> recordList = records.ToList();
            var populatedMetadata = AbleAssessMetadata.Where(metadata => recordList.Any(record => !string.IsNullOrWhiteSpace(metadata.Value(record)))).ToArray();
            using (var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                ShouldQuote = _ => true
            }))
            {
                csv.WriteField("ClientId"); csv.WriteField("FullName"); csv.WriteField("DateOfBirth"); csv.WriteField("Medicare");
                csv.WriteField("ClientIdStatus"); csv.WriteField("FirstName"); csv.WriteField("LastName"); csv.WriteField("MiddleName"); csv.WriteField("ErrorDetails"); csv.WriteField("BestMatch"); csv.WriteField("Phone"); csv.WriteField("Email"); csv.WriteField("VaccineType");
                foreach (var metadata in populatedMetadata) csv.WriteField(metadata.Header);
                csv.NextRecord();
                foreach (ClinicPdfClientRecord record in recordList)
                {
                    csv.WriteField(record.ClientId); csv.WriteField(record.FullName); csv.WriteField(record.DateOfBirth); csv.WriteField(record.Medicare);
                    csv.WriteField((int)record.ClientIdStatus); csv.WriteField(record.FirstName); csv.WriteField(record.LastName); csv.WriteField(record.MiddleName); csv.WriteField(record.ErrorDetails); csv.WriteField(record.BestMatch); csv.WriteField(record.Phone); csv.WriteField(record.Email); csv.WriteField(record.VaccineType);
                    foreach (var metadata in populatedMetadata) csv.WriteField(metadata.Value(record));
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
}
