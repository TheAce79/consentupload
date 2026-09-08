using System.Globalization;
using System.Text;
using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;

namespace ConsentSyncCore.Services.Phis;

public sealed class CohortPhisSearchRunner
{
    private readonly IPhisClientSearch _searchService;
    private readonly double _threshold;

    public CohortPhisSearchRunner(IPhisClientSearch searchService, double? threshold = null)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _threshold = threshold ?? ConfigurationService.GetFuzzyMatchingConfig().SingleResultThreshold;
    }

    public async Task<List<ClinicPdfClientRecord>> ExecuteSearchAsync(List<ClinicPdfClientRecord> records, IProgress<Phase2Progress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        for (int i = 0; i < records.Count; i++)
        {
            ClinicPdfClientRecord record = records[i];
            string recordLabel = $"Record {i + 1}/{records.Count} ({DisplayName(record)}, DOB {record.DateOfBirth})";
            LoggerService.LogInformation($"\n🔎 Phase 2 {recordLabel}");
            progress?.Report(new Phase2Progress(i + 1, records.Count, record.DateOfBirth, DisplayName(record)));
            if (!DateTime.TryParseExact(record.DateOfBirth, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                MarkFailed(record, "InvalidDateOfBirth");
                LogOutcome(recordLabel, record);
                continue;
            }

            SearchResult dobResult = await _searchService.SearchByDobAsync(record.DateOfBirth);
            LogSearchResult(recordLabel, "DOB", dobResult);
            EnsureSearchSucceeded(dobResult, "DOB", recordLabel);
            CandidateSelection candidates = GetActiveCandidates(dobResult.Results);
            if (candidates.StatusUnavailable) { MarkFailed(record, "ActiveStatusUnavailable"); LogOutcome(recordLabel, record); continue; }

            List<PhisSearchResult> nameMatches = candidates.Active.Where(candidate => IsNameMatch(record, candidate)).ToList();
            LoggerService.LogInformation($"   {recordLabel}: active DOB candidates={candidates.Active.Count}; qualifying name matches={nameMatches.Count}.");
            if (nameMatches.Count == 1 && TryMarkFound(record, nameMatches[0])) { LogOutcome(recordLabel, record); continue; }
            if (nameMatches.Count > 1) { MarkFailed(record, "MultipleMatchingClientsFoundInPhis"); LogOutcome(recordLabel, record); continue; }

            if (!string.IsNullOrWhiteSpace(record.Medicare))
            {
                SearchResult medicareResult = await _searchService.SearchByMedicareAsync(record.Medicare);
                LogSearchResult(recordLabel, "Medicare", medicareResult);
                EnsureSearchSucceeded(medicareResult, "Medicare", recordLabel);
                CandidateSelection medicareCandidates = GetActiveCandidates(medicareResult.Results);
                if (medicareCandidates.StatusUnavailable) { MarkFailed(record, "ActiveStatusUnavailable"); LogOutcome(recordLabel, record); continue; }
                if (medicareCandidates.Active.Count > 1) { MarkFailed(record, "MultipleClientsFoundInPhis"); LogOutcome(recordLabel, record); continue; }
                if (medicareCandidates.Active.Count == 1 && TryMarkFound(record, medicareCandidates.Active[0])) { LogOutcome(recordLabel, record); continue; }
            }

            MarkFailed(record, candidates.Active.Count == 0 ? "NoActiveClientFoundInPhis" : "NameMatchBelowThreshold");
            LogOutcome(recordLabel, record);
        }
        return records;
    }

    private bool IsNameMatch(ClinicPdfClientRecord record, PhisSearchResult candidate)
    {
        string sourceName = DisplayName(record);
        string forward = JoinName(candidate.FirstName, candidate.MiddleName, candidate.LastName);
        string reversed = JoinName(candidate.LastName, candidate.FirstName, candidate.MiddleName);
        if (IsTokenMultisetEqual(sourceName, forward) || IsTokenMultisetEqual(sourceName, reversed) ||
            Math.Max(CalculateSimilarity(sourceName, forward), CalculateSimilarity(sourceName, reversed)) >= _threshold)
            return true;
        return false;
    }

    private static bool TryMarkFound(ClinicPdfClientRecord record, PhisSearchResult candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ClientId)) { MarkFailed(record, "MissingClientId"); return true; }
        record.ClientId = candidate.ClientId; record.ClientIdStatus = ClientIdStatus.Found; record.FirstName = candidate.FirstName;
        record.LastName = candidate.LastName; record.MiddleName = candidate.MiddleName; record.ErrorDetails = null;
        return true;
    }

    private static CandidateSelection GetActiveCandidates(IEnumerable<PhisSearchResult> results)
    {
        List<PhisSearchResult> active = []; bool unknown = false;
        foreach (PhisSearchResult result in results)
        {
            string status = result.ActiveStatus.Trim();
            if (status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase) || status.Equals("A", StringComparison.OrdinalIgnoreCase)) active.Add(result);
            else if (!status.Equals("INACTIVE", StringComparison.OrdinalIgnoreCase) && !status.Equals("I", StringComparison.OrdinalIgnoreCase)) unknown = true;
        }
        return new CandidateSelection(active, unknown);
    }

    private static void EnsureSearchSucceeded(SearchResult result, string kind, string recordLabel)
    {
        if (!result.Success) throw new InvalidOperationException($"PHIS {kind} search failed: {result.ErrorMessage ?? "unknown error"}");
        if (result.HasResults && !result.ResultsComplete) throw new InvalidOperationException($"{recordLabel}: PHIS {kind} results could not be verified as complete.");
    }
    private static void LogSearchResult(string recordLabel, string kind, SearchResult result) =>
        LoggerService.LogInformation($"   {recordLabel}: {kind} search — Success={result.Success}; Results={result.Results.Count}; ResultsComplete={result.ResultsComplete}{(string.IsNullOrWhiteSpace(result.ErrorMessage) ? string.Empty : $"; Error={result.ErrorMessage}")}");
    private static void LogOutcome(string recordLabel, ClinicPdfClientRecord record) =>
        LoggerService.LogInformation($"   {recordLabel}: resolution outcome — Status={record.ClientIdStatus}; ClientId={record.ClientId ?? "(none)"}; Reason={record.ErrorDetails ?? "Resolved"}");
    private static void MarkFailed(ClinicPdfClientRecord record, string reason) { record.ClientId = null; record.ClientIdStatus = ClientIdStatus.NeedsManualReview; record.ErrorDetails = reason; }
    private static string DisplayName(ClinicPdfClientRecord r) => !string.IsNullOrWhiteSpace(r.FullName) ? r.FullName : JoinName(r.FirstName, r.MiddleName, r.LastName);
    private static string JoinName(params string?[] names) => string.Join(' ', names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()));
    internal static bool IsTokenMultisetEqual(string a, string b) => Tokenize(a).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(Tokenize(b).OrderBy(x => x, StringComparer.Ordinal));
    internal static double CalculateSimilarity(string a, string b)
    {
        string left = string.Concat(Tokenize(a)); string right = string.Concat(Tokenize(b));
        if (left.Length == 0 || right.Length == 0) return 0;
        int[,] distance = new int[left.Length + 1, right.Length + 1];
        for (int i = 0; i <= left.Length; i++) distance[i, 0] = i;
        for (int j = 0; j <= right.Length; j++) distance[0, j] = j;
        for (int i = 1; i <= left.Length; i++) for (int j = 1; j <= right.Length; j++) distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1), distance[i - 1, j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
        return 100d * (1d - (double)distance[left.Length, right.Length] / Math.Max(left.Length, right.Length));
    }
    private static IEnumerable<string> Tokenize(string value) => RemoveDiacritics(value).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private static string RemoveDiacritics(string value) { var sb = new StringBuilder(); foreach (char c in value.Normalize(NormalizationForm.FormD)) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.IsLetterOrDigit(c) ? c : ' '); return sb.ToString().Normalize(NormalizationForm.FormC); }
    private sealed record CandidateSelection(List<PhisSearchResult> Active, bool StatusUnavailable);
}

public sealed record Phase2Progress(int Current, int Total, string DateOfBirth, string StudentName);
