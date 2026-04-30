using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{
    /// <summary>
    /// Phase 3 configuration
    /// </summary>
    public class Phase3Config
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;

        // Input
        public Phase3InputConfig Input { get; set; } = new();

        // Upload
        public Phase3UploadConfig Upload { get; set; } = new();

        // File Rose
        public Phase3FileRoseConfig FileRose { get; set; } = new();

        // Navigation
        public Phase3NavigationConfig Navigation { get; set; } = new();

        // Output
        public Phase3OutputConfig Output { get; set; } = new();


        // ✅ NEW: Testing configuration
        public Phase3TestingConfig Testing { get; set; } = new();
    }




    /// <summary>
    /// Phase 3 Input configuration — all paths derived from PhisWorkspace at load time
    /// </summary>
    public class Phase3InputConfig
    {
        /// <summary>Derived from PhisWorkspace → 3_Csv</summary>
        public string UploadCsvPath { get; set; } = string.Empty;

        public string UploadCsvFileName { get; set; } = string.Empty;

        /// <summary>Derived from PhisWorkspace → 1_To_Upload → 1 Consent Upload</summary>
        public string ConsentPath { get; set; } = string.Empty;

       

        /// <summary>Derived from PhisWorkspace → 1_To_Upload → 2 File Rose Upload</summary>
        public string FileRosePath { get; set; } = string.Empty;

    }




    /// <summary>
    /// Phase 3 Upload configuration
    /// </summary>
    public class Phase3UploadConfig
    {
        public int MaxUploadRetries { get; set; }
        public int DelayBetweenUploadsMs { get; set; }
        public int WaitAfterUploadMs { get; set; }
        public bool VerifyUploadSuccess { get; set; }
    }




    /// <summary>
    /// Phase 3 File Rose configuration
    /// </summary>
    public class Phase3FileRoseConfig
    {
        public bool FileRoseEnabled { get; set; }
        public string FileRosePath { get; set; } = string.Empty;
        public bool UseCustomFileRosePerVaccine { get; set; }
        public Dictionary<string, string> FileRoseByVaccine { get; set; } = new();
    }





    /// <summary>
    /// Phase 3 Navigation configuration
    /// </summary>
    public class Phase3NavigationConfig
    {
        // Immunization Service navigation
        public string ImmunizationServiceUrl { get; set; } = string.Empty;
        public string ImmunizationServicePageTitle { get; set; } = string.Empty;
        public string PageTitleElementId { get; set; } = string.Empty;
        public string ConsentDirectivesMenuId { get; set; } = string.Empty;
        public string ImmunizationServiceMenuId { get; set; } = string.Empty;

        // Document upload elements
        public string DocumentsSectionId { get; set; } = string.Empty;
        public string UploadButtonId { get; set; } = string.Empty;
        public string DocumentTitleFieldId { get; set; } = string.Empty;
        public string DocumentDescriptionFieldId { get; set; } = string.Empty;
    }





    /// <summary>
    /// Phase 3 Output configuration
    /// </summary>
    public class Phase3OutputConfig
    {
        public string CompletedCsvFileName { get; set; } = string.Empty;
    }


    /// <summary>
    /// Phase 3 Testing configuration
    /// </summary>
    public class Phase3TestingConfig
    {
        /// <summary>
        /// Enable testing mode to filter to specific Client IDs
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// List of Client IDs to process in testing mode
        /// Leave empty to process all records
        /// </summary>
        public string[] TestClientIds { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Maximum number of records to process in testing mode
        /// Set to 0 for unlimited
        /// </summary>
        public int MaxRecordsToProcess { get; set; } = 0;
    }




}
