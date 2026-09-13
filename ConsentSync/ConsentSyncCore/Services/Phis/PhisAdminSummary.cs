namespace ConsentSyncCore.Services.Phis;

public static class PhisAdminSummary
{
    public static string Format(int cohortId, string cohortName, PhisClientListResult list, int exportedCount) =>
        Format(cohortId, cohortName, list, exportedCount, new PhisUploadComparison(true, [], []));

    public static string Format(int cohortId, string cohortName, PhisClientListResult list, int exportedCount, PhisUploadComparison comparison) =>
        $"PHIS Cohort / Client List Summary{Environment.NewLine}" +
        $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}" +
        $"Cohort ID: {cohortId}{Environment.NewLine}" +
        $"Cohort Name: {cohortName}{Environment.NewLine}" +
        $"Client List Name / ID: {cohortName} / {list.ClientListId}{Environment.NewLine}" +
        $"Client IDs exported: {exportedCount}{Environment.NewLine}" +
        $"Clients in PHIS list (verified): {list.ClientCount}{Environment.NewLine}" +
        $"Status: Full exported list uploaded; attached client list verified.{Environment.NewLine}" +
        FormatChanges(comparison);

    private static string FormatChanges(PhisUploadComparison comparison)
    {
        string heading = comparison.IsInitialUpload ? "Initial upload — no previous snapshot available" : "Added since previous successful upload";
        string added = $"{heading}: {comparison.Added.Count}{Environment.NewLine}" + string.Concat(comparison.Added.Select(x => $"{x.ClientId} | {x.FullName}{Environment.NewLine}"));
        if (comparison.IsInitialUpload) return added;
        return added + $"No longer in current payload: {comparison.Removed.Count}{Environment.NewLine}" + string.Concat(comparison.Removed.Select(x => $"{x.ClientId} | {x.FullName}{Environment.NewLine}"));
    }
}
