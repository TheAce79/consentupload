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
            LoggerService.LogInformation("║         Process Each Document Row-by-Row               ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝\n");

            var result = new Phase3Result();

            try
            {
                // Step 1: Load Upload_to_PHIS.csv
                LoggerService.LogInformation("📋 Step 1: Loading Upload_to_PHIS.csv...");
                var uploadRecords = LoadUploadCsv();
                result.TotalRecords = uploadRecords.Count;

                LoggerService.LogInformation($"   ✅ Loaded {uploadRecords.Count} upload records");

                if (uploadRecords.Count == 0)
                {
                    LoggerService.LogInformation("\n⚠️  No records found in Upload_to_PHIS.csv!");
                    LoggerService.LogInformation("   💡 Please run Pre-Phase 3 first to generate this file");
                    return result;
                }

                // Step 2: Filter records to process (skip VerifStatus = Success)
                LoggerService.LogInformation("\n📋 Step 2: Filtering records to process...");

                var recordsToProcess = uploadRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.ClientID) && r.VerifStatus != UploadVerificationStatus.Success)
                    .ToList();

                var alreadyVerified = uploadRecords.Count - recordsToProcess.Count;

                LoggerService.LogInformation($"   ✅ Records to process: {recordsToProcess.Count}");
                LoggerService.LogInformation($"   ⏭️  Already verified (Success): {alreadyVerified}");

                if (recordsToProcess.Count == 0)
                {
                    LoggerService.LogInformation("\n✅ All records already verified!");
                    return result;
                }

                // Step 3: Verify session is active
                LoggerService.LogInformation("\n📋 Step 3: Verifying PHIS session...");
                if (!_sessionManager.EnsureSessionValid())
                {
                    LoggerService.LogInformation("   ❌ PHIS session is not valid!");
                    LoggerService.LogInformation("   💡 Please ensure you are logged into PHIS");
                    result.HasErrors = true;
                    return result;
                }
                LoggerService.LogInformation("   ✅ PHIS session is active");

                // Step 4: Process each record ONE BY ONE (row-by-row)
                LoggerService.LogInformation($"\n📋 Step 4: Processing {recordsToProcess.Count} documents (row-by-row)...");
                LoggerService.LogInformation($"   🔄 Each document will be processed from start to finish\n");

                int successCount = 0;
                int skipCount = 0;
                int failureCount = 0;
                int processedCount = 0;

                foreach (var record in recordsToProcess)
                {
                    processedCount++;

                    LoggerService.LogInformation($"\n{new string('═', 70)}");
                    LoggerService.LogInformation($"📄 DOCUMENT {processedCount}/{recordsToProcess.Count}");
                    LoggerService.LogInformation($"{new string('═', 70)}");
                    LoggerService.LogInformation($"Client: {record.FirstName} {record.LastName}");
                    LoggerService.LogInformation($"Client ID: {record.ClientID}");
                    LoggerService.LogInformation($"Document: {record.DocumentTitle}");
                    LoggerService.LogInformation($"Description: {record.Description}");
                    LoggerService.LogInformation($"Antigen: {record.PhisAntigen}");
                    LoggerService.LogInformation($"Current VerifStatus: {record.VerifStatus}");
                    LoggerService.LogInformation($"{new string('─', 70)}");

                    try
                    {
                        // Validate required fields
                        if (string.IsNullOrWhiteSpace(record.PhisAntigen))
                        {
                            LoggerService.LogInformation($"\n⚠️  WARNING: PhisAntigen is empty");
                            record.VerifStatus = UploadVerificationStatus.NeedsManualReview;
                            result.ErrorMessages.Add($"{record.ClientID} - {record.Description}: PhisAntigen is empty");
                            failureCount++;
                            SaveUploadCsv(uploadRecords);
                            continue;
                        }

                        // STEP A: Search and set client in context
                        LoggerService.LogInformation($"\n🔍 STEP A: Searching and setting client in context...");
                        LoggerService.LogInformation($"   Client ID: {record.ClientID}");

                        bool contextSet = await _phisSearchService.SearchByClientIdAndSetInContextAsync(record.ClientID);

                        if (!contextSet)
                        {
                            LoggerService.LogInformation($"   ❌ FAILED: Could not set client in context");
                            record.VerifStatus = UploadVerificationStatus.NeedsManualReview;
                            result.ErrorMessages.Add($"{record.ClientID}: Failed to set in context");
                            failureCount++;
                            SaveUploadCsv(uploadRecords);
                            continue;
                        }

                        LoggerService.LogInformation($"   ✅ SUCCESS: Client set in context");

                        // STEP B: Navigate to Immunization Service page
                        LoggerService.LogInformation($"\n🧭 STEP B: Navigating to Immunization Service...");

                        bool navigated = await _phisSearchService.NavigateToImmunizationServiceAsync();

                        if (!navigated)
                        {
                            LoggerService.LogInformation($"   ⚠️  Direct navigation failed, trying menu navigation...");
                            navigated = await _phisSearchService.NavigateToImmunizationServiceViaMenuAsync();
                        }

                        if (!navigated)
                        {
                            LoggerService.LogInformation($"   ❌ FAILED: Could not navigate to Immunization Service");
                            record.VerifStatus = UploadVerificationStatus.NeedsManualReview;
                            result.ErrorMessages.Add($"{record.ClientID}: Failed to navigate to Immunization Service");
                            failureCount++;
                            SaveUploadCsv(uploadRecords);
                            continue;
                        }

                        LoggerService.LogInformation($"   ✅ SUCCESS: On Immunization Service page");

                        // STEP C: Select the consent directive matching the antigen
                        LoggerService.LogInformation($"\n🎯 STEP C: Selecting consent directive...");
                        LoggerService.LogInformation($"   Antigen: '{record.PhisAntigen}'");

                        bool directiveSelected = await _phisSearchService.SelectConsentDirectiveByAntigenAsync(record.PhisAntigen);

                        if (!directiveSelected)
                        {
                            LoggerService.LogInformation($"   ❌ FAILED: Could not select consent directive for '{record.PhisAntigen}'");
                            record.VerifStatus = UploadVerificationStatus.NeedsManualReview;
                            result.ErrorMessages.Add($"{record.ClientID} - {record.Description}: Failed to select consent directive");
                            failureCount++;
                            SaveUploadCsv(uploadRecords);
                            continue;
                        }

                        LoggerService.LogInformation($"   ✅ SUCCESS: Consent directive selected");

                        // STEP D: Click Documents button
                        LoggerService.LogInformation($"\n📄 STEP D: Opening Documents page...");

                        bool documentsPageOpened = await _phisSearchService.ClickDocumentsButtonAsync();

                        if (!documentsPageOpened)
                        {
                            LoggerService.LogInformation($"   ❌ FAILED: Could not open Documents page");
                            record.VerifStatus = UploadVerificationStatus.NeedsManualReview;
                            result.ErrorMessages.Add($"{record.ClientID} - {record.Description}: Failed to open Documents page");
                            failureCount++;
                            SaveUploadCsv(uploadRecords);
                            continue;
                        }

                        LoggerService.LogInformation($"   ✅ SUCCESS: Documents page opened");

                        // STEP E: Check if document already exists
                        LoggerService.LogInformation($"\n🔍 STEP E: Checking if document exists...");
                        LoggerService.LogInformation($"   Searching for: '{record.DocumentTitle}'");

                        bool documentExists = await _phisSearchService.CheckIfDocumentExistsAsync(record.DocumentTitle);

                        if (documentExists)
                        {
                            LoggerService.LogInformation($"   ✅ DOCUMENT ALREADY EXISTS in PHIS!");
                            LoggerService.LogInformation($"   ℹ️  Marking as verified (Success)");
                            record.VerifStatus = UploadVerificationStatus.Success;
                            skipCount++;
                            result.SuccessfulUploads++;

                            // Navigate back for next document
                            await _phisSearchService.NavigateBackToSearchPagesAsync();
                            SaveUploadCsv(uploadRecords);

                            LoggerService.LogInformation($"\n✅ DOCUMENT VERIFIED (already exists)");
                            continue;
                        }

                        LoggerService.LogInformation($"   ℹ️  Document does NOT exist - upload needed");

                        // STEP F: Upload document (TODO - not yet implemented)
                        LoggerService.LogInformation($"\n📤 STEP F: Uploading document...");
                        LoggerService.LogInformation($"   ⚠️  Upload functionality not yet implemented");
                        LoggerService.LogInformation($"   💡 This will be implemented in the next phase");

                        // For now, mark as not processed and navigate back
                        record.VerifStatus = UploadVerificationStatus.NotProcessed;

                        // Navigate back for next document
                        await _phisSearchService.NavigateBackToSearchPagesAsync();
                        SaveUploadCsv(uploadRecords);

                        successCount++;

                        LoggerService.LogInformation($"\n✅ DOCUMENT PROCESSED (ready for upload)");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogInformation($"\n❌ ERROR: {ex.Message}");
                        LoggerService.LogInformation($"   Stack trace: {ex.StackTrace}");
                        record.VerifStatus = UploadVerificationStatus.NeedsManualReview;
                        result.ErrorMessages.Add($"{record.ClientID} - {record.Description}: {ex.Message}");
                        failureCount++;

                        // Try to recover by navigating back
                        try
                        {
                            await _phisSearchService.NavigateBackToSearchPagesAsync();
                        }
                        catch { }

                        SaveUploadCsv(uploadRecords);
                    }

                    // Session check every 10 documents
                    if (processedCount % 10 == 0)
                    {
                        LoggerService.LogInformation($"\n🔄 Session check after {processedCount} documents...");
                        if (!_sessionManager.EnsureSessionValid())
                        {
                            LoggerService.LogInformation($"   ⚠️  Session expired! Please refresh");
                            result.HasErrors = true;
                            break;
                        }
                        LoggerService.LogInformation($"   ✅ Session still active");
                    }

                    // Small delay between documents
                    await Task.Delay(_phase3Config.Upload.DelayBetweenUploadsMs);
                }

                // Step 5: Display summary
                DisplaySummary(result, successCount, skipCount, failureCount, uploadRecords.Count, alreadyVerified);

                return result;
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"\n❌ FATAL ERROR: {ex.Message}");
                LoggerService.LogInformation($"Stack trace: {ex.StackTrace}");
                result.HasErrors = true;
                return result;
            }
        }

        /// <summary>
        /// Load Upload_to_PHIS.csv
        /// </summary>
        private List<UploadRecord> LoadUploadCsv()
        {
            var csvPath = Path.Combine(
                _phase3Config.Input.UploadCsvPath,
                _phase3Config.Input.UploadCsvFileName);

            if (!File.Exists(csvPath))
            {
                throw new FileNotFoundException($"Upload CSV not found: {csvPath}");
            }

            LoggerService.LogInformation($"   📂 Reading: {csvPath}");

            using var reader = new StreamReader(csvPath, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));

            csv.Context.RegisterClassMap<UploadRecordMap>();
            return csv.GetRecords<UploadRecord>().ToList();
        }

        /// <summary>
        /// Save Upload_to_PHIS.csv with updated VerifStatus values
        /// </summary>
        private void SaveUploadCsv(List<UploadRecord> uploadRecords)
        {
            try
            {
                var csvPath = Path.Combine(
                    _phase3Config.Input.UploadCsvPath,
                    _phase3Config.Input.UploadCsvFileName);

                using var writer = new StreamWriter(csvPath, false, Encoding.UTF8);
                using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

                csv.Context.RegisterClassMap<UploadRecordMap>();
                csv.WriteRecords(uploadRecords);
            }
            catch (Exception ex)
            {
                LoggerService.LogWarning($"      ⚠️  Could not save CSV: {ex.Message}");
            }
        }

        private void DisplaySummary(Phase3Result result, int successCount, int skipCount, int failureCount,
            int totalRecords, int alreadyVerified)
        {
            LoggerService.LogInformation("\n" + new string('═', 70));
            LoggerService.LogInformation("📊 PHASE 3 COMPLETE - Final Summary");
            LoggerService.LogInformation(new string('═', 70));
            LoggerService.LogInformation($"Total records in CSV: {totalRecords}");
            LoggerService.LogInformation($"Already verified (skipped): {alreadyVerified}");
            LoggerService.LogInformation($"Processed in this run: {successCount + skipCount + failureCount}");
            LoggerService.LogInformation($"\n🎯 Results:");
            LoggerService.LogInformation($"   ✅ Successfully processed: {successCount}");
            LoggerService.LogInformation($"   ⏭️  Already exist (skipped): {skipCount}");
            LoggerService.LogInformation($"   ❌ Failed: {failureCount}");

            if (successCount + skipCount > 0)
            {
                var total = successCount + skipCount + failureCount;
                double successRate = total > 0 ? (double)(successCount + skipCount) / total * 100 : 0;
                LoggerService.LogInformation($"   📈 Success rate: {successRate:F1}%");
            }

            LoggerService.LogInformation(new string('═', 70));

            if (result.ErrorMessages.Count > 0)
            {
                LoggerService.LogInformation($"\n⚠️  Errors ({result.ErrorMessages.Count}):");
                foreach (var error in result.ErrorMessages.Take(10))
                {
                    LoggerService.LogInformation($"   - {error}");
                }
                if (result.ErrorMessages.Count > 10)
                {
                    LoggerService.LogInformation($"   ... and {result.ErrorMessages.Count - 10} more");
                }
            }

            LoggerService.LogInformation($"\n💡 Next Steps:");

            var remainingToProcess = totalRecords - alreadyVerified - skipCount;

            if (remainingToProcess == 0 && failureCount == 0)
            {
                LoggerService.LogInformation($"   ✅ All documents verified!");
                LoggerService.LogInformation($"   📝 Ready to implement upload functionality");
            }
            else if (failureCount > 0)
            {
                LoggerService.LogInformation($"   ⚠️  {failureCount} document(s) failed - review errors above");
                LoggerService.LogInformation($"   🔧 Fix issues and re-run Phase 3");
            }
            else if (remainingToProcess > 0)
            {
                LoggerService.LogInformation($"   ℹ️  {remainingToProcess} document(s) still need upload");
                LoggerService.LogInformation($"   📤 Implement upload functionality and re-run");
            }

            LoggerService.LogInformation($"\n📁 CSV updated with VerifStatus values");
            LoggerService.LogInformation($"   Path: {_phase3Config.Input.UploadCsvPath}\\{_phase3Config.Input.UploadCsvFileName}");
        }
    }
}