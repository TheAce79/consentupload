using ConsentSyncCore.Services.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Orchestrator.Tests;

public sealed class CohortWorkspaceServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ConsentSyncCohortWorkspaceTests", Guid.NewGuid().ToString("N"));

    public CohortWorkspaceServiceTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public void EnsureDirectories_CreatesDefaultTreeAndIsIdempotent()
    {
        IConfiguration config = CreateConfiguration();

        var first = CohortWorkspaceService.EnsureDirectories(config);
        var second = CohortWorkspaceService.EnsureDirectories(config);

        Assert.Equal(first, second);
        Assert.True(Directory.Exists(Path.Combine(_tempDirectory, "Cohort", "1. InputFolder", "1 Input CSV")));
        Assert.True(Directory.Exists(Path.Combine(_tempDirectory, "Cohort", "1. InputFolder", "2 Input PDF")));
        Assert.True(Directory.Exists(Path.Combine(_tempDirectory, "Cohort", "2. OutputFolder", "2 Output CSV")));
        Assert.Equal(
            Path.Combine(_tempDirectory, "Cohort", "2. OutputFolder", "2 Output CSV", "CIPMONCTONSP20260910_Cohort.csv"),
            CohortWorkspaceService.GetStandardizedOutputCsvPath(config, "CIPMONCTONSP20260910"));
        Assert.Equal(
            Path.Combine(_tempDirectory, "Cohort", "1. InputFolder", "1 Input CSV", "CIPMONCTONSP20260910_Cohort.csv"),
            CohortWorkspaceService.GetStandardizedInputCsvPath(config, "CIPMONCTONSP20260910"));
    }

    [Fact]
    public void FormatStandardizedCsvFileName_NormalizesNameWithoutCreatingDirectories()
    {
        IConfiguration config = CreateConfiguration();

        string fileName = CohortWorkspaceService.FormatStandardizedCsvFileName(config, " cipmonctonsp20260910 ");

        Assert.Equal("CIPMONCTONSP20260910_Cohort.csv", fileName);
        Assert.False(Directory.Exists(Path.Combine(_tempDirectory, "Cohort")));
    }

    [Fact]
    public void GetStandardizedOutputCsvPath_UsesConfiguredWorkspaceAndTemplate()
    {
        IConfiguration config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["CohortContext:Workspace:BaseCohortPath"] = "{BaseDirectory}\\Custom Cohorts",
            ["CohortContext:Workspace:InputFolder"] = "Inbound",
            ["CohortContext:Workspace:OutputFolder"] = "Outbound",
            ["CohortContext:Workspace:SubFolders:InputCsv"] = "Csv",
            ["CohortContext:Workspace:SubFolders:InputPdf"] = "Pdf",
            ["CohortContext:Workspace:SubFolders:OutputCsv"] = "Exports",
            ["CohortContext:Workspace:FileNaming:StandardizedCsvFormat"] = "{ClientListName}_Processed.csv"
        });

        string path = CohortWorkspaceService.GetStandardizedOutputCsvPath(config, "testlist");

        Assert.Equal(Path.Combine(_tempDirectory, "Custom Cohorts", "Outbound", "Exports", "TESTLIST_Processed.csv"), path);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)!));
        Assert.Equal(
            Path.Combine(_tempDirectory, "Custom Cohorts", "Inbound", "Csv", "TESTLIST_Processed.csv"),
            CohortWorkspaceService.GetStandardizedInputCsvPath(config, "testlist"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("list/name")]
    [InlineData("list..")]
    public void FormatStandardizedCsvFileName_RejectsInvalidListNames(string clientListName)
    {
        Assert.Throws<ArgumentException>(() => CohortWorkspaceService.FormatStandardizedCsvFileName(CreateConfiguration(), clientListName));
    }

    [Fact]
    public void WorkspaceResolution_RejectsTraversalAndUnresolvedPlaceholders()
    {
        IConfiguration traversalConfig = CreateConfiguration(new Dictionary<string, string?>
        {
            ["CohortContext:Workspace:InputFolder"] = "..\\outside"
        });
        IConfiguration unresolvedTemplateConfig = CreateConfiguration(new Dictionary<string, string?>
        {
            ["CohortContext:Workspace:FileNaming:StandardizedCsvFormat"] = "{ClientListName}_{Run}.csv"
        });

        Assert.Throws<InvalidOperationException>(() => CohortWorkspaceService.EnsureDirectories(traversalConfig));
        Assert.Throws<InvalidOperationException>(() => CohortWorkspaceService.FormatStandardizedCsvFileName(unresolvedTemplateConfig, "list"));
    }

    private IConfiguration CreateConfiguration(IDictionary<string, string?>? values = null)
    {
        var settings = new Dictionary<string, string?> { ["BaseDirectory"] = _tempDirectory };
        if (values is not null)
        {
            foreach (var pair in values)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
