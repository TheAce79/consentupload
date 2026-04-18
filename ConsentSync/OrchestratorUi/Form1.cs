using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

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
            LoadConfiguration();
        }

        // ── Load all 4 config values into UI on startup ───────────────
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

        // ── Portable Chrome — check if already downloaded ────────────
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

            // Already downloaded — ask if they want to re-download
            if (File.Exists(chromeConfig.PortableChromePath))
            {
                var answer = MessageBox.Show(
                    $"✅ Portable Chrome is already installed at:\n{chromeConfig.PortableChromePath}\n\nRe-download anyway?",
                    "Already Installed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (answer == DialogResult.No) return;
            }

            // ── Lock button, start download ───────────────────────────
            btn_PortableChrome.Enabled = false;
            btn_PortableChrome.Text = "⏳ Downloading…";

            _chromeCts = new CancellationTokenSource();

            try
            {
                Log($"\n🌐 Downloading Portable Chrome ({chromeConfig.PortableChromeChannel} channel)...");
                Log($"   Chrome  → {chromeConfig.PortableChromeExtractTo}");
                Log($"   Driver  → {chromeConfig.ChromeDriverExtractTo}");

                var factory = new ChromeDriverFactory();
                bool success = await Task.Run(() =>
                    factory.DownloadPortableChromeAsync(
                        progress: msg => this.InvokeIfRequired(() => Log(msg)),
                        cancellationToken: _chromeCts.Token));

                if (success)
                {
                    Log("✅ Portable Chrome downloaded successfully!");

                    // Auto-detect chrome.exe and update appsettings.json
                    var detectedChrome = Directory
                        .GetFiles(chromeConfig.PortableChromeExtractTo, "chrome.exe",
                                  SearchOption.AllDirectories)
                        .FirstOrDefault();

                    if (detectedChrome != null)
                        SaveChromePathToConfig(detectedChrome);

                    MessageBox.Show(
                        $"✅ Portable Chrome is ready!\n\n{detectedChrome ?? chromeConfig.PortableChromeExtractTo}",
                        "Download Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Log("❌ Download failed — check the log above for details.");
                    MessageBox.Show("❌ Download failed.\nCheck the log for details.",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (OperationCanceledException)
            {
                Log("⚠️  Download cancelled.");
            }
            finally
            {
                _chromeCts?.Dispose();
                _chromeCts = null;
                RefreshChromeButtonState();
            }
        }

        // ── Persist detected chrome.exe path into appsettings.json ───
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
                Log($"   ✅ appsettings.json updated: UsePortableChrome = true");
                Log($"   ✅ PortableChromePath = {chromePath}");
            }
            catch (Exception ex)
            {
                Log($"   ⚠️  Could not update appsettings.json: {ex.Message}");
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

            // Validate write permission + create base dir
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
                    $"✅ Configuration saved and folders created!\n\n" +
                    $"  Base Dir   : {txt_BaseDir.Text}\n" +
                    $"  School     : {txt_SchoolName.Text}\n" +
                    $"  Grade      : {cb_Grade.SelectedItem}\n" +
                    $"  Batch Size : {batchSize}",
                    "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Failed to save configuration:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Log helper ────────────────────────────────────────────────
        private void Log(string msg) =>
            this.InvokeIfRequired(() =>
            {
                rtxt_Log.AppendText(msg + Environment.NewLine);
                rtxt_Log.ScrollToCaret();
            });

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