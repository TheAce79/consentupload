using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Services
{


    /// <summary>
    /// Appends FileRose rows to an existing Upload_to_PHIS.csv
    /// without touching consent rows or already-uploaded records.
    ///
    /// Safe to run multiple times — duplicate rows (same ClientID +
    /// DocumentTitle) are never written twice.
    ///
    /// Callable from:
    ///   • UI button (bt_AppendFileRose_Click)
    ///   • Command line:  Orchestrator.exe --append-filerose
    /// </summary>
    public class FileRoseAppendService
    {
        private readonly IConfiguration _config;
        private readonly PrePhase3Config _prePhase3Config;
        private readonly Phase2Config _phase2Config;
        private readonly BulkPdfExtractionConfig _bulkConfig;
        private readonly SchoolContextConfig _schoolContext;

        public FileRoseAppendService(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _prePhase3Config = ConfigurationService.GetPrePhase3Config();
            _phase2Config = ConfigurationService.GetPhase2Config();
            _bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            _schoolContext = ConfigurationService.GetSchoolContextConfig();
        }

        // ── Result ────────────────────────────────────────────────────

        public class AppendResult
        {
            public int Appended { get; set; }
            public int AlreadyExist { get; set; }
            public int PdfMissing { get; set; }
            public int NoClientId { get; set; }
            public bool HasErrors { get; set; }
            public List<string> Messages { get; set; } = new();
        }

        // ── Public entry point ────────────────────────────────────────

        public AppendResult AppendFileRoseRows()
        {
            var result = new AppendResult();

            try
            {
                var uploadCsvPath = Path.Combine(
                    _prePhase3Config.OutputPath,
                    _phase2Config.UploadCsv);

                var fileRoseReadyDir = _bulkConfig.GetFileRoseOutputReadyPath();
                var suffix = _bulkConfig.RoseSuffix;   // "suiviscolaire"
                var schoolYear = _schoolContext.SchoolYear;

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("🌹 APPEND FILEROSE ROWS TO UPLOAD CSV");
                LoggerService.LogInformation(new string('═', 60));
                LoggerService.LogInformation($"   Upload CSV       : {uploadCsvPath}");
                LoggerService.LogInformation($"   FileRose folder  : {fileRoseReadyDir}");
                LoggerService.LogInformation($"   School year      : {schoolYear}");

                // ── Step 1: Load existing Upload CSV (or start fresh) ─────────
                var existing = LoadUploadCsv(uploadCsvPath);
                var existingKeys = existing
                    .Select(r => MakeKey(r.ClientID, r.DocumentTitle))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                LoggerService.LogInformation($"\n   Existing rows    : {existing.Count}");

                // ── Step 2: Scan FileRose PDFs in 2_Output_Ready_FileRose ──────
                if (!Directory.Exists(fileRoseReadyDir))
                {
                    var msg = $"FileRose output folder not found: {fileRoseReadyDir}";
                    LoggerService.LogWarning($"   ⚠️  {msg}");
                    result.HasErrors = true;
                    result.Messages.Add(msg);
                    return result;
                }

                var pdfFiles = Directory.GetFiles(fileRoseReadyDir, "*.pdf");
                LoggerService.LogInformation($"   PDF files found  : {pdfFiles.Length}");

                var newRows = new List<UploadRecord>();

                foreach (var pdfPath in pdfFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(pdfPath);
                    var documentTitle = fileName;  // without .pdf

                    // ── Parse ClientId from filename: {ClientId}_{suffix}_{year} ──
                    var clientId = ExtractClientIdFromFileName(fileName, suffix);

                    if (string.IsNullOrWhiteSpace(clientId))
                    {
                        LoggerService.LogWarning($"   ⚠️  Cannot extract ClientId from: {fileName}");
                        result.NoClientId++;
                        result.Messages.Add($"Skipped (no ClientId): {fileName}");
                        continue;
                    }

                    // ── Skip if already in Upload CSV ─────────────────────────
                    var key = MakeKey(clientId, documentTitle);
                    if (existingKeys.Contains(key))
                    {
                        LoggerService.LogInformation($"   ⏭️  Already exists  : {documentTitle}");
                        result.AlreadyExist++;
                        continue;
                    }

                    // ── Resolve name from existing consent rows ───────────────
                    var matchingConsent = existing.FirstOrDefault(r =>
                        r.ClientID.Equals(clientId, StringComparison.OrdinalIgnoreCase));

                    var lastName = matchingConsent?.LastName ?? string.Empty;
                    var firstName = matchingConsent?.FirstName ?? string.Empty;

                    newRows.Add(new UploadRecord
                    {
                        ClientID = clientId,
                        LastName = lastName,
                        FirstName = firstName,
                        DocumentTitle = documentTitle,
                        Description = "Suivi scolaire",
                        PhisAntigen = string.Empty,
                        IsFeuilleRose = true,
                        VerifStatus = UploadVerificationStatus.NotProcessed,
                        FailureReason = string.Empty
                    });

                    result.Appended++;
                    LoggerService.LogInformation($"   ✅ Queued for append : {clientId} → {documentTitle}");
                }

                // ── Step 3: Append & save ─────────────────────────────────────
                if (newRows.Count > 0)
                {
                    existing.AddRange(newRows);
                    SaveUploadCsv(uploadCsvPath, existing);
                    LoggerService.LogInformation($"\n   ✅ Appended {newRows.Count} FileRose row(s) → {uploadCsvPath}");
                }
                else
                {
                    LoggerService.LogInformation("\n   ℹ️  Nothing to append — all rows already present.");
                }

                // ── Summary ───────────────────────────────────────────────────
                LoggerService.LogInformation("\n" + new string('─', 60));
                LoggerService.LogInformation($"   ✅ Appended      : {result.Appended}");
                LoggerService.LogInformation($"   ⏭️  Already exist : {result.AlreadyExist}");
                LoggerService.LogInformation($"   ⚠️  No ClientId  : {result.NoClientId}");
                LoggerService.LogInformation(new string('─', 60));
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ FileRoseAppendService error: {ex.Message}", ex);
                result.HasErrors = true;
                result.Messages.Add(ex.Message);
            }

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Parses ClientId from a filename like
        ///   "123456_suiviscolaire_2025-2026"  →  "123456"
        /// </summary>
        private static string ExtractClientIdFromFileName(string nameWithoutExt, string suffix)
        {
            // Expected pattern: {ClientId}_{suffix}_{schoolYear}
            var idx = nameWithoutExt.IndexOf(
                $"_{suffix}_", StringComparison.OrdinalIgnoreCase);

            if (idx > 0)
                return nameWithoutExt[..idx].Trim();

            // Fallback: first segment before any underscore
            var parts = nameWithoutExt.Split('_');
            return parts.Length > 0 ? parts[0].Trim() : string.Empty;
        }

        private static string MakeKey(string clientId, string docTitle)
            => $"{clientId}|{docTitle}".ToUpperInvariant();

        private static List<UploadRecord> LoadUploadCsv(string path)
        {
            if (!File.Exists(path))
            {
                LoggerService.LogInformation($"   ℹ️  Upload CSV not found — will create: {path}");
                return new List<UploadRecord>();
            }

            using var reader = new StreamReader(path, Encoding.UTF8);
            using var csv = new CsvReader(
                reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    MissingFieldFound = null,
                    HeaderValidated = null
                });
            csv.Context.RegisterClassMap<UploadRecordMap>();
            return csv.GetRecords<UploadRecord>().ToList();
        }

        private static void SaveUploadCsv(string path, List<UploadRecord> records)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            using var csv = new CsvWriter(
                writer, new CsvConfiguration(CultureInfo.InvariantCulture));
            csv.Context.RegisterClassMap<UploadRecordMap>();
            csv.WriteRecords(records);
        }
    }

}
