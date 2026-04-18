using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.Phis;
using CsvProcessing;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using Orchestrator;
using Orchestrator.Phase1;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Orchestrator.BulkPdfExtraction;
using Keys = System.Windows.Forms.Keys;
// ✅ Resolve ambiguous references explicitly
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace OrchestratorUi
{
    public partial class UploadConsent : Form
    {
        private static readonly string AppSettingsPath =
            Path.Combine(AppContext.BaseDirectory, "appsettings.json");

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
        private async void bt_SearchClientId_Click(object sender, EventArgs e)
        {
            bt_SearchClientId.Enabled = false;
            bt_SearchClientId.Text = "⏳ Searching…";

            try
            {
                // ── Pre-flight checks before opening Chrome ───────────────
                if (!PreFlightChecks())
                    return;

                IConfiguration config = ConfigurationService.GetConfiguration();

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("🔍 PHASE 1: Search Client IDs on PHIS");
                LoggerService.LogInformation(new string('═', 60));

                (Phase1Result phase1Result,
                 IWebDriver? phase1Driver,
                 PhisSessionManager? phase1SessionMgr,
                 PhisSearchService? phase1SearchSvc) = await RunPhase1Async(config);

                // ── Store session for reuse in Phase 3 ───────────────────
                _driver = phase1Driver;
                _sessionManager = phase1SessionMgr;
                _phisSearchService = phase1SearchSvc;

                // ── Handle critical failure ───────────────────────────────
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

                // ── Nothing left to process ───────────────────────────────
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

                // ── Batch limit reached — more records remain ─────────────
                if (phase1Result.BatchLimitReached)
                {
                    int remaining = phase1Result.ToProcessCount - phase1Result.TotalProcessed;

                    LoggerService.LogInformation($"⏸️  Batch limit reached — {remaining} record(s) remaining.");

                    var answer = MessageBox.Show(
                        $"⏸️  Batch completed — more records remain.\n\n" +
                        $"  ✅ Found in this batch     : {phase1Result.FoundCount}\n" +
                        $"  ⚠️  Manual review needed   : {phase1Result.ManualReviewCount}\n" +
                        $"  📋 Remaining unprocessed   : {remaining}\n\n" +
                        $"Click 'Search' again to process the next batch.\n\n" +
                        $"Do you want to run the next batch now?",
                        "Batch Complete — More Records Remain",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                    if (answer == DialogResult.Yes)
                    {
                        // Recursively trigger next batch
                        bt_SearchClientId.Enabled = true;
                        bt_SearchClientId.Text = "🔍 Search Client IDs on PHIS";
                        bt_SearchClientId_Click(sender, e);
                        return;
                    }
                }

                // ── Manual review required ────────────────────────────────
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
                        "  2. Search for rows where ClientIdStatus = NeedsManualReview.\n" +
                        "  3. Use the BestMatch column as a hint.\n" +
                        "  4. Fill in the ClientId manually.\n\n" +
                        "Continue to Phase 2 without resolving these records?",
                        "Manual Review Required",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (answer == DialogResult.No)
                    {
                        LoggerService.LogInformation("ℹ️  User chose to resolve manual review items before continuing.");
                        return;
                    }

                    LoggerService.LogWarning("⚠️  Continuing to Phase 2 with unresolved manual review items.");
                }
                else
                {
                    // ── Full success ──────────────────────────────────────
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
                // ChromeDriver / browser-level failure
                LoggerService.LogError($"❌ Browser error: {ex.Message}", ex);
                MessageBox.Show(
                    $"❌ A browser error occurred.\n\n{ex.Message}\n\n" +
                    "Ensure Portable Chrome is installed and the ChromeDriver version matches.\n" +
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




        // ── Pre-flight checks before launching Chrome ─────────────────
        private bool PreFlightChecks()
        {
            // 1. Processed CSV must exist
            var csvRepo = new StudentCsvRepository();
            if (!csvRepo.ProcessedCsvExists())
            {
                var csvWs = ConfigurationService.GetCsvWorkspaceConfig();
                MessageBox.Show(
                    "⚠️  No processed CSV file found.\n\n" +
                    "Please run 'Process CSV' in Phase 0 before starting Phase 1.\n\n" +
                    $"Expected location:\n{csvWs.GetProcessedCsvPath()}",
                    "CSV Not Ready", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                LoggerService.LogWarning("⚠️  Pre-flight failed: processed CSV not found.");
                return false;
            }

            // 2. Chrome must be available
            var chromeConfig = ConfigurationService.GetChromeDriverConfig();
            if (chromeConfig.UsePortableChrome && !File.Exists(chromeConfig.PortableChromePath))
            {
                MessageBox.Show(
                    "⚠️  Portable Chrome is not installed.\n\n" +
                    "Please click '🌐 Download Portable Chrome' to install it before running Phase 1.",
                    "Chrome Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                LoggerService.LogWarning("⚠️  Pre-flight failed: portable chrome.exe not found.");
                return false;
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
            bool exists = File.Exists(chromeConfig.PortableChromePath);
            btn_PortableChrome.Text = exists ? "✅ Chrome Ready" : "🌐 Download Portable Chrome";
            btn_PortableChrome.BackColor = exists ? Color.DarkGreen : Color.SteelBlue;
            btn_PortableChrome.Enabled = true;
        }

        private async void btn_PortableChrome_Click(object sender, EventArgs e)
        {
            var chromeConfig = ConfigurationService.GetChromeDriverConfig();
            if (File.Exists(chromeConfig.PortableChromePath))
            {
                if (MessageBox.Show(
                    $"✅ Portable Chrome is already installed at:\n{chromeConfig.PortableChromePath}\n\nRe-download anyway?",
                    "Already Installed", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                    return;
            }

            btn_PortableChrome.Enabled = false;
            btn_PortableChrome.Text = "⏳ Downloading…";
            _chromeCts = new CancellationTokenSource();

            try
            {
                LoggerService.LogInformation($"\n🌐 Downloading Portable Chrome ({chromeConfig.PortableChromeChannel} channel)...");
                var factory = new ChromeDriverFactory();
                bool success = await Task.Run(() =>
                    factory.DownloadPortableChromeAsync(
                        progress: msg => LoggerService.LogInformation(msg),
                        cancellationToken: _chromeCts.Token));

                if (success)
                {
                    var chrome = Directory.GetFiles(chromeConfig.PortableChromeExtractTo, "chrome.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (chrome != null) SaveChromePathToConfig(chrome);
                    MessageBox.Show($"✅ Portable Chrome is ready!\n\n{chrome ?? chromeConfig.PortableChromeExtractTo}",
                        "Download Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("❌ Download failed.\nCheck the log for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (OperationCanceledException) { LoggerService.LogWarning("⚠️  Download cancelled."); }
            finally { _chromeCts?.Dispose(); _chromeCts = null; RefreshChromeButtonState(); }
        }

        private void SaveChromePathToConfig(string chromePath)
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(AppSettingsPath))!;
                node["ChromeDriver"]!["PortableChromePath"] = chromePath;
                node["ChromeDriver"]!["UsePortableChrome"] = true;
                File.WriteAllText(AppSettingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
                File.WriteAllText(AppSettingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                ConfigurationService.ReloadConfiguration();
                WorkspaceInitializer.EnsureAllFoldersExist();
                MessageBox.Show($"✅ Configuration saved!\n\n  Base Dir : {txt_BaseDir.Text}\n  School   : {txt_SchoolName.Text}\n  Grade    : {cb_Grade.SelectedItem}\n  Batch    : {batchSize}", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show($"❌ Failed to save:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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