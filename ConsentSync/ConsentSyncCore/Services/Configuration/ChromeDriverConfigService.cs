using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;

namespace ConsentSyncCore.Services.Configuration
{
    public partial class ConfigurationService
    {
        public static ChromeDriverConfig GetChromeDriverConfig()
        {
            var config = GetConfiguration();

            var portableChromePath = ResolvePath(config["ChromeDriver:PortableChromePath"] ?? "");
            var chromeDriverPath = ResolvePath(config["ChromeDriver:ChromeDriverPath"] ?? "");
            var downloadDir = ResolvePath(config["ChromeDriver:Download:DefaultDownloadChromeDirectory"] ?? "");
            var chromeExtractTo = ResolvePath(config["ChromeDriver:Download:PortableChromeExtractTo"] ?? "");
            var driverExtractTo = ResolvePath(config["ChromeDriver:Download:ChromeDriverExtractTo"] ?? "");

            Console.WriteLine($"\n🌐 ChromeDriver paths (resolved):");
            Console.WriteLine($"   PortableChromePath  : {(string.IsNullOrWhiteSpace(portableChromePath) ? "(empty)" : portableChromePath)}");
            Console.WriteLine($"   ChromeDriverPath    : {(string.IsNullOrWhiteSpace(chromeDriverPath) ? "(empty — auto-detect or AppBaseDir)" : chromeDriverPath)}");
            Console.WriteLine($"   DownloadDirectory   : {(string.IsNullOrWhiteSpace(downloadDir) ? "(empty)" : downloadDir)}");

            bool usePortable = config.GetValue<bool>("ChromeDriver:UsePortableChrome", false);

            if (usePortable && !string.IsNullOrWhiteSpace(portableChromePath) && !File.Exists(portableChromePath))
            {
                Console.WriteLine($"   ⚠️  PortableChromePath does not exist: {portableChromePath}");
                Console.WriteLine($"      Use the UI download button, or set UsePortableChrome=false.");
            }

            return new ChromeDriverConfig
            {
                UsePortableChrome = usePortable,
                PortableChromePath = portableChromePath,
                ChromeDriverPath = chromeDriverPath,
                UseDebuggerMode = config.GetValue<bool>("ChromeDriver:UseDebuggerMode", false),
                DebuggerPort = config.GetValue<int>("ChromeDriver:DebuggerPort", 9222),

                StartMaximized = config.GetValue<bool>("ChromeDriver:Options:StartMaximized", true),
                DisableNotifications = config.GetValue<bool>("ChromeDriver:Options:DisableNotifications", true),
                DisablePopupBlocking = config.GetValue<bool>("ChromeDriver:Options:DisablePopupBlocking", true),
                HideAutomationIndicators = config.GetValue<bool>("ChromeDriver:Options:HideAutomationIndicators", true),
                Headless = config.GetValue<bool>("ChromeDriver:Options:Headless", false),

                DefaultDownloadChromeDirectory = downloadDir,
                PortableChromeVersionsJsonUrl = config["ChromeDriver:Download:PortableChromeVersionsJsonUrl"]
                                                 ?? "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json",
                PortableChromeChannel = config["ChromeDriver:Download:PortableChromeChannel"] ?? "Stable",
                PortableChromeExtractTo = chromeExtractTo,
                ChromeDriverExtractTo = driverExtractTo
            };
        }
    }
}