using Microsoft.Extensions.Configuration;

namespace ConsentSyncCore.Services.Configuration;

public static class CohortDeletionService
{
    public const string DefaultCohortRoot = @"C:\PHIS\Cohort";

    public static string ValidateTarget(IConfiguration config, string clientListName, string cohortRoot = DefaultCohortRoot)
    {
        if (string.IsNullOrWhiteSpace(clientListName) || clientListName != clientListName.Trim() ||
            clientListName is "." or ".." || clientListName.EndsWith('.') ||
            clientListName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            clientListName.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 ||
            clientListName.Equals("1. InputFolder", StringComparison.OrdinalIgnoreCase) ||
            clientListName.Equals("2. OutputFolder", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The selected cohort name is not a safe folder name.", nameof(clientListName));

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cohortRoot));
        string target = Path.GetFullPath(Path.Combine(root, clientListName));
        if (!string.Equals(Path.GetDirectoryName(target), root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target, CohortWorkspaceService.ResolveCohortDirectory(config, clientListName), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The configured cohort workspace does not match the permitted deletion location.");

        if ((Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) ||
            (Directory.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidOperationException("A linked cohort root or folder cannot be deleted.");
        return target;
    }

    public static void DeleteDirectory(IConfiguration config, string clientListName, string cohortRoot = DefaultCohortRoot)
    {
        string target = ValidateTarget(config, clientListName, cohortRoot);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
