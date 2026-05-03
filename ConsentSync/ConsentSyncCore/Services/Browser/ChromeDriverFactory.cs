using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;

namespace ConsentSyncCore.Services.Browser
{
    public class ChromeDriverFactory
    {
        private readonly IConfiguration _config;
        private ChromeDriverConfig _chromeConfig;

        public ChromeDriverFactory(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _chromeConfig = ConfigurationService.GetChromeDriverConfig();
        }

        #region Public API

        public IWebDriver CreateDriver()
        {
            try
            {
                LoggerService.LogInformation("\n🌐 Initializing Chrome WebDriver...");

                var chromeOptions = BuildChromeOptions();

                string driverPath = !string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath) &&
                                    Directory.Exists(_chromeConfig.ChromeDriverPath)
                    ? _chromeConfig.ChromeDriverPath
                    : AppContext.BaseDirectory;

                LoggerService.LogInformation($"   ChromeDriver path  : {driverPath}");
                LoggerService.LogInformation($"   chrome.exe path    : {(_chromeConfig.UsePortableChrome ? _chromeConfig.PortableChromePath : FindSystemChrome() ?? "system Chrome")}");

                var driver = new ChromeDriver(driverPath, chromeOptions);

                LoggerService.LogInformation("✅ Chrome WebDriver initialized successfully\n");
                return driver;
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Failed to initialize ChromeDriver: {ex.Message}", ex);
                DisplayTroubleshootingTips();
                throw;
            }
        }

        /// <summary>
        /// Downloads a matched Portable Chrome + ChromeDriver pair (Chrome for Testing).
        /// No admin rights required — extracts to the configured user-accessible folders.
        /// On network failure, logs manual download links so the user can install manually.
        /// </summary>
        public async Task<bool> DownloadPortableChromeAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string? version = null;
            string? chromeUrl = null;
            string? driverUrl = null;

            try
            {
                progress?.Invoke("📡 Fetching Chrome for Testing version information...");

                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ConsentSync/1.0");



                var json = await http.GetStringAsync(
                            _chromeConfig.PortableChromeVersionsJsonUrl, cancellationToken);

                var simulateFailure = false; // Set to true to test fallback instructions

                if (simulateFailure)
                {
                    json = await http.GetStringAsync(
                        "https://invalid-url-for-testing.com/versions.json", cancellationToken);
                }





                using var doc = JsonDocument.Parse(json);
                var channels = doc.RootElement.GetProperty("channels");
                var channel = channels.GetProperty(_chromeConfig.PortableChromeChannel);
                version = channel.GetProperty("version").GetString() ?? "unknown";

                progress?.Invoke($"📦 Found {_chromeConfig.PortableChromeChannel} version: {version}");

                var downloads = channel.GetProperty("downloads");

                if (downloads.TryGetProperty("chrome", out var ca)) chromeUrl = GetWin64Url(ca);
                if (downloads.TryGetProperty("chromedriver", out var da)) driverUrl = GetWin64Url(da);

                if (chromeUrl is null || driverUrl is null)
                {
                    progress?.Invoke("❌ Could not find win64 download URLs.");
                    return false;
                }

                progress?.Invoke($"⬇️  Downloading Chrome      : {chromeUrl}");
                progress?.Invoke($"⬇️  Downloading ChromeDriver: {driverUrl}");

                var chromeZip = Path.Combine(Path.GetTempPath(), $"chrome-win64-{version}.zip");
                var driverZip = Path.Combine(Path.GetTempPath(), $"chromedriver-win64-{version}.zip");

                await Task.WhenAll(
                    DownloadFileAsync(http, chromeUrl, chromeZip, cancellationToken),
                    DownloadFileAsync(http, driverUrl, driverZip, cancellationToken));

                progress?.Invoke($"📂 Extracting Chrome to      : {_chromeConfig.PortableChromeExtractTo}");
                ExtractZip(chromeZip, _chromeConfig.PortableChromeExtractTo, progress);

                progress?.Invoke($"📂 Extracting ChromeDriver to: {_chromeConfig.ChromeDriverExtractTo}");
                ExtractZip(driverZip, _chromeConfig.ChromeDriverExtractTo, progress);

                TryDelete(chromeZip);
                TryDelete(driverZip);

                var detectedChrome = Directory
                    .GetFiles(_chromeConfig.PortableChromeExtractTo, "chrome.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (detectedChrome != null)
                    progress?.Invoke($"✅ Portable Chrome ready: {detectedChrome}");

                progress?.Invoke($"✅ Download complete! Version: {version}");
                return true;
            }
            catch (OperationCanceledException)
            {
                progress?.Invoke("⚠️  Download cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                progress?.Invoke($"❌ Portable Chrome download failed: {ex.Message}");

                // ── Fallback: manual download instructions (no admin rights needed) ──
                try
                {
                    // If we fetched the version but download failed, use the known URLs.
                    // Otherwise point the user to the release page to pick manually.
                    bool hasUrls = !string.IsNullOrWhiteSpace(chromeUrl) &&
                                   !string.IsNullOrWhiteSpace(driverUrl);

                    progress?.Invoke("");
                    progress?.Invoke("💡 MANUAL INSTALL — no admin rights needed:");

                    if (!string.IsNullOrWhiteSpace(version) && version != "unknown")
                        progress?.Invoke($"   Detected latest {_chromeConfig.PortableChromeChannel} version: {version}");

                    if (hasUrls)
                    {
                        progress?.Invoke($"   1a. Download Portable Chrome ZIP:");
                        progress?.Invoke($"       {chromeUrl}");
                        progress?.Invoke($"   1b. Download ChromeDriver ZIP:");
                        progress?.Invoke($"       {driverUrl}");
                    }
                    else
                    {
                        progress?.Invoke($"   1. Visit the Chrome for Testing release page:");
                        progress?.Invoke($"      https://googlechromelabs.github.io/chrome-for-testing/");
                        progress?.Invoke($"      Pick the '{_chromeConfig.PortableChromeChannel}' channel → win64");
                        progress?.Invoke($"      Download both  chrome-win64.zip  and  chromedriver-win64.zip");
                    }

                    progress?.Invoke($"   2. Extract  chrome-win64.zip  into:");
                    progress?.Invoke($"      {_chromeConfig.PortableChromeExtractTo}");
                    progress?.Invoke($"   3. Extract  chromedriver-win64.zip  into:");
                    progress?.Invoke($"      {_chromeConfig.ChromeDriverExtractTo}");
                    progress?.Invoke($"   4. Click '🌐 Download Portable Chrome' again to verify.");
                }
                catch { /* non-fatal */ }

                return false;
            }
        }

        #endregion

        #region Chrome Options

        private ChromeOptions BuildChromeOptions()
        {
            var options = new ChromeOptions();
            LoggerService.LogInformation("🔧 Building Chrome options:");

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
            LoggerService.LogInformation($"   UsePortableChrome  : {_chromeConfig.UsePortableChrome}");

            if (_chromeConfig.UsePortableChrome && !string.IsNullOrWhiteSpace(_chromeConfig.PortableChromePath))
            {
                if (File.Exists(_chromeConfig.PortableChromePath))
                {
                    options.BinaryLocation = _chromeConfig.PortableChromePath;
                    LoggerService.LogInformation($"   ✅ chrome.exe      : {_chromeConfig.PortableChromePath}");
                }
                else
                {
                    LoggerService.LogWarning($"   ⚠️  chrome.exe not found: {_chromeConfig.PortableChromePath}");
                    LoggerService.LogWarning("   ⚠️  Falling back to system Chrome");
                }
            }
            else if (!_chromeConfig.UsePortableChrome)
            {
                LoggerService.LogInformation("   🖥️  Using system Chrome (BinaryLocation not set)");
            }
        }

        private void ConfigureDebuggerMode(ChromeOptions options)
        {
            if (_chromeConfig.UseDebuggerMode)
            {
                options.DebuggerAddress = $"127.0.0.1:{_chromeConfig.DebuggerPort}";
                LoggerService.LogInformation($"   🔌 Debugger port   : {_chromeConfig.DebuggerPort}");
            }
        }


        private void ConfigureStandardOptions(ChromeOptions options)
        {
            if (_chromeConfig.StartMaximized) options.AddArgument("--start-maximized");
            if (_chromeConfig.DisableNotifications) options.AddArgument("--disable-notifications");
            if (_chromeConfig.DisablePopupBlocking) options.AddArgument("--disable-popup-blocking");

            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");

            if (_chromeConfig.Headless)
            {
                options.AddArgument("--headless=new");
                options.AddArgument("--disable-gpu");
                LoggerService.LogInformation("   👻 Headless mode   : Enabled");
            }

            // ✅ Isolated temp profile
            var tempProfile = Path.Combine(Path.GetTempPath(), $"ConsentSync_Chrome_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempProfile);
            options.AddArgument($"--user-data-dir={tempProfile}");
            LoggerService.LogInformation($"   📁 Chrome profile  : {tempProfile}");
        }

        private void ConfigureDownloadDirectory(ChromeOptions options)
        {
            if (string.IsNullOrWhiteSpace(_chromeConfig.DefaultDownloadChromeDirectory)) return;

            Directory.CreateDirectory(_chromeConfig.DefaultDownloadChromeDirectory);

            var prefs = new Dictionary<string, object>
            {
                { "download.default_directory", _chromeConfig.DefaultDownloadChromeDirectory },
                { "download.prompt_for_download", false },
                { "download.directory_upgrade", true },
                { "safebrowsing.enabled", false }
            };

            options.AddUserProfilePreference("download", prefs);
            LoggerService.LogInformation($"   📁 Download directory: {_chromeConfig.DefaultDownloadChromeDirectory}");
        }

        #endregion

        #region Diagnostics

        public ChromeVersionCheckResult VerifyVersionMatch()
        {
            var result = new ChromeVersionCheckResult();

            if (_chromeConfig.UsePortableChrome)
            {
                result.ChromePath = _chromeConfig.PortableChromePath;
                if (!File.Exists(result.ChromePath))
                {
                    result.ChromeFound = false;
                    result.ErrorMessage = $"chrome.exe not found: {result.ChromePath}";
                    return result;
                }
                result.ChromeFound = true;
                var cvi = FileVersionInfo.GetVersionInfo(result.ChromePath);
                result.ChromeVersion = cvi.FileVersion ?? "unknown";
                result.ChromeMajor = cvi.FileMajorPart;
            }
            else
            {
                var systemPath = FindSystemChrome();
                if (systemPath == null)
                {
                    result.ChromeFound = false;
                    result.ChromePath = "Not found";
                    result.ChromeVersion = "—";
                    result.ErrorMessage = "System Chrome not found";
                    return result;
                }
                result.ChromeFound = true;
                result.ChromePath = systemPath;
                var cvi = FileVersionInfo.GetVersionInfo(systemPath);
                result.ChromeVersion = cvi.FileVersion ?? "unknown";
                result.ChromeMajor = cvi.FileMajorPart;
            }

            var driverDir = !string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath)
                ? _chromeConfig.ChromeDriverPath
                : _chromeConfig.ChromeDriverExtractTo;
            result.DriverPath = Path.Combine(driverDir, "chromedriver.exe");

            if (!File.Exists(result.DriverPath))
            {
                result.DriverFound = false;
                result.ErrorMessage = $"chromedriver.exe not found: {result.DriverPath}";
                return result;
            }

            result.DriverFound = true;
            var dvi = FileVersionInfo.GetVersionInfo(result.DriverPath);
            result.DriverVersion = dvi.FileVersion ?? "unknown";
            result.DriverMajor = dvi.FileMajorPart;

            result.VersionsMatch = result.ChromeMajor == result.DriverMajor;
            if (!result.VersionsMatch)
                result.ErrorMessage =
                    $"Major version mismatch: Chrome v{result.ChromeMajor} vs ChromeDriver v{result.DriverMajor}";

            return result;
        }

        public void DisplayConfiguration()
        {
            LoggerService.LogInformation("\n🌐 Chrome Driver Configuration:");
            LoggerService.LogInformation($"   Portable Chrome     : {_chromeConfig.UsePortableChrome}");
            if (_chromeConfig.UsePortableChrome)
            {
                LoggerService.LogInformation($"   Chrome Path         : {_chromeConfig.PortableChromePath}");
                LoggerService.LogInformation($"   chrome.exe exists   : {File.Exists(_chromeConfig.PortableChromePath)}");
            }
            LoggerService.LogInformation($"   Driver Path         : {(string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath) ? "auto-detect" : _chromeConfig.ChromeDriverPath)}");
        }

        public bool VerifyInstallation()
        {
            var driverPath = !string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath)
                ? _chromeConfig.ChromeDriverPath
                : AppContext.BaseDirectory;
            var exe = Path.Combine(driverPath, "chromedriver.exe");
            if (!File.Exists(exe)) { LoggerService.LogWarning($"❌ ChromeDriver not found: {exe}"); return false; }
            LoggerService.LogInformation($"✅ ChromeDriver found: {exe}");
            return true;
        }

        private void DisplayTroubleshootingTips()
        {
            LoggerService.LogInformation("\n💡 TROUBLESHOOTING:");
            LoggerService.LogInformation($"   Chrome  → {(_chromeConfig.UsePortableChrome ? _chromeConfig.PortableChromePath : "system Chrome")}");
            LoggerService.LogInformation($"   Driver  → {_chromeConfig.ChromeDriverPath}");
            LoggerService.LogInformation("   • Versions must match (same major number)");
            LoggerService.LogInformation("   • Use 🌐 Download Portable Chrome to get a matched pair");
            LoggerService.LogInformation("   • https://googlechromelabs.github.io/chrome-for-testing/");
        }

        private void LogVersionInfo()
        {
            try
            {
                string? chromePath = _chromeConfig.UsePortableChrome
                    ? _chromeConfig.PortableChromePath
                    : FindSystemChrome();

                if (chromePath != null)
                {
                    if (!_chromeConfig.UsePortableChrome)
                        LoggerService.LogInformation($"   🖥️  System Chrome   : {chromePath}");
                    var cv = FileVersionInfo.GetVersionInfo(chromePath);
                    LoggerService.LogInformation($"   🔢 Chrome version   : {cv.FileVersion} (major {cv.FileMajorPart})");
                }

                var driverExe = Path.Combine(_chromeConfig.ChromeDriverPath, "chromedriver.exe");
                if (File.Exists(driverExe))
                {
                    var dv = FileVersionInfo.GetVersionInfo(driverExe);
                    LoggerService.LogInformation($"   🔢 Driver version   : {dv.FileVersion} (major {dv.FileMajorPart})");
                    if (chromePath != null)
                    {
                        var cm = FileVersionInfo.GetVersionInfo(chromePath).FileMajorPart;
                        if (cm != dv.FileMajorPart)
                            LoggerService.LogWarning($"   ⚠️  VERSION MISMATCH — Chrome {cm} vs Driver {dv.FileMajorPart}");
                        else
                            LoggerService.LogInformation($"   ✅ Versions match   : major {cm}");
                    }
                }
            }
            catch (Exception ex) { LoggerService.LogWarning($"   ⚠️  Could not read version info: {ex.Message}"); }
        }

        public static string? FindSystemChrome()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "Application", "chrome.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        public static string? GetChromeVersion()
        {
            var path = FindSystemChrome();
            if (path == null) { LoggerService.LogInformation("⚠️  System Chrome not found"); return null; }
            var v = FileVersionInfo.GetVersionInfo(path);
            LoggerService.LogInformation($"✅ System Chrome: {path} ({v.FileVersion})");
            return v.FileVersion;
        }

        #endregion

        #region Download Helpers


        /// <summary>
        /// Downloads the chromedriver.exe that exactly matches the currently installed
        /// system Chrome version and places it in ChromeDriverPath.
        /// No admin rights required — writes only to the configured user-accessible folder.
        /// On network failure, logs a manual download link so the user can install without admin rights.
        /// </summary>
        public async Task<bool> UpdateSystemChromeDriverAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string? chromeVersion = null;
            string? downloadUrl = null;

            try
            {
                // ── Step 1: Detect system Chrome version ─────────────────────
                var chromePath = FindSystemChrome();
                if (chromePath == null)
                {
                    progress?.Invoke("❌ System Chrome not found. Cannot determine required driver version.");
                    return false;
                }

                chromeVersion = FileVersionInfo.GetVersionInfo(chromePath).FileVersion;
                if (string.IsNullOrWhiteSpace(chromeVersion))
                {
                    progress?.Invoke("❌ Could not read system Chrome version.");
                    return false;
                }

                progress?.Invoke($"🔍 System Chrome detected : {chromeVersion}");
                progress?.Invoke($"   Path                  : {chromePath}");

                // ── Step 2: Check if current driver already matches ───────────
                var driverExe = Path.Combine(_chromeConfig.ChromeDriverPath, "chromedriver.exe");
                if (File.Exists(driverExe))
                {
                    var currentDriver = FileVersionInfo.GetVersionInfo(driverExe).FileVersion ?? "";
                    if (currentDriver == chromeVersion)
                    {
                        progress?.Invoke($"✅ ChromeDriver already matches Chrome {chromeVersion} — no update needed.");
                        return true;
                    }
                    progress?.Invoke($"⚠️  Driver mismatch: have {currentDriver}, need {chromeVersion}");
                }

                // ── Step 3: Build download URL & attempt download ─────────────
                downloadUrl = $"https://storage.googleapis.com/chrome-for-testing-public/{chromeVersion}/win64/chromedriver-win64.zip";
                progress?.Invoke($"⬇️  Downloading ChromeDriver {chromeVersion}...");
                progress?.Invoke($"   URL: {downloadUrl}");

                var handler = new HttpClientHandler { UseProxy = true, Proxy = WebRequest.DefaultWebProxy };
                using var http = new HttpClient(handler);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ConsentSync/1.0");

                byte[] bytes;
                try
                {
                    bytes = await http.GetByteArrayAsync(downloadUrl, cancellationToken);
                }
                catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("404"))
                {
                    // ── Fallback: try major.minor.build.0 variant ─────────────
                    var parts = chromeVersion.Split('.');
                    var approx = $"{parts[0]}.{parts[1]}.{parts[2]}.0";
                    downloadUrl = $"https://storage.googleapis.com/chrome-for-testing-public/{approx}/win64/chromedriver-win64.zip";
                    progress?.Invoke($"   ⚠️  Exact build not found, trying {approx}...");
                    progress?.Invoke($"   URL: {downloadUrl}");
                    bytes = await http.GetByteArrayAsync(downloadUrl, cancellationToken);
                }

                // ── Step 4: Extract into temp folder ─────────────────────────
                var zipPath = Path.Combine(Path.GetTempPath(), $"chromedriver-{chromeVersion}.zip");
                var extractDir = Path.Combine(Path.GetTempPath(), $"chromedriver-{chromeVersion}");

                await File.WriteAllBytesAsync(zipPath, bytes, cancellationToken);
                progress?.Invoke("📂 Extracting...");

                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, recursive: true);
                ZipFile.ExtractToDirectory(zipPath, extractDir);
                TryDelete(zipPath);

                // ── Step 5: Locate chromedriver.exe ──────────────────────────
                var extracted = Directory.GetFiles(extractDir, "chromedriver.exe", SearchOption.AllDirectories)
                                         .FirstOrDefault();
                if (extracted == null)
                {
                    progress?.Invoke("❌ chromedriver.exe not found in downloaded archive.");
                    return false;
                }

                // ── Step 6: Kill running chromedriver & copy ──────────────────
                try
                {
                    foreach (var proc in Process.GetProcessesByName("chromedriver"))
                    {
                        proc.Kill();
                        proc.WaitForExit(3_000);
                    }
                }
                catch { /* non-fatal */ }

                Directory.CreateDirectory(_chromeConfig.ChromeDriverPath);
                File.Copy(extracted, driverExe, overwrite: true);
                TryDelete(extractDir);

                // ── Step 7: Verify ────────────────────────────────────────────
                var installedVersion = FileVersionInfo.GetVersionInfo(driverExe).FileVersion;
                progress?.Invoke($"✅ ChromeDriver updated successfully!");
                progress?.Invoke($"   Installed version : {installedVersion}");
                progress?.Invoke($"   Location          : {driverExe}");

                return true;
            }
            catch (OperationCanceledException)
            {
                progress?.Invoke("⚠️  Update cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                progress?.Invoke($"❌ ChromeDriver update failed: {ex.Message}");

                // ── Fallback: manual download instructions (no admin rights needed) ──
                try
                {
                    if (!string.IsNullOrWhiteSpace(chromeVersion))
                    {
                        var manualUrl = downloadUrl
                            ?? $"https://storage.googleapis.com/chrome-for-testing-public/{chromeVersion}/win64/chromedriver-win64.zip";

                        progress?.Invoke($"❌ Automatic download failed. (Network/Firewall restriction detected)");

                        // Provide a clickable link in the UI if possible, or clear text for documentation
                        progress?.Invoke("\n🛑 FIREWALL DETECTED: Vitalité security prevents automatic downloads.");
                        progress?.Invoke("Follow these steps to update manually:");
                        progress?.Invoke($"1. Open Chrome and paste this URL: {manualUrl}");
                        progress?.Invoke($"2. Download the ZIP file.");
                        progress?.Invoke($"3. Open the ZIP -> Open folder 'chromedriver-win64' -> Copy 'chromedriver.exe' to:");
                        progress?.Invoke($"   📂 {_chromeConfig.ChromeDriverPath}");
                    }
                }
                catch { /* non-fatal */ }

                return false;
            }
        }






        private static string? GetWin64Url(JsonElement arr)
        {
            foreach (var item in arr.EnumerateArray())
                if (item.TryGetProperty("platform", out var p) && p.GetString() == "win64" &&
                    item.TryGetProperty("url", out var u))
                    return u.GetString();
            return null;
        }

        private static async Task DownloadFileAsync(System.Net.Http.HttpClient http, string url, string dest, CancellationToken ct)
            => await File.WriteAllBytesAsync(dest, await http.GetByteArrayAsync(url, ct), ct);

        private static void ExtractZip(string zipPath, string extractTo, Action<string>? progress)
        {
            Directory.CreateDirectory(extractTo);
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                var rel = string.Join('\\', entry.FullName.Split('/').Skip(1));
                if (string.IsNullOrWhiteSpace(rel)) continue;
                var dest = Path.Combine(extractTo, rel);
                if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(dest);
                else { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); entry.ExtractToFile(dest, overwrite: true); }
            }
        }

        private static void TryDelete(string path)
        { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        #endregion
    }
}