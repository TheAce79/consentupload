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
        /// Get Bulk PDF Extraction configuration with folder structure
        /// </summary>
        public static BulkPdfExtractionConfig GetBulkPdfExtractionConfig()
        {
            var config = GetConfiguration();
            var section = config.GetSection("BulkPdfExtraction");

            var bulkConfig = new BulkPdfExtractionConfig
            {
                Enabled = section.GetValue<bool>("Enabled", false),
                Description = section.GetValue<string>("Description") ?? "",

                BasePdfPath = section.GetValue<string>("BasePdfPath") ?? "",
                InputBulkFolder = section.GetValue<string>("InputBulkFolder") ?? "1_Input_Bulk",
                InputScannedFolder = section.GetValue<string>("InputScannedFolder") ?? "2_Input_Scanned",
                OutputReadyFolder = section.GetValue<string>("OutputReadyFolder") ?? "3_Output_Ready",
                FileRoseFolder = section.GetValue<string>("FileRoseFolder") ?? "4 FileRose Extraction",
                DuplicateClientFolder = section.GetValue<string>("Duplicate_Client") ?? "5_Duplicate",
                ErrorFolder = section.GetValue<string>("ErrorFolder") ?? "6_Error",
                ArchiveFolder = section.GetValue<string>("ArchiveFolder") ?? "7_Archive",

                PagesPerConsent = section.GetValue<int>("PagesPerConsent", 1),
                StartPage = section.GetValue<int>("StartPage", 1),
                AutoDetectNames = section.GetValue<bool>("AutoDetectNames", true),
                NamingFormat = section.GetValue<string>("NamingFormat") ?? "{ID}_{LastName}_{FirstName}_consent",
                OverwriteExisting = section.GetValue<bool>("OverwriteExisting", false),
                MoveToArchiveAfterProcessing = section.GetValue<bool>("MoveToArchiveAfterProcessing", true),
                MoveErrorPdfsToErrorFolder = section.GetValue<bool>("MoveErrorPdfsToErrorFolder", true),
            };

            bulkConfig.BasePdfPath = ResolvePath(bulkConfig.BasePdfPath);

            return bulkConfig;
        }


    }


}
