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

        // ── Progress record ───────────────────────────────────────────────────
        public record Phase3Progress(
            int Current,
            int Total,
            string ClientId,
            string StudentName,
            string DocumentTitle,
            bool IsFeuilleRose,
            bool IsSuccess);


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



        public async Task<Phase3Result> RunAsync(IProgress<Phase3Progress>? progress = null)
        {
            LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║       ConsentSync - Phase 3: Upload to PHIS            ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝\n");

            var result = new Phase3Result();

            try
            {
                // ── Step 1: Load Upload_to_PHIS.csv ───────────────────────────
                LoggerService.LogInformation("📋 Step 1: Loading Upload_to_PHIS.csv...");
                var uploadRecords = LoadUploadCsv();
                result.TotalRecords = uploadRecords.Count;
                LoggerService.LogInformation(
                    $"   ✅ Loaded {uploadRecords.Count} upload records " +
                    $"({uploadRecords.Count(r => !r.IsFeuilleRose)} consent, " +
                    $"{uploadRecords.Count(r => r.IsFeuilleRose)} FileRose)");

                if (uploadRecords.Count == 0)
                {
                    LoggerService.LogInformation("\n⚠️  No records found — run Pre-Phase 3 first.");
                    return result;
                }

                // ── Step 2: Filter records to process ─────────────────────────
                LoggerService.LogInformation("\n📋 Step 2: Filtering records to process...");

                var pending = uploadRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.ClientID) &&
                                r.VerifStatus != UploadVerificationStatus.Success)
                    .ToList();

                int alreadyVerified = uploadRecords.Count - pending.Count;

                var phisConfig = ConfigurationService.GetPhisConfig();
                int batchSize = phisConfig.BatchSize > 0 ? phisConfig.BatchSize : int.MaxValue;
                var recordsToProcess = pending.Take(batchSize).ToList();
                bool batchTruncated = recordsToProcess.Count < pending.Count;

                LoggerService.LogInformation($"   ✅ Pending         : {pending.Count}");
                LoggerService.LogInformation($"   ⏭️  Already done    : {alreadyVerified}");

                if (batchTruncated)
                {
                    LoggerService.LogInformation(
                        $"   ⚙️  Batch mode      : processing {recordsToProcess.Count} of {pending.Count} " +
                        $"(BatchSize = {phisConfig.BatchSize})");
                    LoggerService.LogInformation(
                        "   ℹ️  Re-run Phase 3 to continue with the next batch.");
                }

                if (recordsToProcess.Count == 0)
                {
                    LoggerService.LogInformation("\n✅ All records already verified!");
                    return result;
                }

                // ── Step 3: Verify PHIS session ───────────────────────────────
                LoggerService.LogInformation("\n📋 Step 3: Verifying PHIS session...");
                if (!_sessionManager.EnsureSessionValid())
                {
                    LoggerService.LogError("   ❌ PHIS session is not valid!");
                    result.HasErrors = true;
                    return result;
                }
                LoggerService.LogInformation("   ✅ PHIS session is active");

                // ── Step 4: Process each record ───────────────────────────────
                LoggerService.LogInformation(
                    $"\n📋 Step 4: Processing {recordsToProcess.Count} documents...\n");

                int successCount = 0;
                int skipCount = 0;
                int failureCount = 0;
                int processedCount = 0;

                try
                {
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

                        bool success = false;
                        try
                        {
                            success = record.IsFeuilleRose
                                ? await ProcessFileRoseUploadAsync(record)
                                : await ProcessConsentUploadAsync(record);

                            if (success)
                            {
                                successCount++;
                                result.SuccessfulUploads++;
                            }
                            else if (record.VerifStatus == UploadVerificationStatus.Success)
                            {
                                // Already existed on PHIS — counted as skip, not failure
                                skipCount++;
                                result.SuccessfulUploads++;
                            }
                            else
                            {
                                failureCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggerService.LogError($"\n❌ ERROR processing {record.DocumentTitle}: {ex.Message}");
                            SetFailure(record, $"Unhandled exception: {ex.Message}");
                            failureCount++;
                            result.ErrorMessages.Add(
                                $"{record.ClientID} - {record.Description}: {ex.Message}");
                            try { await _phisSearchService.NavigateBackToSearchPagesAsync(); } catch { }
                        }

                        // ── Report progress ───────────────────────────────────
                        progress?.Report(new Phase3Progress(
                            Current: processedCount,
                            Total: recordsToProcess.Count,
                            ClientId: record.ClientID,
                            StudentName: $"{record.FirstName} {record.LastName}",
                            DocumentTitle: record.DocumentTitle,
                            IsFeuilleRose: record.IsFeuilleRose,
                            IsSuccess: success));

                        // ── Persist after every document ──────────────────────
                        SaveUploadCsv(uploadRecords);

                        // ── Session heartbeat every 10 documents ──────────────
                        if (processedCount % 10 == 0)
                        {
                            LoggerService.LogInformation(
                                $"\n🔄 Session check ({processedCount} docs processed)...");
                            if (!_sessionManager.EnsureSessionValid())
                            {
                                LoggerService.LogWarning("   ⚠️  Session expired — stopping batch.");
                                result.HasErrors = true;
                                break;
                            }
                            LoggerService.LogInformation("   ✅ Session still active");
                        }

                        await Task.Delay(_phase3Config.Upload.DelayBetweenUploadsMs);
                    }
                }
                finally
                {
                    // ── Guaranteed flush — fires even on fatal exception or session break ──
                    LoggerService.LogInformation("\n💾 Final CSV flush...");
                    SaveUploadCsv(uploadRecords);
                }

                // ── Batch-complete notice ─────────────────────────────────────
                if (batchTruncated)
                {
                    int remaining = pending.Count - recordsToProcess.Count;
                    LoggerService.LogInformation(
                        $"⏸️  Batch of {recordsToProcess.Count} done. " +
                        $"{remaining} remaining — re-run Phase 3 to continue.");
                    result.BatchLimitReached = true;
                }

                // ── Step 5: Summary ───────────────────────────────────────────
                DisplaySummary(result, successCount, skipCount, failureCount,
                    uploadRecords.Count, alreadyVerified);

                return result;
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"\n❌ FATAL ERROR: {ex.Message}", ex);
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
        //private Task<bool> ProcessFileRoseUploadAsync(UploadRecord record)
        //{
        //    LoggerService.LogInformation(
        //        $"   ℹ️  FileRose upload not yet implemented — " +
        //        $"leaving VerifStatus = NotProcessed for {record.ClientID}");

        //    // Do NOT set NeedsManualReview — keep at NotProcessed so Phase 3
        //    // will pick it up again once the upload logic is coded.
        //    return Task.FromResult(false);
        //}


        private async Task<bool> ProcessFileRoseUploadAsync(UploadRecord record)
        {
            // ── A: Search & set client in context ────────────────────────────
            LoggerService.LogInformation("\n🔍 STEP A: Setting client in context...");
            if (!await _phisSearchService.SearchByClientIdAndSetInContextAsync(record.ClientID))
            {
                SetFailure(record, "Could not set client in context");
                return false;
            }
            LoggerService.LogInformation("   ✅ Client in context");

            // ── B: Resolve & validate PDF path ───────────────────────────────
            LoggerService.LogInformation("\n🌹 STEP B: Resolving FileRose PDF...");
            var pdfPath = Path.Combine(
                _phase3Config.Input.FileRosePath,
                $"{record.DocumentTitle}.pdf");

            LoggerService.LogInformation($"   PDF: {pdfPath}");
            if (!File.Exists(pdfPath))
            {
                SetFailure(record, $"FileRose PDF not found at: {pdfPath}");
                return false;
            }
            LoggerService.LogInformation("   ✅ PDF found on disk");

            // ── C: Navigate to Context Documents ─────────────────────────────
            if (!await _phisSearchService.NavigateToContextDocumentsAsync())
            {
                SetFailure(record, "Could not navigate to Context Documents page");
                return false;
            }
            LoggerService.LogInformation("   ✅ On Context Documents page");

            // ── D: Check if document already exists ───────────────────────────
            if (await _phisSearchService.CheckIfContextDocumentExistsAsync(record.DocumentTitle))
            {
                LoggerService.LogInformation(
                    "   ✅ Document already exists on PHIS — marking Success:-");
                record.VerifStatus = UploadVerificationStatus.Success;
                record.FailureReason = string.Empty;
                await _phisSearchService.NavigateBackToSearchPagesAsync();
                return true;
            }

            // ── Upload not yet enabled ────────────────────────────────────────
            // Step E (Add New → upload) is next once D is confirmed working.
            LoggerService.LogInformation(
                "   ℹ️  Upload step not yet enabled — " +
                $"leaving VerifStatus = NotProcessed for " +
                $"{record.ClientID} ({record.DocumentTitle})");

            record.VerifStatus = UploadVerificationStatus.NotProcessed;
            record.FailureReason = string.Empty;

            await _phisSearchService.NavigateBackToSearchPagesAsync();
            return false;
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

        // ── One-at-a-time CSV write guard ─────────────────────────────────────
        // Prevents two concurrent Phase 3 windows from interleaving reads/writes.
        private static readonly SemaphoreSlim _csvLock = new(1, 1);




        private void SaveUploadCsv(List<UploadRecord> processedRecords)
        {
            if (!_csvLock.Wait(TimeSpan.FromSeconds(15)))
            {
                LoggerService.LogWarning(
                    "      ⚠️  CSV save skipped — lock timeout " +
                    "(is Phase 3 running in two windows simultaneously?)");
                return;
            }
            try
            {
                SaveUploadCsvCore(processedRecords);
            }
            finally
            {
                _csvLock.Release();
            }
        }

        private void SaveUploadCsvCore(List<UploadRecord> processedRecords)
        {
            var csvPath = Path.Combine(
                _phase3Config.Input.UploadCsvPath,
                _phase3Config.Input.UploadCsvFileName);

            var tmpPath = csvPath + ".tmp";
            var bakPath = csvPath + ".bak";

            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
                HeaderValidated = null,
                TrimOptions = TrimOptions.Trim,
                PrepareHeaderForMatch = args => args.Header.ToLower().Replace(" ", "")
            };

            // ── Step 1: Read current CSV from disk ────────────────────────────
            List<UploadRecord> allRecords;
            try
            {
                using var reader = new StreamReader(csvPath, Encoding.UTF8);
                using var csvReader = new CsvReader(reader, csvConfig);
                csvReader.Context.RegisterClassMap<UploadRecordMap>();
                allRecords = csvReader.GetRecords<UploadRecord>().ToList();

                LoggerService.LogDebug(
                    $"      📂 CSV read OK — {allRecords.Count} raw row(s) from disk");
            }
            catch (Exception ex)
            {
                // Never write after a failed read — would erase all progress
                LoggerService.LogError(
                    $"      ❌ CSV read failed — save aborted to protect existing data.\n" +
                    $"         Path : {csvPath}\n" +
                    $"         Error: {ex.Message}");
                return;
            }

            // ── Step 2: Deduplicate on-disk rows by DocumentTitle ─────────────
            // Root cause: both GenerateCsv + AppendFileRose can write the same
            // FileRose row. Keep the row with the highest VerifStatus (most progress).
            var grouped = allRecords
                .GroupBy(r => r.DocumentTitle.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var duplicateGroups = grouped.Where(g => g.Count() > 1).ToList();
            if (duplicateGroups.Count > 0)
            {
                LoggerService.LogWarning(
                    $"      ⚠️  {duplicateGroups.Count} duplicate DocumentTitle(s) collapsed " +
                    "(keeping highest VerifStatus):");
                foreach (var g in duplicateGroups)
                    LoggerService.LogWarning(
                        $"         • \"{g.Key}\"  ({g.Count()} rows → kept status " +
                        $"{g.Max(r => (int)r.VerifStatus)})");
            }

            allRecords = grouped
                .Select(g => g.OrderByDescending(r => (int)r.VerifStatus).First())
                .ToList();

            // ── Step 3: Apply in-memory updates ───────────────────────────────
            var updatedLookup = processedRecords
                .GroupBy(r => r.DocumentTitle.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(r => (int)r.VerifStatus).First(),
                    StringComparer.OrdinalIgnoreCase);

            int updatedCount = 0;
            var notFound = new List<string>();

            foreach (var record in allRecords)
            {
                if (!updatedLookup.TryGetValue(record.DocumentTitle.Trim(), out var upd))
                    continue;

                if (record.VerifStatus != upd.VerifStatus ||
                    record.FailureReason != upd.FailureReason)
                {
                    LoggerService.LogDebug(
                        $"      ✏️  [{record.ClientID}] {record.DocumentTitle}: " +
                        $"{record.VerifStatus} → {upd.VerifStatus}" +
                        (string.IsNullOrWhiteSpace(upd.FailureReason)
                            ? string.Empty
                            : $"  ({upd.FailureReason})"));

                    record.VerifStatus = upd.VerifStatus;
                    record.FailureReason = upd.FailureReason;
                    updatedCount++;
                }
            }

            // Warn about processed records missing from the on-disk CSV
            foreach (var key in updatedLookup.Keys)
            {
                if (!allRecords.Any(r =>
                        r.DocumentTitle.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)))
                    notFound.Add(key);
            }
            if (notFound.Count > 0)
            {
                LoggerService.LogWarning(
                    $"      ⚠️  {notFound.Count} processed record(s) not found in on-disk CSV " +
                    "(progress NOT saved for these titles):");
                foreach (var t in notFound)
                    LoggerService.LogWarning($"         • \"{t}\"");
            }

            if (updatedCount == 0 && duplicateGroups.Count == 0)
            {
                LoggerService.LogDebug("      ℹ️  CSV unchanged — skipping write");
                return;
            }

            // ── Step 4: Backup ────────────────────────────────────────────────
            try { File.Copy(csvPath, bakPath, overwrite: true); }
            catch (Exception ex)
            {
                // Non-fatal — .tmp write is still atomic
                LoggerService.LogWarning(
                    $"      ⚠️  Could not create backup ({bakPath}): {ex.Message}");
            }

            // ── Step 5: Write .tmp ────────────────────────────────────────────
            try
            {
                using (var writer = new StreamWriter(tmpPath, false, Encoding.UTF8))
                using (var csvWriter = new CsvWriter(writer,
                           new CsvConfiguration(CultureInfo.InvariantCulture)))
                {
                    csvWriter.Context.RegisterClassMap<UploadRecordMap>();
                    csvWriter.WriteRecords(allRecords);
                }

                // ── Step 6: Row-count validation before committing ────────────
                // A truncated write (disk full, process kill) looks like success
                // without this check.
                int writtenRows;
                try { writtenRows = File.ReadLines(tmpPath).Count() - 1; } // minus header
                catch (Exception ex)
                {
                    LoggerService.LogError(
                        $"      ❌ Cannot verify .tmp row count — aborting commit: {ex.Message}");
                    try { File.Delete(tmpPath); } catch { }
                    return;
                }

                if (writtenRows < allRecords.Count)
                {
                    LoggerService.LogError(
                        $"      ❌ Row-count mismatch — expected {allRecords.Count}, " +
                        $".tmp has {writtenRows}. Aborting commit; backup intact at: {bakPath}");
                    try { File.Delete(tmpPath); } catch { }
                    return;
                }

                // ── Step 7: Atomic swap ───────────────────────────────────────
                File.Move(tmpPath, csvPath, overwrite: true);

                LoggerService.LogInformation(
                    $"      💾 CSV saved — {updatedCount} status update(s), " +
                    $"{allRecords.Count} total row(s)" +
                    (duplicateGroups.Count > 0
                        ? $", {duplicateGroups.Count} duplicate(s) removed"
                        : string.Empty));
            }
            catch (Exception ex)
            {
                LoggerService.LogError(
                    $"      ❌ CSV write failed — original file is intact (backup: {bakPath}).\n" +
                    $"         Error: {ex.Message}");
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
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