using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Configuration;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;

namespace Orchestrator.Phase4.Auditing.ClientIdentity;

public sealed class ClientIdentityPreAuditService
{
    private static readonly string[] RequiredUploadHeaders = ["ClientID", "Last Name", "First Name", "VerifStatus", "FailureReason", "Remarks By Melisa"];
    private static readonly string[] RequiredRosterHeaders = ["ClientId", "ClientName", "DateOfBirth", "Gender"];
    private static readonly HashSet<string> ClientIdentityAuditHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "VerifClientIdStatus", "VerifReasonCode", "VerificationType", "RosterClientName",
        "VerifNameResult", "VerifNameScore", "VerifError"
    };

    private readonly DeterministicNameComparer _nameComparer = new();

    public ClientIdentityPreAuditResult ExecuteAudit()
    {
        var phase2Config = ConfigurationService.GetPhase2Config();
        var prePhase3Config = ConfigurationService.GetPrePhase3Config();
        string uploadCsvPath = Path.Combine(prePhase3Config.OutputPath, phase2Config.UploadCsv);
        string rosterCsvPath = ConfigurationService.GetMassImmunisationCsvFullPath();
        string outputPath = Path.Combine(prePhase3Config.OutputPath, ClientIdentityPreAuditFileNames.OutputFileName);

        EnsureAvailable(uploadCsvPath, "Upload_to_PHIS.csv");
        EnsureAvailable(rosterCsvPath, "mass_immunisation.csv");
        if (File.Exists(outputPath) && !WorkspaceInitializer.IsFileAvailable(outputPath))
            throw new IOException("Verification_Upload.csv is open or unavailable. Close it in Excel and try again.");

        Encoding encoding = EncodingConfigurationService.GetPriorityEncoding();
        CsvTable uploadTable = ReadUploadTable(uploadCsvPath, encoding);
        List<MassImmunisationRosterRecord> rosterRows = ReadRoster(rosterCsvPath, encoding);
        var rosterByClientId = rosterRows
            .GroupBy(row => NormalizeClientId(row.ClientId), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        AuditOutput output = BuildOutput(uploadTable, rosterRows, rosterByClientId, outputPath);
        WriteOutputAtomically(outputPath, output.Headers, output.Rows, encoding);
        return output.Result;
    }

    private AuditOutput BuildOutput(CsvTable uploadTable, IReadOnlyList<MassImmunisationRosterRecord> rosterRows, IReadOnlyDictionary<string, List<MassImmunisationRosterRecord>> rosterByClientId, string outputPath)
    {
        Dictionary<string, int> headers = BuildCanonicalHeaderIndexes(uploadTable.Headers, "Upload_to_PHIS.csv");
        var context = new ClientIdentityAuditContext(rosterRows, rosterByClientId, FindClientIdsWithMultipleDigitalNames(uploadTable.Rows, headers));
        var counters = new AuditCounters();
        var uniqueClientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var acceptedExceptionClientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manualClientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputHeaders = uploadTable.Headers.Where(header => !IsAuditHeader(header)).ToList();
        outputHeaders.AddRange(["VerifClientIdStatus", "VerifReasonCode", "VerificationType", "RosterClientName", "VerifNameResult", "VerifError"]);
        int[] retainedIndexes = uploadTable.Headers.Select((header, index) => new { Header = header, Index = index }).Where(item => !IsAuditHeader(item.Header)).Select(item => item.Index).ToArray();
        var outputRows = new List<string[]>(uploadTable.Rows.Count);

        foreach (string[] rawFields in uploadTable.Rows)
        {
            var uploadRow = new UploadAuditRow(
                GetField(rawFields, headers["ClientID"]), GetField(rawFields, headers["Last Name"]),
                GetField(rawFields, headers["First Name"]), GetField(rawFields, headers["VerifStatus"]),
                GetField(rawFields, headers["FailureReason"]), GetField(rawFields, headers["Remarks By Melisa"]));
            string normalizedClientId = NormalizeClientId(uploadRow.RawClientId);
            if (!string.IsNullOrEmpty(normalizedClientId)) uniqueClientIds.Add(normalizedClientId);

            ClientIdentityAuditRowResult rowResult = AuditRow(uploadRow, context);
            ApplyCounters(counters, rowResult, normalizedClientId, acceptedExceptionClientIds, manualClientIds);

            var values = retainedIndexes.Select(index => GetField(rawFields, index)).ToList();
            values.Add(((int)rowResult.Status).ToString(CultureInfo.InvariantCulture));
            values.Add(rowResult.ReasonCode);
            values.Add(rowResult.VerificationType.ToString());
            values.Add(rowResult.RosterClientName);
            values.Add(rowResult.NameComparisonResult.ToString());
            values.Add(rowResult.Error);
            outputRows.Add(values.ToArray());
        }

        if (counters.TotalRows != counters.UploadNotCompletedRows + counters.AutomaticallyVerifiedRows + counters.NeedsManualReviewRows + counters.AcceptedUploadExceptionRows)
        {
            throw new InvalidOperationException("Client Identity Pre-Audit primary outcome counters are inconsistent.");
        }

        return new AuditOutput
        {
            Headers = outputHeaders,
            Rows = outputRows,
            Result = new ClientIdentityPreAuditResult
            {
                TotalRows = counters.TotalRows,
                UniqueClientIds = uniqueClientIds.Count,
                SuccessfulUploadRows = counters.SuccessfulUploadRows,
                AutomaticallyVerifiedRows = counters.AutomaticallyVerifiedRows,
                NeedsManualReviewRows = counters.NeedsManualReviewRows,
                AcceptedUploadExceptionRows = counters.AcceptedUploadExceptionRows,
                AcceptedUploadExceptionClientIds = acceptedExceptionClientIds.Count,
                ExactMatchRows = counters.ExactMatchRows,
                CompatibleMatchRows = counters.CompatibleMatchRows,
                TokenOrderEquivalentRows = counters.TokenOrderEquivalentRows,
                UniquePartialMatchRows = counters.UniquePartialMatchRows,
                UploadNotCompletedRows = counters.UploadNotCompletedRows,
                UploadFailedRows = counters.UploadFailedRows,
                InvalidStatusRows = counters.InvalidStatusRows,
                DigitalConsentRows = counters.DigitalConsentRows,
                ManualConsentRows = counters.ManualConsentRows,
                UniqueManualConsentClientIds = manualClientIds.Count,
                MissingClientIdRows = counters.MissingClientIdRows,
                ClientIdNotInRosterRows = counters.ClientIdNotInRosterRows,
                IncompleteNameRows = counters.IncompleteNameRows,
                DuplicateRosterClientIdRows = counters.DuplicateRosterClientIdRows,
                DuplicateRosterClientIds = rosterByClientId.Count(pair => pair.Value.Count > 1),
                ReversedNameRows = counters.ReversedNameRows,
                NameMismatchRows = counters.NameMismatchRows,
                AmbiguousNameRows = counters.AmbiguousNameRows,
                MultipleUploadNameRows = counters.MultipleUploadNameRows,
                OutputPath = outputPath
            }
        };
    }

    private ClientIdentityAuditRowResult AuditRow(UploadAuditRow row, ClientIdentityAuditContext context)
    {
        if (!int.TryParse(row.RawVerifStatus.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int uploadStatus))
            return NeedsReview(ClientIdentityAuditReasonCodes.InvalidUploadStatus, ClientIdentityVerificationType.UploadFailed, NameComparisonResult.NotChecked, $"Invalid VerifStatus value: '{row.RawVerifStatus}'.");

        if (uploadStatus is not 0 and not 1 and not 2)
            return NeedsReview(ClientIdentityAuditReasonCodes.InvalidUploadStatus, ClientIdentityVerificationType.UploadFailed, NameComparisonResult.NotChecked, $"Unsupported VerifStatus value: '{row.RawVerifStatus}'.");

        if (AcceptedUploadExceptionPolicy.IsAcceptedException(uploadStatus, row.RawRemarksByMelisa))
            return AcceptedException();

        if (uploadStatus == 0)
            return new ClientIdentityAuditRowResult
            {
                Status = ClientIdentityAuditStatus.NotProcessed,
                ReasonCode = ClientIdentityAuditReasonCodes.UploadNotCompleted,
                VerificationType = ClientIdentityVerificationType.UploadIncomplete,
                RosterClientName = string.Empty,
                NameComparisonResult = NameComparisonResult.NotChecked,
                Error = "Upload not completed."
            };

        if (uploadStatus == 2)
        {
            string failureReason = row.RawFailureReason.Trim();
            string error = string.IsNullOrWhiteSpace(failureReason) ? "The upload requires review." : $"The upload requires review: {failureReason}";
            return NeedsReview(ClientIdentityAuditReasonCodes.UploadRequiresReview, ClientIdentityVerificationType.UploadFailed, NameComparisonResult.NotChecked, error);
        }

        return AuditSuccessfulUpload(row, context);
    }

    private ClientIdentityAuditRowResult AuditSuccessfulUpload(UploadAuditRow row, ClientIdentityAuditContext context)
    {
        string clientId = row.RawClientId.Trim();
        string firstName = row.RawFirstName.Trim();
        string lastName = row.RawLastName.Trim();
        bool firstNameMissing = string.IsNullOrWhiteSpace(firstName);
        bool lastNameMissing = string.IsNullOrWhiteSpace(lastName);
        bool isManualConsent = firstNameMissing && lastNameMissing;
        ClientIdentityVerificationType type = isManualConsent ? ClientIdentityVerificationType.ManualConsent : ClientIdentityVerificationType.DigitalConsent;

        if (string.IsNullOrWhiteSpace(clientId))
            return NeedsReview(ClientIdentityAuditReasonCodes.ClientIdMissing, type, NameComparisonResult.NotChecked, "Client ID is missing from Upload_to_PHIS.csv.");

        string normalizedClientId = NormalizeClientId(clientId);
        if (isManualConsent)
            return AuditManualConsent(clientId, normalizedClientId, context.RosterByClientId);

        if (firstNameMissing ^ lastNameMissing)
            return NeedsReview(ClientIdentityAuditReasonCodes.IncompleteStudentName, ClientIdentityVerificationType.DigitalConsent, NameComparisonResult.NotChecked, "The student identity is incomplete. Both First Name and Last Name are required for automatic Client ID verification.", TryGetUniqueRosterName(normalizedClientId, context.RosterByClientId));

        if (context.MultipleDigitalNamesByClientId.Contains(normalizedClientId))
            return NeedsReview(ClientIdentityAuditReasonCodes.ClientIdUsedForMultipleNames, ClientIdentityVerificationType.DigitalConsent, NameComparisonResult.NotChecked, $"Client ID '{clientId}' is associated with multiple uploaded student names.", TryGetUniqueRosterName(normalizedClientId, context.RosterByClientId));

        if (!context.RosterByClientId.TryGetValue(normalizedClientId, out List<MassImmunisationRosterRecord>? rosterMatches))
            return NeedsReview(ClientIdentityAuditReasonCodes.ClientIdNotInRoster, ClientIdentityVerificationType.DigitalConsent, NameComparisonResult.NotChecked, $"Client ID '{clientId}' was not found in mass_immunisation.csv.");

        if (rosterMatches.Count != 1)
            return NeedsReview(ClientIdentityAuditReasonCodes.DuplicateClientIdInRoster, ClientIdentityVerificationType.DigitalConsent, NameComparisonResult.NotChecked, $"Client ID '{clientId}' appears more than once in mass_immunisation.csv.");

        string rosterClientName = rosterMatches[0].ClientName;
        DeterministicNameCandidateKind candidate = _nameComparer.CompareCandidate(firstName, lastName, rosterClientName);
        if (candidate == DeterministicNameCandidateKind.Exact)
            return Success(rosterClientName, NameComparisonResult.Exact, ClientIdentityAuditReasonCodes.SuccessExactDigitalMatch);

        if (DeterministicNameComparer.IsAutomaticCandidate(candidate))
        {
            if (HasCompetingRosterIdentity(firstName, lastName, normalizedClientId, context.RosterRows))
                return NeedsReview(ClientIdentityAuditReasonCodes.AmbiguousRosterIdentity, ClientIdentityVerificationType.DigitalConsent, NameComparisonResult.Ambiguous, $"Client ID '{clientId}' has a non-exact deterministic name match that also identifies another roster Client ID.", rosterClientName);

            return candidate switch
            {
                DeterministicNameCandidateKind.Compatible => Success(rosterClientName, NameComparisonResult.Compatible, ClientIdentityAuditReasonCodes.SuccessCompatibleDigitalMatch),
                DeterministicNameCandidateKind.TokenOrderEquivalent => Success(rosterClientName, NameComparisonResult.TokenOrderEquivalent, ClientIdentityAuditReasonCodes.SuccessTokenOrderEquivalent),
                DeterministicNameCandidateKind.Partial => Success(rosterClientName, NameComparisonResult.UniquePartial, ClientIdentityAuditReasonCodes.SuccessUniquePartialIdentityMatch),
                _ => throw new InvalidOperationException($"Unsupported automatic candidate: {candidate}.")
            };
        }

        if (candidate == DeterministicNameCandidateKind.ReversedColumns)
            return NeedsReview(ClientIdentityAuditReasonCodes.NameColumnsReversed, ClientIdentityVerificationType.DigitalConsent, NameComparisonResult.ReversedColumns, $"Client ID '{clientId}' exists in the roster, but First Name and Last Name appear to be reversed.", rosterClientName);

        return NeedsReview(ClientIdentityAuditReasonCodes.ClientIdNameMismatch, ClientIdentityVerificationType.DigitalConsent, NameComparisonResult.Mismatch, $"Client ID '{clientId}' exists in the roster, but the student name differs. Upload name: {lastName}, {firstName}. Roster name: {rosterClientName}.", rosterClientName);
    }

    private ClientIdentityAuditRowResult AuditManualConsent(string clientId, string normalizedClientId, IReadOnlyDictionary<string, List<MassImmunisationRosterRecord>> rosterByClientId)
    {
        if (!rosterByClientId.TryGetValue(normalizedClientId, out List<MassImmunisationRosterRecord>? rosterMatches))
            return NeedsReview(ClientIdentityAuditReasonCodes.ManualConsentClientIdNotInRoster, ClientIdentityVerificationType.ManualConsent, NameComparisonResult.NotChecked, $"Manual consent requires review. Client ID '{clientId}' was assigned manually but was not found in mass_immunisation.csv.");

        if (rosterMatches.Count != 1)
            return NeedsReview(ClientIdentityAuditReasonCodes.DuplicateClientIdInRoster, ClientIdentityVerificationType.ManualConsent, NameComparisonResult.NotChecked, $"Manual consent requires review. Client ID '{clientId}' appears more than once in mass_immunisation.csv.");

        string rosterName = rosterMatches[0].ClientName;
        return NeedsReview(ClientIdentityAuditReasonCodes.ManualConsentRequiresReview, ClientIdentityVerificationType.ManualConsent, NameComparisonResult.NotChecked, $"Manual consent requires review. First Name and Last Name are blank, and the PDF was assigned manually to Client ID '{clientId}'. The roster associates this Client ID with '{rosterName}'. Verify that the handwritten consent belongs to this student.", rosterName);
    }

    private bool HasCompetingRosterIdentity(string firstName, string lastName, string selectedClientId, IReadOnlyList<MassImmunisationRosterRecord> rosterRows) => rosterRows
        .Where(row => !string.IsNullOrWhiteSpace(row.ClientId))
        .GroupBy(row => NormalizeClientId(row.ClientId), StringComparer.OrdinalIgnoreCase)
        .Where(group => !string.Equals(group.Key, selectedClientId, StringComparison.OrdinalIgnoreCase))
        .Select(group => group.First())
        .Any(row => DeterministicNameComparer.IsAutomaticCandidate(_nameComparer.CompareCandidate(firstName, lastName, row.ClientName)));

    private static ClientIdentityAuditRowResult AcceptedException() => new()
    {
        Status = ClientIdentityAuditStatus.Excluded,
        ReasonCode = ClientIdentityAuditReasonCodes.UploadFailureAcceptedException,
        VerificationType = ClientIdentityVerificationType.AcceptedUploadException,
        RosterClientName = string.Empty,
        NameComparisonResult = NameComparisonResult.NotChecked,
        Error = string.Empty
    };

    private static ClientIdentityAuditRowResult Success(string rosterClientName, NameComparisonResult nameResult, string reasonCode) => new()
    {
        Status = ClientIdentityAuditStatus.Success,
        ReasonCode = reasonCode,
        VerificationType = ClientIdentityVerificationType.DigitalConsent,
        RosterClientName = rosterClientName,
        NameComparisonResult = nameResult,
        Error = string.Empty
    };

    private static ClientIdentityAuditRowResult NeedsReview(string reasonCode, ClientIdentityVerificationType type, NameComparisonResult nameResult, string error, string rosterClientName = "") => new()
    {
        Status = ClientIdentityAuditStatus.NeedsManualReview,
        ReasonCode = reasonCode,
        VerificationType = type,
        RosterClientName = rosterClientName,
        NameComparisonResult = nameResult,
        Error = error
    };

    private static void ApplyCounters(AuditCounters counters, ClientIdentityAuditRowResult result, string normalizedClientId, ISet<string> acceptedExceptionClientIds, ISet<string> manualClientIds)
    {
        counters.TotalRows++;
        switch (result.Status)
        {
            case ClientIdentityAuditStatus.NotProcessed: counters.UploadNotCompletedRows++; break;
            case ClientIdentityAuditStatus.Success: counters.AutomaticallyVerifiedRows++; break;
            case ClientIdentityAuditStatus.NeedsManualReview: counters.NeedsManualReviewRows++; break;
            case ClientIdentityAuditStatus.Excluded:
                counters.AcceptedUploadExceptionRows++;
                if (!string.IsNullOrEmpty(normalizedClientId)) acceptedExceptionClientIds.Add(normalizedClientId);
                return;
        }

        if (result.VerificationType is ClientIdentityVerificationType.ManualConsent or ClientIdentityVerificationType.DigitalConsent)
        {
            counters.SuccessfulUploadRows++;
            if (result.VerificationType == ClientIdentityVerificationType.ManualConsent)
            {
                counters.ManualConsentRows++;
                if (!string.IsNullOrEmpty(normalizedClientId)) manualClientIds.Add(normalizedClientId);
            }
            else counters.DigitalConsentRows++;
        }

        switch (result.ReasonCode)
        {
            case ClientIdentityAuditReasonCodes.SuccessExactDigitalMatch: counters.ExactMatchRows++; break;
            case ClientIdentityAuditReasonCodes.SuccessCompatibleDigitalMatch: counters.CompatibleMatchRows++; break;
            case ClientIdentityAuditReasonCodes.SuccessTokenOrderEquivalent: counters.TokenOrderEquivalentRows++; break;
            case ClientIdentityAuditReasonCodes.SuccessUniquePartialIdentityMatch: counters.UniquePartialMatchRows++; break;
            case ClientIdentityAuditReasonCodes.UploadRequiresReview: counters.UploadFailedRows++; break;
            case ClientIdentityAuditReasonCodes.InvalidUploadStatus: counters.InvalidStatusRows++; break;
            case ClientIdentityAuditReasonCodes.ClientIdMissing: counters.MissingClientIdRows++; break;
            case ClientIdentityAuditReasonCodes.ManualConsentClientIdNotInRoster:
            case ClientIdentityAuditReasonCodes.ClientIdNotInRoster: counters.ClientIdNotInRosterRows++; break;
            case ClientIdentityAuditReasonCodes.IncompleteStudentName: counters.IncompleteNameRows++; break;
            case ClientIdentityAuditReasonCodes.DuplicateClientIdInRoster: counters.DuplicateRosterClientIdRows++; break;
            case ClientIdentityAuditReasonCodes.NameColumnsReversed: counters.ReversedNameRows++; break;
            case ClientIdentityAuditReasonCodes.ClientIdNameMismatch: counters.NameMismatchRows++; break;
            case ClientIdentityAuditReasonCodes.AmbiguousRosterIdentity: counters.AmbiguousNameRows++; break;
            case ClientIdentityAuditReasonCodes.ClientIdUsedForMultipleNames: counters.MultipleUploadNameRows++; break;
        }
    }

    private HashSet<string> FindClientIdsWithMultipleDigitalNames(IReadOnlyList<string[]> rows, IReadOnlyDictionary<string, int> headers) => rows
        .Where(row => int.TryParse(GetField(row, headers["VerifStatus"]).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int status) && status == 1)
        .Where(row => !string.IsNullOrWhiteSpace(GetField(row, headers["ClientID"])))
        .Where(row => !string.IsNullOrWhiteSpace(GetField(row, headers["First Name"])) && !string.IsNullOrWhiteSpace(GetField(row, headers["Last Name"])))
        .GroupBy(row => NormalizeClientId(GetField(row, headers["ClientID"])), StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Select(row => _nameComparer.NormalizeFullName(GetField(row, headers["First Name"]), GetField(row, headers["Last Name"]))).Distinct(StringComparer.Ordinal).Count() > 1)
        .Select(group => group.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<MassImmunisationRosterRecord> ReadRoster(string path, Encoding encoding)
    {
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, CreateRosterCsvConfiguration());
        csv.Read();
        csv.ReadHeader();
        ValidateHeaders(csv.HeaderRecord ?? [], RequiredRosterHeaders, "mass_immunisation.csv");
        return csv.GetRecords<MassImmunisationRosterRecord>().ToList();
    }

    private static CsvTable ReadUploadTable(string path, Encoding encoding)
    {
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        using var parser = new CsvParser(reader, CreateUploadCsvConfiguration());
        if (!parser.Read()) throw new InvalidDataException("Upload_to_PHIS.csv is empty.");
        string[] headers = parser.Record ?? throw new InvalidDataException("Upload_to_PHIS.csv is empty.");
        Dictionary<string, int> headerIndexes = BuildCanonicalHeaderIndexes(headers, "Upload_to_PHIS.csv");
        ValidateRequiredHeaders(headerIndexes, RequiredUploadHeaders, "Upload_to_PHIS.csv");
        var rows = new List<string[]>();
        while (parser.Read()) rows.Add(parser.Record ?? Array.Empty<string>());
        return new CsvTable(headers, rows);
    }

    private static void WriteOutputAtomically(string outputPath, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, Encoding encoding)
    {
        string temporaryPath = outputPath + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporaryPath, append: false, encoding))
            using (var csv = new CsvWriter(writer, CreateUploadCsvConfiguration()))
            {
                foreach (string header in headers) csv.WriteField(header);
                csv.NextRecord();
                foreach (string[] row in rows)
                {
                    foreach (string value in row) csv.WriteField(value);
                    csv.NextRecord();
                }
            }
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    private static CsvConfiguration CreateUploadCsvConfiguration() => new(CultureInfo.InvariantCulture) { HasHeaderRecord = true, MissingFieldFound = null, HeaderValidated = null, TrimOptions = TrimOptions.None };
    private static CsvConfiguration CreateRosterCsvConfiguration() => new(CultureInfo.InvariantCulture) { HasHeaderRecord = true, MissingFieldFound = null, HeaderValidated = null, TrimOptions = TrimOptions.Trim };

    private static void ValidateHeaders(IReadOnlyCollection<string> headers, IReadOnlyCollection<string> requiredHeaders, string fileLabel)
    {
        var canonicalHeaders = headers.Select(header => header.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requiredHeaders.Where(required => !canonicalHeaders.Contains(required)).ToArray();
        if (missing.Length > 0) throw new InvalidDataException($"{fileLabel} is missing required column(s): {string.Join(", ", missing)}");
    }

    private static Dictionary<string, int> BuildCanonicalHeaderIndexes(IReadOnlyList<string> headers, string fileLabel)
    {
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < headers.Count; index++)
        {
            string canonicalHeader = headers[index].Trim();
            if (!indexes.TryAdd(canonicalHeader, index)) throw new InvalidDataException($"{fileLabel} contains duplicate or ambiguous header '{canonicalHeader}'.");
        }
        return indexes;
    }

    private static void ValidateRequiredHeaders(IReadOnlyDictionary<string, int> headerIndexes, IReadOnlyCollection<string> requiredHeaders, string fileLabel)
    {
        var missing = requiredHeaders.Where(required => !headerIndexes.ContainsKey(required)).ToArray();
        if (missing.Length > 0) throw new InvalidDataException($"{fileLabel} is missing required column(s): {string.Join(", ", missing)}");
    }

    private static bool IsAuditHeader(string header) => ClientIdentityAuditHeaders.Contains(header.Trim());
    private static void EnsureAvailable(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{label} was not found.", path);
        if (!WorkspaceInitializer.IsFileAvailable(path)) throw new IOException($"{label} is open or unavailable. Close it in Excel and try again.");
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static string TryGetUniqueRosterName(string clientId, IReadOnlyDictionary<string, List<MassImmunisationRosterRecord>> rosterByClientId) => rosterByClientId.TryGetValue(clientId, out List<MassImmunisationRosterRecord>? matches) && matches.Count == 1 ? matches[0].ClientName : string.Empty;
    private static string NormalizeClientId(string value) => (value ?? string.Empty).Trim();
    private static string GetField(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index] ?? string.Empty : string.Empty;

    private sealed record CsvTable(string[] Headers, List<string[]> Rows);
    private sealed record UploadAuditRow(string RawClientId, string RawLastName, string RawFirstName, string RawVerifStatus, string RawFailureReason, string RawRemarksByMelisa);
    private sealed record ClientIdentityAuditContext(IReadOnlyList<MassImmunisationRosterRecord> RosterRows, IReadOnlyDictionary<string, List<MassImmunisationRosterRecord>> RosterByClientId, ISet<string> MultipleDigitalNamesByClientId);
    private sealed class AuditOutput { public required List<string> Headers { get; init; } public required List<string[]> Rows { get; init; } public required ClientIdentityPreAuditResult Result { get; init; } }
    private sealed class AuditCounters { public int TotalRows; public int SuccessfulUploadRows; public int AutomaticallyVerifiedRows; public int NeedsManualReviewRows; public int AcceptedUploadExceptionRows; public int ExactMatchRows; public int CompatibleMatchRows; public int TokenOrderEquivalentRows; public int UniquePartialMatchRows; public int UploadNotCompletedRows; public int UploadFailedRows; public int InvalidStatusRows; public int DigitalConsentRows; public int ManualConsentRows; public int MissingClientIdRows; public int ClientIdNotInRosterRows; public int IncompleteNameRows; public int DuplicateRosterClientIdRows; public int ReversedNameRows; public int NameMismatchRows; public int AmbiguousNameRows; public int MultipleUploadNameRows; }
}

internal sealed class ClientIdentityAuditRowResult
{
    public required ClientIdentityAuditStatus Status { get; init; }
    public required string ReasonCode { get; init; }
    public required ClientIdentityVerificationType VerificationType { get; init; }
    public required string RosterClientName { get; init; }
    public required NameComparisonResult NameComparisonResult { get; init; }
    public required string Error { get; init; }
}

internal static class ClientIdentityPreAuditFileNames
{
    public const string OutputFileName = "Verification_Upload.csv";
}
