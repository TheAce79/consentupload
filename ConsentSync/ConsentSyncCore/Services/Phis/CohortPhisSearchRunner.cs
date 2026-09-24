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
            record.BestMatch = null;
            string recordLabel = $"Record {i + 1}/{records.Count} ({DisplayName(record)}, DOB {record.DateOfBirth})";
            LoggerService.LogInformation($"\n🔎 Phase 2 {recordLabel}");
            progress?.Report(new Phase2Progress(i + 1, records.Count, record.DateOfBirth, DisplayName(record)));
            if (!DateOnly.TryParseExact(record.DateOfBirth, ["yyyy/MM/dd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                MarkFailed(record, "InvalidDateOfBirth");
                LogOutcome(recordLabel, record);
                continue;
            }

            SearchResult dobResult = await _searchService.SearchByDobAsync(parsedDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            LogSearchResult(recordLabel, "DOB", dobResult);
            EnsureSearchSucceeded(dobResult, "DOB", recordLabel);
            CandidateSelection candidates = GetActiveCandidates(dobResult.Results);
            if (candidates.StatusUnavailable) { MarkFailed(record, "ActiveStatusUnavailable"); LogOutcome(recordLabel, record); continue; }

            List<CandidateScore> scoredDobCandidates = ScoreCandidates(record, candidates.Active);
            SetBestMatch(record, scoredDobCandidates);
            List<CandidateScore> nameMatches = scoredDobCandidates.Where(candidate => candidate.IsTokenMatch || candidate.Score >= _threshold).ToList();
            LoggerService.LogInformation($"   {recordLabel}: active DOB candidates={candidates.Active.Count}; qualifying name matches={nameMatches.Count}.");
            if (nameMatches.Count == 1) { await MarkFoundAsync(record, nameMatches[0]); LogOutcome(recordLabel, record); continue; }
            if (nameMatches.Count > 1) { MarkFailed(record, "MultipleMatchingClientsFound"); LogOutcome(recordLabel, record); continue; }

            CandidateScore? previewCandidate = scoredDobCandidates.OrderByDescending(candidate => candidate.Score).FirstOrDefault();
            if (previewCandidate is not null && !string.IsNullOrWhiteSpace(previewCandidate.Candidate.ClientId))
            {
                try
                {
                    PhisClientPreview? preview = await _searchService.GetClientPreviewAsync(previewCandidate.Candidate.ClientId);
                    string? matchedIdentifier = PhisPreviewIdentityVerifier.GetMatchingIdentifier(record.Medicare, record.Phone, record.Email, preview, previewCandidate.Candidate.ClientId);
                    if (matchedIdentifier is not null)
                    {
                        LoggerService.LogInformation($"   {recordLabel}: PHIS preview verified candidate by {matchedIdentifier}.");
                        await MarkFoundAsync(record, new CandidateScore(previewCandidate.Candidate, _threshold, false), preview);
                        LogOutcome(recordLabel, record);
                        continue;
                    }
                    LoggerService.LogInformation($"   {recordLabel}: PHIS preview did not verify the below-threshold candidate; continuing fallbacks.");
                }
                catch (Exception ex) { LoggerService.LogWarning($"   {recordLabel}: PHIS preview verification failed; continuing fallbacks. {ex.Message}"); }
            }

            if (!string.IsNullOrWhiteSpace(record.Medicare))
            {
                SearchResult medicareResult = await _searchService.SearchByMedicareAsync(record.Medicare);
                LogSearchResult(recordLabel, "Medicare", medicareResult);
                EnsureSearchSucceeded(medicareResult, "Medicare", recordLabel);
                CandidateSelection medicareCandidates = GetActiveCandidates(medicareResult.Results);
                if (medicareCandidates.StatusUnavailable) { MarkFailed(record, "ActiveStatusUnavailable"); LogOutcome(recordLabel, record); continue; }
                List<CandidateScore> scoredMedicareCandidates = ScoreCandidates(record, medicareCandidates.Active);
                if (scoredMedicareCandidates.Count > 0) SetBestMatch(record, scoredMedicareCandidates);
                if (scoredMedicareCandidates.Count > 1) { MarkFailed(record, "MultipleClientsFoundInPhis"); LogOutcome(recordLabel, record); continue; }
                if (scoredMedicareCandidates.Count == 1) { await MarkFoundAsync(record, scoredMedicareCandidates[0]); LogOutcome(recordLabel, record); continue; }
            }

            if (IsValidEmail(record.Email) && await TryResolveByEmailAsync(record, recordLabel))
            {
                LogOutcome(recordLabel, record);
                continue;
            }

            MarkFailed(record, candidates.Active.Count == 0 ? "NoActiveClientFoundInPhis" : "NameMatchBelowThreshold");
            LogOutcome(recordLabel, record);
        }
        return records;
    }

    private List<CandidateScore> ScoreCandidates(ClinicPdfClientRecord record, IEnumerable<PhisSearchResult> candidates)
    {
        string sourceName = DisplayName(record);
        return candidates.Select(candidate =>
        {
            string forward = JoinName(candidate.FirstName, candidate.MiddleName, candidate.LastName);
            string reversed = JoinName(candidate.LastName, candidate.FirstName, candidate.MiddleName);
            bool tokenMatch = IsTokenMultisetEqual(sourceName, forward) || IsTokenMultisetEqual(sourceName, reversed) ||
                              IsTokenSubsetMatch(sourceName, forward) || IsTokenSubsetMatch(sourceName, reversed);
            double score = tokenMatch ? 100d : Math.Max(CalculateSimilarity(sourceName, forward), CalculateSimilarity(sourceName, reversed));
            return new CandidateScore(candidate, score, tokenMatch);
        }).ToList();
    }

    private static void SetBestMatch(ClinicPdfClientRecord record, IEnumerable<CandidateScore> candidates)
    {
        CandidateScore? best = candidates.OrderByDescending(candidate => candidate.Score).FirstOrDefault();
        if (best is not null) record.BestMatch = FormatBestMatch(best.Candidate, best.Score);
    }

    private async Task MarkFoundAsync(ClinicPdfClientRecord record, CandidateScore scoredCandidate, PhisClientPreview? preview = null)
    {
        PhisSearchResult candidate = scoredCandidate.Candidate;
        if (string.IsNullOrWhiteSpace(candidate.ClientId)) { MarkFailed(record, "MissingClientId"); return; }
        record.ClientId = candidate.ClientId; record.ClientIdStatus = ClientIdStatus.Found; record.FirstName = candidate.FirstName;
        record.LastName = candidate.LastName; record.MiddleName = candidate.MiddleName; record.ErrorDetails = null;
        record.BestMatch = FormatBestMatch(candidate, scoredCandidate.Score);
        await PopulateEmailAsync(record, preview);
    }

    private async Task PopulateEmailAsync(ClinicPdfClientRecord record, PhisClientPreview? preview = null)
    {
        if (!string.IsNullOrWhiteSpace(record.Email) || string.IsNullOrWhiteSpace(record.ClientId)) return;

        try
        {
            preview ??= await _searchService.GetClientPreviewAsync(record.ClientId);
            if (preview is null || !string.Equals(preview.ClientId, record.ClientId, StringComparison.Ordinal))
            {
                LoggerService.LogWarning($"   PHIS preview was unavailable or did not match resolved Client ID {record.ClientId}; email was not populated.");
                return;
            }

            List<PhisEmailAddress> valid = preview.EmailAddresses
                .Select(address => new PhisEmailAddress { Address = address.Address.Trim(), IsPreferred = address.IsPreferred })
                .Where(address => IsValidEmail(address.Address))
                .GroupBy(address => address.Address, StringComparer.OrdinalIgnoreCase)
                .Select(group => new PhisEmailAddress { Address = group.First().Address, IsPreferred = group.Any(address => address.IsPreferred) })
                .ToList();
            List<PhisEmailAddress> preferred = valid.Where(address => address.IsPreferred).ToList();
            IReadOnlyList<PhisEmailAddress> choice = preferred.Count > 0 ? preferred : valid;
            if (choice.Count == 1) record.Email = choice[0].Address;
            else if (choice.Count > 1) LoggerService.LogWarning($"   PHIS preview has multiple eligible email addresses for Client ID {record.ClientId}; email was not populated.");
            else LoggerService.LogInformation($"   PHIS preview has no valid email address for Client ID {record.ClientId}.");
        }
        catch (Exception ex)
        {
            LoggerService.LogWarning($"   PHIS email preview could not be read for Client ID {record.ClientId}: {ex.Message}");
        }
    }

    private static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return new System.Net.Mail.MailAddress(value).Address.Equals(value, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private async Task<bool> TryResolveByEmailAsync(ClinicPdfClientRecord record, string recordLabel)
    {
        try
        {
            SearchResult emailResult = await _searchService.SearchByEmailAsync(record.Email!.Trim());
            LogSearchResult(recordLabel, "Email", emailResult);
            if (!emailResult.Success || (emailResult.HasResults && !emailResult.ResultsComplete))
            {
                LoggerService.LogWarning($"   {recordLabel}: PHIS email fallback failed or was incomplete; continuing without email resolution.");
                return false;
            }

            CandidateSelection candidates = GetActiveCandidates(emailResult.Results);
            if (candidates.StatusUnavailable)
            {
                LoggerService.LogWarning($"   {recordLabel}: PHIS email fallback returned an unavailable client status; continuing without email resolution.");
                return false;
            }

            List<CandidateScore> scored = ScoreCandidates(record, candidates.Active);
            List<CandidateScore> sameDob = scored.Where(candidate => DatesMatch(record.DateOfBirth, candidate.Candidate.DateOfBirth)).ToList();
            if (sameDob.Count == 1) { await MarkFoundAsync(record, sameDob[0]); return true; }
            if (sameDob.Count > 1) { SetBestMatch(record, sameDob); MarkFailed(record, "MultipleClientsFoundByEmailAndDateOfBirth"); return true; }
            if (scored.Count > 0)
            {
                SetBestMatch(record, scored);
                bool unavailableDob = scored.Any(candidate => !TryNormalizeDate(candidate.Candidate.DateOfBirth, out _));
                MarkFailed(record, unavailableDob ? "EmailDateOfBirthUnavailable" : "EmailDateOfBirthMismatch");
                return true;
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogWarning($"   {recordLabel}: PHIS email fallback failed; continuing without email resolution. {ex.Message}");
        }
        return false;
    }

    private static bool DatesMatch(string left, string right) =>
        TryNormalizeDate(left, out DateOnly leftDate) && TryNormalizeDate(right, out DateOnly rightDate) && leftDate == rightDate;

    private static bool TryNormalizeDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value.Trim(), ["yyyy/MM/dd", "yyyy-MM-dd", "yyyy MMM dd", "yyyy MMMM dd"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);

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
    internal static bool IsTokenSubsetMatch(string source, string candidate)
    {
        string[] sourceTokens = Tokenize(source).ToArray();
        if (sourceTokens.Length == 0) return false;
        var candidateCounts = Tokenize(candidate).GroupBy(token => token).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (string token in sourceTokens)
        {
            if (!candidateCounts.TryGetValue(token, out int count) || count == 0) return false;
            candidateCounts[token] = count - 1;
        }
        return true;
    }
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
    private sealed record CandidateScore(PhisSearchResult Candidate, double Score, bool IsTokenMatch);
    private static string FormatBestMatch(PhisSearchResult candidate, double score) =>
        $"{TrimOrEmpty(candidate.FirstName)}#{TrimOrEmpty(candidate.LastName)}#{TrimOrEmpty(candidate.MiddleName)}#{TrimOrEmpty(candidate.ClientId)}#{score.ToString("F1", CultureInfo.InvariantCulture)}%";
    private static string TrimOrEmpty(string? value) => value?.Trim() ?? string.Empty;
}

public sealed record Phase2Progress(int Current, int Total, string DateOfBirth, string StudentName);
