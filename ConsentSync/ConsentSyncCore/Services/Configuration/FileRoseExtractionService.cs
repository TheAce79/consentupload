
using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
using System.Text;

namespace ConsentSyncCore.Services
{
    /// <summary>
    /// Extracts FileRose PDFs from <c>1 Scan File Rose</c> to <c>2_Output_Ready_FileRose</c>.
    /// <para>
    /// For each file whose base name is a valid ClientId present in <c>Validation_Results.csv</c>
    /// (ClientIdStatus == Found), the file is copied and renamed to
    /// <c>{ClientId}_{roseSuffix}_{schoolYear}.pdf</c> and
    /// <c>IsFileRoseExtracted</c> is set to <c>True</c> in the CSV.
    /// </para>
    /// <para>
    /// Files that cannot be matched are moved to <c>3_Error_FileRose_Extraction</c> and a
    /// human-readable <c>_extraction_errors.txt</c> summary is written there.
    /// Its presence on disk reliably signals that manual intervention is required before upload.
    /// </para>
    /// </summary>
    public class FileRoseExtractionService
    {
        private const string ErrorSummaryFileName = "_extraction_errors.txt";

        private readonly string _scanPath;
        private readonly string _outputPath;
        private readonly string _errorPath;
        private readonly string _validationCsvPath;
        private readonly string _schoolYear;
        private readonly string _roseSuffix;

        // ── Constructors ──────────────────────────────────────────────────────

        public FileRoseExtractionService()
        {
            var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            var prePhase3Config = ConfigurationService.GetPrePhase3Config();
            var schoolContext = ConfigurationService.GetSchoolContextConfig();

            _scanPath = bulkConfig.GetFileRoseScanPath();
            _outputPath = bulkConfig.GetFileRoseOutputReadyPath();
            _errorPath = bulkConfig.GetFileRoseErrorPath();
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
            LoggerService.LogInformation($"   ❌ Errors  : {_errorPath}");
            LoggerService.LogInformation($"   📅 Year    : {_schoolYear}");
            LoggerService.LogInformation($"   🏷️  Suffix  : {_roseSuffix}");

            var lines = File.ReadAllLines(_validationCsvPath, Encoding.UTF8);
            if (lines.Length < 2)
            {
                LoggerService.LogWarning("⚠️  CSV is empty or has no data rows.");
                return result;
            }

            // ── Locate columns ────────────────────────────────────────────────
            var headerCols = SplitCsvLine(lines[0]);
            int idxClientId = FindColumn(headerCols, "ClientId");
            int idxClientIdStatus = FindColumn(headerCols, "ClientIdStatus");
            int idxIsFileRose = FindColumn(headerCols, "IsFileRoseDefault");
            int idxExtracted = FindColumn(headerCols, "IsFileRoseExtracted");

            if (idxClientId < 0 || idxClientIdStatus < 0)
            {
                LoggerService.LogWarning("⚠️  CSV is missing required columns: ClientId, ClientIdStatus.");
                return result;
            }

            if (idxIsFileRose < 0)
                LoggerService.LogInformation("   ℹ️  IsFileRoseDefault column absent — assumed False for all rows.");
            if (idxExtracted < 0)
                LoggerService.LogInformation("   ℹ️  IsFileRoseExtracted column absent — all eligible rows will be processed.");

            // ── Build eligible-row lookup ─────────────────────────────────────
            var eligibleRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var cols = SplitCsvLine(lines[i]);
                if (cols.Count <= Math.Max(idxClientId, idxClientIdStatus)) continue;

                var clientId = cols[idxClientId].Trim();
                var statusRaw = cols[idxClientIdStatus].Trim();

                bool statusIsFound = statusRaw == "1" ||
                                     statusRaw.Equals("Found", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(clientId) || !statusIsFound) continue;

                bool isFileRose = idxIsFileRose >= 0 && idxIsFileRose < cols.Count &&
                                  cols[idxIsFileRose].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                if (!isFileRose) continue;

                bool alreadyExtracted = idxExtracted >= 0 && idxExtracted < cols.Count &&
                                        cols[idxExtracted].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                if (!alreadyExtracted)
                    eligibleRows[clientId] = i;
            }

            LoggerService.LogInformation($"   📊 Eligible records in CSV : {eligibleRows.Count}");

            var scanFiles = Directory.GetFiles(_scanPath, "*.pdf", SearchOption.TopDirectoryOnly);
            LoggerService.LogInformation($"   📁 Files in scan folder    : {scanFiles.Length}\n");

            int patchedLines = 0;

            foreach (var filePath in scanFiles)
            {
                var fileName = Path.GetFileName(filePath);
                var clientId = Path.GetFileNameWithoutExtension(filePath).Trim();
                var extension = Path.GetExtension(filePath);

                LoggerService.LogInformation($"   Processing: {fileName}");

                // Validation 1: filename must be numeric
                if (!IsValidClientId(clientId))
                {
                    var reason = $"'{clientId}' is not a valid numeric ClientId";
                    LoggerService.LogWarning($"      ❌ {reason}");
                    MoveToError(filePath, fileName, reason, FileRoseErrorCategory.InvalidFileName, result);
                    continue;
                }

                // Validation 2: ClientId must be in the eligible CSV rows
                if (!eligibleRows.TryGetValue(clientId, out int rowIndex))
                {
                    var reason = $"ClientId '{clientId}' not found in CSV " +
                                 "(requires ClientIdStatus=Found and IsFileRoseDefault=True)";
                    LoggerService.LogWarning($"      ❌ {reason}");
                    MoveToError(filePath, fileName, reason, FileRoseErrorCategory.ClientIdNotMatched, result);
                    continue;
                }

                var newFileName = $"{clientId}_{_roseSuffix}_{_schoolYear}{extension}";
                var destinationPath = Path.Combine(_outputPath, newFileName);

                if (File.Exists(destinationPath))
                {
                    LoggerService.LogInformation($"      ⏭️  Already extracted → {newFileName}");
                    result.AlreadyExtracted++;
                    patchedLines += PatchCsvRow(lines, rowIndex, idxExtracted, "True");
                    continue;
                }

                try
                {
                    File.Copy(filePath, destinationPath, overwrite: false);
                    LoggerService.LogInformation($"      ✅ Extracted → {newFileName}");

                    result.Extracted++;
                    result.ExtractedFiles.Add((clientId, newFileName));
                    patchedLines += PatchCsvRow(lines, rowIndex, idxExtracted, "True");
                }
                catch (Exception ex)
                {
                    var reason = $"File copy failed: {ex.Message}";
                    LoggerService.LogWarning($"      ❌ {reason}");
                    MoveToError(filePath, fileName, reason, FileRoseErrorCategory.CopyFailed, result);
                    patchedLines += PatchCsvRow(lines, rowIndex, idxExtracted, "False");
                }
            }

            // ── Persist CSV changes ───────────────────────────────────────────
            if (patchedLines > 0)
            {
                string tempPath = _validationCsvPath + ".tmp";
                File.WriteAllLines(tempPath, lines, Encoding.UTF8);
                File.Move(tempPath, _validationCsvPath, overwrite: true);
                LoggerService.LogInformation($"\n   ✅ CSV patched — {patchedLines} line(s) updated.");
            }
            else
            {
                LoggerService.LogInformation("\n   ℹ️  No CSV changes required.");
            }

            // ── Always write (or clear) the error summary ─────────────────────
            WriteErrorSummary(result);

            return result;
        }

        // ── Error summary ─────────────────────────────────────────────────────

        /// <summary>
        /// Writes <c>_extraction_errors.txt</c> in the error folder after every run.
        /// <list type="bullet">
        ///   <item>Zero errors → deletes any stale file from a previous run.</item>
        ///   <item>Errors present → groups them by category with clear action steps.</item>
        /// </list>
        /// The final phase checks for this file's existence as a gate before uploading.
        /// </summary>
        private void WriteErrorSummary(FileRoseExtractionResult result)
        {
            var summaryPath = Path.Combine(_errorPath, ErrorSummaryFileName);

            // No errors → delete stale summary so its presence reliably means "broken"
            if (result.ErrorFiles.Count == 0)
            {
                if (File.Exists(summaryPath)) File.Delete(summaryPath);
                LoggerService.LogInformation("   ✅ No errors — error summary cleared.");
                return;
            }

            var sb = new StringBuilder();

            // ── Header ────────────────────────────────────────────────────────
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║         FileRose Extraction — Error Summary                  ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine($"  Generated  : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  School year: {_schoolYear}");
            sb.AppendLine($"  Scan folder: {_scanPath}");
            sb.AppendLine($"  Error folder: {_errorPath}");
            sb.AppendLine();
            sb.AppendLine("  Run results:");
            sb.AppendLine($"    ✅  Extracted successfully : {result.Extracted}");
            sb.AppendLine($"    ⏭️   Already extracted      : {result.AlreadyExtracted}");
            sb.AppendLine($"    ❌  Errors (need fix)       : {result.Errors}");
            sb.AppendLine();

            // ── Group 1: Invalid file names ───────────────────────────────────
            var invalidNames = result.InvalidFileNameErrors.ToList();
            if (invalidNames.Count > 0)
            {
                sb.AppendLine(new string('─', 64));
                sb.AppendLine($"  ❌ GROUP 1 — Invalid file name  ({invalidNames.Count} file(s))");
                sb.AppendLine("     The filename must be the numeric ClientId only, e.g. 123456.pdf");
                sb.AppendLine(new string('─', 64));

                int n = 1;
                foreach (var (fileName, reason, _) in invalidNames)
                {
                    sb.AppendLine($"  {n++,2}. {fileName}");
                    sb.AppendLine($"      → {reason}");
                }

                sb.AppendLine();
                sb.AppendLine("  ✏️  Action: Rename each file to <ClientId>.pdf and move back to");
                sb.AppendLine($"            '{Path.GetFileName(_scanPath)}'");
                sb.AppendLine();
            }

            // ── Group 2: ClientId not matched ─────────────────────────────────
            var notMatched = result.ClientIdNotMatchedErrors.ToList();
            if (notMatched.Count > 0)
            {
                sb.AppendLine(new string('─', 64));
                sb.AppendLine($"  ❌ GROUP 2 — ClientId not matched  ({notMatched.Count} file(s))");
                sb.AppendLine("     The numeric ClientId was not found in Validation_Results.csv");
                sb.AppendLine("     with ClientIdStatus=Found AND IsFileRoseDefault=True.");
                sb.AppendLine(new string('─', 64));

                int n = 1;
                foreach (var (fileName, reason, _) in notMatched)
                {
                    sb.AppendLine($"  {n++,2}. {fileName}");
                    sb.AppendLine($"      → {reason}");
                }

                sb.AppendLine();
                sb.AppendLine("  ✏️  Action:");
                sb.AppendLine("      a) Verify the ClientId is correct (check PHIS or the student list).");
                sb.AppendLine("      b) If the ClientId is wrong, rename the file and move back to");
                sb.AppendLine($"            '{Path.GetFileName(_scanPath)}'");
                sb.AppendLine("      c) If the CSV record is missing, run --check-filerose first to");
                sb.AppendLine("         refresh IsFileRoseDefault, then re-run --extract-filerose.");
                sb.AppendLine();
            }

            // ── Group 3: Copy / IO failures ───────────────────────────────────
            var copyFailed = result.CopyFailedErrors.ToList();
            if (copyFailed.Count > 0)
            {
                sb.AppendLine(new string('─', 64));
                sb.AppendLine($"  ❌ GROUP 3 — File copy failed  ({copyFailed.Count} file(s))");
                sb.AppendLine("     A system error occurred while copying the file (permissions,");
                sb.AppendLine("     locked file, disk full, etc.).");
                sb.AppendLine(new string('─', 64));

                int n = 1;
                foreach (var (fileName, reason, _) in copyFailed)
                {
                    sb.AppendLine($"  {n++,2}. {fileName}");
                    sb.AppendLine($"      → {reason}");
                }

                sb.AppendLine();
                sb.AppendLine("  ✏️  Action: Resolve the system issue, then move the file back to");
                sb.AppendLine($"            '{Path.GetFileName(_scanPath)}' and re-run --extract-filerose.");
                sb.AppendLine();
            }

            // ── Footer ────────────────────────────────────────────────────────
            sb.AppendLine(new string('═', 64));
            sb.AppendLine("  ⚠️  UPLOAD BLOCKED until all errors above are resolved.");
            sb.AppendLine("  After fixing, re-run:  --extract-filerose");
            sb.AppendLine(new string('═', 64));

            File.WriteAllText(summaryPath, sb.ToString(), Encoding.UTF8);

            LoggerService.LogWarning($"\n   ⚠️  {result.Errors} error(s) — summary written to:");
            LoggerService.LogWarning($"       {summaryPath}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void MoveToError(
            string filePath,
            string fileName,
            string reason,
            FileRoseErrorCategory category,
            FileRoseExtractionResult result)
        {
            var errorDest = Path.Combine(_errorPath, fileName);

            if (File.Exists(errorDest))
            {
                var stem = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                errorDest = Path.Combine(_errorPath, $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
            }

            try
            {
                File.Move(filePath, errorDest, overwrite: false);
                LoggerService.LogInformation($"      ➡️  Moved → {Path.GetFileName(errorDest)}");
            }
            catch (Exception ex)
            {
                LoggerService.LogWarning($"      ⚠️  Could not move file: {ex.Message}");
            }

            result.Errors++;
            result.ErrorFiles.Add((fileName, reason, category));
        }

        private static bool IsValidClientId(string clientId) =>
            !string.IsNullOrWhiteSpace(clientId) && clientId.All(char.IsDigit);

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