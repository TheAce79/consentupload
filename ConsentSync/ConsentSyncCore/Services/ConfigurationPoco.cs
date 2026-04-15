using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services
{


    /// <summary>
    /// School context configuration (shared across all phases)
    /// </summary>
    public class SchoolContextConfig
    {
        public string SchoolName { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string SchoolYear { get; set; } = string.Empty;
    }



    /// <summary>
    /// Bulk PDF Extraction configuration (standalone - can run at any phase)
    /// Organized folder structure: 1_Input_Bulk, 2_Input_Scanned, 3_Output_Ready, 4_Error, 5_Archive
    /// </summary>
    public class BulkPdfExtractionConfig
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;

        // Base path for all PDF operations
        public string BasePdfPath { get; set; } = string.Empty;

        // Folder names (relative to BasePdfPath) - numbered for workflow clarity
        public string InputBulkFolder { get; set; } = "1_Input_Bulk";
        public string InputScannedFolder { get; set; } = "2_Input_Scanned";
        public string OutputReadyFolder { get; set; } = "3_Output_Ready";
        public string ErrorFolder { get; set; } = "4_Error";
        public string ArchiveFolder { get; set; } = "5_Archive";

        // Processing settings
        public int PagesPerConsent { get; set; } = 1;
        public int StartPage { get; set; } = 1;
        public bool AutoDetectNames { get; set; } = true;

        // Naming format: {ID}_{LastName}_{FirstName}_consent.pdf
        public string NamingFormat { get; set; } = "{ID}_{LastName}_{FirstName}_consent";

        public bool OverwriteExisting { get; set; } = false;
        public bool MoveToArchiveAfterProcessing { get; set; } = true;
        public bool MoveErrorPdfsToErrorFolder { get; set; } = true;

    

        // Computed properties for full paths
        public string GetInputBulkPath() => Path.Combine(BasePdfPath, InputBulkFolder);
        public string GetInputScannedPath() => Path.Combine(BasePdfPath, InputScannedFolder);
        public string GetOutputReadyPath() => Path.Combine(BasePdfPath, OutputReadyFolder);
        public string GetErrorPath() => Path.Combine(BasePdfPath, ErrorFolder);
        public string GetArchivePath() => Path.Combine(BasePdfPath, ArchiveFolder);
        public string GetArchiveBulkPath() => Path.Combine(GetArchivePath(), "Bulk");
        public string GetArchiveScannedPath() => Path.Combine(GetArchivePath(), "Scanned");
    }

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

        /// <summary>
        /// by default, we want to use fuzzy matching for Phase 2 to maximize 
        /// the number of records we can automatically process, but this can be disabled for testing or if you want to be more strict with matching criteria
        /// </summary>
        public bool UseFuzzyMatching { get; set; } = true;

        /// <summary>
        /// If true, extract names from filename format: {ID}_{LastName}_{FirstName}_consent.pdf
        /// If false, extract names by reading PDF content (slower but works with any filename)
        /// </summary>
        public bool ReadNamesFromFilename { get; set; } = true;

        /// <summary>
        /// copy any files that fail processing to a separate directory 
        /// for easier troubleshooting and reprocessing after issues are resolved
        /// </summary>
        public string ErrorOutputDir { get; set; } = string.Empty;

        // Output
        public string ValidationResultsCsv { get; set; } = string.Empty;
        public string UploadCsv { get; set; } = string.Empty;
    }




    /// <summary>
    /// Pre-Phase 3 configuration (Validation & PDF Preparation)
    /// </summary>
    public class PrePhase3Config
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ValidationCsvPath { get; set; } = string.Empty;
        public string ValidationCsvFileName { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public double MinMatchScoreToAutoAccept { get; set; } = 90.0;

        /// <summary>
        /// Maps Description values (e.g., "ConsentHPV9") to PHIS Antigen names (e.g., "HPV-9")
        /// </summary>
        public Dictionary<string, string> AntigenMapping { get; set; } = new();
    }



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
    }




    /// <summary>
    /// Phase 3 Input configuration
    /// </summary>
    public class Phase3InputConfig
    {
        public string UploadCsvPath { get; set; } = string.Empty;
        public string UploadCsvFileName { get; set; } = string.Empty;
        public string PdfPath { get; set; } = string.Empty;
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
        public string DefaultDownloadChromeDirectory { get; set; } = string.Empty;
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


        // ✅ Add these strongly-typed properties
        public List<NamePattern> LastNamePatterns { get; set; } = new();
        public List<NamePattern> FirstNamePatterns { get; set; } = new();
        public List<NamePattern> PreferredNamePatterns { get; set; } = new();
    }



    /// <summary>
    /// Name pattern for PDF extraction
    /// </summary>
    public class NamePattern
    {
        public string[] Words { get; set; } = Array.Empty<string>();
        public string Language { get; set; } = string.Empty;
    }



    /// <summary>
    /// Logging configuration
    /// </summary>
    public class LoggingConfig
    {
        public LogLevelConfig LogLevel { get; set; } = new();
        public ConsoleLoggingConfig Console { get; set; } = new();
        public FileLoggingConfig File { get; set; } = new();
    }

    /// <summary>
    /// Log level configuration
    /// </summary>
    public class LogLevelConfig
    {
        public string Default { get; set; } = "Information";
        public string ConsentSync { get; set; } = "Debug";
        public string Microsoft { get; set; } = "Warning";
        public string System { get; set; } = "Warning";
    }

    /// <summary>
    /// Console logging configuration
    /// </summary>
    public class ConsoleLoggingConfig
    {
        public bool Enabled { get; set; } = true;
        public bool UseColoredOutput { get; set; } = true;
        public bool ShowTimestamps { get; set; } = true;
        public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
    }

    /// <summary>
    /// File logging configuration
    /// </summary>
    public class FileLoggingConfig
    {
        public bool Enabled { get; set; } = false;
        public string LogPath { get; set; } = string.Empty;
        public string LogFileName { get; set; } = "ConsentSync_{Date}.log";
        public int MaxFileSizeMB { get; set; } = 10;
        public int RetainDays { get; set; } = 30;
        public string MinimumLevel { get; set; } = "Information";
    }







}
