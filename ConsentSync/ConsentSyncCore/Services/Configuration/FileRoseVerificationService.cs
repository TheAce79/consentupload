using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;

namespace ConsentSyncCore.Services
{
    /// <summary>
    /// Verifies whether a FileRose PDF exists for each eligible student record
    /// and can persist the result back into <c>Validation_Results.csv</c>.
    /// <para>
    /// Eligible row = <c>ClientId</c> not empty AND <c>ClientIdStatus == Found (1)</c>.
    /// Expected file name format: <c>{ClientId}.pdf</c>
    /// Scanned folder: <c>BulkPdfExtraction → 4 FileRose Extraction → 2_Output_Ready_FileRose</c>
    /// </para>
    /// </summary>
    public class FileRoseVerificationService
    {
        private readonly string _fileRoseDirectory;
        private readonly string _validationCsvPath;

        // ── Constructors ──────────────────────────────────────────────────────

        /// <summary>Initialises using paths resolved from <c>appsettings.json</c>.</summary>
        public FileRoseVerificationService()
        {
            var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            var prePhase3Config = ConfigurationService.GetPrePhase3Config();

            _fileRoseDirectory = bulkConfig.GetFileRoseOutputReadyPath();
            _validationCsvPath = Path.Combine(
                prePhase3Config.ValidationCsvPath,
                prePhase3Config.ValidationCsvFileName);
        }

        /// <summary>Overload that accepts explicit paths — useful for unit tests.</summary>
        public FileRoseVerificationService(string fileRoseDirectory, string validationCsvPath)
        {
            _fileRoseDirectory = fileRoseDirectory;
            _validationCsvPath = validationCsvPath;
        }

        // ── Core verification ─────────────────────────────────────────────────

        /// <summary>
        /// Scans <see cref="_fileRoseDirectory"/> and updates
        /// <see cref="ValidationRecord.IsFileRoseDefault"/> in place for every eligible record.
        /// </summary>
        public FileRoseVerificationResult VerifyFileRosePresentInDir(
            IEnumerable<ValidationRecord> records)
        {
            var result = new FileRoseVerificationResult
            {
                ScannedDirectory = _fileRoseDirectory
            };

            // Load all PDF stems once → O(1) per record lookup
            var existingFiles = Directory.Exists(_fileRoseDirectory)
                ? Directory.GetFiles(_fileRoseDirectory, "*.pdf", SearchOption.TopDirectoryOnly)
                           .Select(f => Path.GetFileNameWithoutExtension(f)!)
                           .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.ClientId) ||
                    record.ClientIdStatus != (int)ClientIdStatus.Found)
                {
                    result.Skipped++;
                    continue;
                }

                result.EligibleRecords++;

                bool filePresent = existingFiles.Contains(record.ClientId);
                record.IsFileRoseDefault = filePresent;

                if (filePresent)
                {
                    result.Found++;
                    result.Details[record.ClientId] = $"{record.ClientId}.pdf";
                }
                else
                {
                    result.NotFound++;
                    result.Details[record.ClientId] = null;
                }
            }

            return result;
        }

        // ── Standalone: load → check → patch only changed lines ──────────────

        /// <summary>
        /// Entry point for the <c>--check-filerose</c> command.
        /// <para>
        /// Reads <c>Validation_Results.csv</c> as raw text and rewrites
        /// <b>only the lines whose <c>IsFileRoseDefault</c> value changed</b>.
        /// All other lines — including their exact formatting and field order — are
        /// left completely untouched.
        /// </para>
        /// </summary>
        public FileRoseVerificationResult CheckAndUpdateCsv()
        {
            var emptyResult = new FileRoseVerificationResult { ScannedDirectory = _fileRoseDirectory };

            // ── Guard ─────────────────────────────────────────────────────────
            if (!File.Exists(_validationCsvPath))
            {
                LoggerService.LogWarning($"⚠️  Validation_Results.csv not found at: {_validationCsvPath}");
                LoggerService.LogInformation("   💡 Run Phase 2 first to generate the CSV.");
                return emptyResult;
            }

            LoggerService.LogInformation($"   📄 CSV  : {_validationCsvPath}");
            LoggerService.LogInformation($"   📁 Scan : {_fileRoseDirectory}");

            // ── Read raw lines (preserves original formatting) ────────────────
            var lines = File.ReadAllLines(_validationCsvPath, Encoding.UTF8);

            if (lines.Length < 2)
            {
                LoggerService.LogWarning("⚠️  CSV is empty or has no data rows.");
                return emptyResult;
            }

            // ── Parse header → find required column indices ───────────────────
            var headerCols = SplitCsvLine(lines[0]);

            int idxClientId = FindColumn(headerCols, "ClientId");
            int idxClientIdStatus = FindColumn(headerCols, "ClientIdStatus");
            int idxIsFileRose = FindColumn(headerCols, "IsFileRoseDefault");

            if (idxClientId < 0 || idxClientIdStatus < 0 || idxIsFileRose < 0)
            {
                LoggerService.LogWarning(
                    "⚠️  CSV is missing one of the required columns: " +
                    "ClientId, ClientIdStatus, IsFileRoseDefault.");
                return emptyResult;
            }

            // ── Build FileRose lookup once ────────────────────────────────────
            var existingFiles = Directory.Exists(_fileRoseDirectory)
                ? Directory.GetFiles(_fileRoseDirectory, "*.pdf", SearchOption.TopDirectoryOnly)
                           .Select(f => Path.GetFileNameWithoutExtension(f)!)
                           .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── Walk data rows, patch only what changed ───────────────────────
            var result = new FileRoseVerificationResult { ScannedDirectory = _fileRoseDirectory };
            int patchedLines = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                var cols = SplitCsvLine(lines[i]);

                // Ensure row has enough columns
                if (cols.Count <= Math.Max(idxClientId, Math.Max(idxClientIdStatus, idxIsFileRose)))
                {
                    result.Skipped++;
                    continue;
                }

                var clientId = cols[idxClientId].Trim();
                var statusRaw = cols[idxClientIdStatus].Trim();
                var currentFlagRaw = cols[idxIsFileRose].Trim();

                // Eligibility check
                bool statusIsFound = statusRaw == "1" ||
                                     statusRaw.Equals("Found", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(clientId) || !statusIsFound)
                {
                    result.Skipped++;
                    continue;
                }

                result.EligibleRecords++;

                bool filePresent = existingFiles.Contains(clientId);
                // Normalise to "True" / "False" matching CsvHelper's default bool output
                string newFlag = filePresent ? "True" : "False";

                if (filePresent)
                {
                    result.Found++;
                    result.Details[clientId] = $"{clientId}.pdf";
                }
                else
                {
                    result.NotFound++;
                    result.Details[clientId] = null;
                }

                // Only rewrite the line when the value actually differs
                if (!currentFlagRaw.Equals(newFlag, StringComparison.OrdinalIgnoreCase))
                {
                    cols[idxIsFileRose] = newFlag;
                    lines[i] = JoinCsvLine(cols);
                    patchedLines++;

                    LoggerService.LogInformation(
                        $"   ✏️  [{clientId}] IsFileRoseDefault: {currentFlagRaw} → {newFlag}");
                }
            }

            // ── Write back only if at least one line changed ──────────────────
            if (patchedLines > 0)
            {
                string tempPath = _validationCsvPath + ".tmp";
                File.WriteAllLines(tempPath, lines, Encoding.UTF8);
                File.Move(tempPath, _validationCsvPath, overwrite: true);

                LoggerService.LogInformation(
                    $"   ✅ CSV patched — {patchedLines} line(s) updated in place.");
            }
            else
            {
                LoggerService.LogInformation("   ℹ️  No changes detected — CSV not modified.");
            }

            return result;
        }

        // ── CSV line helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Splits a single CSV line respecting double-quoted fields.
        /// Returns a <see cref="List{T}"/> so individual cells are mutable.
        /// </summary>
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
                    // Escaped quote inside a quoted field: ""
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // skip next quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }

        /// <summary>
        /// Re-joins CSV fields, re-quoting any field that contains a comma or double-quote.
        /// </summary>
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

        /// <summary>
        /// Finds a column index by name (case-insensitive, trims surrounding quotes).
        /// Returns -1 when not found.
        /// </summary>
        private static int FindColumn(List<string> headers, string name)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i].Trim().Trim('"');
                if (h.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }
}