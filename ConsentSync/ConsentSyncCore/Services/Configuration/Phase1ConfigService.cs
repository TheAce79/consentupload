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
        /// Get Phase 1 configuration
        /// </summary>
        public static Phase1Config GetPhase1Config()
        {
            var config = GetConfiguration();
            return new Phase1Config
            {
                Enabled = config.GetValue<bool>("Phase1:Enabled", true),
                Description = config["Phase1:Description"] ?? "Search PHIS by Date of Birth",
                FilterByStatus = config["Phase1:Processing:FilterByStatus"] ?? "NotProcessed",
                SaveProgressEveryNRecords = config.GetValue<int>("Phase1:Processing:SaveProgressEveryNRecords", 5),
                MaxRetries = config.GetValue<int>("Phase1:Processing:MaxRetries", 3),
                DelayBetweenRetriesMs = config.GetValue<int>("Phase1:Processing:DelayBetweenRetriesMs", 2000)
            };
        }




    }
}
