using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using ConsentSyncCore.Services.Pdf;
using ConsentSyncCore.Services.Phis;
using CsvProcessing;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using Orchestrator.Phase1;
using Orchestrator.Phase2;
using Orchestrator.Phase3;
using Orchestrator.PrePhase3;
using System.Text;
using static Orchestrator.BulkPdfExtraction;

namespace Orchestrator
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // ✅ Initialize logging FIRST - inside Main method
            LoggerService.Initialize();

            LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║              ConsentSync - Main Program                ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝");

            // Declare PHIS components at program scope
            IWebDriver? driver = null;
            PhisSessionManager? sessionManager = null;
            PhisSearchService? phisSearchService = null;

            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                PrintHeader();

                LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
                LoggerService.LogInformation("║             ConsentSync - Automated System            ║");
                LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝");

                // ✅ Load configuration
                var config = ConfigurationService.GetConfiguration();

                // ══════════════════════════════════════════════════════════
                // 🧪 TEST COMMAND: --download-chrome
                // Usage:  dotnet run --download-chrome
                //         dotnet run --download-chrome --channel Beta
                // ══════════════════════════════════════════════════════════
                if (args.Contains("--download-chrome"))
                {
                    return await RunDownloadChromeTestAsync(args);
                }

                // ✅ Create ALL csv + pdf + phis folders in one shot
                WorkspaceInitializer.EnsureAllFoldersExist();

                // Display where to drop files
                var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
                var csvWs = ConfigurationService.GetCsvWorkspaceConfig();

                LoggerService.LogInformation("📂 Drop your files here:");
                LoggerService.LogInformation($"   CSV input  → {csvWs.GetConsentCsvPath()}");
                LoggerService.LogInformation($"   Bulk PDFs  → {bulkConfig.GetInputBulkPath()}");
                LoggerService.LogInformation($"   Scanned    → {bulkConfig.GetInputScannedPath()}\n");

                // Check for standalone bulk extraction command
                if (args.Contains("--extract-bulk") || args.Contains("-b"))
                {
                    return await BulkPdfExtractionCommand.ExecuteAsync(args);
                }

                if (bulkConfig.Enabled)
                {
                    LoggerService.LogInformation("\n╔════════════════════════════════════════════════════════╗");
                    LoggerService.LogInformation("║           BULK PDF EXTRACTION (Pre-Processing)         ║");
                    LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝");

                    var bulkOrchestrator = new BulkPdfExtractionOrchestrator(config);

                    if (bulkOrchestrator.IsPdfAvailable())
                    {
                        Console.WriteLine("\n💡 Bulk PDF detected - would you like to extract it now?");
                        Console.WriteLine("   This will create individual PDFs for processing.");
                        Console.WriteLine("\n   Press [Y] to extract, [N] to skip...");

                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Y)
                        {
                            var bulkResult = await bulkOrchestrator.RunAsync();

                            if (!bulkResult.Success)
                            {
                                Console.WriteLine("\n⚠️  Bulk extraction had errors - continue anyway?");
                                Console.WriteLine("   Press [Y] to continue, [N] to exit...");

                                var continueKey = Console.ReadKey(true);
                                if (continueKey.Key != ConsoleKey.Y)
                                {
                                    return 1;
                                }
                            }
                        }
                    }
                    else
                    {
                        LoggerService.LogInformation($"\n💡 Bulk PDF extraction enabled but no bulk PDF found");
                        LoggerService.LogInformation($"   Place bulk PDF at: {bulkConfig.BasePdfPath}");
                        LoggerService.LogInformation("   Continuing with normal workflow...");
                    }
                }

                // Get all phase configurations
                var csvConfig = ConfigurationService.GetCsvConfig();
                var phase1Config = ConfigurationService.GetPhase1Config();
                var phase2Config = ConfigurationService.GetPhase2Config();
                var prePhase3Config = ConfigurationService.GetPrePhase3Config();
                var phase3Config = ConfigurationService.GetPhase3Config();

                DisplayConfigurationSummary(csvConfig, phase1Config, phase2Config, phase3Config);

                if (!ConfirmStart())
                {
                    LoggerService.LogInformation("\n👋 Cancelled by user");
                    return 0;
                }

                // ═══════════════════════════════════════════════════════
                // PRE-PHASE: CSV Processing
                // ═══════════════════════════════════════════════════════
                LoggerService.LogInformation("\n" + new string('═', 70));
                LoggerService.LogInformation("📋 PRE-PHASE: CSV Processing");
                LoggerService.LogInformation(new string('═', 70));

                var csvRepo = new StudentCsvRepository(config);

                if (!csvRepo.ProcessedCsvExists())
                {
                    LoggerService.LogInformation("📄 Processing raw CSV...");
                    csvRepo.ProcessRawCsv();
                    LoggerService.LogInformation("✅ CSV processing complete\n");
                }
                else
                {
                    LoggerService.LogInformation("✅ CSV already processed\n");
                }

                csvRepo.PreviewProcessedCsv(3);
                csvRepo.DisplayStatistics();

                // ═══════════════════════════════════════════════════════
                // PHASE 1
                // ═══════════════════════════════════════════════════════
                if (phase1Config.Enabled)
                {
                    LoggerService.LogInformation("\n" + new string('═', 70));
                    LoggerService.LogInformation($"🔍 PHASE 1: {phase1Config.Description}");
                    LoggerService.LogInformation(new string('═', 70));

                    if (!ConfirmPhase("Phase 1"))
                    {
                        LoggerService.LogInformation("⏭️  Phase 1 skipped");
                    }
                    else
                    {
                        var (phase1Result, phase1Driver, phase1SessionMgr, phase1SearchSvc) = await RunPhase1Async(config);

                        driver = phase1Driver;
                        sessionManager = phase1SessionMgr;
                        phisSearchService = phase1SearchSvc;

                        if (phase1Result.HasErrors)
                        {
                            LoggerService.LogError("\n❌ Phase 1 failed with errors. Cannot proceed to Phase 2.");
                            return 1;
                        }

                        if (phase1Result.ManualReviewCount > 0)
                        {
                            LoggerService.LogWarning($"\n⚠️  {phase1Result.ManualReviewCount} students need manual review.");
                            LoggerService.LogInformation("💡 Please review and fix the CSV before proceeding to Phase 2.");

                            if (!ConfirmContinueWithReview())
                            {
                                LoggerService.LogInformation("\n👋 Stopping before Phase 2 for manual review");
                                return 0;
                            }
                        }
                    }
                }
                else
                {
                    LoggerService.LogInformation("\n⏭️  Phase 1 disabled in configuration");
                }

                // ═══════════════════════════════════════════════════════
                // PHASE 2
                // ═══════════════════════════════════════════════════════
                if (phase2Config.Enabled)
                {
                    LoggerService.LogInformation("\n" + new string('═', 70));
                    LoggerService.LogInformation($"📥 PHASE 2: {phase2Config.Description}");
                    LoggerService.LogInformation(new string('═', 70));

                    if (!ConfirmPhase("Phase 2"))
                        LoggerService.LogInformation("⏭️  Phase 2 skipped");
                    else
                        await RunPhase2Async(config);
                }
                else
                {
                    LoggerService.LogInformation("\n⏭️  Phase 2 disabled in configuration");
                }

                // ═══════════════════════════════════════════════════════
                // PRE-PHASE 3
                // ═══════════════════════════════════════════════════════
                if (prePhase3Config.Enabled)
                {
                    LoggerService.LogInformation("\n" + new string('═', 70));
                    LoggerService.LogInformation($"📋 PRE-PHASE 3: {prePhase3Config.Description}");
                    LoggerService.LogInformation(new string('═', 70));

                    if (!ConfirmPhase("Pre-Phase 3"))
                        LoggerService.LogInformation("⏭️  Pre-Phase 3 skipped");
                    else
                        await RunPrePhase3Async(config);
                }
                else
                {
                    LoggerService.LogInformation("\n⏭️  Pre-Phase 3 disabled in configuration");
                }

                // ═══════════════════════════════════════════════════════
                // PHASE 3
                // ═══════════════════════════════════════════════════════
                if (phase3Config.Enabled)
                {
                    LoggerService.LogInformation("\n\n");
                    LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
                    LoggerService.LogInformation("║         STARTING PHASE 3: Upload to PHIS (TEST)        ║");
                    LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝");

                    if (!ConfirmPhase("Phase 3"))
                    {
                        LoggerService.LogInformation("⏭️  Phase 3 skipped");
                    }
                    else
                    {
                        try
                        {
                            if (driver == null || phisSearchService == null || sessionManager == null)
                            {
                                LoggerService.LogWarning("⚠️  PHIS components not initialized. Initializing now...");
                                LoggerService.LogInformation("💡 This will open a browser - please log into PHIS manually");

                                var driverFactory = new ChromeDriverFactory(config);
                                driver = driverFactory.CreateDriver();

                                var resultExtractor = new PhisResultExtractor(config);
                                sessionManager = new PhisSessionManager(driver, config);
                                phisSearchService = new PhisSearchService(driver, config, resultExtractor, sessionManager);

                                LoggerService.LogInformation("\n🔐 Establishing PHIS session...");
                                bool loginSuccess = sessionManager.Login();

                                if (!loginSuccess)
                                {
                                    LoggerService.LogError("❌ Failed to establish PHIS session!");
                                    LoggerService.LogInformation("   💡 Please ensure you can access PHIS and try again");
                                    return 1;
                                }

                                LoggerService.LogInformation("✅ PHIS session established successfully");
                            }
                            else
                            {
                                LoggerService.LogInformation("✅ Reusing PHIS session from Phase 1");
                            }

                            var phase3Orchestrator = new Phase3Orchestrator(config, driver, phisSearchService, sessionManager);
                            var phase3Result = await phase3Orchestrator.RunAsync();

                            if (phase3Result.IsSuccessful)
                                LoggerService.LogInformation("\n✅ Phase 3 test completed successfully!");
                            else
                                LoggerService.LogWarning("\n⚠️  Phase 3 test completed with errors");
                        }
                        catch (Exception ex)
                        {
                            LoggerService.LogError($"\n❌ Phase 3 failed: {ex.Message}", ex);
                        }
                    }
                }
                else
                {
                    LoggerService.LogInformation("\n⏭️  Phase 3 disabled in configuration");
                }

                PrintCompletionSummary();
                return 0;
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"\n❌ FATAL ERROR: {ex.Message}", ex);
                return 1;
            }
            finally
            {
                if (driver != null)
                {
                    try
                    {
                        LoggerService.LogInformation("\n🔄 Closing browser...");
                        driver.Quit();
                        driver.Dispose();
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogWarning($"⚠️  Error closing browser: {ex.Message}");
                    }
                }

                LoggerService.Dispose();
                Console.WriteLine("\n\nPress any key to exit...");
                Console.ReadKey();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 🧪 TEST: Portable Chrome download
        // Usage:
        //   dotnet run --download-chrome
        //   dotnet run --download-chrome --channel Beta
        // ══════════════════════════════════════════════════════════════
        static async Task<int> RunDownloadChromeTestAsync(string[] args)
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════════╗");
            Console.WriteLine("║        🧪 TEST: Download Portable Chrome               ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

            var chromeConfig = ConfigurationService.GetChromeDriverConfig();

            // Allow --channel override from command line: --channel Beta
            var channelOverride = GetArg(args, "--channel");
            if (!string.IsNullOrWhiteSpace(channelOverride))
            {
                chromeConfig.PortableChromeChannel = channelOverride;
                Console.WriteLine($"   ℹ️  Channel overridden via args: {channelOverride}");
            }

            Console.WriteLine($"   Channel        : {chromeConfig.PortableChromeChannel}");
            Console.WriteLine($"   Chrome  → {chromeConfig.PortableChromeExtractTo}");
            Console.WriteLine($"   Driver  → {chromeConfig.ChromeDriverExtractTo}");
            Console.WriteLine($"   Versions URL   : {chromeConfig.PortableChromeVersionsJsonUrl}");
            Console.WriteLine();

            var factory = new ChromeDriverFactory();

            var cts = new CancellationTokenSource();

            // Allow Ctrl+C to cancel the download
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n⚠️  Cancelling download...");
                cts.Cancel();
            };

            bool success = await factory.DownloadPortableChromeAsync(
                progress: msg => Console.WriteLine(msg),
                cancellationToken: cts.Token);

            if (success)
            {
                Console.WriteLine("\n╔════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  ✅ Download succeeded!  Next steps:                   ║");
                Console.WriteLine("╠════════════════════════════════════════════════════════╣");
                Console.WriteLine($"║  1. Open appsettings.json                              ║");
                Console.WriteLine($"║  2. Set  \"UsePortableChrome\": true                     ║");
                Console.WriteLine($"║  3. Verify PortableChromePath points to chrome.exe     ║");
                Console.WriteLine($"║     (logged above as '✅ Portable Chrome ready')       ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════╝");
            }
            else
            {
                Console.WriteLine("\n❌ Download failed or was cancelled. See messages above.");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
            return success ? 0 : 1;
        }

        static string? GetArg(string[] args, string key)
        {
           
            var index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        #region Phase Execution

        static async Task<(Phase1Result result, IWebDriver? driver, PhisSessionManager? sessionMgr, PhisSearchService? searchSvc)>
            RunPhase1Async(IConfiguration config)
        {
            try
            {
                using var orchestrator = new Phase1Orchestrator(config);
                var result = await orchestrator.RunAsync();

                var driver = orchestrator.GetDriver();
                var sessionMgr = orchestrator.GetSessionManager();
                var searchSvc = orchestrator.GetSearchService();

                return (result, driver, sessionMgr, searchSvc);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Phase 1 error: {ex.Message}", ex);
                return (new Phase1Result { HasErrors = true }, null, null, null);
            }
        }

        static async Task RunPhase2Async(IConfiguration config)
        {
            try
            {
                var orchestrator = new Phase2Orchestrator(config);
                var result = await orchestrator.RunAsync();

                if (result.HasErrors)
                    LoggerService.LogError("\n❌ Phase 2 completed with errors");
                else if (result.FailedToMatch > 0)
                    LoggerService.LogWarning($"\n⚠️  Phase 2 completed - {result.FailedToMatch} files need manual review");
                else
                    LoggerService.LogInformation("\n✅ Phase 2 completed successfully!");
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Phase 2 error: {ex.Message}", ex);
            }
        }

        static async Task RunPrePhase3Async(IConfiguration config)
        {
            try
            {
                var orchestrator = new PrePhase3Orchestrator(config);
                var result = await orchestrator.RunAsync();

                if (result.HasErrors)
                    LoggerService.LogError("\n❌ Pre-Phase 3 completed with errors");
                else if (result.SkippedMissingPdf > 0)
                    LoggerService.LogWarning($"\n⚠️  Pre-Phase 3 completed - {result.SkippedMissingPdf} PDFs missing");
                else
                    LoggerService.LogInformation("\n✅ Pre-Phase 3 completed successfully!");
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Pre-Phase 3 error: {ex.Message}", ex);
            }
        }

        #endregion Phase Execution

        #region UI Helpers

        static void PrintHeader()
        {
            if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
            {
                try { Console.Clear(); }
                catch (IOException) { Console.WriteLine("\n\n\n"); }
            }
            else
            {
                Console.WriteLine("\n\n\n");
            }

            LoggerService.LogInformation("╔════════════════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║                    ConsentSync Orchestrator                        ║");
            LoggerService.LogInformation("║                   Complete 3-Phase Workflow                        ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════════════════╝");
            LoggerService.LogInformation("");
        }

        static void DisplayConfigurationSummary(
            CsvProcessingConfig csvConfig,
            Phase1Config phase1Config,
            Phase2Config phase2Config,
            Phase3Config phase3Config)
        {
            LoggerService.LogInformation("📋 Configuration Summary:");
            LoggerService.LogInformation(new string('─', 70));
            LoggerService.LogInformation($"CSV Input:  {Path.GetFileName(csvConfig.InputCsvFileName)}");
            LoggerService.LogInformation($"CSV Output: {Path.GetFileName(csvConfig.OutputCsvFileName)}");
            LoggerService.LogInformation("");
            LoggerService.LogInformation($"Phase 1: {(phase1Config.Enabled ? "✅ Enabled" : "❌ Disabled")} - {phase1Config.Description}");
            LoggerService.LogInformation($"Phase 2: {(phase2Config.Enabled ? "✅ Enabled" : "❌ Disabled")} - {phase2Config.Description}");
            LoggerService.LogInformation($"Phase 3: {(phase3Config.Enabled ? "✅ Enabled" : "❌ Disabled")} - {phase3Config.Description}");
            LoggerService.LogInformation(new string('─', 70));
        }

        static bool ConfirmStart()
        {
            Console.WriteLine("\n🚀 Ready to start ConsentSync workflow");
            Console.WriteLine("Press [Y] to continue, [N] to cancel...");
            var key = Console.ReadKey(true);
            return key.Key == ConsoleKey.Y;
        }

        static bool ConfirmPhase(string phaseName)
        {
            Console.WriteLine($"\n▶️  Start {phaseName}?");
            Console.WriteLine("Press [Y] to continue, [N] to skip, [Q] to quit...");
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Q)
                Environment.Exit(0);

            return key.Key == ConsoleKey.Y;
        }

        static bool ConfirmContinueWithReview()
        {
            Console.WriteLine("\n⚠️  Continue to Phase 2 with unresolved manual review items?");
            Console.WriteLine("Press [Y] to continue anyway, [N] to stop and fix CSV first...");
            var key = Console.ReadKey(true);
            return key.Key == ConsoleKey.Y;
        }

        static void PrintCompletionSummary()
        {
            LoggerService.LogInformation("\n" + new string('═', 70));
            LoggerService.LogInformation("✅ ConsentSync Workflow Complete!");
            LoggerService.LogInformation(new string('═', 70));
            LoggerService.LogInformation("Next steps:");
            LoggerService.LogInformation("  1. Review any manual review items in the CSV");
            LoggerService.LogInformation("  2. Verify uploaded documents in PHIS");
            LoggerService.LogInformation("  3. Archive processed files");
            LoggerService.LogInformation(new string('═', 70));
        }

        #endregion UI Helpers
    }
}