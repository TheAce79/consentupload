

using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Phis;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using Orchestrator.Phase2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            LoggerService.LogInformation("║     TEST MODE: Set Context + Navigate to Consents      ║");
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

                // Step 2: Group by ClientID (multiple PDFs per client)
                LoggerService.LogInformation("\n📋 Step 2: Grouping records by Client ID...");
                var clientGroups = uploadRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.ClientID))
                    .GroupBy(r => r.ClientID)
                    .ToList();

                var uniqueClients = clientGroups.Count;
                result.UploadReadyRecords = uniqueClients;

                LoggerService.LogInformation($"   ✅ Found {uniqueClients} unique clients");
                LoggerService.LogInformation($"   📄 Total documents to upload: {uploadRecords.Count}");

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

                // Step 4: Process each client
                LoggerService.LogInformation($"\n📋 Step 4: Processing {uniqueClients} clients...");
                LoggerService.LogInformation($"   🧪 TEST MODE: Set context + Navigate to Immunization Service\n");

                int successCount = 0;
                int failureCount = 0;

                foreach (var clientGroup in clientGroups)
                {
                    var clientId = clientGroup.Key;
                    var clientRecords = clientGroup.ToList();
                    var firstRecord = clientRecords.First();

                    LoggerService.LogInformation($"\n{new string('─', 60)}");
                    LoggerService.LogInformation($"Client: {firstRecord.FirstName} {firstRecord.LastName}");
                    LoggerService.LogInformation($"Client ID: {clientId}");
                    LoggerService.LogInformation($"Documents: {clientRecords.Count}");

                    // Display documents for this client
                    foreach (var record in clientRecords)
                    {
                        LoggerService.LogInformation($"   📄 {record.DocumentTitle} ({record.Description})");
                    }

                    try
                    {
                        // Step A: Search and set in context
                        LoggerService.LogInformation($"\n   🔍 Searching and setting in context...");
                        bool contextSet = await _phisSearchService.SearchByClientIdAndSetInContextAsync(clientId);

                        if (!contextSet)
                        {
                            LoggerService.LogInformation($"   ❌ FAILED: Could not set client in context");
                            failureCount++;
                            result.FailedUploads++;
                            result.ErrorMessages.Add($"{clientId}: Failed to set in context");
                            continue; // Skip to next client
                        }

                        LoggerService.LogInformation($"   ✅ SUCCESS: Client set in context");

                        // Step B: Navigate to Immunization Service page
                        LoggerService.LogInformation($"\n   🧭 Navigating to Immunization Service...");
                        bool navigated = await _phisSearchService.NavigateToImmunizationServiceAsync();

                        if (!navigated)
                        {
                            LoggerService.LogInformation($"   ⚠️  Direct navigation failed, trying menu navigation...");
                            navigated = await _phisSearchService.NavigateToImmunizationServiceViaMenuAsync();
                        }

                        if (!navigated)
                        {
                            LoggerService.LogInformation($"   ❌ FAILED: Could not navigate to Immunization Service");
                            failureCount++;
                            result.FailedUploads++;
                            result.ErrorMessages.Add($"{clientId}: Failed to navigate to Immunization Service");
                            continue;
                        }

                        LoggerService.LogInformation($"   ✅ SUCCESS: On Immunization Service page");

                        successCount++;
                        result.SuccessfulUploads++;

                        // TODO Phase 3.2: Add actual PDF upload here
                        // foreach (var record in clientRecords)
                        // {
                        //     await UploadPdfAsync(record);
                        // }
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogInformation($"   ❌ ERROR: {ex.Message}");
                        failureCount++;
                        result.FailedUploads++;
                        result.ErrorMessages.Add($"{clientId} ({firstRecord.FirstName} {firstRecord.LastName}): {ex.Message}");
                    }

                    // Session check every 10 clients
                    if ((successCount + failureCount) % 10 == 0 && (successCount + failureCount) > 0)
                    {
                        LoggerService.LogInformation($"\n   🔄 Session check after {successCount + failureCount} clients...");
                        if (!_sessionManager.EnsureSessionValid())
                        {
                            LoggerService.LogInformation($"   ⚠️  Session expired! Please refresh");
                            result.HasErrors = true;
                            break;
                        }
                        LoggerService.LogInformation($"   ✅ Session still active");
                    }

                    // Small delay between clients
                    await Task.Delay(_phase3Config.Upload.DelayBetweenUploadsMs);
                }

                // Step 5: Display summary
                DisplaySummary(result, successCount, failureCount, uploadRecords.Count);

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



        private void DisplaySummary(Phase3Result result, int successCount, int failureCount, int totalDocuments)
        {
            LoggerService.LogInformation("\n" + new string('═', 60));
            LoggerService.LogInformation("📊 PHASE 3 TEST RUN COMPLETE - Final Summary");
            LoggerService.LogInformation(new string('═', 60));
            LoggerService.LogInformation($"Total records in CSV: {result.TotalRecords}");
            LoggerService.LogInformation($"Unique clients: {result.UploadReadyRecords}");
            LoggerService.LogInformation($"Total documents: {totalDocuments}");
            LoggerService.LogInformation($"\n🎯 Test Results:");
            LoggerService.LogInformation($"   ✅ Successfully navigated: {successCount}");
            LoggerService.LogInformation($"   ❌ Failed: {failureCount}");

            if (successCount > 0)
            {
                double successRate = (double)successCount / result.UploadReadyRecords * 100;
                LoggerService.LogInformation($"   📈 Success rate: {successRate:F1}%");
            }

            LoggerService.LogInformation(new string('═', 60));

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
            if (successCount == result.UploadReadyRecords)
            {
                LoggerService.LogInformation($"   ✅ All clients successfully navigated to Immunization Service!");
                LoggerService.LogInformation($"   📝 Ready to implement PDF upload functionality");
            }
            else
            {
                LoggerService.LogInformation($"   ⚠️  Review errors above before proceeding");
                LoggerService.LogInformation($"   🔧 Fix any session, search, or navigation issues");
            }
        }



    }
}




