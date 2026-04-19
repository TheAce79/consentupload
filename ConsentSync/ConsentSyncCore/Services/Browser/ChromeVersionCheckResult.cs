using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Browser
{
    public  class ChromeVersionCheckResult
    {
        public bool ChromeFound { get; set; }
        public bool DriverFound { get; set; }
        public bool VersionsMatch { get; set; }

        public string ChromePath { get; set; } = string.Empty;
        public string DriverPath { get; set; } = string.Empty;

        public string ChromeVersion { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;

        public int ChromeMajor { get; set; }
        public int DriverMajor { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;

        public bool IsReady => ChromeFound && DriverFound && VersionsMatch;
    }
}
