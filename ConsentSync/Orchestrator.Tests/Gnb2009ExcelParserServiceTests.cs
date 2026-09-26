using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Excel;
using Xunit;

namespace Orchestrator.Tests;

public sealed class Gnb2009ExcelParserServiceTests : IDisposable
{
    private const string SampleWorkbookPath = @"C:\Users\jaabir\Desktop\PDF\phi\GNB2009-Group Immunization Clinic Worksheets _ Groupe-Feuille de travail-Clinique d'immunisation (1).xls";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Gnb2009ParserTests", Guid.NewGuid().ToString("N"));

    public Gnb2009ExcelParserServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void SuppliedWorkbook_ParsesClientSectionsPackedDatesAndXMarkedDose()
    {
        if (!File.Exists(SampleWorkbookPath)) return;

        string criteria = Path.Combine(_directory, "3.Criteria");
        Directory.CreateDirectory(criteria);
        File.Copy(SampleWorkbookPath, Path.Combine(criteria, "gnb2009.xls"));

        Dictionary<string, ClientImmunizationHistory> histories = new Gnb2009ExcelParserService().ExtractHistoriesFromDirectory(criteria);

        Assert.Equal(28, histories.Count);
        Assert.True(histories.TryGetValue("1469000", out ClientImmunizationHistory? first));
        Assert.Equal("ADOU, NIANUTH ELIORA", first.FullName);
        Assert.Equal(new DateTime(2021, 2, 20), first.DateOfBirth);
        Assert.Contains(first.Records, record => record.Agent == "BCG" && record.AdministeredDate == new DateTime(2021, 2, 24));
        Assert.Contains(first.Records, record => record.Agent == "DPT" && record.AdministeredDate == new DateTime(2021, 5, 5));
        Assert.Contains(first.Records, record => record.Agent == "Pneu-C-13" && record.AdministeredDate == new DateTime(2021, 5, 5));
        Assert.True(histories.Values.Sum(history => history.Records.Count) >= 300);
    }

    [Fact]
    public void ExportDiagnosticCsv_WritesOnlyRequiredColumnsAndIsoDates()
    {
        string output = Path.Combine(_directory, "debug.csv");
        var histories = new Dictionary<string, ClientImmunizationHistory>(StringComparer.OrdinalIgnoreCase)
        {
            ["0001"] = new()
            {
                ClientId = "0001",
                FullName = "Test Client",
                DateOfBirth = new DateTime(2020, 1, 2),
                Records = { new ImmunizationRecord { Agent = "DTaP-IPV-Hib", AdministeredDate = new DateTime(2021, 3, 4) } }
            }
        };

        new Gnb2009ExcelParserService().ExportDiagnosticCsv(histories, output);

        string[] lines = File.ReadAllLines(output);
        Assert.Equal("\"ClientId\",\"Agent\",\"AdministeredDate\"", lines[0]);
        Assert.Equal("\"0001\",\"DTaP-IPV-Hib\",\"2021-03-04\"", lines[1]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void BuildPreview_DistinguishesMissingHistoryAndExcelOnlyClients()
    {
        CohortReviewRow activeWithHistory = Row("100", "Active Found", excluded: false);
        CohortReviewRow activeMissingExcel = Row("200", "Active Missing", excluded: false);
        CohortReviewRow excludedInCsv = Row("300", "Excluded Client", excluded: true);
        Dictionary<string, ClientImmunizationHistory> histories = new(StringComparer.OrdinalIgnoreCase)
        {
            ["100"] = History("100", "Active Found", 2),
            ["300"] = History("300", "Excluded Client", 1),
            ["400"] = History("400", "Manual PHIS Add", 3)
        };

        EligibilityHistoryPreviewResult preview = Gnb2009HistoryPreviewService.BuildPreview(
            [activeWithHistory, activeMissingExcel, excludedInCsv], histories);

        Assert.Contains(preview.Rows, row => row.ClientId == "100" && row.HistoryMatchStatus == "History Found" && row.DoseCount == 2);
        Assert.Contains(preview.Rows, row => row.ClientId == "200" && row.HistoryMatchStatus == "No History Found" && row.DoseCount == 0);
        Assert.Contains(preview.Rows, row => row.ClientId == "400" && row.HistoryMatchStatus == "Added in PHIS (Not in Cohort CSV)");
        Assert.DoesNotContain(preview.Rows, row => row.ClientId == "300" && row.HistoryMatchStatus == "Added in PHIS (Not in Cohort CSV)");
        Assert.Single(preview.Warnings);
        Assert.Contains("ClientID 400", preview.Warnings[0]);
    }

    private static CohortReviewRow Row(string clientId, string name, bool excluded) => new()
    {
        Source = new ClinicPdfClientRecord
        {
            ClientId = clientId,
            FullName = name,
            DateOfBirth = "2020-01-02",
            VaccineType = "4 Month Appointment",
            ClientIdStatus = ClientIdStatus.Found
        },
        Excluded = excluded
    };

    private static ClientImmunizationHistory History(string clientId, string name, int doseCount)
    {
        var history = new ClientImmunizationHistory
        {
            ClientId = clientId,
            FullName = name,
            DateOfBirth = new DateTime(2020, 1, 2)
        };
        for (int index = 0; index < doseCount; index++)
            history.Records.Add(new ImmunizationRecord { Agent = $"Agent {index}", AdministeredDate = new DateTime(2021, 1, 1).AddDays(index) });
        return history;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
