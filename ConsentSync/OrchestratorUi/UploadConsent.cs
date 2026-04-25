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


            // ── Show bt_test only when Phase3:Testing:Enabled = true ──
            var config = ConfigurationService.GetConfiguration();
            bt_ScanPdf.Visible = config.GetValue<bool>("Phase3:Testing:Enabled");

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


                // ── Run Phase 1 and get results + session for reuse ───────

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
                        this,
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
                        this,
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
                        this,
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
                        this,
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
                        this,
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
                    this,
                    $"❌ A browser error occurred.\n\n{ex.Message}\n\n" +
                    "Ensure Portable Chrome is installed and ChromeDriver version matches.\n" +
                    "Use the '🌐 Download Portable Chrome' button to re-install if needed.",
                    "Browser Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected Phase 1 error: {ex.Message}", ex);
                MessageBox.Show(
                    this,
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
                    this,
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
                    this,
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
                    this,
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
                    this,
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
                        this,
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
                        this,
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
                        this,
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
                        this,
                        $"⚠️  Extraction completed with errors.\n\n{result.ErrorMessage}\n\nCheck the log for details.",
                        "Extraction Errors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Bulk extraction failed: {ex.Message}", ex);
                MessageBox.Show(this, $"❌ An unexpected error occurred:\n\n{ex.Message}",
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
                        this,
                        $"No CSV file was found.\n\nPlease drop \"{csvConfig.InputCsvFileName}\" into:\n\n{inputCsvFolder}",
                        "No CSV Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (File.Exists(processedFile))
                {
                    var fi = new FileInfo(processedFile);

                    // ── Check if Phase 1 work would be lost ──────────
                    bool hasPhase1Work = false;
                    int clientIdsFound = 0;
                    try
                    {
                        var repo = new StudentCsvRepository(config);
                        var existing = repo.ReadAll();
                        clientIdsFound = existing.Count(s => !string.IsNullOrWhiteSpace(s.ClientId));
                        hasPhase1Work = clientIdsFound > 0;
                    }
                    catch { /* non-fatal — fall through to standard prompt */ }

                    string warningExtra = hasPhase1Work
                        ? $"\n\n⚠️  WARNING — DATA LOSS RISK:\n" +
                          $"  Phase 1 found {clientIdsFound} Client ID(s) that will be permanently erased.\n" +
                          $"  DuplicateResolved flags will also be reset.\n" +
                          $"  You will need to re-run Phase 1 from scratch."
                        : string.Empty;

                    var ans = MessageBox.Show(
                        this,
                        $"A processed CSV already exists:\n\n{processedFile}\n\n" +
                        $"  Last modified : {fi.LastWriteTime:yyyy-MM-dd HH:mm}\n" +
                        $"  Size          : {fi.Length / 1024.0:F1} KB\n" +
                        warningExtra +
                        $"\n\nDo you want to re-process and overwrite it?",
                        hasPhase1Work ? "⚠️  Data Loss Warning" : "Processed CSV Exists",
                        MessageBoxButtons.YesNo,
                        hasPhase1Work ? MessageBoxIcon.Warning : MessageBoxIcon.Question);

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

                MessageBox.Show(this,"✅ CSV processing completed successfully.\n\nSee the log for a preview and statistics.",
                    "Processing Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ CSV processing failed: {ex.Message}", ex);
                MessageBox.Show(this,$"❌ An unexpected error occurred:\n\n{ex.Message}",
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
                        this,
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
                        this,
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
                        this,
                        $"✅ Portable Chrome is ready!\n\n{chrome ?? chromeConfig.PortableChromeExtractTo}",
                        "Download Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this,"❌ Download failed.\nCheck the log for details.",
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
            if (string.IsNullOrWhiteSpace(txt_BaseDir.Text)) { MessageBox.Show(this,"❌ Base Directory cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (string.IsNullOrWhiteSpace(txt_SchoolName.Text)) { MessageBox.Show(this,"❌ School Name cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!int.TryParse(txtBox_BatchSize.Text, out int batchSize) || batchSize < 1) { MessageBox.Show(this,"❌ Batch Size must be a number greater than 0.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var dirError = WorkspaceInitializer.ValidateBaseDirectory(txt_BaseDir.Text);
            if (dirError != null) { MessageBox.Show(this,$"❌ {dirError}", "Directory Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

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
                MessageBox.Show(this,$"✅ Configuration saved!\n\n  Base Dir : {txt_BaseDir.Text}\n  School   : {txt_SchoolName.Text}\n  Grade    : {cb_Grade.SelectedItem}\n  Batch    : {batchSize}", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this,$"❌ Failed to save:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }


        // ── Phase 2: Validate PDFs against student records ────────────

        // ── Phase 2: Validate PDFs against student records ────────────
        private async void bt_ValidatePdf_Click(object sender, EventArgs e)
        {
            bt_ValidatePdf.Enabled = false;
            bt_ValidatePdf.Text = "⏳ Validating…";

            try
            {
                // ── Guard: Phase 1 must be complete ──────────────────
                if (!await CheckAllRowsProcessedAsync("PDF Validation"))
                    return;

                // ── Duplicate pre-check ───────────────────────────────
                if (!await CheckUnresolvedDuplicatesAsync("PDF Validation"))
                    return;

                var config = ConfigurationService.GetConfiguration();

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
                        this,
                        "❌ PDF Validation encountered errors and could not complete.\n\n" +
                        "Please check the log panel for details.",
                        "Validation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var bulkPdfConfig = ConfigurationService.GetBulkPdfExtractionConfig();
                var errorFolder = phase2Result.SessionErrorDir ?? bulkPdfConfig.GetErrorPath();

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("📊 VALIDATION SUMMARY");
                LoggerService.LogInformation(new string('═', 60));
                LoggerService.LogInformation($"   📄 Total PDFs found      : {phase2Result.TotalPdfs}");
                LoggerService.LogInformation($"   ✅ Matched to student    : {phase2Result.SuccessfullyProcessed}");
                LoggerService.LogInformation($"   ⚠️  Unmatched (errors)   : {phase2Result.FailedToMatch}");

                // ── Detect ClientId-only files that were matched this run ─────
                // These are files the user manually renamed to {ClientId}.pdf.
                // Phase 2 validates them but does NOT move them to 1 Consent Upload —
                // that is PrePhase3's job.  Warn the user explicitly so they know
                // they must also click Generate Upload CSV.
                var pdfSourcePath = bulkPdfConfig.GetOutputReadyPath();
                bool hasClientIdFilesReady = Directory.Exists(pdfSourcePath) &&
                    Directory.GetFiles(pdfSourcePath, "*.pdf")
                        .Any(f =>
                        {
                            var stem = Path.GetFileNameWithoutExtension(f).Trim();
                            if (stem.Contains('_')) return false;
                            if (long.TryParse(stem, out _)) return stem.Length >= 4;
                            return true;
                        });

                if (phase2Result.FailedToMatch > 0)
                {
                    LoggerService.LogWarning($"\n   ⚠️  {phase2Result.FailedToMatch} unmatched PDF(s) copied to:");
                    LoggerService.LogWarning($"       {errorFolder}");

                    foreach (var err in phase2Result.ErrorMessages)
                        LoggerService.LogWarning($"      • {err}");
                }

                var hasUnmatched = phase2Result.FailedToMatch > 0;

                // ── Build message ─────────────────────────────────────────────
                string clientIdNote = hasClientIdFilesReady
                    ? "\n\n📋 Manually renamed file(s) detected (e.g. 12345.pdf).\n" +
                      "   Validation is complete — you MUST also click\n" +
                      "   📄 Generate Upload CSV  to move them to the upload folder."
                    : string.Empty;

                MessageBox.Show(
                    this,
                    $"🔍 PDF Validation complete.\n\n" +
                    $"  📄 Total PDFs          : {phase2Result.TotalPdfs}\n" +
                    $"  ✅ Matched             : {phase2Result.SuccessfullyProcessed}\n" +
                    $"  ⚠️  Unmatched (errors) : {phase2Result.FailedToMatch}\n" +
                    (hasUnmatched
                        ? $"\n⚠️  Unmatched PDFs have been copied to:\n  {errorFolder}\n\nReview and correct them, then re-run validation."
                        : "\n✅ All PDFs matched.") +
                    clientIdNote +
                    (!hasUnmatched && !hasClientIdFilesReady
                        ? "\n\nYou may now click  📄 Generate Upload CSV."
                        : string.Empty),
                    hasUnmatched ? "Validation — Review Required" : "Validation Successful",
                    MessageBoxButtons.OK,
                    hasUnmatched ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected error during validation: {ex.Message}", ex);
                MessageBox.Show(
                    this,
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
                // ── Guard: Phase 1 must be complete ──────────────────
                if (!await CheckAllRowsProcessedAsync("PDF Validation"))
                    return;

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
                        this,
                        "❌ Upload CSV generation encountered errors.\n\n" +
                        "Review Validation_Results.csv for records with missing PDFs,\n" +
                        "then retry.",
                        "Generation Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // ── Already processed — nothing new to do ─────────────
                if (prePhase3Result.AlreadyProcessed)
                {
                    var pdfSourcePath = ConfigurationService.GetBulkPdfExtractionConfig().GetOutputReadyPath();
                    LoggerService.LogInformation("ℹ️  Already processed — nothing new to generate.");
                    MessageBox.Show(
                        this,
                        "ℹ️  Everything is already processed.\n\n" +
                        "  • The Upload CSV already exists.\n" +
                        "  • 3_Output_Ready is empty.\n\n" +
                        "If you still have unmatched PDFs to fix:\n" +
                        "  1. Rename each file to  {ClientId}.pdf\n" +
                        $"  2. Drop it into:\n     {pdfSourcePath}\n" +
                        "  3. Click Generate Upload CSV again.",
                        "Already Processed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // ── Log summary ───────────────────────────────────────
                if (prePhase3Result.SkippedMissingPdf > 0)
                    LoggerService.LogWarning(
                        $"⚠️  {prePhase3Result.SkippedMissingPdf} record(s) skipped — PDF not found.");

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("📊 UPLOAD CSV SUMMARY");
                LoggerService.LogInformation(new string('═', 60));
                LoggerService.LogInformation($"   ✅ New rows appended      : {prePhase3Result.UploadRecordsCreated}");
                LoggerService.LogInformation($"   📋 FileRose records       : {prePhase3Result.FileRoseRecordsCreated}");
                LoggerService.LogInformation($"   ⚠️  Skipped (no PDF)      : {prePhase3Result.SkippedMissingPdf}");
                LoggerService.LogInformation($"   ℹ️  Total records          : {prePhase3Result.TotalRecords}");
                LoggerService.LogInformation(new string('═', 60));

                // ── Build unmatched PDF warning ────────────────────────
                bool hasUnmatched = prePhase3Result.RemainingUnmatchedPdfs.Count > 0;
                bool hasMissing = prePhase3Result.SkippedMissingPdf > 0;
                var pdfReadyPath = ConfigurationService.GetBulkPdfExtractionConfig().GetOutputReadyPath();

                string unmatchedSection = string.Empty;
                if (hasUnmatched)
                {
                    // Show up to 10 filenames inline; tell user how many more in the log
                    var shown = prePhase3Result.RemainingUnmatchedPdfs.Take(10).ToList();
                    int extra = prePhase3Result.RemainingUnmatchedPdfs.Count - shown.Count;

                    var lines = string.Join("\n", shown.Select(f => $"    • {f}"));
                    if (extra > 0) lines += $"\n    … and {extra} more — see log panel";

                    unmatchedSection =
                        $"\n\n⚠️  {prePhase3Result.RemainingUnmatchedPdfs.Count} PDF(s) could NOT be matched " +
                        $"and are still in 3_Output_Ready:\n" +
                        lines +
                        $"\n\nTo fix each one:\n" +
                        $"  1. Look up the student's ClientId in Validation_Results.csv\n" +
                        $"  2. Rename the PDF to   {{ClientId}}.pdf   (e.g. 3678.pdf)\n" +
                        $"  3. Drop it into:\n     {pdfReadyPath}\n" +
                        $"  4. Click Generate Upload CSV again.";
                }

                string missingSection = string.Empty;
                if (hasMissing)
                {
                    missingSection =
                        $"\n\n⚠️  {prePhase3Result.SkippedMissingPdf} validated student(s) have no PDF at all.\n" +
                        "   Find the consent form, rename it to {ClientId}.pdf and re-run.";
                }

                string icon = hasUnmatched || hasMissing ? "⚠️" : "✅";
                string title = hasUnmatched || hasMissing
                    ? "Upload CSV Ready — Action Required"
                    : "Upload CSV Ready";
                MessageBoxIcon msgIcon = hasUnmatched || hasMissing
                    ? MessageBoxIcon.Warning
                    : MessageBoxIcon.Information;

                MessageBox.Show(
                    this,
                    $"{icon} Upload CSV updated.\n\n" +
                    $"  ✅ New rows appended      : {prePhase3Result.UploadRecordsCreated}\n" +
                    $"  📋 FileRose records       : {prePhase3Result.FileRoseRecordsCreated}\n" +
                    $"  ⚠️  Skipped (no PDF)      : {prePhase3Result.SkippedMissingPdf}\n" +
                    $"  📄 Unmatched in folder    : {prePhase3Result.RemainingUnmatchedPdfs.Count}" +
                    unmatchedSection +
                    missingSection +
                    (hasUnmatched || hasMissing
                        ? "\n\nYou may proceed to Phase 3, but these students will NOT be uploaded."
                        : "\n\nYou may now proceed to Phase 3 — Upload to PHIS."),
                    title, MessageBoxButtons.OK, msgIcon);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected error generating Upload CSV: {ex.Message}", ex);
                MessageBox.Show(
                    this,
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
        private async void bt_AppendFileRose_Click(object sender, EventArgs e)
        {
            bt_AppendFileRose.Enabled = false;
            bt_AppendFileRose.Text = "⏳ Extracting FileRose…";

            try
            {
                // ── Guard: Phase 1 must be complete ──────────────────
                if (!await CheckAllRowsProcessedAsync("PDF Validation"))
                    return;

                // ── Step 1: Extract (move) FileRose PDFs ──────────────────────
                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("🌹 STEP 1 — FileRose Extraction");
                LoggerService.LogInformation(new string('═', 60));

                var extractionResult = await Task.Run(() =>
                {
                    var svc = new ConsentSyncCore.Services.FileRoseExtractionService();
                    return svc.ExtractFileRose();
                });

                // ── Gate: hard block on ANY extraction error ──────────────────
                // Files with errors remain in the scan folder for the user to fix.
                // We NEVER append FileRose rows to the Upload CSV when errors exist.
                if (extractionResult.Errors > 0)
                {
                    var errorLines = extractionResult.ErrorFiles
                        .Take(8)
                        .Select(ef => $"  • {ef.FileName}\n      → {ef.Reason}");

                    string pendingSection = string.Empty;
                    if (extractionResult.PendingFileRoseRows.Count > 0)
                    {
                        var pendingLines = extractionResult.PendingFileRoseRows
                            .Take(8)
                            .Select(p => $"  • ClientId {p.ClientId}  — {p.LastName}, {p.FirstName}");
                        int extra = extractionResult.PendingFileRoseRows.Count - 8;

                        pendingSection =
                            $"\n\n⚠️  Students with IsFileRoseDefault=True but NOT yet extracted " +
                            $"(IsFileRoseExtracted=False):\n" +
                            string.Join("\n", pendingLines) +
                            (extra > 0 ? $"\n  … and {extra} more — see log panel" : "") +
                            "\n\nThese students will be MISSING from the FileRose upload until fixed.";
                    }

                    LoggerService.LogError(
                        $"❌ FileRose extraction blocked — {extractionResult.Errors} error(s). " +
                        "Upload CSV will NOT be updated.");

                    MessageBox.Show(
                        this,
                        $"❌ FileRose extraction encountered {extractionResult.Errors} error(s).\n\n" +
                        "Files with errors have been LEFT in the scan folder for you to fix:\n\n" +
                        string.Join("\n", errorLines) +
                        (extractionResult.Errors > 8
                            ? $"\n  … and {extractionResult.Errors - 8} more — see log panel" : "") +
                        pendingSection +
                        "\n\nThe Upload CSV has NOT been updated.\n\n" +
                        "How to fix:\n" +
                        "  1. Rename each problem file to {ClientId}.pdf  (e.g. 12345.pdf)\n" +
                        "  2. Ensure that ClientId appears in Validation_Results.csv\n" +
                        "     with ClientIdStatus=Found and IsFileRoseDefault=True\n" +
                        $"  3. Leave the corrected file in the scan folder:\n" +
                        $"     {ConsentSyncCore.Services.Configuration.ConfigurationService
                              .GetBulkPdfExtractionConfig().GetFileRoseScanPath()}\n" +
                        "  4. Click this button again.",
                        "FileRose Extraction Failed — Upload CSV NOT Updated",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // ── Summary: warn if pending rows remain (IsFileRoseDefault=True, not extracted) ─
                if (extractionResult.PendingFileRoseRows.Count > 0)
                {
                    var pendingLines = extractionResult.PendingFileRoseRows
                        .Take(8)
                        .Select(p => $"  • ClientId {p.ClientId}  — {p.LastName}, {p.FirstName}");
                    int extra = extractionResult.PendingFileRoseRows.Count - 8;

                    var answer = MessageBox.Show(
                        this,
                        $"⚠️  {extractionResult.PendingFileRoseRows.Count} student(s) have " +
                        "IsFileRoseDefault=True but no PDF was found in the scan folder " +
                        "(IsFileRoseExtracted=False):\n\n" +
                        string.Join("\n", pendingLines) +
                        (extra > 0 ? $"\n  … and {extra} more — see log panel" : "") +
                        "\n\nThese students will be MISSING from the FileRose upload.\n\n" +
                        "Continue appending for the successfully extracted files?",
                        "Pending FileRose Records",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (answer == DialogResult.No)
                    {
                        LoggerService.LogInformation(
                            "ℹ️  User chose to fix pending FileRose records before appending.");
                        return;
                    }
                }

                // ── Nothing extracted and nothing already in output folder ─────
                var phisWs = ConsentSyncCore.Services.Configuration.ConfigurationService
                    .GetPhisWorkspaceConfig();
                var outputDir = phisWs.GetFileRoseUploadPath();
                bool hasReadyPdfs = Directory.Exists(outputDir) &&
                                    Directory.GetFiles(outputDir, "*.pdf").Length > 0;

                if (!hasReadyPdfs)
                {
                    LoggerService.LogInformation(
                        "ℹ️  No FileRose PDFs in the output folder — nothing to append.");
                    MessageBox.Show(
                        this,
                        "ℹ️  No FileRose PDFs are ready for upload.\n\n" +
                        "Ensure the scan folder contains files named {ClientId}.pdf and that\n" +
                        "those ClientIds appear in Validation_Results.csv with\n" +
                        "ClientIdStatus=Found and IsFileRoseDefault=True.\n\n" +
                        $"Scan folder:\n" +
                        $"{ConsentSyncCore.Services.Configuration.ConfigurationService
                              .GetBulkPdfExtractionConfig().GetFileRoseScanPath()}",
                        "Nothing to Append",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                LoggerService.LogInformation(
                    $"   ✅ Extracted (moved) : {extractionResult.Extracted}  |  " +
                    $"Already done : {extractionResult.AlreadyExtracted}");

                // ── Step 2: Append rows to Upload_to_PHIS.csv ─────────────────
                bt_AppendFileRose.Text = "⏳ Appending…";
                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("🌹 STEP 2 — Append FileRose Rows to Upload CSV");
                LoggerService.LogInformation(new string('═', 60));

                var appendResult = await Task.Run(() =>
                {
                    var svc = new Orchestrator.Services.FileRoseAppendService();
                    return svc.AppendFileRoseRows();
                });

                if (appendResult.HasErrors)
                {
                    MessageBox.Show(
                        this,
                        "❌ FileRose append encountered errors.\n\n" +
                        string.Join("\n", appendResult.Messages.Take(5)) +
                        "\n\nCheck the log panel for details.",
                        "Append Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // ── Final summary ─────────────────────────────────────────────
                string pendingWarn = extractionResult.PendingFileRoseRows.Count > 0
                    ? $"\n\n⚠️  {extractionResult.PendingFileRoseRows.Count} student(s) with " +
                      "IsFileRoseDefault=True still not extracted — see log panel."
                    : string.Empty;

                MessageBox.Show(
                    this,
                    $"🌹 FileRose — Extraction & Append complete.\n\n" +
                    $"  Extraction:\n" +
                    $"    ✅ Moved to upload folder : {extractionResult.Extracted}\n" +
                    $"    ⏭️  Already extracted      : {extractionResult.AlreadyExtracted}\n\n" +
                    $"  Upload CSV:\n" +
                    $"    ✅ Appended               : {appendResult.Appended}\n" +
                    $"    ⏭️  Already exist          : {appendResult.AlreadyExist}\n" +
                    $"    ⚠️  No ClientId            : {appendResult.NoClientId}" +
                    pendingWarn +
                    (appendResult.Appended > 0
                        ? "\n\nYou may now proceed to Phase 3 — Upload to PHIS."
                        : "\n\nAll FileRose rows were already present — nothing new added."),
                    appendResult.Appended > 0 ? "FileRose Ready" : "Nothing New to Append",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected error in FileRose append: {ex.Message}", ex);
                MessageBox.Show(this,$"❌ Unexpected error:\n\n{ex.Message}",
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
                // ── Guard: Phase 1 must be complete ──────────────────
                if (!await CheckAllRowsProcessedAsync("PDF Validation"))
                    return;

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
                            this,
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
                        this,
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
                        this,
                        $"✅ All documents uploaded successfully to PHIS.\n\n" +
                        $"  ✅ Successful uploads : {phase3Result.SuccessfulUploads}\n" +
                        $"  📋 Total records     : {phase3Result.TotalRecords}\n\n" +
                        "The upload process is complete.",
                        "Upload Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        this,
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
                    this,
                    $"❌ A browser error occurred during upload.\n\n{ex.Message}\n\n" +
                    "The Chrome session may have timed out. Try running Phase 1 first\n" +
                    "to establish a fresh PHIS session.",
                    "Browser Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Unexpected error during upload: {ex.Message}", ex);
                MessageBox.Show(
                    this,
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
        /// 
        private async Task<bool> CheckUnresolvedDuplicatesAsync(string callerLabel)
        {
            try
            {
                var config = ConfigurationService.GetConfiguration();
                var repo = new StudentCsvRepository(config);
                var allStudents = await Task.Run(() => repo.ReadAll());
                var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();

                bool csvDirty = false;

                // ── Pass 1: same ClientId, different names ────────────────────
                // Only relevant when the NAMES are different across rows.
                // If a name-based folder already exists for a member, this ClientId
                // group is already handled — do NOT create a redundant ClientId folder.
                var clientIdGroups = allStudents
                    .Where(s => !string.IsNullOrWhiteSpace(s.ClientId))
                    .GroupBy(s => s.ClientId.Trim())
                    .Where(g => g.Count() > 1)
                    .ToList();

                foreach (var g in clientIdGroups)
                {
                    var clientId = g.Key;
                    var members = g.ToList();

                    // ── Check if a name-based folder already covers this group ──
                    // If ANY member already has a  5_Duplicate\{LastName}_{FirstName}\
                    // folder, this is a name-based duplicate — skip the ClientId folder.
                    bool nameBasedFolderExists = members.Any(s =>
                    {
                        string nameFolder = Path.Combine(
                            bulkConfig.GetDuplicateClientPath(),
                            SanitizeFolderName($"{s.LastName}_{s.FirstName}"));
                        return Directory.Exists(nameFolder);
                    });

                    if (nameBasedFolderExists)
                    {
                        LoggerService.LogInformation(
                            $"   ⏭️  ClientId {clientId} — name-based folder already exists, skipping ClientId folder.");
                        continue;
                    }

                    // ── Also skip if all members share exactly the same name ───
                    // (true name-based duplicate — already handled by Pass 2)
                    bool allSameName = members
                        .Select(s => $"{s.LastName.Trim().ToUpperInvariant()}_{s.FirstName.Trim().ToUpperInvariant()}")
                        .Distinct()
                        .Count() == 1;

                    if (allSameName)
                    {
                        LoggerService.LogInformation(
                            $"   ⏭️  ClientId {clientId} — all rows have same name, handled by name-based duplicate logic.");
                        continue;
                    }

                    // ── Genuinely different names sharing a ClientId ───────────
                    // Auto-create 5_Duplicate\{ClientId}\ with a README
                    var clientIdFolder = Path.Combine(
                        bulkConfig.GetDuplicateClientPath(), clientId);

                    if (!Directory.Exists(clientIdFolder))
                    {
                        Directory.CreateDirectory(clientIdFolder);
                        LoggerService.LogInformation(
                            $"   📁 Created: 5_Duplicate\\{clientId}\\");

                        File.WriteAllText(
                            Path.Combine(clientIdFolder, "README.txt"),
                            $"DUPLICATE ClientId — {clientId}\n" +
                            $"{new string('=', 42)}\n\n" +
                            $"These rows share the same ClientId but have different names\n" +
                            $"(likely a typo or OCR error in the source CSV):\n\n" +
                            string.Join("\n", members.Select(s =>
                                $"  • {s.LastName}, {s.FirstName}  (DOB: {s.DateOfBirth})")) +
                            $"\n\nWhat to do:\n" +
                            $"  1. Find the consent PDF(s) for this student.\n" +
                            $"  2. Place them in THIS folder:\n" +
                            $"     {clientIdFolder}\\\n" +
                            $"  3. Open immunizations_processed.csv.\n" +
                            $"  4. Set DuplicateResolved = true on ALL rows for ClientId {clientId}.\n" +
                            $"  5. Click Generate Upload CSV — PDFs will be merged automatically\n" +
                            $"     into {clientId}.pdf and processed.\n");
                    }

                    // Flag rows if not already flagged
                    foreach (var s in members.Where(s => !s.IsDuplicate))
                    {
                        s.IsDuplicate = true;
                        s.DuplicateResolved = false;
                        csvDirty = true;
                        LoggerService.LogWarning(
                            $"   🆕 ClientId duplicate flagged: [{clientId}] " +
                            $"{s.LastName}, {s.FirstName}");
                    }
                }

                if (csvDirty)
                {
                    await Task.Run(() => repo.SaveAll(allStudents));
                    LoggerService.LogWarning(
                        "   💾 immunizations_processed.csv updated with new duplicate flags.");
                }

                // ── Pass 2: same Name+DOB groups ──────────────────────────────
                var nameGroups = allStudents
                    .GroupBy(s =>
                        $"{s.LastName.Trim().ToUpperInvariant()}_" +
                        $"{s.FirstName.Trim().ToUpperInvariant()}_" +
                        $"{s.DateOfBirth.Trim()}")
                    .Where(g => g.Any(s => s.IsDuplicate) && !g.All(s => s.DuplicateResolved))
                    .ToList();

                // ── Pass 3: ClientId groups not yet fully resolved ─────────────
                var unresolvedClientIdGroups = clientIdGroups
                    .Where(g =>
                    {
                        var members = g.ToList();
                        // Only include if it was NOT skipped above
                        bool nameBasedFolderExists = members.Any(s =>
                        {
                            string nameFolder = Path.Combine(
                                bulkConfig.GetDuplicateClientPath(),
                                SanitizeFolderName($"{s.LastName}_{s.FirstName}"));
                            return Directory.Exists(nameFolder);
                        });
                        bool allSameName = members
                            .Select(s => $"{s.LastName.Trim().ToUpperInvariant()}_{s.FirstName.Trim().ToUpperInvariant()}")
                            .Distinct().Count() == 1;

                        return !nameBasedFolderExists && !allSameName &&
                               members.Any(s => !s.DuplicateResolved);
                    })
                    .ToList();

                // Merge both sets — deduplicate by key
                var shownKeys = new HashSet<string>();
                var displayLines = new List<string>();

                foreach (var g in nameGroups)
                {
                    var rep = g.First();
                    int resolved = g.Count(s => s.DuplicateResolved);
                    if (shownKeys.Add($"NAME_{rep.LastName}_{rep.FirstName}_{rep.DateOfBirth}"))
                        displayLines.Add(
                            $"  • {rep.LastName}, {rep.FirstName}  ({rep.DateOfBirth})" +
                            $"  [{resolved}/{g.Count()} resolved]");
                }

                foreach (var g in unresolvedClientIdGroups)
                {
                    var clientId = g.Key;
                    var members = g.ToList();
                    int resolved = members.Count(s => s.DuplicateResolved);
                    var clientIdFolder = Path.Combine(bulkConfig.GetDuplicateClientPath(), clientId);
                    if (shownKeys.Add($"CID_{clientId}"))
                        displayLines.Add(
                            $"  • ClientId {clientId}  — " +
                            string.Join(" / ", members.Select(s => $"{s.LastName}, {s.FirstName}")) +
                            $"  [{resolved}/{members.Count} resolved]\n" +
                            $"    📁 Drop PDFs into: {clientIdFolder}\\");
                }

                if (displayLines.Count == 0)
                    return true;    // ✅ all clear

                // ── Log ───────────────────────────────────────────────────────
                LoggerService.LogWarning($"\n⚠️  UNRESOLVED DUPLICATES — blocked {callerLabel}");
                LoggerService.LogWarning(new string('─', 60));
                foreach (var line in displayLines)
                    LoggerService.LogWarning($"   {line}");
                LoggerService.LogWarning(new string('─', 60));

                // ── Message box ───────────────────────────────────────────────
                var shown = displayLines.Take(10).ToList();
                if (displayLines.Count > 10)
                    shown.Add($"  … and {displayLines.Count - 10} more — see log panel");

                bool hasClientIdDups = unresolvedClientIdGroups.Count > 0;

                string fixSteps = hasClientIdDups
                    ? "For ClientId duplicates (folder already created ✅):\n" +
                      "  1. Drop the consent PDF(s) into the folder shown above.\n" +
                      "  2. Set DuplicateResolved = true on ALL rows for that ClientId.\n" +
                      "  3. Click Generate Upload CSV — PDFs are merged automatically.\n\n" +
                      "For name duplicates:\n" +
                      "  1. Review PDFs in  5_Duplicate\\{LastName}_{FirstName}\\\n" +
                      "  2. Set DuplicateResolved = true on ALL rows, then retry."
                    : "Next steps:\n" +
                      "  1. Open  immunizations_processed.csv\n" +
                      "  2. Review PDFs in  5_Duplicate\\{LastName}_{FirstName}\\\n" +
                      "  3. Set DuplicateResolved = true on ALL rows for each student.\n" +
                      "  4. Retry.";

                var answer = MessageBox.Show(
                    this,
                    $"⚠️  {displayLines.Count} duplicate group(s) have NOT been fully resolved.\n\n" +
                    string.Join(Environment.NewLine, shown) +
                    "\n\n" +
                    fixSteps +
                    "\n\n⚠️  Proceeding without resolving may cause incorrect PDFs to be uploaded.\n\n" +
                    "Continue anyway?",
                    "Unresolved Duplicates — Action Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer == DialogResult.No)
                {
                    LoggerService.LogInformation(
                        $"ℹ️  {callerLabel} cancelled — user chose to resolve duplicates first.");
                    return false;
                }

                LoggerService.LogWarning(
                    $"⚠️  User chose to continue {callerLabel} despite unresolved duplicates.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogWarning(
                    $"⚠️  Could not check for unresolved duplicates: {ex.Message}");
                return true;
            }
        }

        /// <summary>Strips invalid filename chars — mirrors MakeSafeFileName in DuplicateMergeService.</summary>
        private static string SanitizeFolderName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }


        private void bt_ScanPdf_Click(object sender, EventArgs e)
        {
            bt_ScanPdf.Enabled = false;
            bt_ScanPdf.Text = "⏳ Processing Scanned PDFs…";

            LoggerService.LogInformation("\n🧪 Processing Scanned Folder...");

            Task.Run(() =>
            {
                ConsentSyncCore.Services.Pdf.BulkPdfExtractor.ProcessScannedFolder();
                LoggerService.LogInformation("✅ Scanned folder processing complete.");
            }).ContinueWith(_ =>
            {
                this.InvokeIfRequired(() =>
                {
                    bt_ScanPdf.Enabled = true;
                    bt_ScanPdf.Text = "🧪 Process Scanned PDFs (Test)";

                    // ✅ Pass 'this' as owner so the dialog is always parented to
                    //    the main form and cannot slip behind it.
                    MessageBox.Show(
                        this,
                        "🧪 Scanned PDF processing complete.\n\n" +
                        "  • Extracted rows have been appended to the CSV.\n" +
                        "  • Successfully processed PDFs were moved to ScannedOK.\n" +
                        "  • PDFs that could not be fully extracted remain in the scanned folder.\n\n" +
                        "Check the log panel for a detailed per-file report.",
                        "Scanned PDFs Processed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                });
            });
        }




        // ── Guard: all rows must be processed before Phase 2 or Phase 3 ──
        /// <summary>
        /// Returns <c>true</c> if it is safe to proceed (no NotProcessed rows remain).
        /// Shows a detailed MessageBox and logs if any rows are still at
        /// <see cref="ClientIdStatus.NotProcessed"/> (= 0).
        /// </summary>
        private async Task<bool> CheckAllRowsProcessedAsync(string callerLabel)
        {
            try
            {
                var config = ConfigurationService.GetConfiguration();
                var repo = new StudentCsvRepository(config);

                var allStudents = await Task.Run(() => repo.ReadAll());

                var notProcessed = allStudents
                    .Where(s => s.ClientIdStatus == ConsentSyncCore.Models.ClientIdStatus.NotProcessed)
                    .ToList();

                if (notProcessed.Count == 0)
                    return true;    // ✅ all rows have been actioned

                // ── Log ───────────────────────────────────────────────────
                LoggerService.LogWarning($"\n⚠️  {callerLabel} blocked — {notProcessed.Count} row(s) still have ClientIdStatus = NotProcessed (0).");
                LoggerService.LogWarning(new string('─', 60));

                var preview = notProcessed.Take(10).ToList();
                foreach (var s in preview)
                    LoggerService.LogWarning($"   • {s.LastName}, {s.FirstName}  (DOB: {s.DateOfBirth})");

                if (notProcessed.Count > 10)
                    LoggerService.LogWarning($"   … and {notProcessed.Count - 10} more — see full CSV.");

                LoggerService.LogWarning(new string('─', 60));

                // ── Message box ───────────────────────────────────────────
                var shown = preview
                    .Select(s => $"  • {s.LastName}, {s.FirstName}  (DOB: {s.DateOfBirth})")
                    .ToList();

                if (notProcessed.Count > 10)
                    shown.Add($"  … and {notProcessed.Count - 10} more — see log panel");

                MessageBox.Show(
                    this,
                    $"⚠️  {callerLabel} cannot run yet.\n\n" +
                    $"  {notProcessed.Count} student(s) still have ClientIdStatus = NotProcessed.\n\n" +
                    "All rows must be either:\n" +
                    "  ✅  Found            — Client ID located on PHIS\n" +
                    "  ⚠️   NeedsManualReview — reviewed and filled in manually\n\n" +
                    "Unprocessed students:\n" +
                    string.Join(Environment.NewLine, shown) +
                    "\n\nNext steps:\n" +
                    "  1. Click  🔍 Search Client IDs on PHIS  (Phase 1).\n" +
                    "  2. For any remaining NeedsManualReview rows, open\n" +
                    "     immunizations_processed.csv and fill in the ClientId.\n" +
                    "  3. Re-run this step.",
                    $"{callerLabel} — Phase 1 Incomplete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return false;
            }
            catch (Exception ex)
            {
                // Non-fatal — let the caller proceed rather than silently blocking
                LoggerService.LogWarning($"⚠️  Could not verify ClientIdStatus rows: {ex.Message}");
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