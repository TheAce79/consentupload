using ConsentSyncCore.Services;
using CsvProcessing;
using Microsoft.Extensions.Configuration;
using Orchestrator.Phase1;
using System.Text;


namespace Orchestrator
{
    class Program
    {

        static async Task<int> Main(string[] args)
        {
            // Register encoding provider for legacy encodings (required for CSV processing)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            PrintHeader();

            try
            {
                // Load configuration
                var config = ConfigurationService.GetConfiguration();

                // Get all phase configurations
                var csvConfig = ConfigurationService.GetCsvConfig();
                var phase1Config = ConfigurationService.GetPhase1Config();
                var phase2Config = ConfigurationService.GetPhase2Config();
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
                        var phase1Result = await RunPhase1Async(config);

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
                // PHASE 3: Upload Documents to PHIS
                // ═══════════════════════════════════════════════════════
                if (phase3Config.Enabled)
                {
                    Console.WriteLine("\n" + new string('═', 70));
                    Console.WriteLine($"📤 PHASE 3: {phase3Config.Description}");
                    Console.WriteLine(new string('═', 70));

                    if (!ConfirmPhase("Phase 3"))
                    {
                        Console.WriteLine("⏭️  Phase 3 skipped");
                    }
                    else
                    {
                        await RunPhase3Async(config);
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
        /// </summary>
        static async Task<Phase1Result> RunPhase1Async(IConfiguration config)
        {
            try
            {
                using var orchestrator = new Phase1Orchestrator(config);
                var result = await orchestrator.RunAsync();
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Phase 1 error: {ex.Message}");
                return new Phase1Result { HasErrors = true };
            }
        }

        /// <summary>
        /// Execute Phase 2: Download consent PDFs from Vitalite
        /// </summary>
        static async Task RunPhase2Async(IConfiguration config)
        {
            try
            {
                Console.WriteLine("🚧 Phase 2 implementation coming soon...");
                Console.WriteLine("📥 This phase will:");
                Console.WriteLine("   1. Login to Vitalite website");
                Console.WriteLine("   2. Search for student consent forms");
                Console.WriteLine("   3. Download PDFs");
                Console.WriteLine("   4. Extract names from PDFs");
                Console.WriteLine("   5. Rename to ClientID_VaccineType.pdf format");
                Console.WriteLine("   6. Split multi-page PDFs if needed");
                Console.WriteLine("   7. Generate Upload_to_PHIS.csv");

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Phase 2 error: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute Phase 3: Upload documents to PHIS
        /// </summary>
        static async Task RunPhase3Async(IConfiguration config)
        {
            try
            {
                Console.WriteLine("🚧 Phase 3 implementation coming soon...");
                Console.WriteLine("📤 This phase will:");
                Console.WriteLine("   1. Read Upload_to_PHIS.csv");
                Console.WriteLine("   2. Login to PHIS");
                Console.WriteLine("   3. Navigate to each client record");
                Console.WriteLine("   4. Upload consent PDFs");
                Console.WriteLine("   5. Upload File Rose PDFs (if enabled)");
                Console.WriteLine("   6. Verify upload success");
                Console.WriteLine("   7. Mark records as completed");

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Phase 3 error: {ex.Message}");
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