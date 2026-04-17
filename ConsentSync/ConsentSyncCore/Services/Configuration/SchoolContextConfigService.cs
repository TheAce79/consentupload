using ConsentSyncCore.Services.ConfigurationPoco;
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
        /// Get School Context configuration (shared across all phases)
        /// </summary>
        public static SchoolContextConfig GetSchoolContextConfig()
        {
            var config = GetConfiguration();
            return new SchoolContextConfig
            {
                SchoolName = config["SchoolContext:SchoolName"] ?? "Unknown School",
                Grade = config["SchoolContext:Grade"] ?? "0",
                SchoolYear = config["SchoolContext:SchoolYear"] ?? "2024-2025"
            };
        }


    }
}
