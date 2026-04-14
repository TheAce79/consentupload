using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Browser;
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

           
          

           

            // Declare PHIS components at program scope
            IWebDriver? driver = null;
            PhisSessionManager? sessionManager = null;
            PhisSearchService? phisSearchService = null;

            try
            {

                // Register encoding provider for legacy encodings (required for CSV processing)
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                PrintHeader();



                Console.WriteLine("╔════════════════════════════════════════════════════════╗");
                Console.WriteLine("║             ConsentSync - Automated System            ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════╝");

                // ✅ Load configuration
                var config = ConfigurationService.GetConfiguration();
                var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();

                // ✅ Initialize folder structure by creating BulkPdfExtractor instance
                // (constructor automatically creates folders and README files)
                Console.WriteLine($"\n📂 Initializing folder structure...");
                try
                {
                    var _ = new BulkPdfExtractor(config); // This triggers EnsureDirectoriesExist() and CreateReadmeFiles()
                    Console.WriteLine($"   ✅ Folder structure created/verified");
                    Console.WriteLine($"   ✅ README files created");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️  Warning: Could not initialize folder structure: {ex.Message}");
                }

                // ✅ Display folder locations
                Console.WriteLine($"\n📂 Working Directory: {bulkConfig.BasePdfPath}");
                Console.WriteLine($"   Please place your PDF files in the appropriate folders:");
                Console.WriteLine($"   - Bulk downloads → 1_Input_Bulk/");
                Console.WriteLine($"   - Scanned forms  → 2_Input_Scanned/");
                Console.WriteLine($"\n   ℹ️  Check README.txt files in each folder for details\n");


                // Check for standalone bulk extraction command
                if (args.Contains("--extract-bulk") || args.Contains("-b"))
                {
                    return await BulkPdfExtractionCommand.ExecuteAsync(args);
                }



                
                if (bulkConfig.Enabled)
                {
                    Console.WriteLine("\n╔════════════════════════════════════════════════════════╗");
                    Console.WriteLine("║           BULK PDF EXTRACTION (Pre-Processing)         ║");
                    Console.WriteLine("╚════════════════════════════════════════════════════════╝");

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
                        Console.WriteLine($"\n💡 Bulk PDF extraction enabled but no bulk PDF found");
                        Console.WriteLine($"   Place bulk PDF at: {bulkConfig.BasePdfPath}");
                        Console.WriteLine($"   Continuing with normal workflow...");
                    }
                }


                // ═══════════════════════════════════════════════════════════
                // Continue with existing phases (CSV, Phase 1, 2, 3, etc.)
                // ═══════════════════════════════════════════════════════════

                // Get all phase configurations
                var csvConfig = ConfigurationService.GetCsvConfig();
                var phase1Config = ConfigurationService.GetPhase1Config();
                var phase2Config = ConfigurationService.GetPhase2Config();
                var prePhase3Config = ConfigurationService.GetPrePhase3Config();
                var phase3Config = ConfigurationService.GetPhase3Config();

                // Display configuration summary
                DisplayConfigurationSummary(csvConfig, phase1Config, phase2Config, phase3Config);

                // Confirm before proceeding
                if (!ConfirmStart())
                {
                    Console.WriteLine("\n👋 Cancelled by user");
                    return 0;
                }

                // ═══════════════════════════════════════════════════════
                // PRE-PHASE: CSV Processing
                // ═══════════════════════════════════════════════════════
                Console.WriteLine("\n" + new string('═', 70));
                Console.WriteLine("📋 PRE-PHASE: CSV Processing");
                Console.WriteLine(new string('═', 70));

                var csvRepo = new StudentCsvRepository(config);

                if (!csvRepo.ProcessedCsvExists())
                {
                    Console.WriteLine("📄 Processing raw CSV...");
                    csvRepo.ProcessRawCsv();
                    Console.WriteLine("✅ CSV processing complete\n");
                }
                else
                {
                    Console.WriteLine("✅ CSV already processed\n");
                }

                csvRepo.PreviewProcessedCsv(3);
                csvRepo.DisplayStatistics();

                // ═══════════════════════════════════════════════════════
                // PHASE 1: Search PHIS for Client IDs
                // ═══════════════════════════════════════════════════════
                if (phase1Config.Enabled)
                {
                    Console.WriteLine("\n" + new string('═', 70));
                    Console.WriteLine($"🔍 PHASE 1: {phase1Config.Description}");
                    Console.WriteLine(new string('═', 70));

                    if (!ConfirmPhase("Phase 1"))
                    {
                        Console.WriteLine("⏭️  Phase 1 skipped");
                    }
                    else
                    {
                        // Phase 1 will create and return PHIS components
                        var (phase1Result, phase1Driver, phase1SessionMgr, phase1SearchSvc) = await RunPhase1Async(config);

                        // Store for potential use in Phase 3
                        driver = phase1Driver;
                        sessionManager = phase1SessionMgr;
                        phisSearchService = phase1SearchSvc;

                        if (phase1Result.HasErrors)
                        {
                            Console.WriteLine("\n❌ Phase 1 failed with errors. Cannot proceed to Phase 2.");
                            return 1;
                        }

                        if (phase1Result.ManualReviewCount > 0)
                        {
                            Console.WriteLine($"\n⚠️  {phase1Result.ManualReviewCount} students need manual review.");
                            Console.WriteLine("💡 Please review and fix the CSV before proceeding to Phase 2.");

                            if (!ConfirmContinueWithReview())
                            {
                                Console.WriteLine("\n👋 Stopping before Phase 2 for manual review");
                                return 0;
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("\n⏭️  Phase 1 disabled in configuration");
                }

                // ═══════════════════════════════════════════════════════
                // PHASE 2: Download Consent PDFs from Vitalite
                // ═══════════════════════════════════════════════════════
                if (phase2Config.Enabled)
                {
                    Console.WriteLine("\n" + new string('═', 70));
                    Console.WriteLine($"📥 PHASE 2: {phase2Config.Description}");
                    Console.WriteLine(new string('═', 70));

                    if (!ConfirmPhase("Phase 2"))
                    {
                        Console.WriteLine("⏭️  Phase 2 skipped");
                    }
                    else
                    {
                        await RunPhase2Async(config);
                    }
                }
                else
                {
                    Console.WriteLine("\n⏭️  Phase 2 disabled in configuration");
                }

                // ═══════════════════════════════════════════════════════
                // PRE-PHASE 3: Validate and Prepare for Upload
                // ═══════════════════════════════════════════════════════
                if (prePhase3Config.Enabled)
                {
                    Console.WriteLine("\n" + new string('═', 70));
                    Console.WriteLine($"📋 PRE-PHASE 3: {prePhase3Config.Description}");
                    Console.WriteLine(new string('═', 70));

                    if (!ConfirmPhase("Pre-Phase 3"))
                    {
                        Console.WriteLine("⏭️  Pre-Phase 3 skipped");
                    }
                    else
                    {
                        await RunPrePhase3Async(config);
                    }
                }
                else
                {
                    Console.WriteLine("\n⏭️  Pre-Phase 3 disabled in configuration");
                }


                // ═══════════════════════════════════════════════════════════
                // PHASE 3: Upload to PHIS (TEST MODE)
                // ═══════════════════════════════════════════════════════════
                if (phase3Config.Enabled)
                {
                    Console.WriteLine("\n\n");
                    Console.WriteLine("╔════════════════════════════════════════════════════════╗");
                    Console.WriteLine("║         STARTING PHASE 3: Upload to PHIS (TEST)        ║");
                    Console.WriteLine("╚════════════════════════════════════════════════════════╝");

                    if (!ConfirmPhase("Phase 3"))
                    {
                        Console.WriteLine("⏭️  Phase 3 skipped");
                    }
                    else
                    {
                        try
                        {
                            // Initialize PHIS components if not already done (Phase 1 skipped)
                            if (driver == null || phisSearchService == null || sessionManager == null)
                            {
                                Console.WriteLine("⚠️  PHIS components not initialized. Initializing now...");
                                Console.WriteLine("💡 This will open a browser - please log into PHIS manually");

                                // Create driver using ChromeDriverFactory
                                var driverFactory = new ChromeDriverFactory(config);
                                driver = driverFactory.CreateDriver();

                                // Navigate to PHIS and wait for manual login
                                var phisConfig = ConfigurationService.GetPhisConfig();
                                driver.Navigate().GoToUrl(phisConfig.LoginUrl);

                                Console.WriteLine($"\n⏳ Please log into PHIS manually...");
                                Console.WriteLine($"   You have {phisConfig.ManualLoginWaitSeconds} seconds");
                                Console.WriteLine($"   Press any key once you're logged in...");
                                Console.ReadKey();

                                // Initialize PHIS components
                                var resultExtractor = new PhisResultExtractor(config);
                                sessionManager = new PhisSessionManager(driver, config);
                                phisSearchService = new PhisSearchService(driver, config, resultExtractor, sessionManager);

                                Console.WriteLine("✅ PHIS components initialized");
                            }
                            else
                            {
                                Console.WriteLine("✅ Reusing PHIS session from Phase 1");
                            }

                            // Run Phase 3
                            var phase3Orchestrator = new Phase3Orchestrator(
                                config,
                                driver,
                                phisSearchService,
                                sessionManager);

                            var phase3Result = await phase3Orchestrator.RunAsync();

                            if (phase3Result.IsSuccessful)
                            {
                                Console.WriteLine("\n✅ Phase 3 test completed successfully!");
                            }
                            else
                            {
                                Console.WriteLine("\n⚠️  Phase 3 test completed with errors");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\n❌ Phase 3 failed: {ex.Message}");
                            Console.WriteLine($"Stack trace: {ex.StackTrace}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("\n⏭️  Phase 3 disabled in configuration");
                }


                // ═══════════════════════════════════════════════════════
                // COMPLETION
                // ═══════════════════════════════════════════════════════
                PrintCompletionSummary();

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ FATAL ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return 1;
            }
            finally
            {
                // Clean up driver if it was created
                if (driver != null)
                {
                    try
                    {
                        Console.WriteLine("\n🔄 Closing browser...");
                        driver.Quit();
                        driver.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Error closing browser: {ex.Message}");
                    }
                }

                Console.WriteLine("\n\nPress any key to exit...");
                Console.ReadKey();
            }
        }





        /// <summary>
        /// Get command line argument value
        /// </summary>
        static string? GetArg(string[] args, string key)
        {
            var index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }





        #region Phase Execution

        /// <summary>
        /// Execute Phase 1: Search PHIS for Client IDs
        /// Returns Phase1Result and PHIS components for reuse in Phase 3
        /// </summary>
        static async Task<(Phase1Result result, IWebDriver? driver, PhisSessionManager? sessionMgr, PhisSearchService? searchSvc)>
            RunPhase1Async(IConfiguration config)
        {
            try
            {
                using var orchestrator = new Phase1Orchestrator(config);
                var result = await orchestrator.RunAsync();

                // Get the PHIS components from orchestrator for reuse
                var driver = orchestrator.GetDriver();
                var sessionMgr = orchestrator.GetSessionManager();
                var searchSvc = orchestrator.GetSearchService();

                return (result, driver, sessionMgr, searchSvc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Phase 1 error: {ex.Message}");
                return (new Phase1Result { HasErrors = true }, null, null, null);
            }
        }


        /// <summary>
        /// Execute Phase 2: Download consent PDFs from Vitalite
        /// </summary>
        static async Task RunPhase2Async(IConfiguration config)
        {
            try
            {
                var orchestrator = new Phase2Orchestrator(config);
                var result = await orchestrator.RunAsync();

                if (result.HasErrors)
                {
                    Console.WriteLine("\n❌ Phase 2 completed with errors");
                }
                else if (result.FailedToMatch > 0)
                {
                    Console.WriteLine($"\n⚠️  Phase 2 completed - {result.FailedToMatch} files need manual review");
                }
                else
                {
                    Console.WriteLine("\n✅ Phase 2 completed successfully!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Phase 2 error: {ex.Message}");
            }
        }







        /// <summary>
        /// Execute Pre-Phase 3: Validate and prepare PDFs for upload
        /// </summary>
        static async Task RunPrePhase3Async(IConfiguration config)
        {
            try
            {
                var orchestrator = new PrePhase3Orchestrator(config);
                var result = await orchestrator.RunAsync();

                if (result.HasErrors)
                {
                    Console.WriteLine("\n❌ Pre-Phase 3 completed with errors");
                }
                else if (result.SkippedMissingPdf > 0)
                {
                    Console.WriteLine($"\n⚠️  Pre-Phase 3 completed - {result.SkippedMissingPdf} PDFs missing");
                }
                else
                {
                    Console.WriteLine("\n✅ Pre-Phase 3 completed successfully!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Pre-Phase 3 error: {ex.Message}");
            }
        }








        #endregion Phase Execution




        #region UI Helpers

        /// <summary>
        /// Print application header
        /// </summary>
        static void PrintHeader()
        {
            // Only clear if running in a real console (not VS Code Debug Console)
            if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
            {
                try
                {
                    Console.Clear();
                }
                catch (IOException)
                {
                    // Fallback to newlines if clear fails
                    Console.WriteLine("\n\n\n");
                }
            }
            else
            {
                Console.WriteLine("\n\n\n");
            }

            Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    ConsentSync Orchestrator                        ║");
            Console.WriteLine("║                   Complete 3-Phase Workflow                        ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
        }

        /// <summary>
        /// Display configuration summary
        /// </summary>
        static void DisplayConfigurationSummary(
            CsvProcessingConfig csvConfig,
            Phase1Config phase1Config,
            Phase2Config phase2Config,
            Phase3Config phase3Config)
        {
            Console.WriteLine("📋 Configuration Summary:");
            Console.WriteLine(new string('─', 70));
            Console.WriteLine($"CSV Input:  {Path.GetFileName(csvConfig.InputCsvFileName)}");
            Console.WriteLine($"CSV Output: {Path.GetFileName(csvConfig.OutputCsvFileName)}");
            Console.WriteLine();
            Console.WriteLine($"Phase 1: {(phase1Config.Enabled ? "✅ Enabled" : "❌ Disabled")} - {phase1Config.Description}");
            Console.WriteLine($"Phase 2: {(phase2Config.Enabled ? "✅ Enabled" : "❌ Disabled")} - {phase2Config.Description}");
            Console.WriteLine($"Phase 3: {(phase3Config.Enabled ? "✅ Enabled" : "❌ Disabled")} - {phase3Config.Description}");
            Console.WriteLine(new string('─', 70));
        }

        /// <summary>
        /// Confirm before starting the workflow
        /// </summary>
        static bool ConfirmStart()
        {
            Console.WriteLine("\n🚀 Ready to start ConsentSync workflow");
            Console.WriteLine("Press [Y] to continue, [N] to cancel...");
            var key = Console.ReadKey(true);
            return key.Key == ConsoleKey.Y;
        }

        /// <summary>
        /// Confirm before executing each phase
        /// </summary>
        static bool ConfirmPhase(string phaseName)
        {
            Console.WriteLine($"\n▶️  Start {phaseName}?");
            Console.WriteLine("Press [Y] to continue, [N] to skip, [Q] to quit...");
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Q)
            {
                Environment.Exit(0);
            }

            return key.Key == ConsoleKey.Y;
        }

        /// <summary>
        /// Confirm whether to continue with manual review items
        /// </summary>
        static bool ConfirmContinueWithReview()
        {
            Console.WriteLine("\n⚠️  Continue to Phase 2 with unresolved manual review items?");
            Console.WriteLine("Press [Y] to continue anyway, [N] to stop and fix CSV first...");
            var key = Console.ReadKey(true);
            return key.Key == ConsoleKey.Y;
        }


        /// <summary>
        /// Print completion summary
        /// </summary>
        static void PrintCompletionSummary()
        {
            Console.WriteLine("\n" + new string('═', 70));
            Console.WriteLine("✅ ConsentSync Workflow Complete!");
            Console.WriteLine(new string('═', 70));
            Console.WriteLine("Next steps:");
            Console.WriteLine("  1. Review any manual review items in the CSV");
            Console.WriteLine("  2. Verify uploaded documents in PHIS");
            Console.WriteLine("  3. Archive processed files");
            Console.WriteLine(new string('═', 70));
        }



        #endregion UI Helpers





    }

}