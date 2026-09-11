using System.Globalization;
using System.Text;
using ConsentSyncCore.Models;
using CsvHelper;
using CsvHelper.Configuration;

namespace ConsentSyncCore.Services.Csv;

public static class CsvExporterService
{
    public static void SaveToCsv(IEnumerable<ClinicPdfClientRecord> records, string targetCsvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCsvPath);
        string destinationDirectory = Path.GetDirectoryName(targetCsvPath) ?? throw new ArgumentException("The target path must include a directory.", nameof(targetCsvPath));
        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = Path.Combine(destinationDirectory, $".{Path.GetFileName(targetCsvPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                ShouldQuote = _ => true
            }))
            {
                csv.WriteField("ClientId"); csv.WriteField("FullName"); csv.WriteField("DateOfBirth"); csv.WriteField("Medicare");
                csv.WriteField("ClientIdStatus"); csv.WriteField("FirstName"); csv.WriteField("LastName"); csv.WriteField("MiddleName"); csv.WriteField("ErrorDetails"); csv.WriteField("BestMatch"); csv.WriteField("Phone"); csv.WriteField("Email"); csv.NextRecord();
                foreach (ClinicPdfClientRecord record in records)
                {
                    csv.WriteField(record.ClientId); csv.WriteField(record.FullName); csv.WriteField(record.DateOfBirth); csv.WriteField(record.Medicare);
                    csv.WriteField((int)record.ClientIdStatus); csv.WriteField(record.FirstName); csv.WriteField(record.LastName); csv.WriteField(record.MiddleName); csv.WriteField(record.ErrorDetails); csv.WriteField(record.BestMatch); csv.WriteField(record.Phone); csv.WriteField(record.Email); csv.NextRecord();
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
