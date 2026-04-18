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
            LoggerService.Initialize();

            LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║              ConsentSync - Main Program                ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝");

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

                var config = ConfigurationService.GetConfiguration();

                // ── Standalone commands (no browser required) ──────────────────
                if (args.Contains("--download-chrome"))
                    return await RunDownloadChromeTestAsync(args);

                if (args.Contains("--check-filerose"))
                    return RunCheckFileRose();

                // ══════════════════════════════════════════════════════════
                // 🌹 --extract-filerose
                // Copies {ClientId}.pdf from "1 Scan File Rose"
                // to "2_Output_Ready_FileRose" as {ClientId}_{suffix}_{year}.pdf
                // Updates IsFileRoseExtracted in Validation_Results.csv.
                // Invalid/unmatched files → "3_Error_FileRose_Extraction".
                // ══════════════════════════════════════════════════════════
                if (args.Contains("--extract-filerose"))
                    return RunExtractFileRose();

                // ── Normal workflow ────────────────────────────────────────────
                WorkspaceInitializer.EnsureAllFoldersExist();

                var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
                var csvWs = ConfigurationService.GetCsvWorkspaceConfig();

                LoggerService.LogInformation("📂 Drop your files here:");
                LoggerService.LogInformation($"   CSV input  → {csvWs.GetConsentCsvPath()}");
                LoggerService.LogInformation($"   Bulk PDFs  → {bulkConfig.GetInputBulkPath()}");
                LoggerService.LogInformation($"   Scanned    → {bulkConfig.GetInputScannedPath()}");
                LoggerService.LogInformation($"   FileRose   → {bulkConfig.GetFileRoseScanPath()}\n");

                if (args.Contains("--extract-bulk") || args.Contains("-b"))
                    return await BulkPdfExtractionCommand.ExecuteAsync(args);

                if (bulkConfig.Enabled)
                {
                    LoggerService.LogInformation("\n╔════════════════════════════════════════════════════════╗");
                    LoggerService.LogInformation("║           BULK PDF EXTRACTION (Pre-Processing)         ║");
                    LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝");

                    var bulkOrchestrator = new BulkPdfExtractionOrchestrator(config);

                    if (bulkOrchestrator.IsPdfAvailable())
                    {
                        Console.WriteLine("\n💡 Bulk PDF detected - would you like to extract it now?");
                        Console.WriteLine("   Press [Y] to extract, [N] to skip...");

                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Y)
                        {
                            var bulkResult = await bulkOrchestrator.RunAsync();
                            if (!bulkResult.Success)
                            {
                                Console.WriteLine("\n⚠️  Bulk extraction had errors - continue anyway? [Y/N]");
                                if (Console.ReadKey(true).Key != ConsoleKey.Y) return 1;
                            }
                        }
                    }
                    else
                    {
                        LoggerService.LogInformation($"\n💡 Bulk PDF extraction enabled but no bulk PDF found");
                        LoggerService.LogInformation("   Continuing with normal workflow...");
                    }
                }

                var csvConfig = ConfigurationService.GetCsvConfig();
                var phase1Config = ConfigurationService.GetPhase1Config();
                var phase2Config = ConfigurationService.GetPhase2Config();
                var prePhase3Config = ConfigurationService.GetPrePhase3Config();
                var phase3Config = ConfigurationService.GetPhase3Config();

                DisplayConfigurationSummary(csvConfig, phase1Config, phase2Config, phase3Config);

                if (!ConfirmStart()) { LoggerService.LogInformation("\n👋 Cancelled by user"); return 0; }

                // PRE-PHASE
                LoggerService.LogInformation("\n" + new string('═', 70));
                LoggerService.LogInformation("📋 PRE-PHASE: CSV Processing");
                LoggerService.LogInformation(new string('═', 70));

                var csvRepo = new StudentCsvRepository(config);
                if (!csvRepo.ProcessedCsvExists()) { csvRepo.ProcessRawCsv(); }
                csvRepo.PreviewProcessedCsv(3);
                csvRepo.DisplayStatistics();

                // PHASE 1
                if (phase1Config.Enabled)
                {
                    LoggerService.LogInformation("\n" + new string('═', 70));
                    LoggerService.LogInformation($"🔍 PHASE 1: {phase1Config.Description}");
                    LoggerService.LogInformation(new string('═', 70));

                    if (!ConfirmPhase("Phase 1")) { LoggerService.LogInformation("⏭️  Phase 1 skipped"); }
                    else
                    {
                        var (phase1Result, phase1Driver, phase1SessionMgr, phase1SearchSvc) =
                            await RunPhase1Async(config);

                        driver = phase1Driver;
                        sessionManager = phase1SessionMgr;
                        phisSearchService = phase1SearchSvc;

                        if (phase1Result.HasErrors)
                        {
                            LoggerService.LogError("\n❌ Phase 1 failed. Cannot proceed to Phase 2.");
                            return 1;
                        }

                        if (phase1Result.ManualReviewCount > 0)
                        {
                            LoggerService.LogWarning($"\n⚠️  {phase1Result.ManualReviewCount} students need manual review.");
                            if (!ConfirmContinueWithReview()) return 0;
                        }
                    }
                }
                else { LoggerService.LogInformation("\n⏭️  Phase 1 disabled in configuration"); }

                // PHASE 2
                if (phase2Config.Enabled)
                {
                    LoggerService.LogInformation("\n" + new string('═', 70));
                    LoggerService.LogInformation($"📥 PHASE 2: {phase2Config.Description}");
                    LoggerService.LogInformation(new string('═', 70));

                    if (!ConfirmPhase("Phase 2")) LoggerService.LogInformation("⏭️  Phase 2 skipped");
                    else await RunPhase2Async(config);
                }
                else { LoggerService.LogInformation("\n⏭️  Phase 2 disabled in configuration"); }

                // PRE-PHASE 3
                if (prePhase3Config.Enabled)
                {
                    LoggerService.LogInformation("\n" + new string('═', 70));
                    LoggerService.LogInformation($"📋 PRE-PHASE 3: {prePhase3Config.Description}");
                    LoggerService.LogInformation(new string('═', 70));

                    if (!ConfirmPhase("Pre-Phase 3")) LoggerService.LogInformation("⏭️  Pre-Phase 3 skipped");
                    else await RunPrePhase3Async(config);
                }
                else { LoggerService.LogInformation("\n⏭️  Pre-Phase 3 disabled in configuration"); }

                // PHASE 3
                if (phase3Config.Enabled)
                {
                    LoggerService.LogInformation("\n╔════════════════════════════════════════════════════════╗");
                    LoggerService.LogInformation("║         STARTING PHASE 3: Upload to PHIS               ║");
                    LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝");

                    if (!ConfirmPhase("Phase 3")) { LoggerService.LogInformation("⏭️  Phase 3 skipped"); }
                    else
                    {
                        try
                        {
                            if (driver == null || phisSearchService == null || sessionManager == null)
                            {
                                LoggerService.LogWarning("⚠️  PHIS components not initialized. Initializing now...");
                                var driverFactory = new ChromeDriverFactory(config);
                                driver = driverFactory.CreateDriver();
                                var resultExtractor = new PhisResultExtractor(config);
                                sessionManager = new PhisSessionManager(driver, config);
                                phisSearchService = new PhisSearchService(driver, config, resultExtractor, sessionManager);

                                if (!sessionManager.Login())
                                {
                                    LoggerService.LogError("❌ Failed to establish PHIS session!");
                                    return 1;
                                }
                                LoggerService.LogInformation("✅ PHIS session established successfully");
                            }
                            else { LoggerService.LogInformation("✅ Reusing PHIS session from Phase 1"); }

                            var phase3Orchestrator = new Phase3Orchestrator(config, driver, phisSearchService, sessionManager);
                            var phase3Result = await phase3Orchestrator.RunAsync();

                            if (phase3Result.IsSuccessful) LoggerService.LogInformation("\n✅ Phase 3 completed successfully!");
                            else LoggerService.LogWarning("\n⚠️  Phase 3 completed with errors");
                        }
                        catch (Exception ex) { LoggerService.LogError($"\n❌ Phase 3 failed: {ex.Message}", ex); }
                    }
                }
                else { LoggerService.LogInformation("\n⏭️  Phase 3 disabled in configuration"); }

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
                    try { driver.Quit(); driver.Dispose(); }
                    catch (Exception ex) { LoggerService.LogWarning($"⚠️  Error closing browser: {ex.Message}"); }
                }
                LoggerService.Dispose();
                Console.WriteLine("\n\nPress any key to exit...");
                Console.ReadKey();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 🌹 --check-filerose
        // Scans "1 Scan File Rose", updates IsFileRoseDefault in CSV.
        // ══════════════════════════════════════════════════════════════
        static int RunCheckFileRose()
        {
            LoggerService.LogInformation("\n╔════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║         🌹 Check FileRose - Update CSV                  ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝\n");

            try
            {
                var service = new FileRoseVerificationService();
                var result = service.CheckAndUpdateCsv();

                LoggerService.LogInformation("\n" + new string('─', 56));
                LoggerService.LogInformation("📊 FileRose Check Summary");
                LoggerService.LogInformation(new string('─', 56));
                LoggerService.LogInformation($"   Eligible records  : {result.EligibleRecords}");
                LoggerService.LogInformation($"   ✅ FileRose found  : {result.Found}");
                LoggerService.LogInformation($"   ❌ Not found       : {result.NotFound}");
                LoggerService.LogInformation($"   ⏭️  Skipped         : {result.Skipped}");
                LoggerService.LogInformation($"   📁 Directory       : {result.ScannedDirectory}");

                if (result.Found > 0)
                {
                    LoggerService.LogInformation("\n   Found:");
                    foreach (var (clientId, fileName) in result.Details.Where(d => d.Value != null))
                        LoggerService.LogInformation($"      ✅  {clientId} → {fileName}");
                }

                if (result.NotFound > 0)
                {
                    LoggerService.LogInformation("\n   Missing:");
                    foreach (var clientId in result.Details.Where(d => d.Value == null).Select(d => d.Key))
                        LoggerService.LogInformation($"      ❌  {clientId}.pdf not found");
                }

                LoggerService.LogInformation(new string('─', 56));
                LoggerService.LogInformation("✅ Validation_Results.csv updated successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ FileRose check failed: {ex.Message}", ex);
                return 1;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 🌹 --extract-filerose
        // Renames & copies matched files to 2_Output_Ready_FileRose.
        // Moves unmatched files to 3_Error_FileRose_Extraction.
        // Updates IsFileRoseExtracted in Validation_Results.csv.
        // ══════════════════════════════════════════════════════════════
        static int RunExtractFileRose()
        {
            LoggerService.LogInformation("\n╔════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║       🌹 Extract FileRose - Rename & Copy               ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝\n");

            try
            {
                var service = new FileRoseExtractionService();
                var result = service.ExtractFileRose();

                LoggerService.LogInformation("\n" + new string('─', 60));
                LoggerService.LogInformation("📊 FileRose Extraction Summary");
                LoggerService.LogInformation(new string('─', 60));
                LoggerService.LogInformation($"   ✅ Extracted          : {result.Extracted}");
                LoggerService.LogInformation($"   ⏭️  Already extracted  : {result.AlreadyExtracted}");
                LoggerService.LogInformation($"   ❌ Errors (moved out) : {result.Errors}");

                if (result.ExtractedFiles.Count > 0)
                {
                    LoggerService.LogInformation("\n   Extracted files:");
                    foreach (var (clientId, newFileName) in result.ExtractedFiles)
                        LoggerService.LogInformation($"      ✅  {clientId} → {newFileName}");
                }

                if (result.ErrorFiles.Count > 0)
                {
                    LoggerService.LogInformation("\n   Error files (moved to 3_Error_FileRose_Extraction):");
                    // After — all 3 elements deconstructed
                    foreach (var (fileName, reason, category) in result.ErrorFiles)
                        LoggerService.LogInformation($"      ❌  {fileName} — {reason}");
                }

                LoggerService.LogInformation(new string('─', 60));

                if (result.Errors == 0)
                    LoggerService.LogInformation("✅ All FileRose files extracted successfully.");
                else
                    LoggerService.LogWarning(
                        $"⚠️  {result.Errors} file(s) could not be matched. " +
                        "Fix filenames in 3_Error_FileRose_Extraction and re-run.");

                return 0;
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ FileRose extraction failed: {ex.Message}", ex);
                return 1;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 🧪 --download-chrome
        // ══════════════════════════════════════════════════════════════
        static async Task<int> RunDownloadChromeTestAsync(string[] args)
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════════╗");
            Console.WriteLine("║        🧪 TEST: Download Portable Chrome               ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

            var chromeConfig = ConfigurationService.GetChromeDriverConfig();
            var channelOverride = GetArg(args, "--channel");

            if (!string.IsNullOrWhiteSpace(channelOverride))
            {
                chromeConfig.PortableChromeChannel = channelOverride;
                Console.WriteLine($"   ℹ️  Channel overridden: {channelOverride}");
            }

            Console.WriteLine($"   Channel : {chromeConfig.PortableChromeChannel}");
            Console.WriteLine($"   Chrome  → {chromeConfig.PortableChromeExtractTo}");
            Console.WriteLine($"   Driver  → {chromeConfig.ChromeDriverExtractTo}");

            var factory = new ChromeDriverFactory();
            var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            bool success = await factory.DownloadPortableChromeAsync(
                progress: msg => Console.WriteLine(msg),
                cancellationToken: cts.Token);

            Console.WriteLine(success ? "\n✅ Download succeeded!" : "\n❌ Download failed.");
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

        static async Task<(Phase1Result, IWebDriver?, PhisSessionManager?, PhisSearchService?)>
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

                if (result.HasErrors) LoggerService.LogError("\n❌ Phase 2 completed with errors");
                else if (result.FailedToMatch > 0) LoggerService.LogWarning($"\n⚠️  Phase 2 — {result.FailedToMatch} files need review");
                else LoggerService.LogInformation("\n✅ Phase 2 completed successfully!");
            }
            catch (Exception ex) { LoggerService.LogError($"❌ Phase 2 error: {ex.Message}", ex); }
        }

        static async Task RunPrePhase3Async(IConfiguration config)
        {
            try
            {
                var orchestrator = new PrePhase3Orchestrator(config);
                var result = await orchestrator.RunAsync();

                if (result.HasErrors) LoggerService.LogError("\n❌ Pre-Phase 3 completed with errors");
                else if (result.SkippedMissingPdf > 0) LoggerService.LogWarning($"\n⚠️  Pre-Phase 3 — {result.SkippedMissingPdf} PDFs missing");
                else LoggerService.LogInformation("\n✅ Pre-Phase 3 completed successfully!");
            }
            catch (Exception ex) { LoggerService.LogError($"❌ Pre-Phase 3 error: {ex.Message}", ex); }
        }

        #endregion

        #region UI Helpers

        static void PrintHeader()
        {
            if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
            {
                try { Console.Clear(); } catch (IOException) { Console.WriteLine("\n\n"); }
            }
            LoggerService.LogInformation("╔════════════════════════════════════════════════════════════════════╗");
            LoggerService.LogInformation("║                    ConsentSync Orchestrator                        ║");
            LoggerService.LogInformation("║                   Complete 3-Phase Workflow                        ║");
            LoggerService.LogInformation("╚════════════════════════════════════════════════════════════════════╝\n");
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
            LoggerService.LogInformation($"Phase 1: {(phase1Config.Enabled ? "✅ Enabled" : "❌ Disabled")} - {phase1Config.Description}");
            LoggerService.LogInformation($"Phase 2: {(phase2Config.Enabled ? "✅ Enabled" : "❌ Disabled")} - {phase2Config.Description}");
            LoggerService.LogInformation($"Phase 3: {(phase3Config.Enabled ? "✅ Enabled" : "❌ Disabled")} - {phase3Config.Description}");
            LoggerService.LogInformation(new string('─', 70));
        }

        static bool ConfirmStart()
        {
            Console.WriteLine("\n🚀 Ready to start. Press [Y] to continue, [N] to cancel...");
            return Console.ReadKey(true).Key == ConsoleKey.Y;
        }

        static bool ConfirmPhase(string phaseName)
        {
            Console.WriteLine($"\n▶️  Start {phaseName}? [Y] continue  [N] skip  [Q] quit");
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Q) Environment.Exit(0);
            return key.Key == ConsoleKey.Y;
        }

        static bool ConfirmContinueWithReview()
        {
            Console.WriteLine("\n⚠️  Continue to Phase 2 with unresolved items? [Y/N]");
            return Console.ReadKey(true).Key == ConsoleKey.Y;
        }

        static void PrintCompletionSummary()
        {
            LoggerService.LogInformation("\n" + new string('═', 70));
            LoggerService.LogInformation("✅ ConsentSync Workflow Complete!");
            LoggerService.LogInformation(new string('═', 70));
        }

        #endregion
    }
}