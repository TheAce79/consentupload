using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.IO.Compression;
using System.Text.Json;

namespace ConsentSyncCore.Services.Browser
{
    public class ChromeDriverFactory
    {
        private readonly IConfiguration _config;
        private ChromeDriverConfig _chromeConfig;   // mutable — path may be auto-filled

        public ChromeDriverFactory(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _chromeConfig = ConfigurationService.GetChromeDriverConfig();
        }

        #region Public API

        /// <summary>
        /// Create a new Chrome WebDriver with configured options.
        /// Auto-detects ChromeDriver location when UsePortableChrome is true
        /// and ChromeDriverPath is not explicitly set.
        /// </summary>
        public IWebDriver CreateDriver()
        {
            try
            {
                Console.WriteLine("\n🌐 Initializing Chrome WebDriver...");

                // ── Auto-detect: if portable Chrome is used, look for chromedriver.exe
                //    in the same folder as chrome.exe (matches "Chrome for Testing" zip layout)
                if (_chromeConfig.UsePortableChrome &&
                    string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath))
                {
                    var portableDir = Path.GetDirectoryName(_chromeConfig.PortableChromePath);

                    // Also check the configured ChromeDriver extract folder
                    var candidates = new[]
                    {
                        portableDir,
                        _chromeConfig.ChromeDriverExtractTo,
                        Path.Combine(_chromeConfig.ChromeDriverExtractTo, "chromedriver-win64")
                    };

                    foreach (var candidate in candidates)
                    {
                        if (!string.IsNullOrWhiteSpace(candidate) &&
                            File.Exists(Path.Combine(candidate, "chromedriver.exe")))
                        {
                            _chromeConfig.ChromeDriverPath = candidate;
                            Console.WriteLine($"   🔍 Auto-detected ChromeDriver: {candidate}");
                            break;
                        }
                    }
                }

                var chromeOptions = BuildChromeOptions();

                // ── Resolve driver path ───────────────────────────────────────────
                string driverPath = !string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath) &&
                                    Directory.Exists(_chromeConfig.ChromeDriverPath)
                    ? _chromeConfig.ChromeDriverPath
                    : AppContext.BaseDirectory;

                Console.WriteLine($"   ChromeDriver path: {driverPath}");

                var service = ChromeDriverService.CreateDefaultService(driverPath);
                service.SuppressInitialDiagnosticInformation = true;
                service.HideCommandPromptWindow = true;

                var driver = new ChromeDriver(service, chromeOptions);

                Console.WriteLine("✅ Chrome WebDriver initialized successfully\n");
                return driver;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to initialize ChromeDriver: {ex.Message}");
                DisplayTroubleshootingTips();
                throw;
            }
        }

        /// <summary>
        /// Download the latest "Chrome for Testing" (portable chrome.exe + chromedriver.exe)
        /// for the configured channel (Stable / Beta / Dev / Canary).
        /// Call this from your UI's "Download Portable Chrome" button.
        /// Progress is reported via the optional <paramref name="progress"/> callback.
        /// </summary>
        public async Task<bool> DownloadPortableChromeAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                progress?.Invoke("📡 Fetching Chrome for Testing version information...");

                // ── Step 1: Fetch the versions JSON ──────────────────────────────
                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ConsentSync/1.0");

                var json = await http.GetStringAsync(
                    _chromeConfig.PortableChromeVersionsJsonUrl, cancellationToken);

                using var doc = JsonDocument.Parse(json);
                var channels = doc.RootElement.GetProperty("channels");
                var channel = channels.GetProperty(_chromeConfig.PortableChromeChannel);
                var version = channel.GetProperty("version").GetString() ?? "unknown";

                progress?.Invoke($"📦 Found {_chromeConfig.PortableChromeChannel} version: {version}");

                // ── Step 2: Locate win64 download URLs ───────────────────────────
                string? chromeUrl = null;
                string? driverUrl = null;

                var downloads = channel.GetProperty("downloads");

                if (downloads.TryGetProperty("chrome", out var chromeArr))
                    chromeUrl = GetWin64Url(chromeArr);

                if (downloads.TryGetProperty("chromedriver", out var driverArr))
                    driverUrl = GetWin64Url(driverArr);

                if (chromeUrl is null || driverUrl is null)
                {
                    progress?.Invoke("❌ Could not find win64 download URLs in the versions JSON.");
                    return false;
                }

                progress?.Invoke($"⬇️  Downloading Chrome:       {chromeUrl}");
                progress?.Invoke($"⬇️  Downloading ChromeDriver: {driverUrl}");

                // ── Step 3: Download both ZIPs in parallel ───────────────────────
                var chromeZip = Path.Combine(Path.GetTempPath(), $"chrome-win64-{version}.zip");
                var driverZip = Path.Combine(Path.GetTempPath(), $"chromedriver-win64-{version}.zip");

                await Task.WhenAll(
                    DownloadFileAsync(http, chromeUrl, chromeZip, cancellationToken),
                    DownloadFileAsync(http, driverUrl, driverZip, cancellationToken));

                // ── Step 4: Extract ──────────────────────────────────────────────
                progress?.Invoke($"📂 Extracting Chrome to:       {_chromeConfig.PortableChromeExtractTo}");
                ExtractZip(chromeZip, _chromeConfig.PortableChromeExtractTo, progress);

                progress?.Invoke($"📂 Extracting ChromeDriver to: {_chromeConfig.ChromeDriverExtractTo}");
                ExtractZip(driverZip, _chromeConfig.ChromeDriverExtractTo, progress);

                // ── Step 5: Cleanup temp ZIPs ────────────────────────────────────
                TryDelete(chromeZip);
                TryDelete(driverZip);

                // ── Step 6: Locate chrome.exe inside the extracted folder ────────
                // "Chrome for Testing" zips extract as:  chrome-win64\chrome.exe
                var detectedChrome = Directory
                    .GetFiles(_chromeConfig.PortableChromeExtractTo, "chrome.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (detectedChrome != null)
                {
                    progress?.Invoke($"✅ Portable Chrome ready: {detectedChrome}");
                    progress?.Invoke($"   Set PortableChromePath = \"{detectedChrome}\" in appsettings.json");
                    progress?.Invoke($"   Set UsePortableChrome  = true");
                }

                progress?.Invoke($"\n✅ Download complete! Version: {version}");
                return true;
            }
            catch (OperationCanceledException)
            {
                progress?.Invoke("⚠️  Download cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                progress?.Invoke($"❌ Download failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Chrome Options

        private ChromeOptions BuildChromeOptions()
        {
            var options = new ChromeOptions();
            Console.WriteLine($"🔧 Chrome options:");
            ConfigurePortableChrome(options);
            ConfigureDebuggerMode(options);
            ConfigureStandardOptions(options);
            ConfigureDownloadDirectory(options);

            if (_chromeConfig.HideAutomationIndicators)
            {
                options.AddExcludedArgument("enable-automation");
                options.AddAdditionalOption("useAutomationExtension", false);
                options.AddArgument("--disable-blink-features=AutomationControlled");
            }

            options.AddExcludedArgument("enable-logging");
            return options;
        }

        private void ConfigurePortableChrome(ChromeOptions options)
        {
            Console.WriteLine($"   Use portable Chrome: {_chromeConfig.UsePortableChrome}");
            if (_chromeConfig.UsePortableChrome && !string.IsNullOrWhiteSpace(_chromeConfig.PortableChromePath))
            {
                if (File.Exists(_chromeConfig.PortableChromePath))
                {
                    options.BinaryLocation = _chromeConfig.PortableChromePath;
                    Console.WriteLine($"   ✅ Portable Chrome: {_chromeConfig.PortableChromePath}");
                }
                else
                {
                    Console.WriteLine($"   ⚠️  Portable Chrome not found: {_chromeConfig.PortableChromePath}");
                    Console.WriteLine($"   ℹ️  Falling back to system Chrome");
                }
            }
            else if (!_chromeConfig.UsePortableChrome)
            {
                Console.WriteLine($"   Using system Chrome installation");
            }
        }

        private void ConfigureDebuggerMode(ChromeOptions options)
        {
            if (_chromeConfig.UseDebuggerMode)
            {
                options.DebuggerAddress = $"127.0.0.1:{_chromeConfig.DebuggerPort}";
                Console.WriteLine($"   🔌 Debugger mode: Port {_chromeConfig.DebuggerPort}");
            }
        }

        private void ConfigureStandardOptions(ChromeOptions options)
        {
            if (_chromeConfig.StartMaximized) options.AddArgument("--start-maximized");
            if (_chromeConfig.DisableNotifications) options.AddArgument("--disable-notifications");
            if (_chromeConfig.DisablePopupBlocking) options.AddArgument("--disable-popup-blocking");
            if (_chromeConfig.Headless)
            {
                options.AddArgument("--headless");
                options.AddArgument("--disable-gpu");
                Console.WriteLine($"   👻 Headless mode: Enabled");
            }
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
        }

        /// <summary>
        /// ✅ FIX: path is already resolved by ConfigurationService — just create + use it.
        /// </summary>
        private void ConfigureDownloadDirectory(ChromeOptions options)
        {
            if (string.IsNullOrWhiteSpace(_chromeConfig.DefaultDownloadChromeDirectory))
                return;

            // Path arrives already resolved from GetChromeDriverConfig()
            string resolvedPath = _chromeConfig.DefaultDownloadChromeDirectory;
            Directory.CreateDirectory(resolvedPath);

            var prefs = new Dictionary<string, object>
            {
                { "download.default_directory", resolvedPath },
                { "download.prompt_for_download", false },
                { "download.directory_upgrade", true },
                { "safebrowsing.enabled", false }
            };

            options.AddUserProfilePreference("download", prefs);
            Console.WriteLine($"   📁 Download directory: {resolvedPath}");
        }

        #endregion

        #region Download Helpers

        private static string? GetWin64Url(JsonElement platformArray)
        {
            foreach (var item in platformArray.EnumerateArray())
            {
                if (item.TryGetProperty("platform", out var p) &&
                    p.GetString() == "win64" &&
                    item.TryGetProperty("url", out var u))
                    return u.GetString();
            }
            return null;
        }

        private static async Task DownloadFileAsync(
            System.Net.Http.HttpClient http,
            string url,
            string destination,
            CancellationToken ct)
        {
            var bytes = await http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(destination, bytes, ct);
        }

        private static void ExtractZip(string zipPath, string extractTo, Action<string>? progress)
        {
            Directory.CreateDirectory(extractTo);

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                // Flatten one level: strip the top zip folder (e.g. "chrome-win64/")
                var relativePath = string.Join('\\', entry.FullName.Split('/').Skip(1));
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                var destPath = Path.Combine(extractTo, relativePath);

                if (entry.FullName.EndsWith('/'))        // directory entry
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                    progress?.Invoke($"   📄 {relativePath}");
                }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        #endregion

        #region Diagnostics

        public void DisplayConfiguration()
        {
            Console.WriteLine("\n🌐 Chrome Driver Configuration:");
            Console.WriteLine($"   Portable Chrome     : {_chromeConfig.UsePortableChrome}");
            if (_chromeConfig.UsePortableChrome)
            {
                Console.WriteLine($"   Chrome Path         : {_chromeConfig.PortableChromePath}");
                Console.WriteLine($"   chrome.exe exists   : {File.Exists(_chromeConfig.PortableChromePath)}");
            }
            Console.WriteLine($"   Driver Path         : {(string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath) ? "auto-detect" : _chromeConfig.ChromeDriverPath)}");
            Console.WriteLine($"   Channel             : {_chromeConfig.PortableChromeChannel}");
            Console.WriteLine($"   Chrome extract to   : {_chromeConfig.PortableChromeExtractTo}");
            Console.WriteLine($"   Driver extract to   : {_chromeConfig.ChromeDriverExtractTo}");
            Console.WriteLine($"   Download directory  : {_chromeConfig.DefaultDownloadChromeDirectory}");
        }

        public bool VerifyInstallation()
        {
            var driverPath = !string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath)
                ? _chromeConfig.ChromeDriverPath
                : AppContext.BaseDirectory;

            var exe = Path.Combine(driverPath, "chromedriver.exe");
            if (!File.Exists(exe)) { Console.WriteLine($"❌ ChromeDriver not found: {exe}"); return false; }
            Console.WriteLine($"✅ ChromeDriver found: {exe}");
            return true;
        }

        private void DisplayTroubleshootingTips()
        {
            Console.WriteLine("\n💡 TROUBLESHOOTING:");
            Console.WriteLine("   1. Use the UI 'Download Portable Chrome' button — it downloads");
            Console.WriteLine($"      chrome.exe + chromedriver.exe for channel: {_chromeConfig.PortableChromeChannel}");
            Console.WriteLine($"      Chrome  → {_chromeConfig.PortableChromeExtractTo}");
            Console.WriteLine($"      Driver  → {_chromeConfig.ChromeDriverExtractTo}");
            Console.WriteLine("   2. Or download manually:");
            Console.WriteLine("      https://googlechromelabs.github.io/chrome-for-testing/");
            Console.WriteLine("   3. Versions must match (same build number).");
        }

        public static string? GetChromeVersion()
        {
            string[] paths =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),        "Google\\Chrome\\Application\\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),     "Google\\Chrome\\Application\\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google\\Chrome\\Application\\chrome.exe")
            ];

            foreach (var p in paths)
            {
                if (!File.Exists(p)) continue;
                var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(p);
                Console.WriteLine($"✅ System Chrome: {p} ({v.FileVersion})");
                return v.FileVersion;
            }

            Console.WriteLine("⚠️  System Chrome not found");
            return null;
        }

        #endregion
    }
}