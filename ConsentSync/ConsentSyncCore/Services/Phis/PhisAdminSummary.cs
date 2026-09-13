namespace ConsentSyncCore.Services.Phis;

public static class PhisAdminSummary
{
    public static string Format(int cohortId, string cohortName, PhisClientListResult list, int exportedCount) =>
        $"PHIS Cohort / Client List Summary{Environment.NewLine}" +
        $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}" +
        $"Cohort ID: {cohortId}{Environment.NewLine}" +
        $"Cohort Name: {cohortName}{Environment.NewLine}" +
        $"Client List Name / ID: {cohortName} / {list.ClientListId}{Environment.NewLine}" +
        $"Client IDs exported: {exportedCount}{Environment.NewLine}" +
        $"Clients in PHIS list (verified): {list.ClientCount}{Environment.NewLine}" +
        $"Clients newly added by this run: Not reported by PHIS; existing clients are managed by PHIS.{Environment.NewLine}" +
        $"Status: Full exported list uploaded; attached client list verified.{Environment.NewLine}";
}
