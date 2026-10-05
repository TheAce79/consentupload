namespace ConsentSyncCore.Services.Phis;

using ConsentSyncCore.Services;

public static class PhisAdminSummary
{
    private const int DialogClientLimit = 5;

    public static string Format(int cohortId, string cohortName, PhisClientListResult list, int exportedCount) =>
        Format(cohortId, cohortName, list, exportedCount, new PhisUploadComparison(true, [], []));

    public static string Format(int cohortId, string cohortName, PhisClientListResult list, int exportedCount, PhisUploadComparison comparison, IEnumerable<PhisUploadClient>? clients = null) =>
        $"PHIS Cohort / Client List Summary{Environment.NewLine}" +
        $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}" +
        $"Cohort ID: {cohortId}{Environment.NewLine}" +
        $"Cohort Name: {cohortName}{Environment.NewLine}" +
        $"Client List Name / ID: {cohortName} / {list.ClientListId}{Environment.NewLine}" +
        $"Client IDs exported: {exportedCount}{Environment.NewLine}" +
        $"Clients in PHIS list (verified): {list.ClientCount}{Environment.NewLine}" +
        $"Status: Full exported list uploaded; attached client list verified.{Environment.NewLine}" +
        FormatClients(clients) +
        FormatChanges(comparison);

    public static string FormatDialog(int cohortId, string cohortName, PhisClientListResult list, int exportedCount, PhisUploadComparison comparison, IEnumerable<PhisUploadClient>? clients = null)
    {
        List<PhisUploadClient> rows = GetClients(clients);
        var text = new System.Text.StringBuilder()
            .AppendLine("PHIS Cohort / Client List Summary")
            .AppendLine($"Cohort ID: {cohortId}")
            .AppendLine($"Cohort Name: {cohortName}")
            .AppendLine($"Client List Name / ID: {cohortName} / {list.ClientListId}")
            .AppendLine($"Client IDs exported: {exportedCount}")
            .AppendLine($"Clients in PHIS list (verified): {list.ClientCount}")
            .AppendLine("Status: Full exported list uploaded; attached client list verified.")
            .AppendLine()
            .AppendLine("Clients (first 5, sorted by Timeslot):");
        if (rows.Count == 0) text.AppendLine("(None)");
        else
        {
            foreach (PhisUploadClient client in rows.Take(DialogClientLimit))
                text.AppendLine($"{AppointmentTime.Display(client.Timeslot)} | {client.ClientId} | {client.FullName}");
            if (rows.Count > DialogClientLimit) text.AppendLine($"{rows.Count - DialogClientLimit} remaining client(s). See the summary file for the full list.");
        }

        string heading = comparison.IsInitialUpload ? "Initial upload" : "Added since previous successful upload";
        text.AppendLine().AppendLine($"{heading}: {comparison.Added.Count}");
        if (!comparison.IsInitialUpload) text.AppendLine($"No longer in current payload: {comparison.Removed.Count}");
        return text.ToString();
    }

    private static string FormatClients(IEnumerable<PhisUploadClient>? clients)
    {
        if (clients is null) return string.Empty;
        List<PhisUploadClient> rows = GetClients(clients);
        return $"{Environment.NewLine}Clients (Sorted by Timeslot):{Environment.NewLine}" +
               string.Concat(rows.Select(client => $"{AppointmentTime.Display(client.Timeslot)} | {client.ClientId} | {client.FullName}{Environment.NewLine}"));
    }

    private static List<PhisUploadClient> GetClients(IEnumerable<PhisUploadClient>? clients)
    {
        if (clients is null) return [];
        return clients.Where(client => !string.IsNullOrWhiteSpace(client.ClientId))
            .GroupBy(client => client.ClientId.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                PhisUploadClient? timed = group.Where(client => AppointmentTime.TryParse(client.Timeslot, out _))
                    .OrderBy(client => AppointmentTime.SortKey(client.Timeslot)).FirstOrDefault();
                string name = group.Select(client => client.FullName?.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
                return new PhisUploadClient(group.Key, name, timed?.Timeslot);
            })
            .OrderBy(client => AppointmentTime.SortKey(client.Timeslot))
            .ThenBy(client => client.ClientId, StringComparer.Ordinal)
            .ToList();
    }

    private static string FormatChanges(PhisUploadComparison comparison)
    {
        string heading = comparison.IsInitialUpload ? "Initial upload — no previous snapshot available" : "Added since previous successful upload";
        string added = $"{heading}: {comparison.Added.Count}{Environment.NewLine}" + string.Concat(comparison.Added.Select(x => $"{x.ClientId} | {x.FullName}{Environment.NewLine}"));
        if (comparison.IsInitialUpload) return added;
        return added + $"No longer in current payload: {comparison.Removed.Count}{Environment.NewLine}" + string.Concat(comparison.Removed.Select(x => $"{x.ClientId} | {x.FullName}{Environment.NewLine}"));
    }
}
