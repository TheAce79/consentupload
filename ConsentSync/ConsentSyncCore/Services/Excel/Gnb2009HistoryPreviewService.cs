using ConsentSyncCore.Models;

namespace ConsentSyncCore.Services.Excel;

public static class Gnb2009HistoryPreviewService
{
    public static EligibilityHistoryPreviewResult BuildPreview(
        IEnumerable<CohortReviewRow> cohortRows,
        IReadOnlyDictionary<string, ClientImmunizationHistory> histories)
    {
        ArgumentNullException.ThrowIfNull(cohortRows);
        ArgumentNullException.ThrowIfNull(histories);

        var result = new EligibilityHistoryPreviewResult();
        List<CohortReviewRow> rows = cohortRows.ToList();
        var allCsvIds = rows.Select(row => row.ClientId?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (CohortReviewRow row in rows.Where(row => !row.Excluded))
        {
            string clientId = row.ClientId.Trim();
            if (histories.TryGetValue(clientId, out ClientImmunizationHistory? history))
            {
                result.Rows.Add(new EligibilityHistoryPreviewRow
                {
                    ClientId = clientId,
                    FullName = row.FullName,
                    DateOfBirth = row.DateOfBirth,
                    VaccineType = row.Source.VaccineType,
                    HistoryMatchStatus = "History Found",
                    DoseCount = history.Records.Count,
                    LatestDoseDate = history.Records.Count == 0 ? null : history.Records.Max(record => record.AdministeredDate).ToString("yyyy-MM-dd")
                });
            }
            else
            {
                result.Rows.Add(new EligibilityHistoryPreviewRow
                {
                    ClientId = clientId,
                    FullName = row.FullName,
                    DateOfBirth = row.DateOfBirth,
                    VaccineType = row.Source.VaccineType,
                    HistoryMatchStatus = "No History Found",
                    DoseCount = 0,
                    Warning = "CSV client is absent from the GNB2009 Excel history."
                });
            }
        }

        foreach (ClientImmunizationHistory history in histories.Values
                     .Where(history => !allCsvIds.Contains(history.ClientId))
                     .OrderBy(history => history.ClientId, StringComparer.OrdinalIgnoreCase))
        {
            string warning = $"Extra Client in PHIS Excel: ClientID {history.ClientId} - {history.FullName}. Manual review required.";
            result.Warnings.Add(warning);
            result.Rows.Add(new EligibilityHistoryPreviewRow
            {
                ClientId = history.ClientId,
                FullName = history.FullName,
                DateOfBirth = history.DateOfBirth == default ? string.Empty : history.DateOfBirth.ToString("yyyy-MM-dd"),
                HistoryMatchStatus = "Added in PHIS (Not in Cohort CSV)",
                DoseCount = history.Records.Count,
                LatestDoseDate = history.Records.Count == 0 ? null : history.Records.Max(record => record.AdministeredDate).ToString("yyyy-MM-dd"),
                Warning = "Manual PHIS addition. Reconcile against the cohort CSV."
            });
        }

        return result;
    }
}
