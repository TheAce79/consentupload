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
            "10:00 - 10:30 — = + @, • TSENGUE TSENGUE, ESTHER-LYDIA 2016-01-25",
            "10h55 Élodie D'Arcy 2020-03-20",
            "11:00 - 11:30 CIP 2020-03-20",
            "11h25 Invalid Person 2020-02-30",
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
            ClinicPdfClientRecord record = Assert.Single(PdfRosterParserService.ExtractRecordsFromLines(["14h10 Marie Curie 2022-09-06"]));
            Assert.Equal("2022/09/06", record.DateOfBirth);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ExtractRecordsFromLines_ParsesOnlyPrimaryLineOfEachAppointmentBlock()
    {
        List<ClinicPdfClientRecord> records = PdfRosterParserService.ExtractRecordsFromLines([
            "14:00 - 14:30 1h50 Rebaab Kaur REhal 2022-07-07 (harteet kaur 519-216-3098)",
            "14:10 - 14:50 ★2h10 1/2 Czion Jay 2020-02-26 (Maria 778-325-0589) VB-A, Ratt DO NOT BOOK -- J'ai appelé le client",
            "à plusieurs reprises pour l'informer que la clinique du 17 septembre est annulée",
            "2026-08-05: La cliente ne répond pas et retourne pas mes appels",
            "2026-09-09 : La cliente ne répond pas et retourne pas mes appels",
            "14:30 - 15:00 2h25 2/2 Huxley McGillivary - 2017-12-12 (Kathelyn 506-962-6763) 923758213 - LK - A"
        ]);

        Assert.Collection(records,
            record => { Assert.Equal("Rebaab Kaur REhal", record.FullName); Assert.Equal("2022/07/07", record.DateOfBirth); },
            record => { Assert.Equal("Czion Jay", record.FullName); Assert.Equal("2020/02/26", record.DateOfBirth); },
            record => { Assert.Equal("Huxley McGillivary", record.FullName); Assert.Equal("2017/12/12", record.DateOfBirth); Assert.Equal("923758213", record.Medicare); });
    }

    [Fact]
    public void ExtractRecordsFromLines_UsesNextLineForStandaloneRangeAndIgnoresUntimedContent()
    {
        List<ClinicPdfClientRecord> records = PdfRosterParserService.ExtractRecordsFromLines([
            "Clinic roster 17 September",
            "Untimed Client 2020-01-01",
            "14:00 - 14:30",
            "★2h10 1/2 Czion Jay 2020-02-26",
            "2026-08-05: Comment Person 2019-03-04",
            "14:30 - 15:00",
            "DO NOT BOOK",
            "Later Comment 2018-04-05",
            "15:00 - 15:30",
            "15h00 Nova Timmons 2026-07-17",
            "16:00 - 16:30",
            "Invalid Person 2020-02-30",
            "Later Valid Person 2020-02-20"
        ]);

        Assert.Collection(records,
            record => { Assert.Equal("Czion Jay", record.FullName); Assert.Equal("2020/02/26", record.DateOfBirth); },
            record => { Assert.Equal("Nova Timmons", record.FullName); Assert.Equal("2026/07/17", record.DateOfBirth); });
    }

    [Fact]
    public void SaveToCsv_WritesTwelveColumnsBomAndReplacesExistingOutput()
    {
        string outputPath = Path.Combine(_directory, "roster.csv");
        File.WriteAllText(outputPath, "old output", Encoding.UTF8);

        CsvExporterService.SaveToCsv([
            new ClinicPdfClientRecord { FullName = "KOMBOU, LUC", DateOfBirth = "2017/10/01", Medicare = "026547803" }
        ], outputPath);

        byte[] bytes = File.ReadAllBytes(outputPath);
        string csv = File.ReadAllText(outputPath, Encoding.UTF8);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.StartsWith("\"ClientId\",\"FullName\",\"DateOfBirth\",\"Medicare\",\"ClientIdStatus\",\"FirstName\",\"LastName\",\"MiddleName\",\"ErrorDetails\",\"BestMatch\",\"Phone\",\"Email\"", csv);
        Assert.Contains("\"\",\"KOMBOU, LUC\",\"2017/10/01\",\"026547803\",\"0\",\"\",\"\",\"\",\"\",\"\",\"\",\"\"", csv);
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
