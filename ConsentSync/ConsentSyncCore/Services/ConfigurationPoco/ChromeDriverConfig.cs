using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{

    /// <summary>
    /// Chrome Driver configuration
    /// </summary>
    public class ChromeDriverConfig
    {
        public bool UsePortableChrome { get; set; }
        public string PortableChromePath { get; set; } = string.Empty;
        public string ChromeDriverPath { get; set; } = string.Empty;
        public bool UseDebuggerMode { get; set; }
        public int DebuggerPort { get; set; }
        public bool StartMaximized { get; set; }
        public bool DisableNotifications { get; set; }
        public bool DisablePopupBlocking { get; set; }
        public bool HideAutomationIndicators { get; set; }
        public bool Headless { get; set; }
        public string DefaultDownloadChromeDirectory { get; set; } = string.Empty;
    }
}
