using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{

    /// <summary>
    /// Pre-Phase 3 configuration (Validation & PDF Preparation)
    /// </summary>
    public class PrePhase3Config
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ValidationCsvPath { get; set; } = string.Empty;
        public string ValidationCsvFileName { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public double MinMatchScoreToAutoAccept { get; set; } = 90.0;

        /// <summary>
        /// Maps Description values (e.g., "ConsentHPV9") to PHIS Antigen names (e.g., "HPV-9")
        /// </summary>
        public Dictionary<string, string> AntigenMapping { get; set; } = new();
    }


}
