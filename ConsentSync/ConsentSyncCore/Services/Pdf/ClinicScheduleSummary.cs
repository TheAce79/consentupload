using System.Text;
using ConsentSyncCore.Models;

namespace ConsentSyncCore.Services.Pdf;

public sealed record ClinicScheduleClient(string Key, string FullName, string DateOfBirth, string VaccineType, string? ClientId = null);
public sealed record ClinicScheduleComparison(bool IsInitial, IReadOnlyList<ClinicScheduleClient> Current, IReadOnlyList<ClinicScheduleClient> Added, IReadOnlyList<ClinicScheduleClient> Removed);

public static class ClinicScheduleSummary
{
    public static string Key(ClinicPdfClientRecord x) => $"{x.FullName.Trim().ToUpperInvariant()}_{x.DateOfBirth}";
    public static ClinicScheduleComparison Compare(IEnumerable<ClinicScheduleClient> current, IEnumerable<ClinicScheduleClient>? prior)
    {
        var now = current.GroupBy(x => x.Key, StringComparer.Ordinal).Select(x => x.First()).OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        if (prior is null) return new(true, now, now, []);
        var before = prior.GroupBy(x => x.Key, StringComparer.Ordinal).Select(x => x.First()).ToDictionary(x => x.Key, StringComparer.Ordinal);
        var nowByKey = now.ToDictionary(x => x.Key, StringComparer.Ordinal);
        return new(false, now, now.Where(x => !before.ContainsKey(x.Key)).ToList(), before.Values.Where(x => !nowByKey.ContainsKey(x.Key)).OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static IReadOnlyList<ClinicScheduleClient> AttachClientIds(IEnumerable<ClinicPdfClientRecord> schedule, IEnumerable<ClinicPdfClientRecord> csv)
    {
        var ids = csv.Where(x => !string.IsNullOrWhiteSpace(x.ClientId)).GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Select(y => y.ClientId!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.Ordinal);
        return schedule.GroupBy(Key, StringComparer.Ordinal).Select(x => x.First()).Select(x =>
        {
            ids.TryGetValue(Key(x), out var matches);
            string? id = matches is null || matches.Count == 0 ? null : matches.Count == 1 ? matches[0] : "Conflicting client ID";
            return new ClinicScheduleClient(Key(x), x.FullName, x.DateOfBirth, x.VaccineType, id);
        }).ToList();
    }

    public static string Format(string cohortName, int? cohortId, int? listId, ClinicScheduleComparison changes, DateTime? previous, IEnumerable<string> files, DateTime? verifiedUpload, int? verifiedPhisCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Clinic Schedule Administrative Update").AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}").AppendLine($"Cohort: {cohortName}").AppendLine($"PHIS Cohort / Client List ID: {cohortId?.ToString() ?? "Unknown"} / {listId?.ToString() ?? "Unknown"}").AppendLine($"Current scheduled clients: {changes.Current.Count}").AppendLine($"Previous schedule: {(previous is null ? "None (initial preparation)" : previous.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))}").AppendLine($"Last verified PHIS upload: {(verifiedUpload is null ? "Unavailable" : $"{verifiedUpload.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}; clients: {verifiedPhisCount?.ToString() ?? "Unavailable"}")}").AppendLine($"Source PDFs: {string.Join(", ", files)}");
        Section(sb, changes.IsInitial ? "INITIAL CLINIC PREPARATION — Prepare consent paperwork" : "NEWLY ADDED TO SCHEDULE — Prepare and add consent paperwork", changes.Added);
        sb.AppendLine("NO LONGER ON CURRENT SCHEDULE — Administrative review required").AppendLine("Confirm appointment status against the current clinic schedule and pull the corresponding paperwork from the active clinic folder where appropriate. These clients remain in PHIS; this report does not remove or modify their PHIS membership."); List(sb, changes.Removed);
        Section(sb, "FULL CURRENT CLINIC SCHEDULE", changes.Current); return sb.ToString();
    }
    private static void Section(StringBuilder sb, string title, IEnumerable<ClinicScheduleClient> clients) { sb.AppendLine().AppendLine(title); List(sb, clients); }
    private static void List(StringBuilder sb, IEnumerable<ClinicScheduleClient> clients) { var rows = clients.ToList(); if (rows.Count == 0) sb.AppendLine("(None)"); else foreach (var x in rows) sb.AppendLine($"{x.ClientId ?? "Pending resolution"} | {x.FullName} | {x.VaccineType}"); }
}
