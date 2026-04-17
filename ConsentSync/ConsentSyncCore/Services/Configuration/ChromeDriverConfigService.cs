using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Configuration
{
    public partial class ConfigurationService
    {

        /// <summary>
        /// Get ChromeDriver configuration with resolved paths
        /// </summary>
        public static ChromeDriverConfig GetChromeDriverConfig()
        {
            var config = GetConfiguration();
            return new ChromeDriverConfig
            {
                UsePortableChrome = config.GetValue<bool>("ChromeDriver:UsePortableChrome", false),
                PortableChromePath = ResolvePath(config["ChromeDriver:PortableChromePath"] ?? ""),
                ChromeDriverPath = ResolvePath(config["ChromeDriver:ChromeDriverPath"] ?? ""),
                UseDebuggerMode = config.GetValue<bool>("ChromeDriver:UseDebuggerMode", false),
                DebuggerPort = config.GetValue<int>("ChromeDriver:DebuggerPort", 9222),

                StartMaximized = config.GetValue<bool>("ChromeDriver:Options:StartMaximized", true),
                DisableNotifications = config.GetValue<bool>("ChromeDriver:Options:DisableNotifications", true),
                DisablePopupBlocking = config.GetValue<bool>("ChromeDriver:Options:DisablePopupBlocking", true),
                HideAutomationIndicators = config.GetValue<bool>("ChromeDriver:Options:HideAutomationIndicators", true),
                Headless = config.GetValue<bool>("ChromeDriver:Options:Headless", false),

                DefaultDownloadChromeDirectory = ResolvePath(config["ChromeDriver:Download:DefaultDownloadChromeDirectory"] ?? "")
            };
        }

    }
}
