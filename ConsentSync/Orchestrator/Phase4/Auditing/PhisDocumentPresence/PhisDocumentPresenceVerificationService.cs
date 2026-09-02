using ConsentSyncCore.Services.Configuration;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace Orchestrator.Phase4.Auditing.PhisDocumentPresence;

public sealed class PhisDocumentPresenceVerificationService
{
    private static readonly string[] RequiredHeaders = ["ClientID", "Document Title", "IsFeuilleRose", "PhisAntigen", "VerifStatus", "VerifClientIdStatus"];
    private readonly IPhisDocumentPresenceGateway _gateway;

    public PhisDocumentPresenceVerificationService(IPhisDocumentPresenceGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public static PhisDocumentPresenceVerificationPlan Prepare(string verificationCsvPath)
    {
        if (string.IsNullOrWhiteSpace(verificationCsvPath)) throw new ArgumentException("A verification CSV path is required.", nameof(verificationCsvPath));
        if (!File.Exists(verificationCsvPath)) throw new FileNotFoundException("Verification_Upload.csv was not found.", verificationCsvPath);

        using var reader = new StreamReader(verificationCsvPath, EncodingConfigurationService.GetPriorityEncoding());
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null
        });
        if (!csv.Read() || !csv.ReadHeader()) throw new InvalidDataException("Verification_Upload.csv has no header row.");
        var headers = BuildHeaders(csv.HeaderRecord ?? Array.Empty<string>());
        string[] missingHeaders = RequiredHeaders.Where(header => !headers.ContainsKey(header)).ToArray();
        if (missingHeaders.Length > 0) throw new InvalidDataException($"Verification_Upload.csv is missing required column(s): {string.Join(", ", missingHeaders)}.");

        var examples = new List<PhisDocumentPresencePreconditionItem>();
        var targets = new List<PhisDocumentPresenceTarget>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int rows = 0, eligible = 0, accepted = 0, duplicates = 0, uploadNotCompleted = 0, uploadReview = 0, clientIdentityUnresolved = 0, invalidStatus = 0;
        while (csv.Read())
        {
            rows++;
            string[] record = csv.Parser.Record ?? Array.Empty<string>();
            string clientId = NormalizeClientId(Field(record, headers["ClientID"]));
            string title = Field(record, headers["Document Title"]).Trim();
            string antigen = Field(record, headers["PhisAntigen"]).Trim();
            string verifStatusRaw = Field(record, headers["VerifStatus"]).Trim();
            string clientStatusRaw = Field(record, headers["VerifClientIdStatus"]).Trim();
            string roseRaw = Field(record, headers["IsFeuilleRose"]).Trim();

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

            if (!bool.TryParse(roseRaw, out bool isFileRose)) { invalidStatus++; AddExample("IsFeuilleRose must be true or false for an eligible row."); continue; }
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(title) || (!isFileRose && string.IsNullOrWhiteSpace(antigen)))
            {
                invalidStatus++;
                AddExample("Eligible rows require ClientID, Document Title, and PhisAntigen for consent documents.");
                continue;
            }

            eligible++;
            string normalizedTitle = isFileRose ? NormalizeContextTitle(title) : NormalizeConsentTitle(title);
            string key = string.Join("\u001f", clientId, normalizedTitle, isFileRose, NormalizeAntigen(antigen));
            if (!keys.Add(key)) { duplicates++; continue; }
            targets.Add(new PhisDocumentPresenceTarget { ClientId = clientId, DocumentTitle = title, IsFileRose = isFileRose, PhisAntigen = antigen });

            void AddExample(string reason)
            {
                if (examples.Count < 10) examples.Add(new PhisDocumentPresencePreconditionItem { ClientId = clientId, DocumentTitle = title, VerifStatus = verifStatusRaw, VerifClientIdStatus = clientStatusRaw, Reason = reason });
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

        return new PhisDocumentPresenceVerificationPlan
        {
            VerificationCsvRows = rows,
            EligibleRows = eligible,
            DuplicateTargetsCollapsed = duplicates,
            ExcludedAcceptedExceptions = accepted,
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
                bool returned = await _gateway.ReturnToSearchAsync();
                if (!returned || !_gateway.EnsureSessionValid()) return CreateResult(plan, items, plan.Targets.Count - index - 1);
                progress?.Report(new PhisDocumentPresenceProgress { Current = index + 1, Total = plan.Targets.Count, Target = target, Status = status });
            }
            catch (OperationCanceledException) { return CreateResult(plan, items, plan.Targets.Count - index); }
            catch (Exception ex)
            {
                items.Add(new PhisDocumentPresenceItemResult { Target = target, Status = PhisDocumentPresenceStatus.VerificationError, Detail = ex.Message });
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
        bool recovered = false;
        try { recovered = await _gateway.ReturnToSearchAsync() && _gateway.EnsureSessionValid(); } catch { }
        progress?.Report(new PhisDocumentPresenceProgress { Current = index + 1, Total = plan.Targets.Count, Target = target, Status = PhisDocumentPresenceStatus.VerificationError });
        return recovered;
    }

    private static PhisDocumentPresenceVerificationResult CreateResult(PhisDocumentPresenceVerificationPlan plan, List<PhisDocumentPresenceItemResult> items, int unprocessed) => new() { Plan = plan, Items = items, UnprocessedDocuments = unprocessed };
    private static Dictionary<string, int> BuildHeaders(IReadOnlyList<string> headers) { var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); for (int i = 0; i < headers.Count; i++) if (!result.TryAdd(headers[i].Trim(), i)) throw new InvalidDataException($"Verification_Upload.csv contains duplicate header '{headers[i]}'."); return result; }
    private static string Field(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index] ?? string.Empty : string.Empty;
    private static string NormalizeClientId(string? value) => value?.Trim() ?? string.Empty;
    private static string NormalizeConsentTitle(string value) => value.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
    private static string NormalizeContextTitle(string value) => value.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
    private static string NormalizeAntigen(string value) => value.Trim().ToLowerInvariant();
}
