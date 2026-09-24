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
    public void ReadFromCsv_PreservesOptionalEmail()
    {
        string path = Path.Combine(_directory, "email.csv");
        CsvExporterService.SaveToCsv([new ClinicPdfClientRecord { FullName = "Name", DateOfBirth = "2017/10/01", Email = "parent@example.test" }], path);
        Assert.Equal("parent@example.test", Assert.Single(CsvImporterService.ReadFromCsv(path)).Email);
    }

    [Fact]
    public void ReadFromCsv_AcceptsContactAliasesAndRoundTripsCanonicalFields()
    {
        string input = Path.Combine(_directory, "contacts.csv");
        File.WriteAllText(input, "ClientId,FullName,DateOfBirth,Medicare,ClientIdStatus,FirstName,LastName,MiddleName,Telephone Number,Email Address\n1,Name,2017/10/01,,0,,,,506-721-2234,parent@example.test\n", Encoding.UTF8);
        ClinicPdfClientRecord record = Assert.Single(CsvImporterService.ReadFromCsv(input));
        Assert.Equal("506-721-2234", record.Phone); Assert.Equal("parent@example.test", record.Email);
        string output = Path.Combine(_directory, "contacts-out.csv");
        CsvExporterService.SaveToCsv([record], output);
        ClinicPdfClientRecord reread = Assert.Single(CsvImporterService.ReadFromCsv(output));
        Assert.Equal(record.Phone, reread.Phone); Assert.Equal(record.Email, reread.Email);
    }

    [Fact]
    public void ReadFromCsv_ReportsMalformedRows()
    {
        string path = Path.Combine(_directory, "bad.csv");
        File.WriteAllText(path, "ClientId,FullName,DateOfBirth,Medicare,ClientIdStatus,FirstName,LastName,MiddleName\n1,Bad,2017/10/01,001,wrong,,,\n");
        Assert.Contains("row 2", Assert.Throws<FormatException>(() => CsvImporterService.ReadFromCsv(path)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadFromCsv_CanonicalizesAbleAssessAndRoundTripsMetadata()
    {
        string input = Path.Combine(_directory, "able.csv");
        File.WriteAllText(input, " BookingID ,Clinic Name,Clinic Date,Appointment Type,Catalog Item,Enrolled Person Name,Medicare Number,Email,Date of Birth,Phone,Timeslot,Comment,SDC Id,Preferred Language\nB-1,Clinique Étoile,2026-10-02,Catchup Appointment,\"\",\" AWE DJONYANG, BOUAGNI JEDIDJA \", NaN , parent@example.test ,2/3/2020,05065881736,09:00,Français,SDC-9,Français\n", Encoding.UTF8);

        ClinicPdfClientRecord record = Assert.Single(CsvImporterService.ReadFromCsv(input));
        Assert.Null(record.ClientId); Assert.Equal(ClientIdStatus.NeedsManualReview, record.ClientIdStatus);
        Assert.Equal("AWE DJONYANG, BOUAGNI JEDIDJA", record.FullName); Assert.Equal("2020-02-03", record.DateOfBirth);
        Assert.Null(record.Medicare); Assert.Equal("05065881736", record.Phone); Assert.Equal("Catchup Appointment", record.VaccineType);
        Assert.Equal("B-1", record.BookingId); Assert.Equal("Clinique Étoile", record.ClinicName); Assert.Equal("Français", record.PreferredLanguage);

        record.ClientId = "001"; record.ClientIdStatus = ClientIdStatus.Found;
        string output = Path.Combine(_directory, "able-out.csv");
        CsvExporterService.SaveToCsv([record], output);
        string csv = File.ReadAllText(output, Encoding.UTF8);
        Assert.Contains("\"Booking ID\"", csv); Assert.Contains("\"Preferred Language\"", csv);
        ClinicPdfClientRecord reread = Assert.Single(CsvImporterService.ReadFromCsv(output));
        Assert.Equal("001", reread.ClientId); Assert.Equal(ClientIdStatus.Found, reread.ClientIdStatus);
        Assert.Equal("B-1", reread.BookingId); Assert.Equal("Clinique Étoile", reread.ClinicName);
    }

    [Fact]
    public void ReadFromCsv_AbleAssessReportsMissingRequiredAndInvalidDates()
    {
        string missing = Path.Combine(_directory, "missing.csv");
        File.WriteAllText(missing, "Booking ID,Date of Birth\nB-1,2/3/2020\n", Encoding.UTF8);
        Assert.Contains("Enrolled Person Name", Assert.Throws<FormatException>(() => CsvImporterService.ReadFromCsv(missing)).Message);

        string invalid = Path.Combine(_directory, "invalid.csv");
        File.WriteAllText(invalid, "Booking ID,Enrolled Person Name,Date of Birth\nB-1,Name,2020/40/03\n", Encoding.UTF8);
        Assert.Contains("row 2", Assert.Throws<FormatException>(() => CsvImporterService.ReadFromCsv(invalid)).Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
