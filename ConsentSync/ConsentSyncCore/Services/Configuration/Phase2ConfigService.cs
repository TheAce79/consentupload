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

        public static Phase2Config GetPhase2Config()
        {
            var config = GetConfiguration();
            var phis = GetPhisWorkspaceConfig();
            var csv = GetCsvWorkspaceConfig();

            return new Phase2Config
            {
                Enabled = config.GetValue<bool>("Phase2:Enabled", false),
                Description = config["Phase2:Description"] ?? "Validate extracted PDFs against student CSV",

                RenamedPath = phis.GetConsentUploadPath(),  // Phis\1_To_Upload\1 Consent Upload
                ErrorOutputDir = phis.GetErrorPath(),          // Phis\2_Error

                VitaliteLoginUrl = config["Phase2:VitaliteWebsite:LoginUrl"] ?? "",
                VitaliteSearchUrl = config["Phase2:VitaliteWebsite:SearchUrl"] ?? "",
                VitaliteUsername = config["Phase2:VitaliteWebsite:Username"] ?? "",
                VitalitePassword = config["Phase2:VitaliteWebsite:Password"] ?? "",
                WaitAfterLoginSeconds = config.GetValue<int>("Phase2:VitaliteWebsite:WaitAfterLoginSeconds", 3),
                DownloadTimeoutSeconds = config.GetValue<int>("Phase2:VitaliteWebsite:DownloadTimeoutSeconds", 30),

                TempPath = ResolvePath(config["Phase2:Download:TempPath"] ?? ""),
                MaxDownloadRetries = config.GetValue<int>("Phase2:Download:MaxDownloadRetries", 3),
                DelayBetweenDownloadsMs = config.GetValue<int>("Phase2:Download:DelayBetweenDownloadsMs", 1000),

                ValidateNamesBeforeRename = config.GetValue<bool>("Phase2:PdfProcessing:ValidateNamesBeforeRename", true),
                SplitMultiPagePdfs = config.GetValue<bool>("Phase2:PdfProcessing:SplitMultiPagePdfs", true),
                FileRosePageThreshold = config.GetValue<int>("Phase2:PdfProcessing:FileRosePageThreshold", 1),
                DebugMode = config.GetValue<bool>("Phase2:PdfProcessing:DebugMode", false),
                DebugOutputDir = ResolvePath(config["Phase2:PdfProcessing:DebugOutputDir"] ?? ""),
                UseFuzzyMatching = config.GetValue<bool>("Phase2:PdfProcessing:UseFuzzyMatching", true),
                ReadNamesFromFilename = config.GetValue<bool>("Phase2:PdfProcessing:ReadNamesFromFilename", true),

                // ✅ Validation_Results.csv written to Upload Csv folder
                ValidationResultsCsv = config["Phase2:Output:ValidationResultsCsv"] ?? "Validation_Results.csv",
                UploadCsv = config["Phase2:Output:UploadCsv"] ?? "Upload_to_PHIS.csv",
            };
        }



        /// <summary>
        /// Get vaccine types for a specific grade
        /// </summary>
        public static string[] GetVaccineTypesForGrade(string grade)
        {
            var config = GetConfiguration();
            var gradeKey = grade.Replace(" ", ""); // "Grade 7" -> "Grade7"
            return config.GetSection($"Phase2:VaccineTypes:{gradeKey}").Get<string[]>()
                ?? Array.Empty<string>();
        }






    }
}
