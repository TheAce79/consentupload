using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services
{

    /// <summary>
    /// CSV Processing configuration
    /// </summary>
    public class CsvProcessingConfig
    {
        public string InputCsvPath { get; set; } = string.Empty;
        public string InputCsvFileName { get; set; } = string.Empty;
        public string OutputCsvPath { get; set; } = string.Empty;
        public string OutputCsvFileName { get; set; } = string.Empty;
        public int SaveProgressEveryNRecords { get; set; }
        public string DateOfBirthColumn { get; set; } = string.Empty;
        public string DateFormat { get; set; } = string.Empty;
        public string[] InputDateFormats { get; set; } = Array.Empty<string>();
        public string LastNameColumn { get; set; } = string.Empty;
        public string FirstNameColumn { get; set; } = string.Empty;
        public string ClientIdColumn { get; set; } = string.Empty;
    }


    /// <summary>
    /// Phase 1 configuration
    /// </summary>
    public class Phase1Config
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;
        public string FilterByStatus { get; set; } = string.Empty;
        public int SaveProgressEveryNRecords { get; set; }
        public int MaxRetries { get; set; }
        public int DelayBetweenRetriesMs { get; set; }
    }


    /// <summary>
    /// Phase 2 configuration
    /// </summary>
    public class Phase2Config
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;

        // Vitalite Website
        public string VitaliteLoginUrl { get; set; } = string.Empty;
        public string VitaliteSearchUrl { get; set; } = string.Empty;
        public string VitaliteUsername { get; set; } = string.Empty;
        public string VitalitePassword { get; set; } = string.Empty;
        public int WaitAfterLoginSeconds { get; set; }
        public int DownloadTimeoutSeconds { get; set; }

        // Download
        public string DownloadPath { get; set; } = string.Empty;
        public string RenamedPath { get; set; } = string.Empty;
        public string TempPath { get; set; } = string.Empty;
        public int MaxDownloadRetries { get; set; }
        public int DelayBetweenDownloadsMs { get; set; }

        // PDF Processing
        public bool ValidateNamesBeforeRename { get; set; }
        public bool SplitMultiPagePdfs { get; set; }
        public int FileRosePageThreshold { get; set; }
        public bool DebugMode { get; set; }
        public string DebugOutputDir { get; set; } = string.Empty;

        // Output
        public string ValidationResultsCsv { get; set; } = string.Empty;
        public string UploadCsv { get; set; } = string.Empty;
        public string CurrentYear { get; set; } = string.Empty;
    }


    /// <summary>
    /// Phase 3 configuration
    /// </summary>
    public class Phase3Config
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;

        // Input
        public string UploadCsvPath { get; set; } = string.Empty;
        public string UploadCsvFileName { get; set; } = string.Empty;
        public string PdfPath { get; set; } = string.Empty;

        // Upload
        public int MaxUploadRetries { get; set; }
        public int DelayBetweenUploadsMs { get; set; }
        public int WaitAfterUploadMs { get; set; }
        public bool VerifyUploadSuccess { get; set; }

        // File Rose
        public bool FileRoseEnabled { get; set; }
        public string FileRosePath { get; set; } = string.Empty;
        public bool UseCustomFileRosePerVaccine { get; set; }

        // Navigation
        public string DocumentsSectionId { get; set; } = string.Empty;
        public string UploadButtonId { get; set; } = string.Empty;
        public string DocumentTitleFieldId { get; set; } = string.Empty;
        public string DocumentDescriptionFieldId { get; set; } = string.Empty;

        // Output
        public string CompletedCsvFileName { get; set; } = string.Empty;
    }


    /// <summary>
    /// PHIS Automation configuration (shared Phase 1 & 3)
    /// </summary>
    public class PhisConfig
    {
        public string LoginUrl { get; set; } = string.Empty;
        public string SearchUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool ManualLoginMode { get; set; }
        public int ManualLoginWaitSeconds { get; set; }
        public int SessionTimeoutMinutes { get; set; }
        public bool SessionRefreshEnabled { get; set; }
        public int RefreshBufferMinutes { get; set; }
        public int WebDriverWaitSeconds { get; set; }
        public int DelayBetweenSearchesMs { get; set; }
        public int PageLoadDelayMs { get; set; }
        public int AjaxWaitMs { get; set; }
    }



    /// <summary>
    /// PHIS Column Headers
    /// </summary>
    public class PhisColumnHeaders
    {
        public string ClientId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Medicare { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty;
    }


    /// <summary>
    /// Fuzzy Matching configuration
    /// </summary>
    public class FuzzyMatchingConfig
    {
        public bool Enabled { get; set; }
        public double SingleResultThreshold { get; set; }
        public double MultipleResultsThreshold { get; set; }
        public double ManualReviewThreshold { get; set; }
        public double LastNameWeight { get; set; }
        public double FirstNameWeight { get; set; }
        public bool IgnoreHyphensInComparison { get; set; }
        public bool IgnoreSpacesInComparison { get; set; }
        public bool TreatCompoundNamesAsPartialMatch { get; set; }
        public bool UseMedicareNumberAsConfirmation { get; set; }
        public double MedicareNumberBoostScore { get; set; }


        // ✅ NEW - Add these properties
        public bool TreatSpaceSeparatedNamesAsCompound { get; set; }
        public double CompoundNameMatchScore { get; set; }
        public double MinimumCompoundMatchRatio { get; set; }
    }




    /// <summary>
    /// Chrome Driver configuration
    /// </summary>
    public class ChromeDriverConfig
    {
        public bool UsePortableChrome { get; set; }
        public string PortableChromePath { get; set; } = string.Empty;
        public string ChromeDriverPath { get; set; } = string.Empty;
        public bool UseDebuggerMode { get; set; }
        public int DebuggerPort { get; set; }
        public bool StartMaximized { get; set; }
        public bool DisableNotifications { get; set; }
        public bool DisablePopupBlocking { get; set; }
        public bool HideAutomationIndicators { get; set; }
        public bool Headless { get; set; }
        public string DefaultDownloadDirectory { get; set; } = string.Empty;
    }

    /// <summary>
    /// PDF Extraction configuration
    /// </summary>
    public class PdfExtractionConfig
    {
        public string[] LastNameKeywords { get; set; } = Array.Empty<string>();
        public string[] FirstNameKeywords { get; set; } = Array.Empty<string>();
        public string[] ExcludeKeywords { get; set; } = Array.Empty<string>();
        public string[] FieldLabelWords { get; set; } = Array.Empty<string>();
        public int SearchRange { get; set; }
        public int MinNameLength { get; set; }
    }










}
