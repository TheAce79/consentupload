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
        /// Get the shared Phis working-directory config (Phase 2 → PrePhase 3 → Phase 3)
        /// </summary>
        public static PhisWorkspaceConfig GetPhisWorkspaceConfig()
        {
            var config = GetConfiguration();
            var section = config.GetSection("PhisWorkspace");

            var ws = new PhisWorkspaceConfig
            {
                BasePath = section.GetValue<string>("BasePath") ?? "{BaseDirectory}\\Phis",
                ToUploadFolder = section.GetValue<string>("ToUploadFolder") ?? "1_To_Upload",
                ErrorFolder = section.GetValue<string>("ErrorFolder") ?? "2_Error",
                CsvFolder = section.GetValue<string>("CsvFolder") ?? "3_Csv",
                ConsentUploadSubFolder = section.GetValue<string>("ConsentUploadSubFolder") ?? "1 Consent Upload",
                FileRoseUploadSubFolder = section.GetValue<string>("FileRoseUploadSubFolder") ?? "2 File Rose Upload",
            };

            ws.BasePath = ResolvePath(ws.BasePath);
            return ws;
        }


    }
}
