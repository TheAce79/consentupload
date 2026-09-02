using ConsentSyncCore.Services.Configuration;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;

namespace Orchestrator.Phase4.Auditing.PhisDocumentPresence;

public sealed class PhisDocumentPresenceVerificationService
{
    private static readonly string[] RequiredHeaders = ["ClientID", "Document Title", "IsFeuilleRose", "PhisAntigen", "VerifStatus", "VerifClientIdStatus"];
    private const string PhisVerificationStatusHeader = "PhisVerificationStatus";
    private static readonly SemaphoreSlim CsvLock = new(1, 1);
    private readonly IPhisDocumentPresenceGateway _gateway;

    public PhisDocumentPresenceVerificationService(IPhisDocumentPresenceGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public static PhisDocumentPresenceVerificationPlan Prepare(string verificationCsvPath, int batchSize = 0)
    {
        if (string.IsNullOrWhiteSpace(verificationCsvPath)) throw new ArgumentException("A verification CSV path is required.", nameof(verificationCsvPath));
        if (!File.Exists(verificationCsvPath)) throw new FileNotFoundException("Verification_Upload.csv was not found.", verificationCsvPath);

        Encoding encoding = EncodingConfigurationService.GetPriorityEncoding();
        CsvTable table = ReadCsvTable(verificationCsvPath, encoding);
        Dictionary<string, int> headers = BuildHeaders(table.Headers);
        string[] missingHeaders = RequiredHeaders.Where(header => !headers.ContainsKey(header)).ToArray();
        if (missingHeaders.Length > 0) throw new InvalidDataException($"Verification_Upload.csv is missing required column(s): {string.Join(", ", missingHeaders)}.");

        var examples = new List<PhisDocumentPresencePreconditionItem>();
        var notVerifiedTargets = new List<PhisDocumentPresenceTarget>();
        var retryTargets = new List<PhisDocumentPresenceTarget>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        bool statusColumnMissing = !headers.ContainsKey(PhisVerificationStatusHeader);
        int rows = 0, eligible = 0, accepted = 0, duplicates = 0, alreadyVerified = 0, uploadNotCompleted = 0, uploadReview = 0, clientIdentityUnresolved = 0, invalidStatus = 0;
        foreach (string[] record in table.Rows)
        {
            rows++;
            string clientId = NormalizeClientId(Field(record, headers["ClientID"]));
            string title = Field(record, headers["Document Title"]).Trim();
            string antigen = Field(record, headers["PhisAntigen"]).Trim();
            string verifStatusRaw = Field(record, headers["VerifStatus"]).Trim();
            string clientStatusRaw = Field(record, headers["VerifClientIdStatus"]).Trim();
            string roseRaw = Field(record, headers["IsFeuilleRose"]).Trim();
            string phisStatusRaw = statusColumnMissing ? "0" : Field(record, headers[PhisVerificationStatusHeader]).Trim();

            if (!int.TryParse(verifStatusRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int verifStatus) ||
                !int.TryParse(clientStatusRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientStatus))
            {
                invalidStatus++;
                AddExample("Verification statuses must be numeric.");
                continue;
            }

            if (verifStatus == 2 && clientStatus == 3) { accepted++; continue; }
            if (verifStatus != 1 || clientStatus != 1)
            {
                if (verifStatus == 0) uploadNotCompleted++;
                else if (verifStatus == 2) uploadReview++;
                if (clientStatus == 3) clientIdentityUnresolved++;
                invalidStatus++;
                AddExample("Only VerifStatus = 1 and VerifClientIdStatus = 1 are eligible for PHIS verification.");
                continue;
            }

            if (!TryParsePhisVerificationStatus(phisStatusRaw, out PhisVerificationStatus phisStatus))
            {
                invalidStatus++;
                AddExample("PhisVerificationStatus must be 0 (Not Verified), 1 (Verified OK), or 2 (Verified KO).");
                continue;
            }

            if (!bool.TryParse(roseRaw, out bool isFileRose)) { invalidStatus++; AddExample("IsFeuilleRose must be true or false for an eligible row."); continue; }
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(title) || (!isFileRose && string.IsNullOrWhiteSpace(antigen)))
            {
                invalidStatus++;
                AddExample("Eligible rows require ClientID, Document Title, and PhisAntigen for consent documents.");
                continue;
            }

            eligible++;
            if (phisStatus == PhisVerificationStatus.VerifiedOk) { alreadyVerified++; continue; }
            string normalizedTitle = isFileRose ? NormalizeContextTitle(title) : NormalizeConsentTitle(title);
            string key = string.Join("\u001f", clientId, normalizedTitle, isFileRose, NormalizeAntigen(antigen));
            if (!keys.Add(key)) { duplicates++; continue; }
            var target = new PhisDocumentPresenceTarget { ClientId = clientId, DocumentTitle = title, IsFileRose = isFileRose, PhisAntigen = antigen, VerificationKey = key };
            if (phisStatus == PhisVerificationStatus.VerifiedKo) retryTargets.Add(target);
            else notVerifiedTargets.Add(target);

            void AddExample(string reason)
            {
                if (examples.Count < 10) examples.Add(new PhisDocumentPresencePreconditionItem { ClientId = clientId, DocumentTitle = title, VerifStatus = verifStatusRaw, VerifClientIdStatus = clientStatusRaw, PhisVerificationStatus = phisStatusRaw, Reason = reason });
            }
        }

        if (invalidStatus > 0)
            throw new PhisDocumentPresencePreconditionException("Verification_Upload.csv contains rows that are not eligible for PHIS document verification.")
            {
                UploadNotCompletedRows = uploadNotCompleted,
                UploadReviewRows = uploadReview,
                ClientIdentityUnresolvedRows = clientIdentityUnresolved,
                InvalidStatusRows = invalidStatus,
                Examples = examples
            };

        if (statusColumnMissing) EnsurePhisVerificationStatusColumn(verificationCsvPath, encoding);
        List<PhisDocumentPresenceTarget> pendingTargets = notVerifiedTargets.Concat(retryTargets).ToList();
        int effectiveBatchSize = batchSize > 0 ? batchSize : int.MaxValue;
        List<PhisDocumentPresenceTarget> targets = pendingTargets.Take(effectiveBatchSize).ToList();
        bool batchLimitReached = targets.Count < pendingTargets.Count;

        return new PhisDocumentPresenceVerificationPlan
        {
            VerificationCsvPath = verificationCsvPath,
            VerificationCsvRows = rows,
            EligibleRows = eligible,
            DuplicateTargetsCollapsed = duplicates,
            ExcludedAcceptedExceptions = accepted,
            AlreadyVerifiedRows = alreadyVerified,
            PendingRows = pendingTargets.Count,
            BatchLimitReached = batchLimitReached,
            RemainingAfterBatch = pendingTargets.Count - targets.Count,
            Targets = targets
        };
    }

    public async Task<PhisDocumentPresenceVerificationResult> VerifyAsync(PhisDocumentPresenceVerificationPlan plan, IProgress<PhisDocumentPresenceProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var items = new List<PhisDocumentPresenceItemResult>();
        for (int index = 0; index < plan.Targets.Count; index++)
        {
            PhisDocumentPresenceTarget target = plan.Targets[index];
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_gateway.EnsureSessionValid()) return CreateResult(plan, items, plan.Targets.Count - index);
                if (!await _gateway.SetClientContextAsync(target.ClientId))
                {
                    if (!await RecordRecoverableErrorAsync(plan, items, target, index, "Client context could not be set.", progress))
                        return CreateResult(plan, items, plan.Targets.Count - index - 1);
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                bool opened = target.IsFileRose ? await _gateway.OpenFileRoseDocumentListAsync() : await _gateway.OpenConsentDocumentListAsync(target.PhisAntigen);
                if (!opened)
                {
                    if (!await RecordRecoverableErrorAsync(plan, items, target, index, "PHIS document list could not be opened.", progress))
                        return CreateResult(plan, items, plan.Targets.Count - index - 1);
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var lookup = target.IsFileRose ? await _gateway.FindFileRoseDocumentAsync(target.DocumentTitle) : await _gateway.FindConsentDocumentAsync(target.DocumentTitle);
                cancellationToken.ThrowIfCancellationRequested();
                var status = lookup.Status switch
                {
                    ConsentSyncCore.Services.Phis.PhisDocumentLookupStatus.Found => PhisDocumentPresenceStatus.Found,
                    ConsentSyncCore.Services.Phis.PhisDocumentLookupStatus.NotFound => PhisDocumentPresenceStatus.Missing,
                    _ => PhisDocumentPresenceStatus.VerificationError
                };
                items.Add(new PhisDocumentPresenceItemResult { Target = target, Status = status, Detail = lookup.ErrorMessage ?? lookup.MatchedTitle ?? string.Empty });
                SavePhisVerificationStatus(plan, target, ToCsvStatus(status));
                bool returned = await _gateway.ReturnToSearchAsync();
                if (!returned || !_gateway.EnsureSessionValid()) return CreateResult(plan, items, plan.Targets.Count - index - 1);
                progress?.Report(new PhisDocumentPresenceProgress { Current = index + 1, Total = plan.Targets.Count, Target = target, Status = status });
            }
            catch (OperationCanceledException) { return CreateResult(plan, items, plan.Targets.Count - index); }
            catch (Exception ex)
            {
                items.Add(new PhisDocumentPresenceItemResult { Target = target, Status = PhisDocumentPresenceStatus.VerificationError, Detail = ex.Message });
                SavePhisVerificationStatus(plan, target, PhisVerificationStatus.VerifiedKo);
                bool recovered = false;
                try { recovered = await _gateway.ReturnToSearchAsync() && _gateway.EnsureSessionValid(); } catch { }
                if (!recovered) return CreateResult(plan, items, plan.Targets.Count - index - 1);
                progress?.Report(new PhisDocumentPresenceProgress { Current = index + 1, Total = plan.Targets.Count, Target = target, Status = PhisDocumentPresenceStatus.VerificationError });
            }
        }
        return CreateResult(plan, items, 0);
    }

    private async Task<bool> RecordRecoverableErrorAsync(PhisDocumentPresenceVerificationPlan plan, List<PhisDocumentPresenceItemResult> items, PhisDocumentPresenceTarget target, int index, string detail, IProgress<PhisDocumentPresenceProgress>? progress)
    {
        items.Add(new PhisDocumentPresenceItemResult { Target = target, Status = PhisDocumentPresenceStatus.VerificationError, Detail = detail });
        SavePhisVerificationStatus(plan, target, PhisVerificationStatus.VerifiedKo);
        bool recovered = false;
        try { recovered = await _gateway.ReturnToSearchAsync() && _gateway.EnsureSessionValid(); } catch { }
        progress?.Report(new PhisDocumentPresenceProgress { Current = index + 1, Total = plan.Targets.Count, Target = target, Status = PhisDocumentPresenceStatus.VerificationError });
        return recovered;
    }

    private static PhisVerificationStatus ToCsvStatus(PhisDocumentPresenceStatus status) =>
        status == PhisDocumentPresenceStatus.Found ? PhisVerificationStatus.VerifiedOk : PhisVerificationStatus.VerifiedKo;

    private static bool TryParsePhisVerificationStatus(string rawValue, out PhisVerificationStatus status)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            status = PhisVerificationStatus.NotVerified;
            return true;
        }

        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
            Enum.IsDefined(typeof(PhisVerificationStatus), value))
        {
            status = (PhisVerificationStatus)value;
            return true;
        }

        status = PhisVerificationStatus.NotVerified;
        return false;
    }

    private static void EnsurePhisVerificationStatusColumn(string path, Encoding encoding)
    {
        CsvTable table = ReadCsvTable(path, encoding);
        Dictionary<string, int> headers = BuildHeaders(table.Headers);
        if (headers.ContainsKey(PhisVerificationStatusHeader)) return;

        var outputHeaders = table.Headers.Concat([PhisVerificationStatusHeader]).ToArray();
        var outputRows = table.Rows.Select(row => row.Concat(["0"]).ToArray()).ToList();
        WriteCsvTableAtomically(path, outputHeaders, outputRows, encoding);
    }

    private static void SavePhisVerificationStatus(PhisDocumentPresenceVerificationPlan plan, PhisDocumentPresenceTarget target, PhisVerificationStatus status)
    {
        if (string.IsNullOrWhiteSpace(plan.VerificationCsvPath)) return;
        if (!CsvLock.Wait(TimeSpan.FromSeconds(15))) return;

        try
        {
            Encoding encoding = EncodingConfigurationService.GetPriorityEncoding();
            CsvTable table = ReadCsvTable(plan.VerificationCsvPath, encoding);
            Dictionary<string, int> headers = BuildHeaders(table.Headers);
            bool statusColumnMissing = !headers.ContainsKey(PhisVerificationStatusHeader);
            int statusIndex = statusColumnMissing ? table.Headers.Length : headers[PhisVerificationStatusHeader];
            string[] outputHeaders = statusColumnMissing ? table.Headers.Concat([PhisVerificationStatusHeader]).ToArray() : table.Headers;
            var outputRows = new List<string[]>(table.Rows.Count);
            bool changed = statusColumnMissing;

            foreach (string[] sourceRow in table.Rows)
            {
                string[] row = EnsureLength(sourceRow, outputHeaders.Length);
                if (RowMatchesTarget(row, headers, target) && row[statusIndex] != ((int)status).ToString(CultureInfo.InvariantCulture))
                {
                    row[statusIndex] = ((int)status).ToString(CultureInfo.InvariantCulture);
                    changed = true;
                }
                outputRows.Add(row);
            }

            if (changed) WriteCsvTableAtomically(plan.VerificationCsvPath, outputHeaders, outputRows, encoding);
        }
        finally
        {
            CsvLock.Release();
        }
    }

    private static bool RowMatchesTarget(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> headers, PhisDocumentPresenceTarget target)
    {
        if (!headers.ContainsKey("ClientID") || !headers.ContainsKey("Document Title") || !headers.ContainsKey("IsFeuilleRose") || !headers.ContainsKey("PhisAntigen") || !headers.ContainsKey("VerifStatus") || !headers.ContainsKey("VerifClientIdStatus")) return false;
        if (!int.TryParse(Field(row, headers["VerifStatus"]).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int verifStatus) || verifStatus != 1) return false;
        if (!int.TryParse(Field(row, headers["VerifClientIdStatus"]).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientStatus) || clientStatus != 1) return false;
        if (!bool.TryParse(Field(row, headers["IsFeuilleRose"]).Trim(), out bool isFileRose)) return false;

        string clientId = NormalizeClientId(Field(row, headers["ClientID"]));
        string title = Field(row, headers["Document Title"]).Trim();
        string antigen = Field(row, headers["PhisAntigen"]).Trim();
        string normalizedTitle = isFileRose ? NormalizeContextTitle(title) : NormalizeConsentTitle(title);
        string key = string.Join("\u001f", clientId, normalizedTitle, isFileRose, NormalizeAntigen(antigen));
        return string.Equals(key, target.VerificationKey, StringComparison.Ordinal);
    }

    private static CsvTable ReadCsvTable(string path, Encoding encoding)
    {
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        using var parser = new CsvParser(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.None
        });
        if (!parser.Read()) throw new InvalidDataException("Verification_Upload.csv has no header row.");
        string[] headers = parser.Record ?? Array.Empty<string>();
        var rows = new List<string[]>();
        while (parser.Read()) rows.Add(parser.Record ?? Array.Empty<string>());
        return new CsvTable(headers, rows);
    }

    private static void WriteCsvTableAtomically(string path, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, Encoding encoding)
    {
        string temporaryPath = path + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporaryPath, append: false, encoding))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                foreach (string header in headers) csv.WriteField(header);
                csv.NextRecord();
                foreach (string[] row in rows)
                {
                    foreach (string value in EnsureLength(row, headers.Count)) csv.WriteField(value);
                    csv.NextRecord();
                }
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    private static string[] EnsureLength(IReadOnlyList<string> row, int length)
    {
        var result = new string[length];
        for (int index = 0; index < length; index++) result[index] = index < row.Count ? row[index] ?? string.Empty : string.Empty;
        return result;
    }

    private static PhisDocumentPresenceVerificationResult CreateResult(PhisDocumentPresenceVerificationPlan plan, List<PhisDocumentPresenceItemResult> items, int unprocessed) => new() { Plan = plan, Items = items, UnprocessedDocuments = unprocessed };
    private static Dictionary<string, int> BuildHeaders(IReadOnlyList<string> headers) { var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); for (int i = 0; i < headers.Count; i++) if (!result.TryAdd(headers[i].Trim(), i)) throw new InvalidDataException($"Verification_Upload.csv contains duplicate header '{headers[i]}'."); return result; }
    private static string Field(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index] ?? string.Empty : string.Empty;
    private static string NormalizeClientId(string? value) => value?.Trim() ?? string.Empty;
    private static string NormalizeConsentTitle(string value) => value.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
    private static string NormalizeContextTitle(string value) => value.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
    private static string NormalizeAntigen(string value) => value.Trim().ToLowerInvariant();

    private sealed record CsvTable(string[] Headers, List<string[]> Rows);
}
