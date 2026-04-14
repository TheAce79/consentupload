using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Pdf;
using Microsoft.Extensions.Configuration;
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

            public BulkPdfExtractionOrchestrator(IConfiguration? config = null)
            {
                _config = config ?? ConfigurationService.GetConfiguration();
                _bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
                _schoolContext = ConfigurationService.GetSchoolContextConfig();
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
                Console.WriteLine("╔════════════════════════════════════════════════════════╗");
                Console.WriteLine("║      PDF Processing - Smart Extraction                ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

                var result = new BulkExtractionResult();

                try
                {
                    Console.WriteLine("📋 Step 1: Validating configuration...");
                    if (!ValidateConfiguration())
                    {
                        return new BulkExtractionResult { ErrorMessage = "Configuration validation failed" };
                    }

                    Console.WriteLine("\n📋 Step 2: Verifying folder structure...");
                    DisplayFolderStructure();

                    // ✅ NEW: Initialize extractor (creates folders automatically)
                    Console.WriteLine("\n📋 Step 3: Initializing folder structure...");
                    var extractor = new BulkPdfExtractor(_config);
                    Console.WriteLine("   ✅ Folder structure created/verified");
                    Console.WriteLine($"   📂 Base path: {_bulkConfig.BasePdfPath}");

                    Console.WriteLine("\n📋 Step 4: Processing PDFs...");
                    result = extractor.ProcessAllPdfs();

                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n❌ FATAL ERROR: {ex.Message}");
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
                    Console.WriteLine($"   ❌ BasePdfPath not configured");
                    isValid = false;
                }
                else
                {
                    Console.WriteLine($"   ✅ BasePdfPath: {_bulkConfig.BasePdfPath}");
                }

                // Validate folder names
                if (string.IsNullOrWhiteSpace(_bulkConfig.InputBulkFolder))
                {
                    Console.WriteLine($"   ❌ InputBulkFolder not configured");
                    isValid = false;
                }

                if (string.IsNullOrWhiteSpace(_bulkConfig.InputScannedFolder))
                {
                    Console.WriteLine($"   ❌ InputScannedFolder not configured");
                    isValid = false;
                }

                if (string.IsNullOrWhiteSpace(_bulkConfig.OutputReadyFolder))
                {
                    Console.WriteLine($"   ❌ OutputReadyFolder not configured");
                    isValid = false;
                }

                // Validate processing settings
                if (_bulkConfig.PagesPerConsent < 1)
                {
                    Console.WriteLine($"   ❌ PagesPerConsent must be >= 1");
                    isValid = false;
                }
                else
                {
                    Console.WriteLine($"   ✅ PagesPerConsent: {_bulkConfig.PagesPerConsent}");
                }

                if (_bulkConfig.StartPage < 1)
                {
                    Console.WriteLine($"   ❌ StartPage must be >= 1");
                    isValid = false;
                }

                return isValid;
            }






            /// <summary>
            /// Display extraction summary
            /// </summary>
            private void DisplaySummary(BulkExtractionResult result)
            {
                Console.WriteLine("\n" + new string('═', 60));
                Console.WriteLine("📊 BULK PDF EXTRACTION COMPLETE - Final Summary");
                Console.WriteLine(new string('═', 60));
                Console.WriteLine($"Total PDFs extracted: {result.TotalExtracted}");
                Console.WriteLine($"Failed extractions: {result.FailedExtractions}");

                if (result.DuplicatesFound > 0)
                {
                    Console.WriteLine($"⚠️  Duplicates detected: {result.DuplicatesFound}");
                }

                Console.WriteLine($"Status: {(result.Success ? "✅ Success" : "⚠️  Completed with errors")}");
                Console.WriteLine(new string('═', 60));

                if (result.ErrorMessages.Count > 0)
                {
                    Console.WriteLine($"\n⚠️  Errors ({result.ErrorMessages.Count}):");
                    foreach (var error in result.ErrorMessages.Take(10))
                    {
                        Console.WriteLine($"   - {error}");
                    }
                    if (result.ErrorMessages.Count > 10)
                    {
                        Console.WriteLine($"   ... and {result.ErrorMessages.Count - 10} more");
                    }
                }

                Console.WriteLine($"\n📁 Output location:");
                Console.WriteLine($"   {_bulkConfig.GetOutputReadyPath()}");

                if (result.Success)
                {
                    Console.WriteLine($"\n✅ Ready to proceed with Phase 2 processing!");
                }
            }



            private void DisplayFolderStructure()
            {
                Console.WriteLine($"   📁 Folder Structure:");
                Console.WriteLine($"");
                Console.WriteLine($"   📂 {_bulkConfig.BasePdfPath}");
                Console.WriteLine($"   ├── 📁 1_Input_Bulk/     ← DROP BULK PDFs HERE");
                Console.WriteLine($"   ├── 📁 2_Input_Scanned/  ← DROP SCANNED PDFs HERE");
                Console.WriteLine($"   ├── 📁 3_Output_Ready/   ← Processed files (Phase 3 source)");
                Console.WriteLine($"   ├── 📁 4_Error/          ← Failed files");
                Console.WriteLine($"   └── 📁 5_Archive/");
                Console.WriteLine($"       ├── 📁 Bulk/         ← Original bulk files");
                Console.WriteLine($"       └── 📁 Scanned/      ← Original scans");
                Console.WriteLine($"");
                Console.WriteLine($"   💡 README.txt files created in each folder with instructions");
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
            Console.WriteLine("🚀 ConsentSync - Bulk PDF Extraction Tool\n");

            var config = ConfigurationService.GetConfiguration();
            var orchestrator = new BulkPdfExtractionOrchestrator(config);

            var result = await orchestrator.RunAsync();

            return result.Success ? 0 : 1;
        }
    }

}
