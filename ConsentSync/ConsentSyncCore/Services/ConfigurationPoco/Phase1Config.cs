using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{


    /// <summary>
    /// Phase 1 configuration
    /// </summary>
    public class Phase1Config
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;
        public string FilterByStatus { get; set; } = string.Empty;
        public int SaveProgressEveryNRecords { get; set; }
        public int MaxRetries { get; set; }
        public int DelayBetweenRetriesMs { get; set; }
    }
}
