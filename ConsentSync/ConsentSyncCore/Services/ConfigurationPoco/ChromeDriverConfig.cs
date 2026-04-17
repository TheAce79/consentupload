namespace ConsentSyncCore.Services.ConfigurationPoco
{
    public class ChromeDriverConfig
    {
        // ── Core ──────────────────────────────────────────────────────────
        public bool UsePortableChrome { get; set; }
        public string PortableChromePath { get; set; } = string.Empty;
        public string ChromeDriverPath { get; set; } = string.Empty;
        public bool UseDebuggerMode { get; set; }
        public int DebuggerPort { get; set; }

        // ── Options ───────────────────────────────────────────────────────
        public bool StartMaximized { get; set; }
        public bool DisableNotifications { get; set; }
        public bool DisablePopupBlocking { get; set; }
        public bool HideAutomationIndicators { get; set; }
        public bool Headless { get; set; }

        // ── Download ──────────────────────────────────────────────────────
        public string DefaultDownloadChromeDirectory { get; set; } = string.Empty;

        /// <summary>
        /// URL to Google's "Chrome for Testing" versions JSON.
        /// https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json
        /// </summary>
        public string PortableChromeVersionsJsonUrl { get; set; } =
            "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json";

        /// <summary>"Stable", "Beta", "Dev", or "Canary"</summary>
        public string PortableChromeChannel { get; set; } = "Stable";

        /// <summary>Folder where chrome-win64.zip is extracted (resolved path)</summary>
        public string PortableChromeExtractTo { get; set; } = string.Empty;

        /// <summary>Folder where chromedriver-win64.zip is extracted (resolved path)</summary>
        public string ChromeDriverExtractTo { get; set; } = string.Empty;
    }
}