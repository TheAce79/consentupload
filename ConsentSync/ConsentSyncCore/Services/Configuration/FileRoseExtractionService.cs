
using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
using System.Text;

namespace ConsentSyncCore.Services
{
    /// <summary>
    /// Moves FileRose PDFs from <c>1 Scan File Rose</c> to
    /// <c>PhisWorkspace\1_To_Upload\2 File Rose Upload</c>,
    /// renaming each to <c>{ClientId}_{roseSuffix}_{schoolYear}.pdf</c>.
    ///
    /// Self-contained — does NOT require FileRoseVerificationService to run first.
    ///
    /// Contract per scan-folder file:
    ///   MATCH + MOVE OK  → IsFileRoseDefault=True, IsFileRoseExtracted=True  written to CSV.
    ///   MATCH + MOVE FAIL→ IsFileRoseDefault=True, IsFileRoseExtracted=False written to CSV.
    ///                      File LEFT in scan folder.
    ///   NO MATCH         → file LEFT in scan folder, CSV unchanged for that row.
    ///
    /// Re-run safety: output folder is scanned first; already-present files patch
    ///   IsFileRoseDefault=True + IsFileRoseExtracted=True without reprocessing.
    ///
    /// Validation_Results.csv is always fully overwritten (read-all → patch → write-all).
    /// </summary>
    public class FileRoseExtractionService
    {
        private const string ErrorSummaryFileName = "_extraction_errors.txt";

        private readonly string _scanPath;
        private readonly string _outputPath;   // PhisWorkspace/1_To_Upload/2 File Rose Upload
        private readonly string _errorPath;    // summary text file only — no PDFs moved here
        private readonly string _validationCsvPath;
        private readonly string _schoolYear;
        private readonly string _roseSuffix;

        // ── Constructors ──────────────────────────────────────────────────────

        public FileRoseExtractionService()
        {
            var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            var phisWs = ConfigurationService.GetPhisWorkspaceConfig();
            var prePhase3Config = ConfigurationService.GetPrePhase3Config();
            var schoolContext = ConfigurationService.GetSchoolContextConfig();

            _scanPath = bulkConfig.GetFileRoseScanPath();
            _outputPath = phisWs.GetFileRoseUploadPath();       // 2 File Rose Upload
            _errorPath = bulkConfig.GetFileRoseErrorPath();     // summary only
            _validationCsvPath = Path.Combine(
                prePhase3Config.ValidationCsvPath,
                prePhase3Config.ValidationCsvFileName);
            _schoolYear = schoolContext.SchoolYear;
            _roseSuffix = bulkConfig.RoseSuffix;
        }

        public FileRoseExtractionService(
            string scanPath, string outputPath, string errorPath,
            string validationCsvPath, string schoolYear,
            string roseSuffix = "suiviscolaire")
        {
            _scanPath = scanPath;
            _outputPath = outputPath;
            _errorPath = errorPath;
            _validationCsvPath = validationCsvPath;
            _schoolYear = schoolYear;
            _roseSuffix = roseSuffix;
        }

        // ── Main entry point ──────────────────────────────────────────────────

        public FileRoseExtractionResult ExtractFileRose()
        {
            var result = new FileRoseExtractionResult();

            if (!Directory.Exists(_scanPath))
            {
                LoggerService.LogWarning($"⚠️  Scan folder not found: {_scanPath}");
                return result;
            }

            if (!File.Exists(_validationCsvPath))
            {
                LoggerService.LogWarning($"⚠️  Validation_Results.csv not found: {_validationCsvPath}");
                LoggerService.LogInformation("   💡 Run Phase 2 first to generate the CSV.");
                return result;
            }

            Directory.CreateDirectory(_outputPath);
            Directory.CreateDirectory(_errorPath);

            LoggerService.LogInformation($"   📥 Scan    : {_scanPath}");
            LoggerService.LogInformation($"   📤 Output  : {_outputPath}");
            LoggerService.LogInformation($"   📋 CSV     : {_validationCsvPath}");
            LoggerService.LogInformation($"   📅 Year    : {_schoolYear}  |  Suffix : {_roseSuffix}");

            // ── Load entire CSV into memory ───────────────────────────────────
            var lines = File.ReadAllLines(_validationCsvPath, Encoding.UTF8);
            if (lines.Length < 2)
            {
                LoggerService.LogWarning("⚠️  Validation_Results.csv is empty or has no data rows.");
                return result;
            }

            // ── Locate columns ────────────────────────────────────────────────
            var headerCols = SplitCsvLine(lines[0]);
            int idxClientId = FindColumn(headerCols, "ClientId");
            int idxClientIdStatus = FindColumn(headerCols, "ClientIdStatus");
            int idxLastName = FindColumn(headerCols, "Last Name");    // ← was "LastName"
            int idxFirstName = FindColumn(headerCols, "First Name");  // ← was "FirstName"
            int idxIsFileRose = FindColumn(headerCols, "IsFileRoseDefault");
            int idxExtracted = FindColumn(headerCols, "IsFileRoseExtracted");

            if (idxClientId < 0 || idxClientIdStatus < 0)
            {
                LoggerService.LogWarning("⚠️  CSV missing required columns (ClientId, ClientIdStatus).");
                return result;
            }

            if (idxIsFileRose < 0)
                LoggerService.LogWarning(
                    "   ⚠️  IsFileRoseDefault column not found in CSV — " +
                    "it will be set automatically for matched files.");
            if (idxExtracted < 0)
                LoggerService.LogWarning(
                    "   ⚠️  IsFileRoseExtracted column not found in CSV — " +
                    "it will be set automatically for matched files.");

            // ── STEP A: Re-scan output folder (re-run support) ────────────────
            // Any {ClientId}_{suffix}_{year}.pdf already in the output folder from a
            // previous run → patch IsFileRoseDefault=True + IsFileRoseExtracted=True now
            // so Step B excludes them from the eligible map.
            int patchedLines = RescanOutputFolderAndPatchCsv(
                lines, idxClientId, idxIsFileRose, idxExtracted);

            // ── STEP B: Build eligible-row lookup ─────────────────────────────
            // Eligible = ClientIdStatus=Found AND IsFileRoseExtracted≠True.
            // NOTE: IsFileRoseDefault is intentionally NOT required here —
            //       we set it ourselves in Step C when a matching file is found.
            //       This is the fix: no dependency on FileRoseVerificationService.
            var eligibleRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var cols = SplitCsvLine(lines[i]);
                if (cols.Count <= Math.Max(idxClientId, idxClientIdStatus)) continue;

                var clientId = cols[idxClientId].Trim();
                var statusRaw = cols[idxClientIdStatus].Trim();

                // Must have ClientIdStatus = Found
                bool statusIsFound = statusRaw == "1" ||
                                     statusRaw.Equals("Found", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(clientId) || !statusIsFound) continue;

                // Skip already-extracted rows
                bool alreadyExtracted = idxExtracted >= 0 && idxExtracted < cols.Count &&
                                        cols[idxExtracted].Trim()
                                            .Equals("True", StringComparison.OrdinalIgnoreCase);
                if (!alreadyExtracted)
                    eligibleRows[clientId] = i;
            }

            var scanFiles = Directory.GetFiles(_scanPath, "*.pdf", SearchOption.TopDirectoryOnly);

            LoggerService.LogInformation(
                $"\n   📊 Eligible records in CSV (ClientIdStatus=Found, not yet extracted) : {eligibleRows.Count}");
            LoggerService.LogInformation(
                $"   📁 Files in scan folder : {scanFiles.Length}\n");

            // ── STEP C: Process each file in the scan folder ──────────────────
            foreach (var filePath in scanFiles)
            {
                var fileName = Path.GetFileName(filePath);
                var stem = Path.GetFileNameWithoutExtension(filePath).Trim();
                var extension = Path.GetExtension(filePath);

                LoggerService.LogInformation($"   Processing: {fileName}");

                // ── Validation 1: filename must be a valid numeric ClientId ────
                if (!IsValidClientId(stem))
                {
                    var reason = $"'{stem}' is not a valid numeric ClientId — rename to <ClientId>.pdf";
                    LoggerService.LogWarning($"      ❌ {reason} — file LEFT in scan folder.");
                    RecordError(fileName, reason, FileRoseErrorCategory.InvalidFileName, result);
                    continue;
                }

                var clientId = stem;

                // ── Validation 2: ClientId must be in the eligible map ────────
                if (!eligibleRows.TryGetValue(clientId, out int rowIndex))
                {
                    bool alreadyDone = IsAlreadyExtractedInLines(
                        lines, clientId, idxClientId, idxExtracted);

                    if (alreadyDone)
                    {
                        LoggerService.LogInformation(
                            $"      ⏭️  IsFileRoseExtracted already True in CSV — skipping {fileName}");
                        result.AlreadyExtracted++;
                    }
                    else
                    {
                        bool statusFound = GetCsvStatusIsFound(
                            lines, clientId, idxClientId, idxClientIdStatus);
                        var reason = statusFound
                            ? $"ClientId '{clientId}' found in CSV but already fully processed"
                            : $"ClientId '{clientId}' not found in CSV or ClientIdStatus≠Found — " +
                              "run Phase 1 first to resolve this ClientId";
                        LoggerService.LogWarning($"      ❌ {reason} — file LEFT in scan folder.");
                        RecordError(fileName, reason, FileRoseErrorCategory.ClientIdNotMatched, result);
                    }
                    continue;
                }

                var newFileName = $"{clientId}_{_roseSuffix}_{_schoolYear}{extension}";
                var destinationPath = Path.Combine(_outputPath, newFileName);

                // ── Already in destination (concurrent re-run safety) ─────────
                if (File.Exists(destinationPath))
                {
                    LoggerService.LogInformation($"      ⏭️  Already in output folder → {newFileName}");
                    result.AlreadyExtracted++;
                    // Ensure both flags are True
                    patchedLines += PatchCsvRow(lines, rowIndex, idxIsFileRose, "True");
                    patchedLines += PatchCsvRow(lines, rowIndex, idxExtracted, "True");
                    continue;
                }

                // ── MOVE + set both flags ─────────────────────────────────────
                try
                {
                    File.Move(filePath, destinationPath, overwrite: false);
                    LoggerService.LogInformation($"      ✅ Moved → {newFileName}");

                    result.Extracted++;
                    result.ExtractedFiles.Add((clientId, newFileName));

                    // SUCCESS: IsFileRoseDefault=True + IsFileRoseExtracted=True
                    patchedLines += PatchCsvRow(lines, rowIndex, idxIsFileRose, "True");
                    patchedLines += PatchCsvRow(lines, rowIndex, idxExtracted, "True");

                    LoggerService.LogInformation(
                        $"         IsFileRoseDefault=True, IsFileRoseExtracted=True written to CSV.");
                }
                catch (Exception ex)
                {
                    var reason = $"File move failed: {ex.Message}";
                    LoggerService.LogWarning($"      ❌ {reason} — file LEFT in scan folder.");
                    RecordError(fileName, reason, FileRoseErrorCategory.CopyFailed, result);

                    // FAILURE: IsFileRoseDefault=True but IsFileRoseExtracted=False
                    patchedLines += PatchCsvRow(lines, rowIndex, idxIsFileRose, "True");
                    patchedLines += PatchCsvRow(lines, rowIndex, idxExtracted, "False");

                    LoggerService.LogInformation(
                        $"         IsFileRoseDefault=True, IsFileRoseExtracted=False written to CSV.");
                }
            }

            // ── STEP D: Write entire CSV back (full overwrite, atomic) ────────
            if (patchedLines > 0)
            {
                string tempPath = _validationCsvPath + ".tmp";
                File.WriteAllLines(tempPath, lines, Encoding.UTF8);
                File.Move(tempPath, _validationCsvPath, overwrite: true);
                LoggerService.LogInformation(
                    $"\n   ✅ Validation_Results.csv rewritten — {patchedLines} field(s) updated.");
            }
            else
            {
                LoggerService.LogInformation(
                    "\n   ℹ️  No CSV changes — Validation_Results.csv unchanged.");
            }

            // ── STEP E: Collect pending rows for UI warning ───────────────────
            // Rows with IsFileRoseDefault=True but IsFileRoseExtracted≠True after this run.
            CollectPendingFileRoseRows(
                lines, idxClientId, idxIsFileRose, idxExtracted,
                idxLastName, idxFirstName, result);

            if (result.PendingFileRoseRows.Count > 0)
            {
                LoggerService.LogWarning(
                    $"\n   ⚠️  {result.PendingFileRoseRows.Count} record(s) have " +
                    "IsFileRoseDefault=True but IsFileRoseExtracted=False after this run:");
                foreach (var (cid, ln, fn) in result.PendingFileRoseRows)
                    LoggerService.LogWarning(
                        $"      • ClientId {cid}  — {ln}, {fn}" +
                        $"  → place {cid}.pdf in the scan folder and re-run");
            }

            // ── STEP F: Write (or clear) error summary text file ─────────────
            WriteErrorSummary(result);

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // STEP A — Re-scan output folder, patch CSV in-memory
        // ─────────────────────────────────────────────────────────────────────

        private int RescanOutputFolderAndPatchCsv(
            string[] lines,
            int idxClientId,
            int idxIsFileRose,
            int idxExtracted)
        {
            if (!Directory.Exists(_outputPath)) return 0;

            int patched = 0;
            var suffix = $"_{_roseSuffix}_{_schoolYear}";

            foreach (var filePath in Directory.GetFiles(_outputPath, "*.pdf"))
            {
                var stem = Path.GetFileNameWithoutExtension(filePath);
                if (!stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

                var clientId = stem[..^suffix.Length].Trim();
                if (!IsValidClientId(clientId)) continue;

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var cols = SplitCsvLine(lines[i]);
                    if (cols.Count <= idxClientId) continue;
                    if (!cols[idxClientId].Trim().Equals(clientId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Set BOTH flags — file is confirmed physically present
                    int c1 = PatchCsvRow(lines, i, idxIsFileRose, "True");
                    int c2 = PatchCsvRow(lines, i, idxExtracted, "True");
                    int changed = c1 + c2;

                    if (changed > 0)
                    {
                        patched += changed;
                        LoggerService.LogInformation(
                            $"   🔄 Re-scan: {Path.GetFileName(filePath)} already in output folder " +
                            $"→ IsFileRoseDefault=True, IsFileRoseExtracted=True (ClientId {clientId})");
                    }
                    break;
                }
            }

            return patched;
        }

        // ─────────────────────────────────────────────────────────────────────
        // STEP E — Collect pending rows for UI warning
        // ─────────────────────────────────────────────────────────────────────

        private static void CollectPendingFileRoseRows(
            string[] lines,
            int idxClientId, int idxIsFileRose, int idxExtracted,
            int idxLastName, int idxFirstName,
            FileRoseExtractionResult result)
        {
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = SplitCsvLine(lines[i]);

                if (idxIsFileRose < 0 || idxIsFileRose >= cols.Count) continue;
                if (!cols[idxIsFileRose].Trim().Equals("True", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool extracted = idxExtracted >= 0 && idxExtracted < cols.Count &&
                                 cols[idxExtracted].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                if (extracted) continue;

                var clientId = idxClientId >= 0 && idxClientId < cols.Count ? cols[idxClientId].Trim() : "?";
                var lastName = idxLastName >= 0 && idxLastName < cols.Count ? cols[idxLastName].Trim() : "";
                var firstName = idxFirstName >= 0 && idxFirstName < cols.Count ? cols[idxFirstName].Trim() : "";

                result.PendingFileRoseRows.Add((clientId, lastName, firstName));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Error summary — text file only, no PDFs moved
        // ─────────────────────────────────────────────────────────────────────

        private void WriteErrorSummary(FileRoseExtractionResult result)
        {
            var summaryPath = Path.Combine(_errorPath, ErrorSummaryFileName);

            if (result.ErrorFiles.Count == 0)
            {
                if (File.Exists(summaryPath)) File.Delete(summaryPath);
                LoggerService.LogInformation("   ✅ No errors — error summary cleared.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║       FileRose Extraction — Error Summary                    ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine($"  Generated   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  School year : {_schoolYear}");
            sb.AppendLine($"  Scan folder : {_scanPath}");
            sb.AppendLine($"  Output      : {_outputPath}");
            sb.AppendLine();
            sb.AppendLine("  ⚠️  Files with errors are LEFT in the scan folder — fix and re-run.");
            sb.AppendLine();
            sb.AppendLine($"    ✅  Moved successfully     : {result.Extracted}");
            sb.AppendLine($"    ⏭️   Already in output      : {result.AlreadyExtracted}");
            sb.AppendLine($"    ❌  Errors (still in scan) : {result.Errors}");
            sb.AppendLine();

            void WriteGroup(
                string title,
                IEnumerable<(string FileName, string Reason, FileRoseErrorCategory _)> items,
                string action)
            {
                var list = items.ToList();
                if (list.Count == 0) return;
                sb.AppendLine(new string('─', 64));
                sb.AppendLine($"  ❌ {title}  ({list.Count} file(s))");
                sb.AppendLine(new string('─', 64));
                int n = 1;
                foreach (var (fn, reason, _) in list)
                {
                    sb.AppendLine($"  {n++,2}. {fn}");
                    sb.AppendLine($"      → {reason}");
                }
                sb.AppendLine();
                sb.AppendLine($"  ✏️  Action: {action}");
                sb.AppendLine();
            }

            WriteGroup(
                "Invalid filename — must be the numeric ClientId only, e.g. 12345.pdf",
                result.InvalidFileNameErrors,
                $"Rename to <ClientId>.pdf and place in:\n            {_scanPath}");

            WriteGroup(
                "ClientId not matched — not found in CSV or ClientIdStatus≠Found",
                result.ClientIdNotMatchedErrors,
                "Verify the ClientId in PHIS (run Phase 1 if needed).\n" +
                $"            Then rename to <ClientId>.pdf and place in:\n            {_scanPath}");

            WriteGroup(
                "File move failed — system error (permissions, disk full, locked)",
                result.CopyFailedErrors,
                "Resolve the system issue, then click the button again.");

            sb.AppendLine(new string('═', 64));
            sb.AppendLine("  After fixing ALL errors, click '🌹 Append FileRose Rows' again.");
            sb.AppendLine(new string('═', 64));

            File.WriteAllText(summaryPath, sb.ToString(), Encoding.UTF8);
            LoggerService.LogWarning(
                $"\n   ⚠️  {result.Errors} error(s) — summary: {summaryPath}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Error record — never moves files
        // ─────────────────────────────────────────────────────────────────────

        private static void RecordError(
            string fileName, string reason,
            FileRoseErrorCategory category, FileRoseExtractionResult result)
        {
            result.Errors++;
            result.ErrorFiles.Add((fileName, reason, category));
        }

        // ─────────────────────────────────────────────────────────────────────
        // CSV helpers
        // ─────────────────────────────────────────────────────────────────────

        private static bool IsAlreadyExtractedInLines(
            string[] lines, string clientId, int idxClientId, int idxExtracted)
        {
            if (idxExtracted < 0) return false;
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = SplitCsvLine(lines[i]);
                if (cols.Count <= idxClientId) continue;
                if (!cols[idxClientId].Trim().Equals(clientId, StringComparison.OrdinalIgnoreCase))
                    continue;
                return idxExtracted < cols.Count &&
                       cols[idxExtracted].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static bool GetCsvStatusIsFound(
            string[] lines, string clientId, int idxClientId, int idxStatus)
        {
            if (idxStatus < 0) return false;
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = SplitCsvLine(lines[i]);
                if (cols.Count <= idxClientId) continue;
                if (!cols[idxClientId].Trim().Equals(clientId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (idxStatus >= cols.Count) return false;
                var v = cols[idxStatus].Trim();
                return v == "1" || v.Equals("Found", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static bool IsValidClientId(string s) =>
            !string.IsNullOrWhiteSpace(s) && s.All(char.IsDigit);

        private static int PatchCsvRow(string[] lines, int rowIndex, int colIndex, string newValue)
        {
            if (colIndex < 0) return 0;
            var cols = SplitCsvLine(lines[rowIndex]);
            if (colIndex >= cols.Count) return 0;
            if (cols[colIndex].Trim().Equals(newValue, StringComparison.OrdinalIgnoreCase)) return 0;
            cols[colIndex] = newValue;
            lines[rowIndex] = JoinCsvLine(cols);
            return 1;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    { current.Append('"'); i++; }
                    else { inQuotes = !inQuotes; }
                }
                else if (c == ',' && !inQuotes)
                { fields.Add(current.ToString()); current.Clear(); }
                else { current.Append(c); }
            }
            fields.Add(current.ToString());
            return fields;
        }

        private static string JoinCsvLine(List<string> fields)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var f = fields[i];
                if (f.Contains(',') || f.Contains('"') || f.Contains('\n'))
                    sb.Append('"').Append(f.Replace("\"", "\"\"")).Append('"');
                else
                    sb.Append(f);
            }
            return sb.ToString();
        }

        private static int FindColumn(List<string> headers, string name)
        {
            for (int i = 0; i < headers.Count; i++)
                if (headers[i].Trim().Trim('"').Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }
}