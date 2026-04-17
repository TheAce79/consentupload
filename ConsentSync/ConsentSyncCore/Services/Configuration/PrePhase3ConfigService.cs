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

        public static PrePhase3Config GetPrePhase3Config()
        {
            var config = GetConfiguration();
            var csv = GetCsvWorkspaceConfig();

            var uploadCsvPath = csv.GetUploadCsvPath(); // Csv\2_Output Csv\2 Upload Csv

            return new PrePhase3Config
            {
                Enabled = config.GetValue<bool>("PrePhase3:Enabled", false),
                Description = config["PrePhase3:Description"] ?? "Process validated PDFs and generate Upload_to_PHIS.csv",

                // ✅ Both read and write from Upload Csv folder
                ValidationCsvPath = uploadCsvPath,
                OutputPath = uploadCsvPath,
                ValidationCsvFileName = config["PrePhase3:ValidationCsvFileName"] ?? "Validation_Results.csv",
                MinMatchScoreToAutoAccept = config.GetValue<double>("PrePhase3:MinMatchScoreToAutoAccept", 90.0),

                AntigenMapping = config.GetSection("PrePhase3:AntigenMapping")
                                       .Get<Dictionary<string, string>>() ?? new(),
            };
        }

    }
}
