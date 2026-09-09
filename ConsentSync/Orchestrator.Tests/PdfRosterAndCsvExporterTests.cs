using System.Text;
using System.Globalization;
using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Csv;
using ConsentSyncCore.Services.Pdf;
using Xunit;

namespace Orchestrator.Tests;

public sealed class PdfRosterAndCsvExporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ConsentSyncPdfRosterTests", Guid.NewGuid().ToString("N"));

    public PdfRosterAndCsvExporterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ExtractRecordsFromLines_ParsesNamesDatesMedicareAndPreservesDuplicates()
    {
        List<ClinicPdfClientRecord> records = PdfRosterParserService.ExtractRecordsFromLines([
            "★10h50 KOMBOU TCHAPDU, LUC OLIVIER (2017-10-01) 026 547 803",
            "9h05- Jamila Diallo 2022-09-06",
            "8h30 1/2 Élodie D'Arcy 2020-03-20",
            "— = + @, • TSENGUE TSENGUE, ESTHER-LYDIA 2016-01-25",
            "CIP 2020-03-20",
            "Invalid Person 2020-02-30",
            "Élodie D'Arcy 2020-03-20"
        ]);

        Assert.Equal(5, records.Count);
        Assert.Equal("KOMBOU TCHAPDU, LUC OLIVIER", records[0].FullName);
        Assert.Equal("026547803", records[0].Medicare);
        Assert.Equal("2017/10/01", records[0].DateOfBirth);
        Assert.Equal("Jamila Diallo", records[1].FullName);
        Assert.Equal("2022/09/06", records[1].DateOfBirth);
        Assert.Equal("Élodie D'Arcy", records[2].FullName);
        Assert.Null(records[2].Medicare);
        Assert.Equal("TSENGUE TSENGUE, ESTHER-LYDIA", records[3].FullName);
        Assert.Equal(records[2].FullName, records[4].FullName);
        Assert.All(records, record =>
        {
            Assert.Null(record.ClientId);
            Assert.Equal(ClientIdStatus.NotProcessed, record.ClientIdStatus);
            Assert.Null(record.FirstName);
            Assert.Null(record.LastName);
            Assert.Null(record.MiddleName);
        });
    }

    [Fact]
    public void ExtractRecordsFromLines_UsesInvariantSlashDateFormat()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            ClinicPdfClientRecord record = Assert.Single(PdfRosterParserService.ExtractRecordsFromLines(["Marie Curie 2022-09-06"]));
            Assert.Equal("2022/09/06", record.DateOfBirth);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void SaveToCsv_WritesElevenColumnsBomAndReplacesExistingOutput()
    {
        string outputPath = Path.Combine(_directory, "roster.csv");
        File.WriteAllText(outputPath, "old output", Encoding.UTF8);

        CsvExporterService.SaveToCsv([
            new ClinicPdfClientRecord { FullName = "KOMBOU, LUC", DateOfBirth = "2017/10/01", Medicare = "026547803" }
        ], outputPath);

        byte[] bytes = File.ReadAllBytes(outputPath);
        string csv = File.ReadAllText(outputPath, Encoding.UTF8);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.StartsWith("\"ClientId\",\"FullName\",\"DateOfBirth\",\"Medicare\",\"ClientIdStatus\",\"FirstName\",\"LastName\",\"MiddleName\",\"ErrorDetails\",\"BestMatch\",\"Email\"", csv);
        Assert.Contains("\"\",\"KOMBOU, LUC\",\"2017/10/01\",\"026547803\",\"0\",\"\",\"\",\"\",\"\",\"\",\"\"", csv);
        Assert.DoesNotContain("old output", csv);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void SaveToCsv_WhenDestinationIsLocked_PreservesExistingOutput()
    {
        string outputPath = Path.Combine(_directory, "locked.csv");
        File.WriteAllText(outputPath, "original", Encoding.UTF8);

        using (new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => CsvExporterService.SaveToCsv(
                [new ClinicPdfClientRecord { FullName = "New Record", DateOfBirth = "2017-10-01" }], outputPath));
        }

        Assert.Equal("original", File.ReadAllText(outputPath, Encoding.UTF8));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
