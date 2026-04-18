using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Phis;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using System.Globalization;
using System.Text;
using ConsentSyncCore.Services.ConfigurationPoco;
using ConsentSyncCore.Services.Configuration;

namespace Orchestrator.Phase3
{
    public class Phase3Orchestrator
    {
        private readonly IConfiguration _config;
        private readonly Phase3Config _phase3Config;
        private readonly SchoolContextConfig _schoolContext;
        private readonly IWebDriver _driver;
        private readonly PhisSearchService _phisSearchService;
        private readonly PhisSessionManager _sessionManager;
        private readonly ILogger<Phase3Orchestrator> _logger;

        public Phase3Orchestrator(
            IConfiguration? config = null,
            IWebDriver? driver = null,
            PhisSearchService? phisSearchService = null,
            PhisSessionManager? sessionManager = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _phase3Config = ConfigurationService.GetPhase3Config();
            _schoolContext = ConfigurationService.GetSchoolContextConfig();
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _phisSearchService = phisSearchService ?? throw new ArgumentNullException(nameof(phisSearchService));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = LoggerService.GetLogger<Phase3Orchestrator>();
        }

        public async Task<Phase3Result> RunAsync()
        {
            LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║       ConsentSync - Phase 3: Upload to PHIS            ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝\n");

            var result = new Phase3Result();

            try
            {
                // Step 1: Load Upload_to_PHIS.csv
                LoggerService.LogInformation("📋 Step 1: Loading Upload_to_PHIS.csv...");
                var uploadRecords = LoadUploadCsv();
                result.TotalRecords = uploadRecords.Count;
                LoggerService.LogInformation($"   ✅ Loaded {uploadRecords.Count} upload records " +
                    $"({uploadRecords.Count(r => !r.IsFeuilleRose)} consent, " +
                    $"{uploadRecords.Count(r => r.IsFeuilleRose)} FileRose)");

                if (uploadRecords.Count == 0)
                {
                    LoggerService.LogInformation("\n⚠️  No records found — run Pre-Phase 3 first.");
                    return result;
                }

                // Step 2: Filter records to process (skip already successful)
                LoggerService.LogInformation("\n📋 Step 2: Filtering records to process...");
                var recordsToProcess = uploadRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.ClientID) &&
                                r.VerifStatus != UploadVerificationStatus.Success)
                    .ToList();

                int alreadyVerified = uploadRecords.Count - recordsToProcess.Count;
                LoggerService.LogInformation($"   ✅ To process  : {recordsToProcess.Count}");
                LoggerService.LogInformation($"   ⏭️  Already done: {alreadyVerified}");

                if (recordsToProcess.Count == 0)
                {
                    LoggerService.LogInformation("\n✅ All records already verified!");
                    return result;
                }

                // Step 3: Verify PHIS session
                LoggerService.LogInformation("\n📋 Step 3: Verifying PHIS session...");
                if (!_sessionManager.EnsureSessionValid())
                {
                    LoggerService.LogInformation("   ❌ PHIS session is not valid!");
                    result.HasErrors = true;
                    return result;
                }
                LoggerService.LogInformation("   ✅ PHIS session is active");

                // Step 4: Process each record row-by-row
                LoggerService.LogInformation(
                    $"\n📋 Step 4: Processing {recordsToProcess.Count} documents...\n");

                int successCount = 0;
                int skipCount = 0;
                int failureCount = 0;
                int processedCount = 0;

                foreach (var record in recordsToProcess)
                {
                    processedCount++;

                    LoggerService.LogInformation($"\n{new string('═', 70)}");
                    LoggerService.LogInformation(
                        $"📄 DOCUMENT {processedCount}/{recordsToProcess.Count}  " +
                        $"[{(record.IsFeuilleRose ? "🌹 FileRose" : "📋 Consent")}]");
                    LoggerService.LogInformation($"{new string('═', 70)}");
                    LoggerService.LogInformation($"Client     : {record.FirstName} {record.LastName}  ({record.ClientID})");
                    LoggerService.LogInformation($"Document   : {record.DocumentTitle}");
                    LoggerService.LogInformation($"Description: {record.Description}");
                    LoggerService.LogInformation($"VerifStatus: {record.VerifStatus}");
                    LoggerService.LogInformation($"{new string('─', 70)}");

                    try
                    {
                        bool success;

                        if (record.IsFeuilleRose)
                        {
                            // ── FileRose upload (stub — will be implemented later) ──
                            success = await ProcessFileRoseUploadAsync(record);
                        }
                        else
                        {
                            // ── Standard consent upload ────────────────────────────
                            success = await ProcessConsentUploadAsync(record);
                        }

                        if (success)
                        {
                            successCount++;
                            result.SuccessfulUploads++;
                        }
                        else
                        {
                            failureCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogInformation($"\n❌ ERROR: {ex.Message}");
                        SetFailure(record, $"Unhandled exception: {ex.Message}");
                        failureCount++;
                        result.ErrorMessages.Add($"{record.ClientID} - {record.Description}: {ex.Message}");
                        try { await _phisSearchService.NavigateBackToSearchPagesAsync(); } catch { }
                    }

                    // Always persist after each document
                    SaveUploadCsv(uploadRecords);

                    // Session heartbeat every 10 documents
                    if (processedCount % 10 == 0)
                    {
                        LoggerService.LogInformation($"\n🔄 Session check ({processedCount} docs processed)...");
                        if (!_sessionManager.EnsureSessionValid())
                        {
                            LoggerService.LogInformation("   ⚠️  Session expired!");
                            result.HasErrors = true;
                            break;
                        }
                        LoggerService.LogInformation("   ✅ Session still active");
                    }

                    await Task.Delay(_phase3Config.Upload.DelayBetweenUploadsMs);
                }

                DisplaySummary(result, successCount, skipCount, failureCount,
                    uploadRecords.Count, alreadyVerified);

                return result;
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"\n❌ FATAL ERROR: {ex.Message}");
                result.HasErrors = true;
                return result;
            }
        }

        // ── Consent upload (existing logic, extracted to own method) ──────────

        private async Task<bool> ProcessConsentUploadAsync(UploadRecord record)
        {
            // Validate antigen
            if (string.IsNullOrWhiteSpace(record.PhisAntigen))
            {
                SetFailure(record, "PhisAntigen is empty — cannot select consent directive");
                return false;
            }

            // A: Search & set client in context
            LoggerService.LogInformation("\n🔍 STEP A: Setting client in context...");
            if (!await _phisSearchService.SearchByClientIdAndSetInContextAsync(record.ClientID))
            {
                SetFailure(record, "Could not set client in context");
                return false;
            }
            LoggerService.LogInformation("   ✅ Client in context");

            // B: Navigate to Immunization Service
            LoggerService.LogInformation("\n🧭 STEP B: Navigating to Immunization Service...");
            bool navigated = await _phisSearchService.NavigateToImmunizationServiceAsync() ||
                             await _phisSearchService.NavigateToImmunizationServiceViaMenuAsync();
            if (!navigated)
            {
                SetFailure(record, "Could not navigate to Immunization Service");
                return false;
            }
            LoggerService.LogInformation("   ✅ On Immunization Service page");

            // C: Select consent directive
            LoggerService.LogInformation($"\n🎯 STEP C: Selecting consent directive for '{record.PhisAntigen}'...");
            if (!await _phisSearchService.SelectConsentDirectiveByAntigenAsync(record.PhisAntigen))
            {
                SetFailure(record, $"Could not select consent directive for '{record.PhisAntigen}'");
                return false;
            }
            LoggerService.LogInformation("   ✅ Consent directive selected");

            // D: Open Documents page
            LoggerService.LogInformation("\n📄 STEP D: Opening Documents page...");
            if (!await _phisSearchService.ClickDocumentsButtonAsync())
            {
                SetFailure(record, "Could not open Documents page");
                return false;
            }
            LoggerService.LogInformation("   ✅ Documents page opened");

            // E: Check if document already exists
            LoggerService.LogInformation($"\n🔍 STEP E: Checking for existing document '{record.DocumentTitle}'...");
            if (await _phisSearchService.CheckIfDocumentExistsAsync(record.DocumentTitle))
            {
                LoggerService.LogInformation("   ✅ Document already exists — marking Success");
                record.VerifStatus = UploadVerificationStatus.Success;
                record.FailureReason = string.Empty;
                await _phisSearchService.NavigateBackToSearchPagesAsync();
                return true;
            }
            LoggerService.LogInformation("   ℹ️  Document not found — upload required");

            // F: Click Add New
            LoggerService.LogInformation("\n📤 STEP F: Opening upload page...");
            if (!await _phisSearchService.ClickAddNewDocumentButtonAsync())
            {
                SetFailure(record, "Could not open upload page");
                return false;
            }
            LoggerService.LogInformation("   ✅ Upload page opened");

            // G: Upload PDF
            LoggerService.LogInformation("\n📎 STEP G: Uploading document...");
            var pdfPath = Path.Combine(_phase3Config.Input.ConsentPath, $"{record.DocumentTitle}.pdf");
            LoggerService.LogInformation($"   PDF: {pdfPath}");

            if (!File.Exists(pdfPath))
            {
                SetFailure(record, $"PDF not found at: {pdfPath}");
                return false;
            }

            if (!await _phisSearchService.UploadDocumentAsync(pdfPath, record.DocumentTitle, record.Description))
            {
                SetFailure(record, "Document upload failed (PHIS returned an error)");
                try { await _phisSearchService.NavigateBackToSearchPagesAsync(); } catch { }
                return false;
            }

            LoggerService.LogInformation("   ✅ Document uploaded successfully!");
            record.VerifStatus = UploadVerificationStatus.Success;
            record.FailureReason = string.Empty;
            await _phisSearchService.NavigateBackToSearchPagesAsync();
            return true;
        }

        // ── FileRose upload (stub — to be implemented) ────────────────────────

        /// <summary>
        /// FileRose upload is not yet implemented.
        /// Leaves <see cref="UploadRecord.VerifStatus"/> as
        /// <see cref="UploadVerificationStatus.NotProcessed"/> and returns
        /// <c>false</c> so it is retried on the next run once implemented.
        /// </summary>
        private Task<bool> ProcessFileRoseUploadAsync(UploadRecord record)
        {
            LoggerService.LogInformation(
                $"   ℹ️  FileRose upload not yet implemented — " +
                $"leaving VerifStatus = NotProcessed for {record.ClientID}");

            // Do NOT set NeedsManualReview — keep at NotProcessed so Phase 3
            // will pick it up again once the upload logic is coded.
            return Task.FromResult(false);
        }

        // ── Shared helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Marks a record as failed and records the reason so the user knows
        /// exactly what to fix. Setting VerifStatus = 0 (NotProcessed) in the
        /// CSV will cause Phase 3 to retry on the next run.
        /// </summary>
        private static void SetFailure(UploadRecord record, string reason)
        {
            record.VerifStatus = UploadVerificationStatus.NeedsManualReview;
            record.FailureReason = reason;
            LoggerService.LogWarning($"   ❌ {reason}");
        }

        // ── CSV I/O (unchanged) ───────────────────────────────────────────────

        private List<UploadRecord> LoadUploadCsv()
        {
            var csvPath = Path.Combine(
                _phase3Config.Input.UploadCsvPath,
                _phase3Config.Input.UploadCsvFileName);

            if (!File.Exists(csvPath))
                throw new FileNotFoundException($"Upload CSV not found: {csvPath}");

            LoggerService.LogInformation($"   📂 Reading: {csvPath}");

            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
                HeaderValidated = null,
                TrimOptions = TrimOptions.Trim,
                PrepareHeaderForMatch = args => args.Header.ToLower().Replace(" ", "")
            };

            using var reader = new StreamReader(csvPath, Encoding.UTF8);
            using var csv = new CsvReader(reader, csvConfig);
            csv.Context.RegisterClassMap<UploadRecordMap>();

            var allRecords = csv.GetRecords<UploadRecord>().ToList();
            List<UploadRecord> records;

            if (_phase3Config.Testing.Enabled)
            {
                LoggerService.LogInformation("\n   🧪 TESTING MODE");
                records = allRecords;

                if (_phase3Config.Testing.TestClientIds?.Length > 0)
                {
                    records = records
                        .Where(r => _phase3Config.Testing.TestClientIds.Contains(r.ClientID))
                        .ToList();
                    LoggerService.LogInformation(
                        $"   🎯 Filtering to: {string.Join(", ", _phase3Config.Testing.TestClientIds)}");
                }

                if (_phase3Config.Testing.MaxRecordsToProcess > 0 &&
                    records.Count > _phase3Config.Testing.MaxRecordsToProcess)
                {
                    records = records.Take(_phase3Config.Testing.MaxRecordsToProcess).ToList();
                    LoggerService.LogInformation(
                        $"   ⚠️  Limited to {_phase3Config.Testing.MaxRecordsToProcess} records");
                }
            }
            else
            {
                records = allRecords;
                LoggerService.LogInformation($"   ✅ Loaded {records.Count} records (Production)");
            }

            records = records
                .OrderBy(r => r.ClientID)
                .ThenBy(r => r.IsFeuilleRose)   // consent first, then FileRose
                .ThenBy(r => r.PhisAntigen)
                .ToList();

            LoggerService.LogInformation(
                $"   👥 Unique clients: {records.Select(r => r.ClientID).Distinct().Count()}");

            return records;
        }

        private void SaveUploadCsv(List<UploadRecord> processedRecords)
        {
            try
            {
                var csvPath = Path.Combine(
                    _phase3Config.Input.UploadCsvPath,
                    _phase3Config.Input.UploadCsvFileName);

                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    MissingFieldFound = null,
                    HeaderValidated = null,
                    TrimOptions = TrimOptions.Trim,
                    PrepareHeaderForMatch = args => args.Header.ToLower().Replace(" ", "")
                };

                List<UploadRecord> allRecords;
                using (var reader = new StreamReader(csvPath, Encoding.UTF8))
                using (var csvReader = new CsvReader(reader, csvConfig))
                {
                    csvReader.Context.RegisterClassMap<UploadRecordMap>();
                    allRecords = csvReader.GetRecords<UploadRecord>().ToList();
                }

                // Update VerifStatus + FailureReason by DocumentTitle (unique key)
                var updatedLookup = processedRecords
                    .ToDictionary(r => r.DocumentTitle, r => (r.VerifStatus, r.FailureReason));

                int updatedCount = 0;
                foreach (var record in allRecords)
                {
                    if (updatedLookup.TryGetValue(record.DocumentTitle, out var upd))
                    {
                        if (record.VerifStatus != upd.VerifStatus ||
                            record.FailureReason != upd.FailureReason)
                        {
                            record.VerifStatus = upd.VerifStatus;
                            record.FailureReason = upd.FailureReason;
                            updatedCount++;
                        }
                    }
                }

                using var writer = new StreamWriter(csvPath, false, Encoding.UTF8);
                using var csvWriter = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
                csvWriter.Context.RegisterClassMap<UploadRecordMap>();
                csvWriter.WriteRecords(allRecords);

                if (updatedCount > 0)
                    LoggerService.LogInformation(
                        $"      💾 CSV saved — {updatedCount} record(s) updated");
            }
            catch (Exception ex)
            {
                LoggerService.LogWarning($"      ⚠️  Could not save CSV: {ex.Message}");
            }
        }

        private void DisplaySummary(Phase3Result result, int successCount, int skipCount,
            int failureCount, int totalRecords, int alreadyVerified)
        {
            LoggerService.LogInformation("\n" + new string('═', 70));
            LoggerService.LogInformation("📊 PHASE 3 COMPLETE");
            LoggerService.LogInformation(new string('═', 70));
            LoggerService.LogInformation($"Total records in CSV : {totalRecords}");
            LoggerService.LogInformation($"Already verified     : {alreadyVerified}");
            LoggerService.LogInformation($"Processed this run   : {successCount + skipCount + failureCount}");
            LoggerService.LogInformation($"   ✅ Uploaded        : {successCount}");
            LoggerService.LogInformation($"   ⏭️  Already existed : {skipCount}");
            LoggerService.LogInformation($"   ❌ Failed          : {failureCount}");

            if (result.ErrorMessages.Count > 0)
            {
                LoggerService.LogInformation($"\n⚠️  Errors ({result.ErrorMessages.Count}):");
                foreach (var error in result.ErrorMessages.Take(10))
                    LoggerService.LogInformation($"   - {error}");
            }

            LoggerService.LogInformation(
                $"\n💡 To retry failed rows: set VerifStatus = 0 in the CSV and re-run Phase 3.");
            LoggerService.LogInformation(
                $"   CSV: {_phase3Config.Input.UploadCsvPath}\\{_phase3Config.Input.UploadCsvFileName}");
        }
    }
}