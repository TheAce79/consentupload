using Microsoft.Extensions.Configuration;

namespace ConsentSyncCore.Services.Configuration;

public static class CohortWorkspaceService
{
    private const string WorkspaceSection = "CohortContext:Workspace";
    private const string DefaultBaseCohortPath = "{BaseDirectory}\\Cohort\\{ClientListName}";
    private const string DefaultInputFolder = "1. InputFolder";
    private const string DefaultOutputFolder = "2. OutputFolder";
    private const string DefaultInputCsvFolder = "1 Input CSV";
    private const string DefaultInputPdfFolder = "2 Input PDF";
    private const string DefaultOutputCsvFolder = "2 Output CSV";
    private const string DefaultCriteriaFolder = "3.Criteria";
    private const string DefaultCsvFormat = "{ClientListName}_Cohort.csv";

    public static (string inputCsvDir, string inputPdfDir, string outputCsvDir) EnsureDirectories(IConfiguration config)
        => EnsureDirectories(config, config?["CohortContext:LastClientListName"]);

    public static (string inputCsvDir, string inputPdfDir, string outputCsvDir) EnsureDirectories(
        IConfiguration config,
        string? clientListName)
    {
        var paths = ResolveWorkspacePathsCore(config, clientListName);
        Directory.CreateDirectory(paths.InputCsvDir);
        Directory.CreateDirectory(paths.InputPdfDir);
        Directory.CreateDirectory(paths.OutputCsvDir);
        Directory.CreateDirectory(paths.CriteriaDir);
        return (paths.InputCsvDir, paths.InputPdfDir, paths.OutputCsvDir);
    }

    /// <summary>Gets the cohort folder where eligibility criteria files are stored.</summary>
    public static string GetCriteriaDirectory(IConfiguration config, string clientListName)
    {
        WorkspacePaths paths = ResolveWorkspacePathsCore(config, clientListName);
        Directory.CreateDirectory(paths.CriteriaDir);
        return paths.CriteriaDir;
    }

    public static string GetStandardizedOutputCsvPath(IConfiguration config, string clientListName)
    {
        string fileName = FormatStandardizedCsvFileName(config, clientListName);
        var (_, _, outputCsvDir) = EnsureDirectories(config, clientListName);
        return Path.Combine(outputCsvDir, fileName);
    }

    public static string GetStandardizedInputCsvPath(IConfiguration config, string clientListName)
    {
        string fileName = FormatStandardizedCsvFileName(config, clientListName);
        var (inputCsvDir, _, _) = EnsureDirectories(config, clientListName);
        return Path.Combine(inputCsvDir, fileName);
    }

    public static string FormatStandardizedCsvFileName(IConfiguration config, string clientListName)
    {
        ArgumentNullException.ThrowIfNull(config);

        string normalizedName = NormalizeClientListName(clientListName);

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

    /// <summary>Resolves a cohort's directories without creating them.</summary>
    public static (string inputCsvDir, string inputPdfDir, string outputCsvDir) ResolveWorkspacePaths(
        IConfiguration config,
        string? clientListName)
    {
        WorkspacePaths paths = ResolveWorkspacePathsCore(config, clientListName);
        return (paths.InputCsvDir, paths.InputPdfDir, paths.OutputCsvDir);
    }

    private static WorkspacePaths ResolveWorkspacePathsCore(IConfiguration config, string? clientListName)
    {
        ArgumentNullException.ThrowIfNull(config);

        string normalizedName = NormalizeClientListName(clientListName);

        string? baseDirectory = config["BaseDirectory"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = @"C:\PHIS";
        }

        string fullBaseDirectory = Path.GetFullPath(baseDirectory);
        string configuredTemplate = config[$"{WorkspaceSection}:BaseCohortPath"] ?? DefaultBaseCohortPath;
        bool includesClientListName = configuredTemplate.Contains("{ClientListName}", StringComparison.OrdinalIgnoreCase);
        string configuredCohortPath = configuredTemplate
            .Replace("{BaseDirectory}", fullBaseDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{ClientListName}", normalizedName, StringComparison.OrdinalIgnoreCase);
        if (configuredCohortPath.Contains('{') || configuredCohortPath.Contains('}'))
        {
            throw new InvalidOperationException("The cohort workspace path contains an unresolved placeholder.");
        }

        string configuredBasePath = Path.GetFullPath(Path.IsPathRooted(configuredCohortPath)
            ? configuredCohortPath
            : Path.Combine(fullBaseDirectory, configuredCohortPath));
        string cohortPath = includesClientListName
            ? configuredBasePath
            : ResolveChildPath(configuredBasePath, normalizedName, "BaseCohortPath");
        EnsureContained(fullBaseDirectory, cohortPath, "BaseCohortPath");

        string inputFolder = ResolveChildPath(cohortPath, config[$"{WorkspaceSection}:InputFolder"] ?? DefaultInputFolder, "InputFolder");
        string outputFolder = ResolveChildPath(cohortPath, config[$"{WorkspaceSection}:OutputFolder"] ?? DefaultOutputFolder, "OutputFolder");
        string inputCsvDir = ResolveChildPath(inputFolder, config[$"{WorkspaceSection}:SubFolders:InputCsv"] ?? DefaultInputCsvFolder, "SubFolders:InputCsv");
        string inputPdfDir = ResolveChildPath(inputFolder, config[$"{WorkspaceSection}:SubFolders:InputPdf"] ?? DefaultInputPdfFolder, "SubFolders:InputPdf");
        string outputCsvDir = ResolveChildPath(outputFolder, config[$"{WorkspaceSection}:SubFolders:OutputCsv"] ?? DefaultOutputCsvFolder, "SubFolders:OutputCsv");
        string criteriaDir = ResolveChildPath(cohortPath, config[$"{WorkspaceSection}:CriteriaFolder"] ?? DefaultCriteriaFolder, "CriteriaFolder");

        return new WorkspacePaths(inputCsvDir, inputPdfDir, outputCsvDir, criteriaDir);
    }

    private static string NormalizeClientListName(string? clientListName)
    {
        string normalizedName = clientListName?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("Client list name is required.", nameof(clientListName));
        }

        ValidateFileNameSegment(normalizedName, nameof(clientListName));
        return normalizedName;
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

    private sealed record WorkspacePaths(string InputCsvDir, string InputPdfDir, string OutputCsvDir, string CriteriaDir);
}
