using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Browser
{
    public class ChromeDriverFactory
    {
        private readonly IConfiguration _config;
        private readonly ChromeDriverConfig _chromeConfig;

        public ChromeDriverFactory(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _chromeConfig = ConfigurationService.GetChromeDriverConfig();
        }


        #region Public API



        /// <summary>
        /// Create a new Chrome WebDriver with configured options
        /// </summary>
        public IWebDriver CreateDriver()
        {
            try
            {
                Console.WriteLine("\n🌐 Initializing Chrome WebDriver...");

                var chromeOptions = BuildChromeOptions();
                var driverService = BuildDriverService();

                IWebDriver driver;

                if (!string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath) &&
                    Directory.Exists(_chromeConfig.ChromeDriverPath))
                {
                    Console.WriteLine($"   ChromeDriver path: {_chromeConfig.ChromeDriverPath}");
                    driver = new ChromeDriver(_chromeConfig.ChromeDriverPath, chromeOptions);
                }
                else
                {
                    string defaultPath = AppContext.BaseDirectory;
                    Console.WriteLine($"   ChromeDriver path: {defaultPath} (default)");
                    driver = new ChromeDriver(defaultPath, chromeOptions);
                }

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
        /// Create driver with custom download directory
        /// </summary>
        public IWebDriver CreateDriverWithDownloadDirectory(string downloadPath)
        {
            var tempConfig = _chromeConfig;
            tempConfig.DefaultDownloadDirectory = downloadPath;

            return CreateDriver();
        }




        #endregion Public API



        #region Chrome Options Configuration


        /// <summary>
        /// Build Chrome options from configuration
        /// </summary>
        private ChromeOptions BuildChromeOptions()
        {
            var options = new ChromeOptions();

            Console.WriteLine($"🔧 Chrome Driver configuration:");

            // 1. Portable Chrome Support
            ConfigurePortableChrome(options);

            // 2. Debugger Mode Support
            ConfigureDebuggerMode(options);

            // 3. Standard Options
            ConfigureStandardOptions(options);

            // 4. Download Directory
            ConfigureDownloadDirectory(options);

            // 5. Hide Automation Indicators
            if (_chromeConfig.HideAutomationIndicators)
            {
                options.AddExcludedArgument("enable-automation");
                options.AddAdditionalOption("useAutomationExtension", false);
                options.AddArgument("--disable-blink-features=AutomationControlled");
            }

            // 6. Suppress DevTools Console Messages
            options.AddExcludedArgument("enable-logging");

            return options;
        }

        /// <summary>
        /// Configure portable Chrome executable
        /// </summary>
        private void ConfigurePortableChrome(ChromeOptions options)
        {
            Console.WriteLine($"   Use portable Chrome: {_chromeConfig.UsePortableChrome}");

            if (_chromeConfig.UsePortableChrome &&
                !string.IsNullOrWhiteSpace(_chromeConfig.PortableChromePath))
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

        /// <summary>
        /// Configure debugger mode (attach to existing Chrome instance)
        /// </summary>
        private void ConfigureDebuggerMode(ChromeOptions options)
        {
            if (_chromeConfig.UseDebuggerMode)
            {
                options.DebuggerAddress = $"127.0.0.1:{_chromeConfig.DebuggerPort}";
                Console.WriteLine($"   🔌 Debugger mode: Port {_chromeConfig.DebuggerPort}");
                Console.WriteLine($"   ℹ️  Start Chrome with: chrome.exe --remote-debugging-port={_chromeConfig.DebuggerPort}");
            }
        }

        /// <summary>
        /// Configure standard Chrome options
        /// </summary>
        private void ConfigureStandardOptions(ChromeOptions options)
        {
            if (_chromeConfig.StartMaximized)
            {
                options.AddArgument("--start-maximized");
            }

            if (_chromeConfig.DisableNotifications)
            {
                options.AddArgument("--disable-notifications");
            }

            if (_chromeConfig.DisablePopupBlocking)
            {
                options.AddArgument("--disable-popup-blocking");
            }

            if (_chromeConfig.Headless)
            {
                options.AddArgument("--headless");
                options.AddArgument("--disable-gpu");
                Console.WriteLine($"   👻 Headless mode: Enabled");
            }

            // Additional stability options
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
        }

        /// <summary>
        /// Configure download directory
        /// </summary>
        private void ConfigureDownloadDirectory(ChromeOptions options)
        {
            if (!string.IsNullOrWhiteSpace(_chromeConfig.DefaultDownloadDirectory))
            {
                // Ensure directory exists
                Directory.CreateDirectory(_chromeConfig.DefaultDownloadDirectory);

                var prefs = new Dictionary<string, object>
            {
                { "download.default_directory", _chromeConfig.DefaultDownloadDirectory },
                { "download.prompt_for_download", false },
                { "download.directory_upgrade", true },
                { "safebrowsing.enabled", false }
            };

                options.AddUserProfilePreference("download", prefs);
                Console.WriteLine($"   📁 Download directory: {_chromeConfig.DefaultDownloadDirectory}");
            }
        }

        /// <summary>
        /// Build ChromeDriver service (for advanced configuration)
        /// </summary>
        private ChromeDriverService BuildDriverService()
        {
            var driverPath = !string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath) &&
                            Directory.Exists(_chromeConfig.ChromeDriverPath)
                ? _chromeConfig.ChromeDriverPath
                : AppContext.BaseDirectory;

            var service = ChromeDriverService.CreateDefaultService(driverPath);

            // Suppress console output
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            return service;
        }



        #endregion Chrome Options Configuration



        #region Diagnostics & Help



        /// <summary>
        /// Display troubleshooting tips for ChromeDriver issues
        /// </summary>
        private void DisplayTroubleshootingTips()
        {
            Console.WriteLine($"\n💡 TROUBLESHOOTING TIPS:");
            Console.WriteLine($"   1. Ensure chromedriver.exe is in the same folder as your .exe");
            Console.WriteLine($"   2. Download from: https://googlechromelabs.github.io/chrome-for-testing/");
            Console.WriteLine($"   3. ChromeDriver version MUST match your Chrome browser version");
            Console.WriteLine($"   4. Check Chrome version: chrome://version");
            Console.WriteLine($"   5. Set ChromeDriverPath in appsettings.json if using custom location");
            Console.WriteLine($"   6. Run as administrator if you get permission errors");
            Console.WriteLine($"\n   Current Configuration:");
            Console.WriteLine($"   - Portable Chrome: {_chromeConfig.UsePortableChrome}");
            Console.WriteLine($"   - Chrome Path: {_chromeConfig.PortableChromePath}");
            Console.WriteLine($"   - Driver Path: {_chromeConfig.ChromeDriverPath}");
            Console.WriteLine($"   - Debugger Mode: {_chromeConfig.UseDebuggerMode}");
        }

        /// <summary>
        /// Display current Chrome configuration
        /// </summary>
        public void DisplayConfiguration()
        {
            Console.WriteLine("\n🌐 Chrome Driver Configuration:");
            Console.WriteLine($"   Portable Chrome: {_chromeConfig.UsePortableChrome}");

            if (_chromeConfig.UsePortableChrome)
            {
                Console.WriteLine($"   Chrome Path: {_chromeConfig.PortableChromePath}");
                Console.WriteLine($"   Exists: {File.Exists(_chromeConfig.PortableChromePath)}");
            }

            Console.WriteLine($"   Driver Path: {_chromeConfig.ChromeDriverPath ?? "Default"}");
            Console.WriteLine($"   Debugger Mode: {_chromeConfig.UseDebuggerMode}");

            if (_chromeConfig.UseDebuggerMode)
            {
                Console.WriteLine($"   Debugger Port: {_chromeConfig.DebuggerPort}");
            }

            Console.WriteLine($"   Start Maximized: {_chromeConfig.StartMaximized}");
            Console.WriteLine($"   Headless: {_chromeConfig.Headless}");
            Console.WriteLine($"   Disable Notifications: {_chromeConfig.DisableNotifications}");
            Console.WriteLine($"   Hide Automation: {_chromeConfig.HideAutomationIndicators}");

            if (!string.IsNullOrWhiteSpace(_chromeConfig.DefaultDownloadDirectory))
            {
                Console.WriteLine($"   Download Directory: {_chromeConfig.DefaultDownloadDirectory}");
            }
        }

        /// <summary>
        /// Verify ChromeDriver installation
        /// </summary>
        public bool VerifyInstallation()
        {
            try
            {
                var driverPath = !string.IsNullOrWhiteSpace(_chromeConfig.ChromeDriverPath)
                    ? _chromeConfig.ChromeDriverPath
                    : AppContext.BaseDirectory;

                var chromeDriverExe = Path.Combine(driverPath, "chromedriver.exe");

                if (!File.Exists(chromeDriverExe))
                {
                    Console.WriteLine($"❌ ChromeDriver not found at: {chromeDriverExe}");
                    return false;
                }

                Console.WriteLine($"✅ ChromeDriver found: {chromeDriverExe}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Verification failed: {ex.Message}");
                return false;
            }
        }






        #endregion Diagnostics & Help

    }
}
