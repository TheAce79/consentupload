using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Phis;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
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
        }



        public async Task<Phase3Result> RunAsync()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════╗");
            Console.WriteLine("║       ConsentSync - Phase 3: Upload to PHIS            ║");
            Console.WriteLine("║              TEST MODE: Set In Context Only            ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

            var result = new Phase3Result();

            try
            {
                // Step 1: Load Upload_to_PHIS.csv
                Console.WriteLine("📋 Step 1: Loading Upload_to_PHIS.csv...");
                var uploadRecords = LoadUploadCsv();
                result.TotalRecords = uploadRecords.Count;

                Console.WriteLine($"   ✅ Loaded {uploadRecords.Count} upload records");

                if (uploadRecords.Count == 0)
                {
                    Console.WriteLine("\n⚠️  No records found in Upload_to_PHIS.csv!");
                    Console.WriteLine("   💡 Please run Pre-Phase 3 first to generate this file");
                    return result;
                }

                // Step 2: Group by ClientID (multiple PDFs per client)
                Console.WriteLine("\n📋 Step 2: Grouping records by Client ID...");
                var clientGroups = uploadRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.ClientID))
                    .GroupBy(r => r.ClientID)
                    .ToList();

                var uniqueClients = clientGroups.Count;
                result.UploadReadyRecords = uniqueClients;

                Console.WriteLine($"   ✅ Found {uniqueClients} unique clients");
                Console.WriteLine($"   📄 Total documents to upload: {uploadRecords.Count}");

                // Step 3: Verify session is active
                Console.WriteLine("\n📋 Step 3: Verifying PHIS session...");
                if (!_sessionManager.EnsureSessionValid())
                {
                    Console.WriteLine("   ❌ PHIS session is not valid!");
                    Console.WriteLine("   💡 Please ensure you are logged into PHIS");
                    result.HasErrors = true;
                    return result;
                }
                Console.WriteLine("   ✅ PHIS session is active");

                // Step 4: Process each client
                Console.WriteLine($"\n📋 Step 4: Processing {uniqueClients} clients...");
                Console.WriteLine($"   🧪 TEST MODE: Only setting clients in context\n");

                int successCount = 0;
                int failureCount = 0;

                foreach (var clientGroup in clientGroups)
                {
                    var clientId = clientGroup.Key;
                    var clientRecords = clientGroup.ToList();
                    var firstRecord = clientRecords.First();

                    Console.WriteLine($"\n{new string('─', 60)}");
                    Console.WriteLine($"Client: {firstRecord.FirstName} {firstRecord.LastName}");
                    Console.WriteLine($"Client ID: {clientId}");
                    Console.WriteLine($"Documents: {clientRecords.Count}");

                    // Display documents for this client
                    foreach (var record in clientRecords)
                    {
                        Console.WriteLine($"   📄 {record.DocumentTitle} ({record.Description})");
                    }

                    try
                    {
                        // TEST: Only search and set in context (no upload yet)
                        Console.WriteLine($"\n   🔍 Searching and setting in context...");
                        bool contextSet = await _phisSearchService.SearchByClientIdAndSetInContextAsync(clientId);

                        if (contextSet)
                        {
                            Console.WriteLine($"   ✅ SUCCESS: Client set in context");
                            successCount++;
                            result.SuccessfulUploads++;

                            // TODO Phase 3.2: Add actual PDF upload here
                            // foreach (var record in clientRecords)
                            // {
                            //     await UploadPdfAsync(record);
                            // }
                        }
                        else
                        {
                            Console.WriteLine($"   ❌ FAILED: Could not set client in context");
                            failureCount++;
                            result.FailedUploads++;
                            result.ErrorMessages.Add($"{clientId}: Failed to set in context");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ❌ ERROR: {ex.Message}");
                        failureCount++;
                        result.FailedUploads++;
                        result.ErrorMessages.Add($"{clientId} ({firstRecord.FirstName} {firstRecord.LastName}): {ex.Message}");
                    }

                    // Session check every 10 clients
                    if ((successCount + failureCount) % 10 == 0 && (successCount + failureCount) > 0)
                    {
                        Console.WriteLine($"\n   🔄 Session check after {successCount + failureCount} clients...");
                        if (!_sessionManager.EnsureSessionValid())
                        {
                            Console.WriteLine($"   ⚠️  Session expired! Please refresh");
                            result.HasErrors = true;
                            break;
                        }
                        Console.WriteLine($"   ✅ Session still active");
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
                Console.WriteLine($"\n❌ FATAL ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
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

            Console.WriteLine($"   📂 Reading: {csvPath}");

            using var reader = new StreamReader(csvPath, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));

            csv.Context.RegisterClassMap<UploadRecordMap>();
            return csv.GetRecords<UploadRecord>().ToList();
        }



        private void DisplaySummary(Phase3Result result, int successCount, int failureCount, int totalDocuments)
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("📊 PHASE 3 TEST RUN COMPLETE - Final Summary");
            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"Total records in CSV: {result.TotalRecords}");
            Console.WriteLine($"Unique clients: {result.UploadReadyRecords}");
            Console.WriteLine($"Total documents: {totalDocuments}");
            Console.WriteLine($"\n🎯 Test Results:");
            Console.WriteLine($"   ✅ Successfully set in context: {successCount}");
            Console.WriteLine($"   ❌ Failed to set in context: {failureCount}");

            if (successCount > 0)
            {
                double successRate = (double)successCount / result.UploadReadyRecords * 100;
                Console.WriteLine($"   📈 Success rate: {successRate:F1}%");
            }

            Console.WriteLine(new string('═', 60));

            if (result.ErrorMessages.Count > 0)
            {
                Console.WriteLine($"\n⚠️  Errors ({result.ErrorMessages.Count}):");
                foreach (var error in result.ErrorMessages.Take(10))
                {
                    Console.WriteLine($"   - {error}");
                }
                if (result.ErrorMessages.Count > 10)
                {
                    Console.WriteLine($"   ... and {result.ErrorMessages.Count - 10} more");
                }
            }

            Console.WriteLine($"\n💡 Next Steps:");
            if (successCount == result.UploadReadyRecords)
            {
                Console.WriteLine($"   ✅ All clients successfully set in context!");
                Console.WriteLine($"   📝 Ready to implement PDF upload functionality");
            }
            else
            {
                Console.WriteLine($"   ⚠️  Review errors above before proceeding");
                Console.WriteLine($"   🔧 Fix any session or search issues");
            }
        }



    }
}
