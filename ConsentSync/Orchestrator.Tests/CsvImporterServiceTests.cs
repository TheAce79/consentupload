using System.Text;
using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Csv;
using Xunit;

namespace Orchestrator.Tests;

public sealed class CsvImporterServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ConsentSyncCsvImportTests", Guid.NewGuid().ToString("N"));
    public CsvImporterServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReadFromCsv_AcceptsEightColumnsAndPreservesLeadingZeros()
    {
        string path = Path.Combine(_directory, "legacy.csv");
        File.WriteAllText(path, "ClientId,FullName,DateOfBirth,Medicare,ClientIdStatus,FirstName,LastName,MiddleName\n001,\"KOMBOU, LUC\",2017/10/01,026547803,0,,,\n", Encoding.UTF8);
        ClinicPdfClientRecord record = Assert.Single(CsvImporterService.ReadFromCsv(path));
        Assert.Equal("001", record.ClientId); Assert.Equal("026547803", record.Medicare); Assert.Null(record.ErrorDetails);
    }

    [Fact]
    public void ReadFromCsv_RoundTripsNineColumnsAndQuotedFields()
    {
        string path = Path.Combine(_directory, "new.csv");
        CsvExporterService.SaveToCsv([new ClinicPdfClientRecord { ClientId = "001", FullName = "KOMBOU, LUC", DateOfBirth = "2017/10/01", Medicare = "026547803", ClientIdStatus = ClientIdStatus.NeedsManualReview, ErrorDetails = "NameMatchBelowThreshold" }], path);
        ClinicPdfClientRecord record = Assert.Single(CsvImporterService.ReadFromCsv(path));
        Assert.Equal("001", record.ClientId); Assert.Equal("KOMBOU, LUC", record.FullName); Assert.Equal("NameMatchBelowThreshold", record.ErrorDetails);
    }

    [Fact]
    public void ReadFromCsv_AcceptsNineColumnsAndTenColumnBestMatch()
    {
        string nineColumnPath = Path.Combine(_directory, "nine.csv");
        File.WriteAllText(nineColumnPath, "ClientId,FullName,DateOfBirth,Medicare,ClientIdStatus,FirstName,LastName,MiddleName,ErrorDetails\n001,Name,2017/10/01,,2,,,,Reason\n", Encoding.UTF8);
        Assert.Null(Assert.Single(CsvImporterService.ReadFromCsv(nineColumnPath)).BestMatch);

        string tenColumnPath = Path.Combine(_directory, "ten.csv");
        CsvExporterService.SaveToCsv([new ClinicPdfClientRecord { ClientId = "001", FullName = "Name", DateOfBirth = "2017/10/01", ClientIdStatus = ClientIdStatus.NeedsManualReview, BestMatch = "A#B##1#75.0%" }], tenColumnPath);
        Assert.Equal("A#B##1#75.0%", Assert.Single(CsvImporterService.ReadFromCsv(tenColumnPath)).BestMatch);
    }

    [Fact]
    public void ReadFromCsv_ReportsMalformedRows()
    {
        string path = Path.Combine(_directory, "bad.csv");
        File.WriteAllText(path, "ClientId,FullName,DateOfBirth,Medicare,ClientIdStatus,FirstName,LastName,MiddleName\n1,Bad,2017/10/01,001,wrong,,,\n");
        Assert.Contains("row 2", Assert.Throws<FormatException>(() => CsvImporterService.ReadFromCsv(path)).Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
