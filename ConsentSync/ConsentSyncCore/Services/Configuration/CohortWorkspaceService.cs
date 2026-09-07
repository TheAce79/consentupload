using Microsoft.Extensions.Configuration;

namespace ConsentSyncCore.Services.Configuration;

public static class CohortWorkspaceService
{
    private const string WorkspaceSection = "CohortContext:Workspace";
    private const string DefaultBaseCohortPath = "{BaseDirectory}\\Cohort";
    private const string DefaultInputFolder = "1. InputFolder";
    private const string DefaultOutputFolder = "2. OutputFolder";
    private const string DefaultInputCsvFolder = "1 Input CSV";
    private const string DefaultInputPdfFolder = "2 Input PDF";
    private const string DefaultOutputCsvFolder = "2 Output CSV";
    private const string DefaultCsvFormat = "{ClientListName}_Cohort.csv";

    public static (string inputCsvDir, string inputPdfDir, string outputCsvDir) EnsureDirectories(IConfiguration config)
    {
        var paths = ResolveWorkspacePaths(config);
        Directory.CreateDirectory(paths.InputCsvDir);
        Directory.CreateDirectory(paths.InputPdfDir);
        Directory.CreateDirectory(paths.OutputCsvDir);
        return (paths.InputCsvDir, paths.InputPdfDir, paths.OutputCsvDir);
    }

    public static string GetStandardizedOutputCsvPath(IConfiguration config, string clientListName)
    {
        string fileName = FormatStandardizedCsvFileName(config, clientListName);
        var (_, _, outputCsvDir) = EnsureDirectories(config);
        return Path.Combine(outputCsvDir, fileName);
    }

    public static string GetStandardizedInputCsvPath(IConfiguration config, string clientListName)
    {
        string fileName = FormatStandardizedCsvFileName(config, clientListName);
        var (inputCsvDir, _, _) = EnsureDirectories(config);
        return Path.Combine(inputCsvDir, fileName);
    }

    public static string FormatStandardizedCsvFileName(IConfiguration config, string clientListName)
    {
        ArgumentNullException.ThrowIfNull(config);

        string normalizedName = clientListName?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("Client list name is required.", nameof(clientListName));
        }

        ValidateFileNameSegment(normalizedName, nameof(clientListName));

        string pattern = config[$"{WorkspaceSection}:FileNaming:StandardizedCsvFormat"] ?? DefaultCsvFormat;
        if (!pattern.Contains("{ClientListName}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The standardized CSV format must include {ClientListName}.");
        }
        string fileName = pattern.Replace("{ClientListName}", normalizedName, StringComparison.Ordinal);
        if (fileName.Contains('{') || fileName.Contains('}'))
        {
            throw new InvalidOperationException("The standardized CSV format contains an unresolved placeholder.");
        }

        ValidateFileNameSegment(fileName, "StandardizedCsvFormat");
        return fileName;
    }

    private static WorkspacePaths ResolveWorkspacePaths(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        string? baseDirectory = config["BaseDirectory"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = @"C:\PHIS";
        }

        string fullBaseDirectory = Path.GetFullPath(baseDirectory);
        string configuredCohortPath = (config[$"{WorkspaceSection}:BaseCohortPath"] ?? DefaultBaseCohortPath)
            .Replace("{BaseDirectory}", fullBaseDirectory, StringComparison.Ordinal);
        if (configuredCohortPath.Contains('{') || configuredCohortPath.Contains('}'))
        {
            throw new InvalidOperationException("The cohort workspace path contains an unresolved placeholder.");
        }

        string cohortPath = Path.GetFullPath(Path.IsPathRooted(configuredCohortPath)
            ? configuredCohortPath
            : Path.Combine(fullBaseDirectory, configuredCohortPath));
        EnsureContained(fullBaseDirectory, cohortPath, "BaseCohortPath");

        string inputFolder = ResolveChildPath(cohortPath, config[$"{WorkspaceSection}:InputFolder"] ?? DefaultInputFolder, "InputFolder");
        string outputFolder = ResolveChildPath(cohortPath, config[$"{WorkspaceSection}:OutputFolder"] ?? DefaultOutputFolder, "OutputFolder");
        string inputCsvDir = ResolveChildPath(inputFolder, config[$"{WorkspaceSection}:SubFolders:InputCsv"] ?? DefaultInputCsvFolder, "SubFolders:InputCsv");
        string inputPdfDir = ResolveChildPath(inputFolder, config[$"{WorkspaceSection}:SubFolders:InputPdf"] ?? DefaultInputPdfFolder, "SubFolders:InputPdf");
        string outputCsvDir = ResolveChildPath(outputFolder, config[$"{WorkspaceSection}:SubFolders:OutputCsv"] ?? DefaultOutputCsvFolder, "SubFolders:OutputCsv");

        return new WorkspacePaths(inputCsvDir, inputPdfDir, outputCsvDir);
    }

    private static string ResolveChildPath(string parent, string child, string settingName)
    {
        if (string.IsNullOrWhiteSpace(child) || Path.IsPathRooted(child))
        {
            throw new InvalidOperationException($"{settingName} must be a non-empty relative folder name.");
        }

        string resolved = Path.GetFullPath(Path.Combine(parent, child));
        EnsureContained(parent, resolved, settingName);
        return resolved;
    }

    private static void ValidateFileNameSegment(string value, string settingName)
    {
        if (value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ') || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 ||
            Path.GetFileName(value) != value)
        {
            throw new ArgumentException($"{settingName} must be a valid file name.", settingName);
        }
    }

    private static void EnsureContained(string parent, string candidate, string settingName)
    {
        string normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        string prefix = normalizedParent + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{settingName} must remain inside '{normalizedParent}'.");
        }
    }

    private sealed record WorkspacePaths(string InputCsvDir, string InputPdfDir, string OutputCsvDir);
}
