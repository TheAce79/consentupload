using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Configuration;
using CsvProcessing;
using Microsoft.Extensions.Logging;
using Orchestrator;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Orchestrator.BulkPdfExtraction;

namespace OrchestratorUi
{
    public partial class Form1 : Form
    {
        private static readonly string AppSettingsPath =
            Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        private CancellationTokenSource? _chromeCts;

        public Form1()
        {
            InitializeComponent();

            // ── Hook ALL LoggerService output → rtxt_Log + log files ─
            // LoggerService already writes to file internally.
            // This subscription adds the UI stream on top.
            LoggerService.LogMessage += OnLogMessage;

            LoadConfiguration();
        }

        // ── Route every LoggerService call into rtxt_Log ──────────────
        private void OnLogMessage(object? sender, LogEventArgs e)
        {
            this.InvokeIfRequired(() =>
            {
                // Colour-code by level for readability
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

        // ── Button 1: Extract Bulk PDF ────────────────────────────────
        private async void btn_ExtractBulk_Click(object sender, EventArgs e)
        {
            btn_ExtractBulk.Enabled = false;
            btn_ExtractBulk.Text = "⏳ Extracting…";

            try
            {
                var config = ConfigurationService.GetConfiguration();
                var bulkOrchestrator = new BulkPdfExtractionOrchestrator(config);

                if (!bulkOrchestrator.IsPdfAvailable())
                {
                    LoggerService.LogInformation("💡 No bulk PDF found in input folders — nothing to extract.");
                    MessageBox.Show(
                        "No PDF files found in the input folders.\n\nDrop a bulk PDF into:\n" +
                        ConfigurationService.GetBulkPdfExtractionConfig().GetInputBulkPath(),
                        "No PDF Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                LoggerService.LogInformation("\n📄 Starting Bulk PDF Extraction...");

                var result = await Task.Run(() => bulkOrchestrator.RunAsync());

                if (result.Success)
                {
                    LoggerService.LogInformation($"✅ Bulk extraction complete — {result.TotalExtracted} file(s) extracted.");
                    MessageBox.Show(
                        $"✅ Extraction complete!\n\n  Extracted : {result.TotalExtracted}\n  Errors    : {result.FailedExtractions}",
                        "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    LoggerService.LogWarning($"⚠️  Extraction finished with errors: {result.ErrorMessage}");
                    var answer = MessageBox.Show(
                        $"⚠️  Extraction had errors.\n\n{result.ErrorMessage}\n\nContinue anyway?",
                        "Errors Detected", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (answer == DialogResult.No) return;
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Bulk extraction failed: {ex.Message}", ex);
                MessageBox.Show($"❌ Extraction failed:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                var csvRepo = new StudentCsvRepository(config);

                LoggerService.LogInformation("\n" + new string('═', 60));
                LoggerService.LogInformation("📋 PRE-PHASE: CSV Processing");
                LoggerService.LogInformation(new string('═', 60));

                await Task.Run(() =>
                {
                    if (!csvRepo.ProcessedCsvExists())
                    {
                        LoggerService.LogInformation("📄 No processed CSV found — running processor...");
                        csvRepo.ProcessRawCsv();
                    }
                    else
                    {
                        LoggerService.LogInformation("✔  Processed CSV already exists — skipping re-process.");
                        LoggerService.LogInformation("   Delete the output CSV to force a re-process.");
                    }

                    csvRepo.PreviewProcessedCsv(3);
                    csvRepo.DisplayStatistics();
                });

                MessageBox.Show("✅ CSV processing complete!\nSee the log for statistics.",
                    "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ CSV processing failed: {ex.Message}", ex);
                MessageBox.Show($"❌ CSV processing failed:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btn_ProcessCsv.Enabled = true;
                btn_ProcessCsv.Text = "📋 Process CSV";
            }
        }

        // ── Portable Chrome — check if already downloaded ─────────────
        private void RefreshChromeButtonState()
        {
            var chromeConfig = ConfigurationService.GetChromeDriverConfig();
            bool exists = File.Exists(chromeConfig.PortableChromePath);

            btn_PortableChrome.Text = exists ? "✅ Chrome Ready" : "🌐 Download Portable Chrome";
            btn_PortableChrome.BackColor = exists ? Color.DarkGreen : Color.SteelBlue;
            btn_PortableChrome.Enabled = true;
        }

        // ── Download Portable Chrome ──────────────────────────────────
        private async void btn_PortableChrome_Click(object sender, EventArgs e)
        {
            var chromeConfig = ConfigurationService.GetChromeDriverConfig();

            if (File.Exists(chromeConfig.PortableChromePath))
            {
                var answer = MessageBox.Show(
                    $"✅ Portable Chrome is already installed at:\n{chromeConfig.PortableChromePath}\n\nRe-download anyway?",
                    "Already Installed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer == DialogResult.No) return;
            }

            btn_PortableChrome.Enabled = false;
            btn_PortableChrome.Text = "⏳ Downloading…";
            _chromeCts = new CancellationTokenSource();

            try
            {
                LoggerService.LogInformation($"\n🌐 Downloading Portable Chrome ({chromeConfig.PortableChromeChannel} channel)...");
                LoggerService.LogInformation($"   Chrome → {chromeConfig.PortableChromeExtractTo}");
                LoggerService.LogInformation($"   Driver → {chromeConfig.ChromeDriverExtractTo}");

                var factory = new ChromeDriverFactory();
                bool success = await Task.Run(() =>
                    factory.DownloadPortableChromeAsync(
                        progress: msg => LoggerService.LogInformation(msg),
                        cancellationToken: _chromeCts.Token));

                if (success)
                {
                    var detectedChrome = Directory
                        .GetFiles(chromeConfig.PortableChromeExtractTo, "chrome.exe", SearchOption.AllDirectories)
                        .FirstOrDefault();

                    if (detectedChrome != null)
                        SaveChromePathToConfig(detectedChrome);

                    MessageBox.Show($"✅ Portable Chrome is ready!\n\n{detectedChrome ?? chromeConfig.PortableChromeExtractTo}",
                        "Download Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("❌ Download failed.\nCheck the log for details.",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (OperationCanceledException)
            {
                LoggerService.LogWarning("⚠️  Download cancelled.");
            }
            finally
            {
                _chromeCts?.Dispose();
                _chromeCts = null;
                RefreshChromeButtonState();
            }
        }

        // ── Persist chrome.exe path to appsettings.json ───────────────
        private void SaveChromePathToConfig(string chromePath)
        {
            try
            {
                var json = File.ReadAllText(AppSettingsPath);
                var node = JsonNode.Parse(json)!;
                node["ChromeDriver"]!["PortableChromePath"] = chromePath;
                node["ChromeDriver"]!["UsePortableChrome"] = true;
                File.WriteAllText(AppSettingsPath,
                    node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                ConfigurationService.ReloadConfiguration();
                LoggerService.LogInformation($"   ✅ PortableChromePath saved: {chromePath}");
            }
            catch (Exception ex)
            {
                LoggerService.LogWarning($"   ⚠️  Could not update appsettings.json: {ex.Message}");
            }
        }

        // ── Browse for Base Directory ─────────────────────────────────
        private void btn_BrowseDir_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select the Base Directory (e.g. C:\\PHIS)";
            folderBrowserDialog1.SelectedPath = txt_BaseDir.Text;
            folderBrowserDialog1.UseDescriptionForTitle = true;

            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txt_BaseDir.Text = folderBrowserDialog1.SelectedPath;
        }

        // ── Save all 4 values to appsettings.json ────────────────────
        private void btn_SaveConfig_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txt_BaseDir.Text))
            {
                MessageBox.Show("❌ Base Directory cannot be empty.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(txt_SchoolName.Text))
            {
                MessageBox.Show("❌ School Name cannot be empty.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!int.TryParse(txtBox_BatchSize.Text, out int batchSize) || batchSize < 1)
            {
                MessageBox.Show("❌ Batch Size must be a number greater than 0.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var dirError = WorkspaceInitializer.ValidateBaseDirectory(txt_BaseDir.Text);
            if (dirError != null)
            {
                MessageBox.Show($"❌ {dirError}", "Directory Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                var json = File.ReadAllText(AppSettingsPath);
                var node = JsonNode.Parse(json)!;
                node["BaseDirectory"] = txt_BaseDir.Text;
                node["SchoolContext"]!["SchoolName"] = txt_SchoolName.Text;
                node["SchoolContext"]!["Grade"] = cb_Grade.SelectedItem!.ToString();
                node["PhisAutomation"]!["BatchSize"] = batchSize;

                File.WriteAllText(AppSettingsPath,
                    node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                ConfigurationService.ReloadConfiguration();
                WorkspaceInitializer.EnsureAllFoldersExist();

                MessageBox.Show(
                    $"✅ Configuration saved!\n\n" +
                    $"  Base Dir   : {txt_BaseDir.Text}\n" +
                    $"  School     : {txt_SchoolName.Text}\n" +
                    $"  Grade      : {cb_Grade.SelectedItem}\n" +
                    $"  Batch Size : {batchSize}",
                    "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Failed to save:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Accept numbers only ───────────────────────────────────────
        private void txtBox_BatchSize_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != (char)Keys.Back)
                e.Handled = true;
        }

        // ── Prevent paste of non-numeric text ────────────────────────
        private void txtBox_BatchSize_TextChanged(object sender, EventArgs e)
        {
            if (!int.TryParse(txtBox_BatchSize.Text, out _) && txtBox_BatchSize.Text.Length > 0)
            {
                txtBox_BatchSize.Text = txtBox_BatchSize.Text[..^1];
                txtBox_BatchSize.SelectionStart = txtBox_BatchSize.Text.Length;
            }
        }

        // ── Cleanup on close ─────────────────────────────────────────
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            LoggerService.LogMessage -= OnLogMessage;
            base.OnFormClosing(e);
        }
    }

    // ── Thread-safe invoke helper ─────────────────────────────────────
    internal static class ControlExtensions
    {
        public static void InvokeIfRequired(this Control c, Action a)
        {
            if (c.InvokeRequired) c.Invoke(a);
            else a();
        }
    }
}