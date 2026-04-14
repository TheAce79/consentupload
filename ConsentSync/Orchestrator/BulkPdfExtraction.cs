using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Pdf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orchestrator.Phase3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Orchestrator.BulkPdfExtraction;

namespace Orchestrator
{
    public class BulkPdfExtraction
    {

        /// <summary>
        /// Standalone orchestrator for bulk PDF extraction
        /// Can be run independently or as part of any phase
        /// </summary>
        public class BulkPdfExtractionOrchestrator
        {
            private readonly IConfiguration _config;
            private readonly BulkPdfExtractionConfig _bulkConfig;
            private readonly SchoolContextConfig _schoolContext;

            private readonly ILogger<BulkPdfExtractionOrchestrator> _logger;

            public BulkPdfExtractionOrchestrator(IConfiguration? config = null)
            {
                _config = config ?? ConfigurationService.GetConfiguration();
                _bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
                _schoolContext = ConfigurationService.GetSchoolContextConfig();
                _logger = LoggerService.GetLogger<BulkPdfExtractionOrchestrator>();
            }




            /// <summary>
            /// Check if any PDFs are available for processing in input folders
            /// </summary>
            public bool IsPdfAvailable()
            {
                var inputBulkPath = _bulkConfig.GetInputBulkPath();
                var inputScannedPath = _bulkConfig.GetInputScannedPath();

                // Check if directories exist and contain PDFs
                bool hasBulkPdfs = Directory.Exists(inputBulkPath) &&
                                   Directory.GetFiles(inputBulkPath, "*.pdf").Length > 0;

                bool hasScannedPdfs = Directory.Exists(inputScannedPath) &&
                                      Directory.GetFiles(inputScannedPath, "*.pdf").Length > 0;

                return hasBulkPdfs || hasScannedPdfs;
            }




            public async Task<BulkExtractionResult> RunAsync()
            {
                 LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
                 LoggerService.LogInformation("║      PDF Processing - Smart Extraction                ║");
                 LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝\n");

                var result = new BulkExtractionResult();

                try
                {
                     LoggerService.LogInformation("📋 Step 1: Validating configuration...");
                    if (!ValidateConfiguration())
                    {
                        return new BulkExtractionResult { ErrorMessage = "Configuration validation failed" };
                    }

                     LoggerService.LogInformation("\n📋 Step 2: Verifying folder structure...");
                    DisplayFolderStructure();

                    // ✅ NEW: Initialize extractor (creates folders automatically)
                     LoggerService.LogInformation("\n📋 Step 3: Initializing folder structure...");
                    var extractor = new BulkPdfExtractor(_config);
                     LoggerService.LogInformation("   ✅ Folder structure created/verified");
                     LoggerService.LogInformation($"   📂 Base path: {_bulkConfig.BasePdfPath}");

                     LoggerService.LogInformation("\n📋 Step 4: Processing PDFs...");
                    result = extractor.ProcessAllPdfs();

                    return result;
                }
                catch (Exception ex)
                {
                     LoggerService.LogInformation($"\n❌ FATAL ERROR: {ex.Message}");
                    result.ErrorMessage = ex.Message;
                    return result;
                }
            }




            /// <summary>
            /// Validate configuration
            /// </summary>
            private bool ValidateConfiguration()
            {
                bool isValid = true;

                // Validate BasePdfPath
                if (string.IsNullOrWhiteSpace(_bulkConfig.BasePdfPath))
                {
                     LoggerService.LogInformation($"   ❌ BasePdfPath not configured");
                    isValid = false;
                }
                else
                {
                     LoggerService.LogInformation($"   ✅ BasePdfPath: {_bulkConfig.BasePdfPath}");
                }

                // Validate folder names
                if (string.IsNullOrWhiteSpace(_bulkConfig.InputBulkFolder))
                {
                     LoggerService.LogInformation($"   ❌ InputBulkFolder not configured");
                    isValid = false;
                }

                if (string.IsNullOrWhiteSpace(_bulkConfig.InputScannedFolder))
                {
                     LoggerService.LogInformation($"   ❌ InputScannedFolder not configured");
                    isValid = false;
                }

                if (string.IsNullOrWhiteSpace(_bulkConfig.OutputReadyFolder))
                {
                     LoggerService.LogInformation($"   ❌ OutputReadyFolder not configured");
                    isValid = false;
                }

                // Validate processing settings
                if (_bulkConfig.PagesPerConsent < 1)
                {
                     LoggerService.LogInformation($"   ❌ PagesPerConsent must be >= 1");
                    isValid = false;
                }
                else
                {
                     LoggerService.LogInformation($"   ✅ PagesPerConsent: {_bulkConfig.PagesPerConsent}");
                }

                if (_bulkConfig.StartPage < 1)
                {
                     LoggerService.LogInformation($"   ❌ StartPage must be >= 1");
                    isValid = false;
                }

                return isValid;
            }






            /// <summary>
            /// Display extraction summary
            /// </summary>
            private void DisplaySummary(BulkExtractionResult result)
            {
                 LoggerService.LogInformation("\n" + new string('═', 60));
                 LoggerService.LogInformation("📊 BULK PDF EXTRACTION COMPLETE - Final Summary");
                 LoggerService.LogInformation(new string('═', 60));
                 LoggerService.LogInformation($"Total PDFs extracted: {result.TotalExtracted}");
                 LoggerService.LogInformation($"Failed extractions: {result.FailedExtractions}");

                if (result.DuplicatesFound > 0)
                {
                     LoggerService.LogInformation($"⚠️  Duplicates detected: {result.DuplicatesFound}");
                }

                 LoggerService.LogInformation($"Status: {(result.Success ? "✅ Success" : "⚠️  Completed with errors")}");
                 LoggerService.LogInformation(new string('═', 60));

                if (result.ErrorMessages.Count > 0)
                {
                     LoggerService.LogInformation($"\n⚠️  Errors ({result.ErrorMessages.Count}):");
                    foreach (var error in result.ErrorMessages.Take(10))
                    {
                         LoggerService.LogInformation($"   - {error}");
                    }
                    if (result.ErrorMessages.Count > 10)
                    {
                         LoggerService.LogInformation($"   ... and {result.ErrorMessages.Count - 10} more");
                    }
                }

                 LoggerService.LogInformation($"\n📁 Output location:");
                 LoggerService.LogInformation($"   {_bulkConfig.GetOutputReadyPath()}");

                if (result.Success)
                {
                     LoggerService.LogInformation($"\n✅ Ready to proceed with Phase 2 processing!");
                }
            }



            private void DisplayFolderStructure()
            {
                 LoggerService.LogInformation($"   📁 Folder Structure:");
                 LoggerService.LogInformation($"");
                 LoggerService.LogInformation($"   📂 {_bulkConfig.BasePdfPath}");
                 LoggerService.LogInformation($"   ├── 📁 1_Input_Bulk/     ← DROP BULK PDFs HERE");
                 LoggerService.LogInformation($"   ├── 📁 2_Input_Scanned/  ← DROP SCANNED PDFs HERE");
                 LoggerService.LogInformation($"   ├── 📁 3_Output_Ready/   ← Processed files (Phase 3 source)");
                 LoggerService.LogInformation($"   ├── 📁 4_Error/          ← Failed files");
                 LoggerService.LogInformation($"   └── 📁 5_Archive/");
                 LoggerService.LogInformation($"       ├── 📁 Bulk/         ← Original bulk files");
                 LoggerService.LogInformation($"       └── 📁 Scanned/      ← Original scans");
                 LoggerService.LogInformation($"");
                 LoggerService.LogInformation($"   💡 README.txt files created in each folder with instructions");
            }



        }

    }


    /// <summary>
    /// Standalone command to run bulk PDF extraction only
    /// Usage: ConsentSync.exe --extract-bulk
    /// </summary>
    public class BulkPdfExtractionCommand
    {
        public static async Task<int> ExecuteAsync(string[] args)
        {
             LoggerService.LogInformation("🚀 ConsentSync - Bulk PDF Extraction Tool\n");

            var config = ConfigurationService.GetConfiguration();
            var orchestrator = new BulkPdfExtractionOrchestrator(config);

            var result = await orchestrator.RunAsync();

            return result.Success ? 0 : 1;
        }
    }

}
