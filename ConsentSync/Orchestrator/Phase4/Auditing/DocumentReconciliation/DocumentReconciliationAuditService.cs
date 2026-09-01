using ConsentSyncCore.Services.Configuration;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Orchestrator.Phase4.Auditing.DocumentReconciliation;

public sealed class DocumentReconciliationAuditService
{
    private static readonly string[] RequiredHeaders = ["ClientID", "Document Title", "IsFeuilleRose", "PhisAntigen", "VerifClientIdStatus"];
    private readonly IPdfAuditInspector _pdfInspector;
    private readonly Func<AuditConfiguration> _configurationFactory;

    public DocumentReconciliationAuditService() : this(new PdfConsentInspector(), GetConfiguration) { }
    internal DocumentReconciliationAuditService(IPdfAuditInspector inspector, Func<AuditConfiguration>? configurationFactory = null) { _pdfInspector = inspector; _configurationFactory = configurationFactory ?? GetConfiguration; }

    public DocumentReconciliationAuditResult ExecuteAudit()
    {
        var prePhase3 = ConfigurationService.GetPrePhase3Config();
        var workspace = ConfigurationService.GetPhisWorkspaceConfig();
        return ExecuteAudit(new AuditPaths(Path.Combine(prePhase3.OutputPath, "Verification_Upload.csv"), workspace.GetConsentArchivePath(), workspace.GetFileRoseArchivePath(), Path.Combine(prePhase3.OutputPath, "Document_Reconciliation_Audit.txt")));
    }

    internal DocumentReconciliationAuditResult ExecuteAudit(AuditPaths paths)
    {
        CsvTable table = ReadCsv(paths.VerificationCsvPath);
        Dictionary<string, int> headers = BuildHeaders(table.Headers);
        ValidateHeaders(headers);
        var issues = new List<DocumentReconciliationIssue>();
        List<AuditRow> rows = ParseRows(table.Rows, headers, issues);
        if (rows.Any(row => row.Status is not 1 and not 3)) throw new InvalidDataException("Verification_Upload.csv contains a blocking verification status.");

        AuditConfiguration configuration = _configurationFactory();
        if (!configuration.IsValid)
        {
            issues.Add(Issue(DocumentReconciliationIssueCodes.UnsupportedGradeVaccineConfiguration, DocumentReconciliationIssueSeverity.Error, "", "", null, configuration.Error, true));
            var unsupported = CreateResult(new Counters(), issues, paths.OutputPath, configuration);
            WriteReportAtomically(unsupported);
            return unsupported;
        }

        List<AuditRow> status1 = rows.Where(row => row.Status == 1).ToList();
        List<AuditRow> consentRows = status1.Where(row => row.IsFileRose == false).ToList();
        List<AuditRow> fileRoseRows = status1.Where(row => row.IsFileRose == true).ToList();
        List<AuditRow> exceptions = rows.Where(row => row.Status == 3 && row.IsFileRose == false).ToList();
        var counters = new Counters { ConsentUploadRows = consentRows.Count, FileRoseUploadRows = fileRoseRows.Count, InvalidIsFeuilleRoseRows = issues.Count(issue => issue.Code == DocumentReconciliationIssueCodes.InvalidIsFeuilleRose) };

        List<ClientConsentGroup> clients = BuildConsentGroups(consentRows, exceptions, configuration, issues, counters);
        List<ResolvedArchiveRow> consentPaths = ResolveRows(consentRows, paths.ConsentArchivePath, issues, false);
        List<ResolvedArchiveRow> rosePaths = ResolveRows(fileRoseRows, paths.FileRoseArchivePath, issues, true);
        counters.ExpectedConsentArchiveFiles = consentPaths.Select(row => row.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        counters.ExpectedFileRoseArchiveFiles = rosePaths.Select(row => row.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        counters.InvalidDocumentTitleRows = issues.Count(issue => issue.Code == DocumentReconciliationIssueCodes.InvalidDocumentTitle);
        if (consentPaths.Count > 0 && !Directory.Exists(paths.ConsentArchivePath)) throw new DirectoryNotFoundException($"Consent archive was not found: {paths.ConsentArchivePath}");
        if (rosePaths.Count > 0 && !Directory.Exists(paths.FileRoseArchivePath)) throw new DirectoryNotFoundException($"FileRose archive was not found: {paths.FileRoseArchivePath}");

        List<InspectedFile> consentFiles = InspectDistinct(consentPaths, true, issues, counters);
        List<InspectedFile> roseFiles = InspectDistinct(rosePaths, false, issues, counters);
        ApplyFileCounters(consentFiles, roseFiles, counters);
        CountConsentClients(clients, consentFiles, issues, counters);
        CountFileRoseClients(fileRoseRows, roseFiles, issues, counters);
        var result = CreateResult(counters, issues, paths.OutputPath, configuration);
        WriteReportAtomically(result);
        return result;
    }

    private static AuditConfiguration GetConfiguration()
    {
        var school = ConfigurationService.GetSchoolContextConfig();
        var phase2 = ConfigurationService.GetPhase2Config();
        var prePhase3 = ConfigurationService.GetPrePhase3Config();
        string grade = school.Grade?.Trim() ?? string.Empty;
        string gradeKey = GetVaccineGradeKey(grade);
        if (!phase2.VaccineTypes.TryGetValue(gradeKey, out List<string>? vaccineTypes) || vaccineTypes is null || vaccineTypes.Count == 0) return AuditConfiguration.Invalid(grade, $"No configured vaccine list exists for '{gradeKey}'.");
        var antigens = new List<string>();
        foreach (string vaccineType in vaccineTypes)
        {
            if (!prePhase3.AntigenMapping.TryGetValue($"Consent{vaccineType}", out string? antigen) || string.IsNullOrWhiteSpace(antigen)) return AuditConfiguration.Invalid(grade, $"No PHIS antigen mapping exists for configured vaccine '{vaccineType}'.");
            antigens.Add(antigen.Trim());
        }
        return new AuditConfiguration(grade, vaccineTypes, antigens, "");
    }

    private static List<ClientConsentGroup> BuildConsentGroups(IEnumerable<AuditRow> consentRows, IEnumerable<AuditRow> exceptions, AuditConfiguration config, List<DocumentReconciliationIssue> issues, Counters counters)
    {
        var exceptionAntigens = exceptions.Where(row => !string.IsNullOrWhiteSpace(row.ClientId)).GroupBy(row => NormalizeClientId(row.ClientId), StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Select(row => row.PhisAntigen).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var groups = new List<ClientConsentGroup>();
        foreach (IGrouping<string, AuditRow> group in consentRows.GroupBy(row => NormalizeClientId(row.ClientId), StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key)) { foreach (AuditRow row in group) issues.Add(Issue(DocumentReconciliationIssueCodes.InvalidDocumentTitle, DocumentReconciliationIssueSeverity.Error, "", row.DocumentTitle, null, "ClientID is required for physical consent reconciliation.", true)); continue; }
            List<AuditRow> rows = group.ToList();
            counters.UniqueConsentClientIds++; counters.ExpectedPhysicalConsentClients++; counters.VaccineSpecificConsentRowsCollapsed += Math.Max(0, rows.Count - 1);
            var observed = rows.GroupBy(row => row.PhisAntigen, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (IGrouping<string, AuditRow> antigen in observed)
            {
                if (!config.PhisAntigens.Contains(antigen.Key, StringComparer.OrdinalIgnoreCase)) { counters.UnexpectedConsentAntigenRows += antigen.Count(); foreach (AuditRow row in antigen) issues.Add(Issue(DocumentReconciliationIssueCodes.UnexpectedConsentAntigen, DocumentReconciliationIssueSeverity.Warning, row.ClientId, row.DocumentTitle, null, $"PhisAntigen '{row.PhisAntigen}' is not configured for Grade {config.Grade}.", false)); }
                else if (antigen.Count() > 1) { counters.DuplicateConsentAntigenRows += antigen.Count() - 1; foreach (AuditRow row in antigen.Skip(1)) issues.Add(Issue(DocumentReconciliationIssueCodes.DuplicateConsentAntigenRow, DocumentReconciliationIssueSeverity.Warning, row.ClientId, row.DocumentTitle, null, $"Configured antigen '{row.PhisAntigen}' appears more than once for this ClientID.", false)); }
            }
            exceptionAntigens.TryGetValue(group.Key, out HashSet<string>? accepted);
            foreach (string antigen in config.PhisAntigens.Where(expected => !observed.Any(actual => string.Equals(actual.Key, expected, StringComparison.OrdinalIgnoreCase)) && (accepted is null || !accepted.Contains(expected)))) { counters.MissingConfiguredConsentAntigens++; issues.Add(Issue(DocumentReconciliationIssueCodes.MissingConfiguredConsentAntigen, DocumentReconciliationIssueSeverity.Warning, group.Key, "", null, $"Configured antigen '{antigen}' has no status-1 row or status-3 accepted exception.", false)); }
            groups.Add(new ClientConsentGroup(group.Key, rows));
        }
        return groups;
    }

    private static List<ResolvedArchiveRow> ResolveRows(IEnumerable<AuditRow> rows, string root, List<DocumentReconciliationIssue> issues, bool fileRose)
    {
        var result = new List<ResolvedArchiveRow>();
        foreach (AuditRow row in rows) try { result.Add(new ResolvedArchiveRow(row, ResolveArchivePath(root, row.DocumentTitle))); } catch (InvalidDataException ex) { issues.Add(Issue(DocumentReconciliationIssueCodes.InvalidDocumentTitle, DocumentReconciliationIssueSeverity.Error, row.ClientId, row.DocumentTitle, null, ex.Message, true)); }
        foreach (IGrouping<string, ResolvedArchiveRow> duplicate in result.GroupBy(row => row.Path, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)) foreach (ResolvedArchiveRow row in duplicate.Skip(1)) issues.Add(Issue(fileRose ? DocumentReconciliationIssueCodes.DuplicateFileRoseRow : DocumentReconciliationIssueCodes.DuplicateArchivePath, DocumentReconciliationIssueSeverity.Warning, row.Row.ClientId, row.Row.DocumentTitle, null, "Multiple CSV rows resolve to the same archive PDF.", false));
        return result;
    }

    internal static string ResolveArchivePath(string archiveRoot, string documentTitle)
    {
        string title = (documentTitle ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title) || title is "." or ".." || Path.IsPathRooted(title) || title.Contains(':') || title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || title.Contains(Path.DirectorySeparatorChar) || title.Contains(Path.AltDirectorySeparatorChar)) throw new InvalidDataException($"Invalid Document Title '{documentTitle}'.");
        string root = Path.GetFullPath(archiveRoot); string path = Path.GetFullPath(Path.Combine(root, title.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? title : title + ".pdf")); string relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException($"Document Title resolves outside the archive: '{documentTitle}'.");
        return path;
    }

    private List<InspectedFile> InspectDistinct(IEnumerable<ResolvedArchiveRow> rows, bool consent, List<DocumentReconciliationIssue> issues, Counters counters)
    {
        var results = new List<InspectedFile>();
        foreach (ResolvedArchiveRow resolved in rows.GroupBy(row => row.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
        {
            if (!File.Exists(resolved.Path)) { issues.Add(Issue(DocumentReconciliationIssueCodes.MissingArchiveFile, DocumentReconciliationIssueSeverity.Error, resolved.Row.ClientId, resolved.Row.DocumentTitle, null, "Expected archive PDF was not found.", true)); results.Add(new InspectedFile(resolved.Path, resolved.Row, false, false, null, null)); continue; }
            FileInfo before = new(resolved.Path);
            try
            {
                string hash; using (var stream = new FileStream(resolved.Path, FileMode.Open, FileAccess.Read, FileShare.Read)) hash = Convert.ToHexString(SHA256.HashData(stream));
                PdfInspection inspection = _pdfInspector.Inspect(resolved.Path, consent); FileInfo after = new(resolved.Path);
                if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc) { counters.ArchiveFilesChangedDuringAudit++; issues.Add(Issue(DocumentReconciliationIssueCodes.ArchiveFileChangedDuringAudit, DocumentReconciliationIssueSeverity.Error, resolved.Row.ClientId, resolved.Row.DocumentTitle, null, "Archive PDF changed during audit.", true)); results.Add(new InspectedFile(resolved.Path, resolved.Row, true, false, hash, inspection)); }
                else if (consent && inspection.PdfPigPageCount != inspection.DocnetPageCount) { counters.PdfPageCountMismatchFiles++; issues.Add(Issue(DocumentReconciliationIssueCodes.PdfPageCountMismatch, DocumentReconciliationIssueSeverity.Error, resolved.Row.ClientId, resolved.Row.DocumentTitle, null, "PdfPig and Docnet page counts differ.", true)); results.Add(new InspectedFile(resolved.Path, resolved.Row, true, false, hash, inspection)); }
                else results.Add(new InspectedFile(resolved.Path, resolved.Row, true, true, hash, inspection));
            }
            catch (Exception ex) { issues.Add(Issue(DocumentReconciliationIssueCodes.UnreadableArchiveFile, DocumentReconciliationIssueSeverity.Error, resolved.Row.ClientId, resolved.Row.DocumentTitle, null, $"Archive PDF could not be inspected: {ex.Message}", true)); results.Add(new InspectedFile(resolved.Path, resolved.Row, true, false, null, null)); }
        }
        return results;
    }

    private static void ApplyFileCounters(IReadOnlyList<InspectedFile> consent, IReadOnlyList<InspectedFile> rose, Counters c) { c.FoundConsentArchiveFiles = consent.Count(file => file.Exists); c.FoundFileRoseArchiveFiles = rose.Count(file => file.Exists); c.ReadableConsentArchiveFiles = consent.Count(file => file.Trusted); c.ReadableFileRoseArchiveFiles = rose.Count(file => file.Trusted); c.ConsentArchiveCopies = c.ReadableConsentArchiveFiles; }
    private static void CountConsentClients(IEnumerable<ClientConsentGroup> clients, IReadOnlyList<InspectedFile> files, List<DocumentReconciliationIssue> issues, Counters c)
    {
        foreach (ClientConsentGroup client in clients)
        {
            List<InspectedFile> copies = files.Where(file => file.Trusted && string.Equals(NormalizeClientId(file.Row.ClientId), client.ClientId, StringComparison.OrdinalIgnoreCase)).OrderBy(file => file.Row.DocumentTitle, StringComparer.OrdinalIgnoreCase).ToList();
            if (copies.Count == 0) continue;
            if (copies.Select(file => file.Hash).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) { c.ConsentVaccineCopyMismatchGroups++; issues.Add(Issue(DocumentReconciliationIssueCodes.ConsentVaccineCopyContentMismatch, DocumentReconciliationIssueSeverity.Error, client.ClientId, "", null, "Vaccine-specific archive copies have different SHA-256 values.", true)); continue; }
            c.TrustedConsentClientsCounted++; c.IdenticalConsentArchiveCopiesCollapsed += copies.Count - 1; CountConsentPages(copies[0], c, issues);
        }
    }
    private static void CountFileRoseClients(IEnumerable<AuditRow> rows, IReadOnlyList<InspectedFile> files, List<DocumentReconciliationIssue> issues, Counters c)
    {
        foreach (IGrouping<string, AuditRow> group in rows.GroupBy(row => NormalizeClientId(row.ClientId), StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key)) continue;
            c.UniqueFileRoseClientIds++;
            List<InspectedFile> paths = files.Where(file => file.Trusted && string.Equals(NormalizeClientId(file.Row.ClientId), group.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (paths.Count == 1) { c.FileRoseDocuments++; c.FileRosePages += paths[0].Inspection!.PdfPigPageCount; }
            else if (paths.Count > 1) { c.MultipleFileRoseDocumentClientGroups++; c.FileRosePages += paths.Sum(file => file.Inspection!.PdfPigPageCount); issues.Add(Issue(DocumentReconciliationIssueCodes.MultipleFileRoseDocumentsForClient, DocumentReconciliationIssueSeverity.Error, group.Key, "", null, "Multiple distinct FileRose archive PDFs exist for this ClientID.", true)); }
        }
        c.DuplicateFileRoseRowsCollapsed = issues.Count(issue => issue.Code == DocumentReconciliationIssueCodes.DuplicateFileRoseRow);
    }
    private static void CountConsentPages(InspectedFile file, Counters c, List<DocumentReconciliationIssue> issues)
    {
        int digital = 0, manual = 0;
        foreach ((PdfPageEvidence evidence, int number) in file.Inspection!.Pages.Select((page, index) => (page, index + 1)))
        {
            c.ConsentPages++;
            switch (Classify(evidence)) { case ConsentPageOrigin.DigitalConsent: c.DigitalConsentPages++; digital++; break; case ConsentPageOrigin.ManualConsent: c.ManualConsentPages++; manual++; break; case ConsentPageOrigin.Blank: c.BlankConsentPages++; issues.Add(Issue(DocumentReconciliationIssueCodes.BlankConsentPage, DocumentReconciliationIssueSeverity.Warning, file.Row.ClientId, file.Row.DocumentTitle, number, "Consent packet contains a blank page.", false)); break; default: c.UnknownConsentPages++; if (!evidence.HasReliableRasterGeometry) { c.RasterGeometryUnavailablePages++; issues.Add(Issue(DocumentReconciliationIssueCodes.PdfRasterGeometryUnavailable, DocumentReconciliationIssueSeverity.Warning, file.Row.ClientId, file.Row.DocumentTitle, number, "Reliable embedded-image geometry is unavailable for this page.", true)); } issues.Add(Issue(DocumentReconciliationIssueCodes.UnknownConsentPage, DocumentReconciliationIssueSeverity.Warning, file.Row.ClientId, file.Row.DocumentTitle, number, "Consent page origin could not be determined.", true)); break; }
        }
        c.ConfirmedConsentSubmissions += digital + manual; c.AdditionalDuplicateConsentSubmissions += Math.Max(0, digital + manual - 1); if (digital > 0 && manual > 0) c.MixedDigitalManualClients++;
    }

    internal static ConsentPageOrigin Classify(PdfPageEvidence evidence) => evidence.IsVisuallyBlank ? ConsentPageOrigin.Blank : evidence.HasReliableRasterGeometry && (evidence.LargestRasterCoverageRatio >= .80 || evidence.RasterUnionCoverageRatio >= .80) ? ConsentPageOrigin.ManualConsent : evidence.NativeTextCharacterCount >= 100 || evidence.NativeWordCount >= 20 ? ConsentPageOrigin.DigitalConsent : ConsentPageOrigin.Unknown;
    internal static string GetVaccineGradeKey(string grade) => $"Grade{(grade ?? "").Trim()}";
    private static CsvTable ReadCsv(string path) { if (!File.Exists(path)) throw new FileNotFoundException("Verification_Upload.csv was not found.", path); using var reader = new StreamReader(path, EncodingConfigurationService.GetPriorityEncoding()); using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, MissingFieldFound = null, HeaderValidated = null }); if (!csv.Read() || !csv.ReadHeader()) throw new InvalidDataException("Verification_Upload.csv has no header row."); var rows = new List<string[]>(); while (csv.Read()) rows.Add(csv.Parser.Record ?? Array.Empty<string>()); return new CsvTable(csv.HeaderRecord ?? Array.Empty<string>(), rows); }
    private static Dictionary<string, int> BuildHeaders(IReadOnlyList<string> headers) { var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); for (int index = 0; index < headers.Count; index++) if (!result.TryAdd(headers[index].Trim(), index)) throw new InvalidDataException($"Verification_Upload.csv contains duplicate header '{headers[index]}'."); return result; }
    private static void ValidateHeaders(IReadOnlyDictionary<string, int> headers) { string[] missing = RequiredHeaders.Where(header => !headers.ContainsKey(header)).ToArray(); if (missing.Length > 0) throw new InvalidDataException($"Verification_Upload.csv is missing required column(s): {string.Join(", ", missing)}."); }
    private static List<AuditRow> ParseRows(IEnumerable<string[]> rawRows, IReadOnlyDictionary<string, int> headers, List<DocumentReconciliationIssue> issues) { var result = new List<AuditRow>(); foreach (string[] raw in rawRows) { string status = Field(raw, headers["VerifClientIdStatus"]); if (!int.TryParse(status.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) parsed = -1; bool? rose = TryParseBoolean(Field(raw, headers["IsFeuilleRose"])); string client = Field(raw, headers["ClientID"]), title = Field(raw, headers["Document Title"]); if (parsed is 1 or 3 && rose is null) issues.Add(Issue(DocumentReconciliationIssueCodes.InvalidIsFeuilleRose, DocumentReconciliationIssueSeverity.Error, client, title, null, "IsFeuilleRose is invalid.", true)); result.Add(new AuditRow(client, title, Field(raw, headers["PhisAntigen"]).Trim(), status, parsed, rose)); } return result; }
    private static bool? TryParseBoolean(string value) => bool.TryParse(value.Trim(), out bool result) ? result : null;
    private static string NormalizeClientId(string value) => (value ?? "").Trim();
    private static string Field(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index] ?? "" : "";
    private static DocumentReconciliationIssue Issue(string code, DocumentReconciliationIssueSeverity severity, string clientId, string title, int? page, string message, bool incomplete) => new() { Code = code, Severity = severity, ClientId = clientId, DocumentTitle = title, PageNumber = page, Message = message, AffectsCompleteness = incomplete };
    private static DocumentReconciliationAuditResult CreateResult(Counters c, List<DocumentReconciliationIssue> issues, string output, AuditConfiguration config) => new() { SelectedGrade = config.Grade, ConfiguredConsentVaccineCount = config.VaccineTypes.Count, ConfiguredPhisAntigens = config.PhisAntigens, ConsentUploadRows = c.ConsentUploadRows, FileRoseUploadRows = c.FileRoseUploadRows, ExpectedConsentArchiveFiles = c.ExpectedConsentArchiveFiles, ExpectedFileRoseArchiveFiles = c.ExpectedFileRoseArchiveFiles, FoundConsentArchiveFiles = c.FoundConsentArchiveFiles, FoundFileRoseArchiveFiles = c.FoundFileRoseArchiveFiles, ReadableConsentArchiveFiles = c.ReadableConsentArchiveFiles, ReadableFileRoseArchiveFiles = c.ReadableFileRoseArchiveFiles, ConsentArchiveCopies = c.ConsentArchiveCopies, FileRoseDocuments = c.FileRoseDocuments, UniqueFileRoseClientIds = c.UniqueFileRoseClientIds, UniqueConsentClientIds = c.UniqueConsentClientIds, ExpectedPhysicalConsentClients = c.ExpectedPhysicalConsentClients, TrustedConsentClientsCounted = c.TrustedConsentClientsCounted, VaccineSpecificConsentRowsCollapsed = c.VaccineSpecificConsentRowsCollapsed, IdenticalConsentArchiveCopiesCollapsed = c.IdenticalConsentArchiveCopiesCollapsed, ConsentVaccineCopyMismatchGroups = c.ConsentVaccineCopyMismatchGroups, DuplicateConsentAntigenRows = c.DuplicateConsentAntigenRows, MissingConfiguredConsentAntigens = c.MissingConfiguredConsentAntigens, UnexpectedConsentAntigenRows = c.UnexpectedConsentAntigenRows, DuplicateFileRoseRowsCollapsed = c.DuplicateFileRoseRowsCollapsed, MultipleFileRoseDocumentClientGroups = c.MultipleFileRoseDocumentClientGroups, ConfirmedConsentSubmissions = c.ConfirmedConsentSubmissions, AdditionalDuplicateConsentSubmissions = c.AdditionalDuplicateConsentSubmissions, MixedDigitalManualClients = c.MixedDigitalManualClients, ConsentPages = c.ConsentPages, DigitalConsentPages = c.DigitalConsentPages, ManualConsentPages = c.ManualConsentPages, BlankConsentPages = c.BlankConsentPages, UnknownConsentPages = c.UnknownConsentPages, FileRosePages = c.FileRosePages, InvalidDocumentTitleRows = c.InvalidDocumentTitleRows, InvalidIsFeuilleRoseRows = c.InvalidIsFeuilleRoseRows, PdfPageCountMismatchFiles = c.PdfPageCountMismatchFiles, RasterGeometryUnavailablePages = c.RasterGeometryUnavailablePages, ArchiveFilesChangedDuringAudit = c.ArchiveFilesChangedDuringAudit, OutputPath = output, Issues = issues };
    private static void WriteReportAtomically(DocumentReconciliationAuditResult result) { string temp = result.OutputPath + ".tmp"; try { using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false))) { writer.WriteLine("DOCUMENT RECONCILIATION AUDIT"); writer.WriteLine("CONFIGURED VACCINE EXPANSION"); writer.WriteLine($"Selected grade                         : {result.SelectedGrade}"); writer.WriteLine($"Configured vaccines per consent        : {result.ConfiguredConsentVaccineCount}"); writer.WriteLine($"Configured PHIS antigens               : {string.Join(", ", result.ConfiguredPhisAntigens)}"); writer.WriteLine(); writer.WriteLine("CONSENT CLIENT CONSOLIDATION"); foreach ((string label, int value) in ReportCounts(result)) writer.WriteLine($"{label,-42}: {value,8}"); writer.WriteLine(); writer.WriteLine($"Counts complete: {result.CountsAreComplete}"); writer.WriteLine($"Review required: {result.HasReviewIssues}"); writer.WriteLine(); writer.WriteLine("ISSUES"); foreach (DocumentReconciliationIssue issue in result.Issues) writer.WriteLine($"{issue.Severity,-11} {issue.Code,-42} {issue.Message}"); } File.Move(temp, result.OutputPath, true); } catch { try { if (File.Exists(temp)) File.Delete(temp); } catch { } throw; } }
    private static IEnumerable<(string, int)> ReportCounts(DocumentReconciliationAuditResult r) { yield return ("PHIS consent upload rows", r.ConsentUploadRows); yield return ("Unique consent Client IDs", r.UniqueConsentClientIds); yield return ("Expected physical consent clients", r.ExpectedPhysicalConsentClients); yield return ("Trusted consent clients counted", r.TrustedConsentClientsCounted); yield return ("Technical vaccine rows collapsed", r.VaccineSpecificConsentRowsCollapsed); yield return ("Identical archive copies collapsed", r.IdenticalConsentArchiveCopiesCollapsed); yield return ("Vaccine-copy mismatch groups", r.ConsentVaccineCopyMismatchGroups); yield return ("Confirmed consent submissions", r.ConfirmedConsentSubmissions); yield return ("Additional genuine submissions", r.AdditionalDuplicateConsentSubmissions); yield return ("FileRose documents", r.FileRoseDocuments); yield return ("FileRose pages", r.FileRosePages); }

    internal sealed record AuditPaths(string VerificationCsvPath, string ConsentArchivePath, string FileRoseArchivePath, string OutputPath);
    internal sealed record AuditConfiguration(string Grade, IReadOnlyList<string> VaccineTypes, IReadOnlyList<string> PhisAntigens, string Error) { public bool IsValid => string.IsNullOrEmpty(Error); public static AuditConfiguration Invalid(string grade, string error) => new(grade, Array.Empty<string>(), Array.Empty<string>(), error); }
    private sealed record CsvTable(string[] Headers, List<string[]> Rows);
    private sealed record AuditRow(string ClientId, string DocumentTitle, string PhisAntigen, string RawStatus, int Status, bool? IsFileRose);
    private sealed record ResolvedArchiveRow(AuditRow Row, string Path);
    private sealed record ClientConsentGroup(string ClientId, IReadOnlyList<AuditRow> Rows);
    private sealed record InspectedFile(string Path, AuditRow Row, bool Exists, bool Trusted, string? Hash, PdfInspection? Inspection);
    private sealed class Counters { public int ConsentUploadRows; public int FileRoseUploadRows; public int ExpectedConsentArchiveFiles; public int ExpectedFileRoseArchiveFiles; public int FoundConsentArchiveFiles; public int FoundFileRoseArchiveFiles; public int ReadableConsentArchiveFiles; public int ReadableFileRoseArchiveFiles; public int ConsentArchiveCopies; public int FileRoseDocuments; public int UniqueFileRoseClientIds; public int UniqueConsentClientIds; public int ExpectedPhysicalConsentClients; public int TrustedConsentClientsCounted; public int VaccineSpecificConsentRowsCollapsed; public int IdenticalConsentArchiveCopiesCollapsed; public int ConsentVaccineCopyMismatchGroups; public int DuplicateConsentAntigenRows; public int MissingConfiguredConsentAntigens; public int UnexpectedConsentAntigenRows; public int DuplicateFileRoseRowsCollapsed; public int MultipleFileRoseDocumentClientGroups; public int ConfirmedConsentSubmissions; public int AdditionalDuplicateConsentSubmissions; public int MixedDigitalManualClients; public int ConsentPages; public int DigitalConsentPages; public int ManualConsentPages; public int BlankConsentPages; public int UnknownConsentPages; public int FileRosePages; public int InvalidDocumentTitleRows; public int InvalidIsFeuilleRoseRows; public int PdfPageCountMismatchFiles; public int RasterGeometryUnavailablePages; public int ArchiveFilesChangedDuringAudit; }
}
