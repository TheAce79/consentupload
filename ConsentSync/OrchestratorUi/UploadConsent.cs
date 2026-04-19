using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.Phis;
using CsvProcessing;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using Orchestrator;
using Orchestrator.Phase1;
using Orchestrator.Phase2;
using Orchestrator.Phase3;
using Orchestrator.PrePhase3;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Orchestrator.BulkPdfExtraction;
using static Orchestrator.Phase1.Phase1Orchestrator;
using static Orchestrator.Phase3.Phase3Orchestrator;
using Keys = System.Windows.Forms.Keys;
// ✅ Resolve ambiguous references explicitly
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace OrchestratorUi
{
    public partial class UploadConsent : Form
    {
        private static readonly string AppSettingsPath =
         Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        // ✅ Source appsettings.json in the project — kept in sync with the output copy
        private static readonly string AppSettingsSourcePath =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", // bin\Debug\net9.0-windows\win-x64 → solution root
                "ConsentSyncCore", "appsettings.json"));

        private CancellationTokenSource? _chromeCts;

        // ── Shared PHIS session — reused across Phase 1 → Phase 3 ─────
        private IWebDriver? _driver;
        private PhisSessionManager? _sessionManager;
        private PhisSearchService? _phisSearchService;

        public UploadConsent()
        {
            InitializeComponent();
            LoggerService.LogMessage += OnLogMessage;
            LoadConfiguration();
        }

        // ── Route every LoggerService call into rtxt_Log ──────────────
        private void OnLogMessage(object? sender, LogEventArgs e)
        {
            this.InvokeIfRequired(() =>
            {
                Color color = e.Level switch
                {
                    LogLevel.Error or LogLevel.Critical => Color.Red,
                    LogLevel.Warning => Color.Yellow,
                    LogLevel.Debug => Color.Gray,
                    _ => Color.LimeGreen
                };

                rtxt_Log.SelectionStart = rtxt_Log.TextLength;
                rtxt_Log.SelectionLength = 0;
                rtxt_Log.SelectionColor = color;
                rtxt_Log.AppendText(e.FormattedMessage + Environment.NewLine);
                rtxt_Log.SelectionColor = rtxt_Log.ForeColor;
                rtxt_Log.ScrollToCaret();
            });
        }

        // ── Load all config values into UI on startup ─────────────────
        private void LoadConfiguration()
        {
            txt_BaseDir.Text = ConfigurationService.GetBaseDirectory();

            var phisConfig = ConfigurationService.GetPhisConfig();
            txtBox_BatchSize.Text = phisConfig.BatchSize.ToString();

            var schoolContext = ConfigurationService.GetSchoolContextConfig();
            txt_SchoolName.Text = schoolContext.SchoolName;

            cb_Grade.Items.Clear();
            cb_Grade.Items.Add("7");
            cb_Grade.Items.Add("9");
            cb_Grade.SelectedItem = schoolContext.Grade.Trim();
            if (cb_Grade.SelectedIndex == -1)
                cb_Grade.SelectedIndex = 0;

            RefreshChromeButtonState();
        }




        // ── Phase 1: Search Client IDs on PHIS ───────────────────────




        // ── Phase 1: Search Client IDs on PHIS ───────────────────────
        private async void bt_SearchClientId_Click(object sender, EventArgs e)
        {
            bt_SearchClientId.Enabled = false;
            bt_SearchClientId.Text = "⏳ Searching…";

            // ── Reset progress bar ────────────────────────────────────
            var phisConfig = ConfigurationService.GetPhisConfig();
            pb_Phase1.Maximum = phisConfig.BatchSize > 0 ? phisConfig.BatchSize : 100;
            pb_Phase1.Value = 0;
            lbl_Phase1Progress.Text = "Initialising…";
            lbl_Phase1Progress.ForeColor = Color.FromArgb(0, 90, 160);

            try
            {
                if (!PreFlightChecks())
                {
                    lbl_Phase1Progress.Text = "";   // ✅ clear on early exit
                    lbl_Phase1Progress.ForeColor = Color.FromArgb(0, 90, 160);
                    return;
                }

                IConfiguration config = ConfigurationService.GetConfiguration();

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("🔍 PHASE 1: Search Client IDs on PHIS");
                LoggerService.LogInformation(new string('═', 60));

                // ── Progress callback — UI thread safe ────────────────
                var progress = new Progress<Phase1Progress>(p =>
                {
                    this.InvokeIfRequired(() =>
                    {
                        pb_Phase1.Maximum = p.Total;
                        pb_Phase1.Value = Math.Min(p.Current, p.Total);

                        lbl_Phase1Progress.Text = $"{p.Current} / {p.Total}  —  {p.StudentName}";
                        lbl_Phase1Progress.ForeColor = p.IsFound
                            ? Color.DarkGreen
                            : Color.DarkOrange;
                    });
                });

                (Phase1Result phase1Result,
                 IWebDriver? phase1Driver,
                 PhisSessionManager? phase1SessionMgr,
                 PhisSearchService? phase1SearchSvc) = await RunPhase1Async(config, progress);

                // ── Store session for reuse in Phase 3 ───────────────
                _driver = phase1Driver;
                _sessionManager = phase1SessionMgr;
                _phisSearchService = phase1SearchSvc;

                // ── Handle critical failure ───────────────────────────
                if (phase1Result.HasErrors)
                {
                    LoggerService.LogError("❌ Phase 1 failed with a critical error.");
                    MessageBox.Show(
                        "❌ Phase 1 encountered a critical error and could not complete.\n\n" +
                        "Possible causes:\n" +
                        "  • Chrome / ChromeDriver not found or version mismatch\n" +
                        "  • PHIS login failed or session timed out\n" +
                        "  • Network connectivity issue\n" +
                        "  • Processed CSV file is missing or corrupt\n\n" +
                        "Please check the log panel for details, then try again.",
                        "Phase 1 Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // ── Nothing left to process ───────────────────────────
                if (phase1Result.ToProcessCount == 0 && phase1Result.TotalStudents > 0)
                {
                    LoggerService.LogInformation("ℹ️  All students already processed.");
                    MessageBox.Show(
                        "ℹ️  All students have already been processed.\n\n" +
                        $"  📋 Total students        : {phase1Result.TotalStudents}\n" +
                        $"  ✅ Client IDs found      : {phase1Result.FoundCount}\n" +
                        $"  ♻️  Duplicates assigned  : {phase1Result.DuplicatesAssigned}\n\n" +
                        "No further action required for Phase 1.\n" +
                        "You may proceed to Phase 2.",
                        "Already Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // ── Batch limit reached ───────────────────────────────
                if (phase1Result.BatchLimitReached)
                {
                    int remaining = phase1Result.ToProcessCount - phase1Result.TotalProcessed;
                    LoggerService.LogInformation($"⏸️  Batch limit reached — {remaining} record(s) remaining.");

                    var answer = MessageBox.Show(
                        $"⏸️  Batch completed — more records remain.\n\n" +
                        $"  ✅ Found in this batch     : {phase1Result.FoundCount}\n" +
                        $"  ⚠️  Manual review needed   : {phase1Result.ManualReviewCount}\n" +
                        $"  📋 Remaining unprocessed   : {remaining}\n\n" +
                        "Click 'Search' again to process the next batch.\n\n" +
                        "Do you want to run the next batch now?",
                        "Batch Complete — More Records Remain",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                    if (answer == DialogResult.Yes)
                    {
                        bt_SearchClientId.Enabled = true;
                        bt_SearchClientId.Text = "🔍 Search Client IDs on PHIS";
                        bt_SearchClientId_Click(sender, e);
                        return;
                    }
                }

                // ── Manual review required ────────────────────────────
                if (phase1Result.ManualReviewCount > 0)
                {
                    LoggerService.LogWarning($"⚠️  {phase1Result.ManualReviewCount} student(s) need manual review.");

                    var answer = MessageBox.Show(
                        $"⚠️  Phase 1 completed — {phase1Result.ManualReviewCount} record(s) need manual attention.\n\n" +
                        $"  ✅ Client IDs found       : {phase1Result.FoundCount}\n" +
                        $"  ♻️  Duplicates assigned   : {phase1Result.DuplicatesAssigned}\n" +
                        $"  ⚠️  Needs manual review   : {phase1Result.ManualReviewCount}\n" +
                        $"  ❌ Search errors          : {phase1Result.ErrorCount}\n\n" +
                        "Next steps:\n" +
                        "  1. Open the processed CSV file.\n" +
                        "  2. Find rows where ClientIdStatus = NeedsManualReview.\n" +
                        "  3. Use the BestMatch column as a hint.\n" +
                        "  4. Fill in the ClientId manually.\n\n" +
                        "Continue to Phase 2 without resolving these records?",
                        "Manual Review Required",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (answer == DialogResult.No)
                    {
                        LoggerService.LogInformation("ℹ️  User chose to resolve manual review items first.");
                        return;
                    }
                }
                else
                {
                    // ── Full success ──────────────────────────────────
                    LoggerService.LogInformation("✅ Phase 1 completed successfully — all Client IDs found.");
                    MessageBox.Show(
                        $"✅ Phase 1 completed successfully.\n\n" +
                        $"  ✅ Client IDs found       : {phase1Result.FoundCount}\n" +
                        $"  ♻️  Duplicates assigned   : {phase1Result.DuplicatesAssigned}\n" +
                        $"  📋 Total processed        : {phase1Result.TotalProcessed}\n\n" +
                        "You may now proceed to Phase 2.",
                        "Phase 1 Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (InvalidOperationException ex)
            {
                LoggerService.LogError($"❌ Browser error: {ex.Message}", ex);
                MessageBox.Show(
                    $"❌ A browser error occurred.\n\n{ex.Message}\n\n" +
                    "Ensure Portable Chrome is installed and ChromeDriver version matches.\n" +
                    "Use the '🌐 Download Portable Chrome' button to re-install if needed.",
                    "Browser Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected Phase 1 error: {ex.Message}", ex);
                MessageBox.Show(
                    $"❌ An unexpected error occurred during Phase 1:\n\n{ex.Message}\n\n" +
                    "Check the log panel for the full stack trace.",
                    "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                bt_SearchClientId.Enabled = true;
                bt_SearchClientId.Text = "🔍 Search Client IDs on PHIS";

                // ✅ Correct final label state
                this.InvokeIfRequired(() =>
                {
                    if (pb_Phase1.Value > 0 && pb_Phase1.Value == pb_Phase1.Maximum)
                    {
                        lbl_Phase1Progress.Text = $"✅ Batch complete — {pb_Phase1.Maximum} / {pb_Phase1.Maximum}";
                        lbl_Phase1Progress.ForeColor = Color.DarkGreen;
                    }
                    else if (pb_Phase1.Value == 0)
                    {
                        lbl_Phase1Progress.Text = "";
                        lbl_Phase1Progress.ForeColor = Color.FromArgb(0, 90, 160);
                    }
                    // else: keep last student name shown (mid-batch stop)
                });
            }
        }

        // ── Phase 1 runner ────────────────────────────────────────────
        private static async Task<(Phase1Result, IWebDriver?, PhisSessionManager?, PhisSearchService?)>
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




        // ── Phase 1 runner (with progress) — single overload ─────────
        private static async Task<(Phase1Result, IWebDriver?, PhisSessionManager?, PhisSearchService?)>
            RunPhase1Async(IConfiguration config, IProgress<Phase1Progress>? progress = null)
        {
            try
            {
                using var orchestrator = new Phase1Orchestrator(config);
                var result = await orchestrator.RunAsync(progress);
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




        // ── Pre-flight checks before launching Chrome ─────────────────
        private bool PreFlightChecks()
        {
            // 1. Processed CSV must exist
            var processedCsvPath = ConfigurationService.GetOutputCsvFullPath();
            var csvConfig = ConfigurationService.GetCsvConfig();
            var csvWs = ConfigurationService.GetCsvWorkspaceConfig();

            if (!File.Exists(processedCsvPath))
            {
                LoggerService.LogWarning($"⚠️  Pre-flight failed: {processedCsvPath}");
                MessageBox.Show(
                    $"⚠️  The processed CSV file was not found.\n\n" +
                    $"  Expected file   : {csvConfig.OutputCsvFileName}\n" +
                    $"  Expected folder : {csvWs.GetProcessedCsvPath()}\n\n" +
                    "Please click  📋 Process CSV  in Phase 0 first.",
                    "Processed CSV Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // 2. Chrome + ChromeDriver version check
            var factory = new ChromeDriverFactory();
            var check = factory.VerifyVersionMatch();

            // Always log the details
            LoggerService.LogInformation("🔍 Chrome Version Check:");
            LoggerService.LogInformation($"   chrome.exe       : v{check.ChromeVersion}  ({check.ChromePath})");
            LoggerService.LogInformation($"   chromedriver.exe : v{check.DriverVersion}  ({check.DriverPath})");

            if (!check.ChromeFound)
            {
                LoggerService.LogWarning($"⚠️  chrome.exe not found: {check.ChromePath}");
                MessageBox.Show(
                    $"⚠️  Portable Chrome was not found.\n\n" +
                    $"  Expected: {check.ChromePath}\n\n" +
                    "Please click  🌐 Download Portable Chrome  to install it.",
                    "Chrome Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!check.DriverFound)
            {
                LoggerService.LogWarning($"⚠️  chromedriver.exe not found: {check.DriverPath}");
                MessageBox.Show(
                    $"⚠️  ChromeDriver was not found.\n\n" +
                    $"  Expected: {check.DriverPath}\n\n" +
                    "Please click  🌐 Download Portable Chrome  to re-download both\n" +
                    "chrome.exe and chromedriver.exe together.",
                    "ChromeDriver Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!check.VersionsMatch)
            {
                LoggerService.LogWarning($"⚠️  Version mismatch — {check.ErrorMessage}");
                var answer = MessageBox.Show(
                    $"⚠️  Chrome and ChromeDriver version mismatch!\n\n" +
                    $"  chrome.exe       : v{check.ChromeVersion}\n" +
                    $"  chromedriver.exe : v{check.DriverVersion}\n\n" +
                    "Chrome will crash immediately with mismatched versions.\n\n" +
                    "Click  🌐 Download Portable Chrome  to re-download matching versions.\n\n" +
                    "Continue anyway?",
                    "Version Mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer == DialogResult.No) return false;
            }
            else
            {
                LoggerService.LogInformation($"   ✅ Versions match — v{check.ChromeMajor} ready.");
            }

            return true;
        }

        // ── Button 1: Extract Bulk PDF ────────────────────────────────
        private async void btn_ExtractBulk_Click(object sender, EventArgs e)
        {
            btn_ExtractBulk.Enabled = false;
            btn_ExtractBulk.Text = "⏳ Extracting…";

            try
            {
                var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
                var inputBulkPath = bulkConfig.GetInputBulkPath();
                var outputReadyPath = bulkConfig.GetOutputReadyPath();

                bool hasBulkInput = Directory.Exists(inputBulkPath) && Directory.GetFiles(inputBulkPath, "*.pdf").Length > 0;
                bool hasOutputReady = Directory.Exists(outputReadyPath) && Directory.GetFiles(outputReadyPath, "*.pdf", SearchOption.AllDirectories).Length > 0;

                if (!hasBulkInput && hasOutputReady)
                {
                    int readyCount = Directory.GetFiles(outputReadyPath, "*.pdf", SearchOption.AllDirectories).Length;
                    LoggerService.LogInformation("ℹ️  Bulk PDF extraction already completed.");
                    MessageBox.Show(
                        $"The bulk PDF has already been extracted.\n\n" +
                        $"  ✅  {readyCount} file(s) are ready in the output folder.\n\n" +
                        $"Output folder:\n{outputReadyPath}\n\n" +
                        $"To re-extract, drop a new bulk PDF into:\n{inputBulkPath}",
                        "Already Extracted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!hasBulkInput && !hasOutputReady)
                {
                    LoggerService.LogInformation("⚠️  No bulk PDF found — input and output folders are both empty.");
                    MessageBox.Show(
                        $"No bulk PDF was found.\n\n" +
                        $"Please place your bulk PDF file into the following folder, then try again:\n\n" +
                        $"{inputBulkPath}",
                        "No PDF Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                LoggerService.LogInformation("\n📄 Starting Bulk PDF Extraction...");
                var config = ConfigurationService.GetConfiguration();
                var bulkOrchestrator = new BulkPdfExtractionOrchestrator(config);
                var result = await Task.Run(() => bulkOrchestrator.RunAsync());

                if (result.Success)
                {
                    int total = result.TotalExtracted + result.DuplicatesFound;
                    LoggerService.LogInformation($"✅ Extraction complete — {total} total file(s).");
                    MessageBox.Show(
                        $"✅ Bulk PDF extraction completed successfully.\n\n" +
                        $"  Total files extracted  :  {total}\n" +
                        $"  ├── Unique files       :  {result.TotalExtracted}\n" +
                        $"  └── Duplicate copies   :  {result.DuplicatesFound}\n" +
                        (result.FailedExtractions > 0 ? $"  ❌ Failed              :  {result.FailedExtractions}\n" : "") +
                        $"\nFiles are ready in:\n{outputReadyPath}",
                        "Extraction Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"⚠️  Extraction completed with errors.\n\n{result.ErrorMessage}\n\nCheck the log for details.",
                        "Extraction Errors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Bulk extraction failed: {ex.Message}", ex);
                MessageBox.Show($"❌ An unexpected error occurred:\n\n{ex.Message}",
                    "Extraction Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btn_ExtractBulk.Enabled = true;
                btn_ExtractBulk.Text = "📄 Extract Bulk PDF";
            }
        }

        // ── Button 2: Process CSV ─────────────────────────────────────
        private async void btn_ProcessCsv_Click(object sender, EventArgs e)
        {
            btn_ProcessCsv.Enabled = false;
            btn_ProcessCsv.Text = "⏳ Processing…";

            try
            {
                var config = ConfigurationService.GetConfiguration();
                var csvWs = ConfigurationService.GetCsvWorkspaceConfig();
                var csvConfig = ConfigurationService.GetCsvConfig();
                var inputCsvFolder = csvWs.GetConsentCsvPath();
                var processedFolder = csvWs.GetProcessedCsvPath();
                var inputFile = Path.Combine(inputCsvFolder, csvConfig.InputCsvFileName);
                var processedFile = Path.Combine(processedFolder, csvConfig.OutputCsvFileName);

                if (!File.Exists(inputFile) && !File.Exists(processedFile))
                {
                    MessageBox.Show(
                        $"No CSV file was found.\n\nPlease drop \"{csvConfig.InputCsvFileName}\" into:\n\n{inputCsvFolder}",
                        "No CSV Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (File.Exists(processedFile))
                {
                    var fi = new FileInfo(processedFile);
                    var ans = MessageBox.Show(
                        $"A processed CSV already exists:\n\n{processedFile}\n\n" +
                        $"  Last modified : {fi.LastWriteTime:yyyy-MM-dd HH:mm}\n" +
                        $"  Size          : {fi.Length / 1024.0:F1} KB\n\n" +
                        $"Do you want to re-process and overwrite it?",
                        "Processed CSV Exists", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (ans == DialogResult.No)
                    {
                        await Task.Run(() =>
                        {
                            var repo = new StudentCsvRepository(config);
                            repo.PreviewProcessedCsv(3);
                            repo.DisplayStatistics();
                        });
                        return;
                    }
                }

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("📋 PRE-PHASE: CSV Processing");
                LoggerService.LogInformation(new string('═', 60));

                await Task.Run(() =>
                {
                    var repo = new StudentCsvRepository(config);
                    repo.ProcessRawCsv();
                    repo.PreviewProcessedCsv(3);
                    repo.DisplayStatistics();
                });

                MessageBox.Show("✅ CSV processing completed successfully.\n\nSee the log for a preview and statistics.",
                    "Processing Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ CSV processing failed: {ex.Message}", ex);
                MessageBox.Show($"❌ An unexpected error occurred:\n\n{ex.Message}",
                    "Processing Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btn_ProcessCsv.Enabled = true;
                btn_ProcessCsv.Text = "📋 Process CSV";
            }
        }

        // ── Portable Chrome ───────────────────────────────────────────



        private void RefreshChromeButtonState()
        {
            var chromeConfig = ConfigurationService.GetChromeDriverConfig();

            if (!chromeConfig.UsePortableChrome)
            {
                // ✅ System Chrome mode — button updates the driver
                var check = new ChromeDriverFactory().VerifyVersionMatch();
                bool matched = check.IsReady;
                btn_PortableChrome.Text = matched ? "✅ Driver Up-to-Date" : "🔄 Update ChromeDriver";
                btn_PortableChrome.BackColor = matched ? Color.DarkGreen : Color.DarkOrange;
                btn_PortableChrome.Enabled = true;
            }
            else
            {
                // Portable Chrome mode — button downloads CfT
                bool exists = File.Exists(chromeConfig.PortableChromePath);
                btn_PortableChrome.Text = exists ? "✅ Chrome Ready" : "🌐 Download Portable Chrome";
                btn_PortableChrome.BackColor = exists ? Color.DarkGreen : Color.SteelBlue;
                btn_PortableChrome.Enabled = true;
            }
        }




        private async void btn_PortableChrome_Click(object sender, EventArgs e)
        {
            var chromeConfig = ConfigurationService.GetChromeDriverConfig();
            btn_PortableChrome.Enabled = false;
            _chromeCts = new CancellationTokenSource();

            try
            {
                var factory = new ChromeDriverFactory();

                // ✅ System Chrome mode — update chromedriver to match installed Chrome
                if (!chromeConfig.UsePortableChrome)
                {
                    btn_PortableChrome.Text = "⏳ Updating driver…";
                    LoggerService.LogInformation("\n🔄 Updating ChromeDriver to match system Chrome...");

                    bool success = await Task.Run(() =>
                        factory.UpdateSystemChromeDriverAsync(
                            progress: msg => LoggerService.LogInformation(msg),
                            cancellationToken: _chromeCts.Token));

                    MessageBox.Show(
                        success
                            ? "✅ ChromeDriver updated successfully!\n\nIt now matches your system Chrome version."
                            : "❌ ChromeDriver update failed.\nCheck the log panel for details.",
                        success ? "Driver Updated" : "Update Failed",
                        MessageBoxButtons.OK,
                        success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

                    return;
                }

                // ── Portable Chrome mode — download CfT ──────────────────────
                if (File.Exists(chromeConfig.PortableChromePath))
                {
                    if (MessageBox.Show(
                        $"✅ Portable Chrome is already installed at:\n{chromeConfig.PortableChromePath}\n\nRe-download anyway?",
                        "Already Installed", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                        return;
                }

                btn_PortableChrome.Text = "⏳ Downloading…";
                LoggerService.LogInformation($"\n🌐 Downloading Portable Chrome ({chromeConfig.PortableChromeChannel} channel)...");

                bool dlSuccess = await Task.Run(() =>
                    factory.DownloadPortableChromeAsync(
                        progress: msg => LoggerService.LogInformation(msg),
                        cancellationToken: _chromeCts.Token));

                if (dlSuccess)
                {
                    var chrome = Directory.GetFiles(
                        chromeConfig.PortableChromeExtractTo, "chrome.exe",
                        SearchOption.AllDirectories).FirstOrDefault();
                    if (chrome != null) SaveChromePathToConfig(chrome);
                    MessageBox.Show(
                        $"✅ Portable Chrome is ready!\n\n{chrome ?? chromeConfig.PortableChromeExtractTo}",
                        "Download Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("❌ Download failed.\nCheck the log for details.",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (OperationCanceledException) { LoggerService.LogWarning("⚠️  Cancelled."); }
            finally
            {
                _chromeCts?.Dispose();
                _chromeCts = null;
                RefreshChromeButtonState();
            }
        }






        private void SaveChromePathToConfig(string chromePath)
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(AppSettingsPath))!;
                node["ChromeDriver"]!["PortableChromePath"] = chromePath;
                node["ChromeDriver"]!["UsePortableChrome"] = true;

                var json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

                // ✅ Write to output copy (runtime reads this)
                File.WriteAllText(AppSettingsPath, json);

                // ✅ Write back to source file so next build doesn't overwrite with stale values
                SyncToSourceAppsettings(json);

                ConfigurationService.ReloadConfiguration();
                LoggerService.LogInformation($"   ✅ PortableChromePath saved: {chromePath}");
            }
            catch (Exception ex) { LoggerService.LogWarning($"   ⚠️  Could not update appsettings.json: {ex.Message}"); }
        }


        private void btn_BrowseDir_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select the Base Directory (e.g. C:\\PHIS)";
            folderBrowserDialog1.SelectedPath = txt_BaseDir.Text;
            folderBrowserDialog1.UseDescriptionForTitle = true;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txt_BaseDir.Text = folderBrowserDialog1.SelectedPath;
        }


        private void btn_SaveConfig_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txt_BaseDir.Text)) { MessageBox.Show("❌ Base Directory cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (string.IsNullOrWhiteSpace(txt_SchoolName.Text)) { MessageBox.Show("❌ School Name cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!int.TryParse(txtBox_BatchSize.Text, out int batchSize) || batchSize < 1) { MessageBox.Show("❌ Batch Size must be a number greater than 0.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var dirError = WorkspaceInitializer.ValidateBaseDirectory(txt_BaseDir.Text);
            if (dirError != null) { MessageBox.Show($"❌ {dirError}", "Directory Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            try
            {
                var node = JsonNode.Parse(File.ReadAllText(AppSettingsPath))!;
                node["BaseDirectory"] = txt_BaseDir.Text;
                node["SchoolContext"]!["SchoolName"] = txt_SchoolName.Text;
                node["SchoolContext"]!["Grade"] = cb_Grade.SelectedItem!.ToString();
                node["PhisAutomation"]!["BatchSize"] = batchSize;

                var json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

                // ✅ Write to output copy (runtime reads this)
                File.WriteAllText(AppSettingsPath, json);

                // ✅ Write back to source file so next build doesn't overwrite with stale values
                SyncToSourceAppsettings(json);

                ConfigurationService.ReloadConfiguration();
                WorkspaceInitializer.EnsureAllFoldersExist();
                MessageBox.Show($"✅ Configuration saved!\n\n  Base Dir : {txt_BaseDir.Text}\n  School   : {txt_SchoolName.Text}\n  Grade    : {cb_Grade.SelectedItem}\n  Batch    : {batchSize}", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show($"❌ Failed to save:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }



        // ── Phase 2: Validate PDFs against student records ────────────


        // ── Phase 2: Validate PDFs against student records ────────────
        private async void bt_ValidatePdf_Click(object sender, EventArgs e)
        {
            bt_ValidatePdf.Enabled = false;
            bt_ValidatePdf.Text = "⏳ Validating…";

            try
            {
                // ── Duplicate pre-check ───────────────────────────────
                if (!await CheckUnresolvedDuplicatesAsync("PDF Validation"))
                    return;

                var config = ConfigurationService.GetConfiguration();
                var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
                var errorFolder = bulkConfig.GetErrorPath();

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("🔍 PHASE 2 — PDF Validation");
                LoggerService.LogInformation(new string('═', 60));

                var phase2Result = await Task.Run(async () =>
                {
                    var orchestrator = new Phase2Orchestrator(config);
                    return await orchestrator.RunAsync();
                });

                if (phase2Result.HasErrors)
                {
                    LoggerService.LogError("❌ Phase 2 completed with errors.");
                    MessageBox.Show(
                        "❌ PDF Validation encountered errors and could not complete.\n\n" +
                        "Please check the log panel for details.",
                        "Validation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("📊 VALIDATION SUMMARY");
                LoggerService.LogInformation(new string('═', 60));
                LoggerService.LogInformation($"   📄 Total PDFs found      : {phase2Result.TotalPdfs}");
                LoggerService.LogInformation($"   ✅ Matched to student    : {phase2Result.SuccessfullyProcessed}");
                LoggerService.LogInformation($"   ⚠️  Unmatched (errors)   : {phase2Result.FailedToMatch}");

                if (phase2Result.FailedToMatch > 0)
                {
                    LoggerService.LogWarning($"\n   ⚠️  {phase2Result.FailedToMatch} unmatched PDF(s) copied to:");
                    LoggerService.LogWarning($"       {errorFolder}");
                    LoggerService.LogWarning("   Review the error folder, correct filenames if needed,");
                    LoggerService.LogWarning("   then re-run validation before generating the Upload CSV.");

                    foreach (var err in phase2Result.ErrorMessages)
                        LoggerService.LogWarning($"      • {err}");
                }

                LoggerService.LogInformation(new string('═', 60));

                var hasUnmatched = phase2Result.FailedToMatch > 0;
                MessageBox.Show(
                    $"🔍 PDF Validation complete.\n\n" +
                    $"  📄 Total PDFs          : {phase2Result.TotalPdfs}\n" +
                    $"  ✅ Matched             : {phase2Result.SuccessfullyProcessed}\n" +
                    $"  ⚠️  Unmatched (errors) : {phase2Result.FailedToMatch}\n" +
                    (hasUnmatched
                        ? $"\n⚠️  Unmatched PDFs have been copied to:\n  {errorFolder}\n\nReview and correct them, then re-run validation."
                        : "\n✅ All PDFs matched — you may now click\n   📄 Generate Upload CSV."),
                    hasUnmatched ? "Validation — Review Required" : "Validation Successful",
                    MessageBoxButtons.OK,
                    hasUnmatched ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected error during validation: {ex.Message}", ex);
                MessageBox.Show(
                    $"❌ An unexpected error occurred:\n\n{ex.Message}\n\nCheck the log panel for details.",
                    "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                bt_ValidatePdf.Enabled = true;
                bt_ValidatePdf.Text = "🔍 Validate PDFs Against Student Records";
            }
        }






        // ── PrePhase 3: Generate Upload_to_PHIS.csv ───────────────────
        private async void bt_GenerateCsv_Click(object sender, EventArgs e)
        {
            bt_GenerateCsv.Enabled = false;
            bt_GenerateCsv.Text = "⏳ Generating…";

            try
            {
                // ── Duplicate pre-check ───────────────────────────────
                if (!await CheckUnresolvedDuplicatesAsync("Generate Upload CSV"))
                    return;

                var config = ConfigurationService.GetConfiguration();

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("📄 PRE-PHASE 3 — Generate Upload CSV");
                LoggerService.LogInformation(new string('═', 60));

                var prePhase3Result = await Task.Run(async () =>
                {
                    var orchestrator = new PrePhase3Orchestrator(config);
                    return await orchestrator.RunAsync();
                });

                if (prePhase3Result.HasErrors)
                {
                    LoggerService.LogError("❌ Pre-Phase 3 completed with errors.");
                    MessageBox.Show(
                        "❌ Upload CSV generation encountered errors.\n\n" +
                        "Review Validation_Results.csv for records with missing PDFs,\n" +
                        "then retry.",
                        "Generation Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (prePhase3Result.SkippedMissingPdf > 0)
                    LoggerService.LogWarning($"⚠️  {prePhase3Result.SkippedMissingPdf} record(s) skipped — PDF not found.");

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("📊 UPLOAD CSV SUMMARY");
                LoggerService.LogInformation(new string('═', 60));
                LoggerService.LogInformation($"   ✅ New rows appended      : {prePhase3Result.UploadRecordsCreated}");
                LoggerService.LogInformation($"   📋 FileRose records       : {prePhase3Result.FileRoseRecordsCreated}");
                LoggerService.LogInformation($"   ⚠️  Skipped (no PDF)      : {prePhase3Result.SkippedMissingPdf}");
                LoggerService.LogInformation($"   ℹ️  Total records          : {prePhase3Result.TotalRecords}");
                LoggerService.LogInformation(new string('═', 60));

                MessageBox.Show(
                    $"✅ Upload CSV updated successfully.\n\n" +
                    $"  ✅ New rows appended      : {prePhase3Result.UploadRecordsCreated}\n" +
                    $"  📋 FileRose records       : {prePhase3Result.FileRoseRecordsCreated}\n" +
                    $"  ⚠️  Skipped (no PDF)      : {prePhase3Result.SkippedMissingPdf}\n\n" +
                    "You may now proceed to Phase 3 — Upload to PHIS.",
                    "Upload CSV Ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected error generating Upload CSV: {ex.Message}", ex);
                MessageBox.Show(
                    $"❌ An unexpected error occurred:\n\n{ex.Message}\n\nCheck the log panel for details.",
                    "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                bt_GenerateCsv.Enabled = true;
                bt_GenerateCsv.Text = "📄 Generate Upload CSV";
            }
        }





        // ── Append FileRose rows to Upload_to_PHIS.csv ────────────────
        private void bt_AppendFileRose_Click(object sender, EventArgs e)
        {
            bt_AppendFileRose.Enabled = false;
            bt_AppendFileRose.Text = "⏳ Appending…";

            try
            {
                var svc = new Orchestrator.Services.FileRoseAppendService();
                var result = svc.AppendFileRoseRows();

                if (result.HasErrors)
                {
                    MessageBox.Show(
                        "❌ FileRose append encountered errors.\n\n" +
                        string.Join("\n", result.Messages.Take(5)) +
                        "\n\nCheck the log panel for details.",
                        "Append Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                MessageBox.Show(
                    $"🌹 FileRose rows appended to Upload_to_PHIS.csv\n\n" +
                    $"  ✅ Appended        : {result.Appended}\n" +
                    $"  ⏭️  Already exist  : {result.AlreadyExist}\n" +
                    $"  ⚠️  No ClientId    : {result.NoClientId}\n\n" +
                    (result.Appended > 0
                        ? "You may now proceed to Phase 3 — Upload to PHIS."
                        : "All FileRose rows were already present — nothing to add."),
                    result.Appended > 0 ? "Append Complete" : "Nothing to Append",
                    MessageBoxButtons.OK,
                    result.Appended > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected error appending FileRose rows: {ex.Message}", ex);
                MessageBox.Show($"❌ Unexpected error:\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                bt_AppendFileRose.Enabled = true;
                bt_AppendFileRose.Text = "🌹 Append FileRose Rows to Upload CSV";
            }
        }


        /// <summary>
        /// Writes the given JSON content back to the source appsettings.json in ConsentSyncCore,
        /// so that a future build does not overwrite the output copy with stale values.
        /// Silently skips if the source path cannot be resolved (e.g. published single-file).
        /// </summary>
        private static void SyncToSourceAppsettings(string json)
        {
            try
            {
                if (File.Exists(AppSettingsSourcePath))
                {
                    File.WriteAllText(AppSettingsSourcePath, json);
                    LoggerService.LogInformation($"   🔄 Source appsettings.json synced: {AppSettingsSourcePath}");
                }
            }
            catch (Exception ex)
            {
                // Non-fatal — output copy is the authoritative runtime file
                LoggerService.LogWarning($"   ⚠️  Could not sync source appsettings.json: {ex.Message}");
            }
        }



        // ── Phase 3: Upload Consent & FileRose to PHIS ────────────────
        private async void bt_Upload_Click(object sender, EventArgs e)
        {
            bt_Upload.Enabled = false;
            bt_Upload.Text = "⏳ Uploading…";

            var phisConfig = ConfigurationService.GetPhisConfig();
            pb_Phase3.Maximum = phisConfig.BatchSize > 0 ? phisConfig.BatchSize : 100;
            pb_Phase3.Value = 0;
            lbl_Phase3Progress.Text = "Initialising…";
            lbl_Phase3Progress.ForeColor = Color.FromArgb(140, 30, 30);

            try
            {
                // ── Duplicate pre-check ───────────────────────────────
                if (!await CheckUnresolvedDuplicatesAsync("Upload to PHIS"))
                {
                    lbl_Phase3Progress.Text = "";
                    return;
                }

                if (!PreFlightChecks())
                {
                    lbl_Phase3Progress.Text = "";
                    return;
                }

                var config = ConfigurationService.GetConfiguration();

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("⬆️  PHASE 3 — Upload Consent & FileRose to PHIS");
                LoggerService.LogInformation(new string('═', 60));

                if (_driver == null || _sessionManager == null || _phisSearchService == null)
                {
                    LoggerService.LogInformation("🌐 No active PHIS session — initializing Chrome...");
                    var factory = new ChromeDriverFactory(config);
                    _driver = factory.CreateDriver();

                    var resultExtractor = new PhisResultExtractor(config);
                    _sessionManager = new PhisSessionManager(_driver, config);
                    _phisSearchService = new PhisSearchService(_driver, config, resultExtractor, _sessionManager);

                    if (!_sessionManager.Login())
                    {
                        LoggerService.LogError("❌ PHIS login failed — cannot proceed with upload.");
                        MessageBox.Show(
                            "❌ Could not log into PHIS.\n\n" +
                            "Ensure your credentials are correct and the PHIS portal is accessible,\n" +
                            "then try again.",
                            "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    LoggerService.LogInformation("✅ PHIS session established.");
                }
                else
                {
                    LoggerService.LogInformation("✅ Reusing active PHIS session from Phase 1.");
                }

                var progress = new Progress<Phase3Progress>(p =>
                {
                    this.InvokeIfRequired(() =>
                    {
                        pb_Phase3.Maximum = p.Total;
                        pb_Phase3.Value = Math.Min(p.Current, p.Total);
                        string icon = p.IsFeuilleRose ? "🌹" : "📋";
                        lbl_Phase3Progress.Text = $"{p.Current} / {p.Total}  {icon}  {p.StudentName}";
                        lbl_Phase3Progress.ForeColor = p.IsSuccess ? Color.DarkGreen : Color.DarkOrange;
                    });
                });

                Phase3Result phase3Result = null!;
                await Task.Run(async () =>
                {
                    var orchestrator = new Orchestrator.Phase3.Phase3Orchestrator(
                        config, _driver!, _phisSearchService!, _sessionManager!);
                    phase3Result = await orchestrator.RunAsync(progress);
                });

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("📊 UPLOAD SUMMARY");
                LoggerService.LogInformation(new string('═', 60));
                LoggerService.LogInformation($"   📋 Total records      : {phase3Result.TotalRecords}");
                LoggerService.LogInformation($"   ✅ Successful uploads : {phase3Result.SuccessfulUploads}");
                LoggerService.LogInformation($"   ❌ Errors             : {phase3Result.TotalRecords - phase3Result.SuccessfulUploads}");
                if (phase3Result.BatchLimitReached)
                    LoggerService.LogWarning("   ⏸️  Batch limit reached — run again to continue.");
                LoggerService.LogInformation(new string('═', 60));

                if (phase3Result.BatchLimitReached)
                {
                    var answer = MessageBox.Show(
                        $"⏸️  Batch limit reached — upload paused.\n\n" +
                        $"  ✅ Uploaded this batch : {phase3Result.SuccessfulUploads}\n" +
                        $"  📋 Total records      : {phase3Result.TotalRecords}\n\n" +
                        "Progress has been saved. Click Upload again to continue.\n\n" +
                        "Do you want to run the next batch now?",
                        "Batch Complete — More Records Remain",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                    if (answer == DialogResult.Yes)
                    {
                        bt_Upload.Enabled = true;
                        bt_Upload.Text = "⬆️  Upload Consent & FileRose to PHIS";
                        bt_Upload_Click(sender, e);
                        return;
                    }
                }
                else if (phase3Result.IsSuccessful)
                {
                    MessageBox.Show(
                        $"✅ All documents uploaded successfully to PHIS.\n\n" +
                        $"  ✅ Successful uploads : {phase3Result.SuccessfulUploads}\n" +
                        $"  📋 Total records     : {phase3Result.TotalRecords}\n\n" +
                        "The upload process is complete.",
                        "Upload Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"⚠️  Upload completed with errors.\n\n" +
                        $"  ✅ Successful : {phase3Result.SuccessfulUploads}\n" +
                        $"  ❌ Failed     : {phase3Result.TotalRecords - phase3Result.SuccessfulUploads}\n\n" +
                        "Check the log panel for details on failed records.",
                        "Upload Completed with Errors",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (InvalidOperationException ex)
            {
                LoggerService.LogError($"❌ Browser error during upload: {ex.Message}", ex);
                MessageBox.Show(
                    $"❌ A browser error occurred during upload.\n\n{ex.Message}\n\n" +
                    "The Chrome session may have timed out. Try running Phase 1 first\n" +
                    "to establish a fresh PHIS session.",
                    "Browser Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected error during upload: {ex.Message}", ex);
                MessageBox.Show(
                    $"❌ An unexpected error occurred:\n\n{ex.Message}\n\nCheck the log panel for details.",
                    "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                bt_Upload.Enabled = true;
                bt_Upload.Text = "⬆️  Upload Consent & FileRose to PHIS";

                this.InvokeIfRequired(() =>
                {
                    if (pb_Phase3.Value > 0 && pb_Phase3.Value == pb_Phase3.Maximum)
                    {
                        lbl_Phase3Progress.Text = $"✅ Batch complete — {pb_Phase3.Maximum} / {pb_Phase3.Maximum}";
                        lbl_Phase3Progress.ForeColor = Color.DarkGreen;
                    }
                    else if (pb_Phase3.Value == 0)
                    {
                        lbl_Phase3Progress.Text = "";
                    }
                });
            }
        }


        private void txtBox_BatchSize_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != (char)Keys.Back) e.Handled = true;
        }

        private void txtBox_BatchSize_TextChanged(object sender, EventArgs e)
        {
            if (!int.TryParse(txtBox_BatchSize.Text, out _) && txtBox_BatchSize.Text.Length > 0)
            {
                txtBox_BatchSize.Text = txtBox_BatchSize.Text[..^1];
                txtBox_BatchSize.SelectionStart = txtBox_BatchSize.Text.Length;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Cleanly dispose the PHIS browser session if still open
            if (_driver != null)
            {
                try { _driver.Quit(); _driver.Dispose(); }
                catch { /* non-fatal */ }
            }
            LoggerService.LogMessage -= OnLogMessage;
            base.OnFormClosing(e);
        }



        // ── Shared duplicate pre-check — call before Validate, Generate CSV, Upload ──
        /// <summary>
        /// Reads the processed CSV, finds all duplicate groups that are not fully
        /// resolved, logs a detailed summary and shows a warning MessageBox.
        /// Returns <c>true</c> if the caller should proceed, <c>false</c> to abort.
        /// </summary>
        private async Task<bool> CheckUnresolvedDuplicatesAsync(string callerLabel)
        {
            try
            {
                var config = ConfigurationService.GetConfiguration();
                var repo = new StudentCsvRepository(config);
                var allStudents = await Task.Run(() => repo.ReadAll());

                var unresolvedGroups = allStudents
                    .GroupBy(s => $"{s.LastName.Trim().ToUpperInvariant()}_{s.FirstName.Trim().ToUpperInvariant()}_{s.DateOfBirth.Trim()}")
                    .Where(g => g.Any(s => s.IsDuplicate) && !g.All(s => s.DuplicateResolved))
                    .ToList();

                if (unresolvedGroups.Count == 0)
                    return true;    // ✅ all clear

                // ── Log full detail ───────────────────────────────────
                LoggerService.LogWarning($"\n⚠️  UNRESOLVED DUPLICATES — blocked {callerLabel}");
                LoggerService.LogWarning(new string('─', 60));
                foreach (var g in unresolvedGroups)
                {
                    var rep = g.First();
                    int resolved = g.Count(s => s.DuplicateResolved);
                    LoggerService.LogWarning(
                        $"   • {rep.LastName}, {rep.FirstName}  (DOB: {rep.DateOfBirth})  " +
                        $"— {resolved}/{g.Count()} row(s) resolved");
                }
                LoggerService.LogWarning(new string('─', 60));
                LoggerService.LogWarning("   Set DuplicateResolved = true on ALL rows per student, then retry.");

                // ── Message box (capped at 10 rows) ──────────────────
                var lines = unresolvedGroups.Take(10).Select(g =>
                {
                    var rep = g.First();
                    int resolved = g.Count(s => s.DuplicateResolved);
                    return $"  • {rep.LastName}, {rep.FirstName}  ({rep.DateOfBirth})  [{resolved}/{g.Count()} resolved]";
                }).ToList();

                if (unresolvedGroups.Count > 10)
                    lines.Add($"  … and {unresolvedGroups.Count - 10} more — see log panel");

                var answer = MessageBox.Show(
                    $"⚠️  {unresolvedGroups.Count} duplicate group(s) have NOT been fully resolved.\n\n" +
                    string.Join(Environment.NewLine, lines) +
                    "\n\n" +
                    "Next steps:\n" +
                    "  1. Open  immunizations_processed.csv\n" +
                    "  2. Review PDFs in  5_Duplicate\\{LastName}_{FirstName}\\\n" +
                    "  3. Set  DuplicateResolved = true  on ALL rows for each student.\n" +
                    "  4. Retry.\n\n" +
                    $"⚠️  Proceeding without resolving duplicates may cause\n" +
                    $"   incorrect PDFs to be uploaded to PHIS.\n\n" +
                    "Continue anyway?",
                    "Unresolved Duplicates — Action Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer == DialogResult.No)
                {
                    LoggerService.LogInformation($"ℹ️  {callerLabel} cancelled — user chose to resolve duplicates first.");
                    return false;
                }

                LoggerService.LogWarning($"⚠️  User chose to continue {callerLabel} despite unresolved duplicates.");
                return true;
            }
            catch (Exception ex)
            {
                // Non-fatal — if CSV is unreadable, let the main flow handle it
                LoggerService.LogWarning($"⚠️  Could not check for unresolved duplicates: {ex.Message}");
                return true;
            }
        }

    }

    internal static class ControlExtensions
    {
        public static void InvokeIfRequired(this Control c, Action a)
        {
            if (c.InvokeRequired) c.Invoke(a);
            else a();
        }
    }
}