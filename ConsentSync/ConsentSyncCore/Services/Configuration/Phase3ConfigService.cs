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



        public static Phase3Config GetPhase3Config()
        {
            var config = GetConfiguration();
            var phase3Section = config.GetSection("Phase3");
            var ws = GetPhisWorkspaceConfig();

            var phase3Config = new Phase3Config
            {
                Enabled = phase3Section.GetValue<bool>("Enabled"),
                Description = phase3Section.GetValue<string>("Description") ?? string.Empty,

                // ✅ Input paths derived entirely from PhisWorkspace — nothing hardcoded
                Input = new Phase3InputConfig
                {
                    UploadCsvPath = ws.GetCsvPath(),
                    UploadCsvFileName = phase3Section.GetValue<string>("Input:UploadCsvFileName") ?? "Upload_to_PHIS.csv",
                    ConsentPath = ws.GetConsentUploadPath(),
                    FileRosePath = ws.GetFileRoseUploadPath(),
                },

                Upload = new Phase3UploadConfig
                {
                    MaxUploadRetries = phase3Section.GetValue<int>("Upload:MaxUploadRetries"),
                    DelayBetweenUploadsMs = phase3Section.GetValue<int>("Upload:DelayBetweenUploadsMs"),
                    WaitAfterUploadMs = phase3Section.GetValue<int>("Upload:WaitAfterUploadMs"),
                    VerifyUploadSuccess = phase3Section.GetValue<bool>("Upload:VerifyUploadSuccess")
                },

                Testing = new Phase3TestingConfig
                {
                    Enabled = phase3Section.GetValue<bool>("Testing:Enabled", false),
                    TestClientIds = phase3Section.GetSection("Testing:TestClientIds").Get<string[]>() ?? Array.Empty<string>(),
                    MaxRecordsToProcess = phase3Section.GetValue<int>("Testing:MaxRecordsToProcess", 0)
                },

                FileRose = new Phase3FileRoseConfig
                {
                    FileRoseEnabled = phase3Section.GetValue<bool>("FileRose:FileRoseEnabled"),
                    UseCustomFileRosePerVaccine = phase3Section.GetValue<bool>("FileRose:UseCustomFileRosePerVaccine"),
                    FileRoseByVaccine = phase3Section.GetSection("FileRose:FileRoseByVaccine")
                                                            .Get<Dictionary<string, string>>() ?? new()
                },

                Navigation = new Phase3NavigationConfig
                {
                    ImmunizationServiceUrl = phase3Section.GetValue<string>("Navigation:ImmunizationServiceUrl") ?? string.Empty,
                    ImmunizationServicePageTitle = phase3Section.GetValue<string>("Navigation:ImmunizationServicePageTitle") ?? string.Empty,
                    PageTitleElementId = phase3Section.GetValue<string>("Navigation:PageTitleElementId") ?? string.Empty,
                    ConsentDirectivesMenuId = phase3Section.GetValue<string>("Navigation:ConsentDirectivesMenuId") ?? string.Empty,
                    ImmunizationServiceMenuId = phase3Section.GetValue<string>("Navigation:ImmunizationServiceMenuId") ?? string.Empty,
                    DocumentsSectionId = phase3Section.GetValue<string>("Navigation:DocumentsSectionId") ?? string.Empty,
                    UploadButtonId = phase3Section.GetValue<string>("Navigation:UploadButtonId") ?? string.Empty,
                    DocumentTitleFieldId = phase3Section.GetValue<string>("Navigation:DocumentTitleFieldId") ?? string.Empty,
                    DocumentDescriptionFieldId = phase3Section.GetValue<string>("Navigation:DocumentDescriptionFieldId") ?? string.Empty
                },

                Output = new Phase3OutputConfig
                {
                    CompletedCsvFileName = phase3Section.GetValue<string>("Output:CompletedCsvFileName") ?? string.Empty
                }
            };

            return phase3Config;
        }






        /// <summary>
        /// Get file rose path for specific vaccine type
        /// </summary>
        public static string GetFileRosePathForVaccine(string vaccineType)
        {
            var config = GetConfiguration();
            var customPath = config[$"Phase3:FileRose:FileRoseByVaccine:{vaccineType}"];

            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            {
                return customPath;
            }

            // Fallback to default
            return config["Phase3:FileRose:FileRosePath"] ?? "";
        }

        /// <summary>
        /// Get full upload CSV path
        /// </summary>
        public static string GetUploadCsvFullPath()
        {
            var phase3Config = GetPhase3Config();
            return Path.Combine(phase3Config.Input.UploadCsvPath, phase3Config.Input.UploadCsvFileName);
        }




    }
}
