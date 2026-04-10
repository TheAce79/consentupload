using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services

{
    /// <summary>
    /// Centralized configuration service for ConsentSync
    /// Provides strongly-typed access to appsettings.json
    /// Thread-safe singleton pattern
    /// </summary>
    public class ConfigurationService
    {

        private static IConfiguration? _config;
        private static readonly object _lock = new object();

        /// <summary>
        /// Get the configuration instance (singleton)
        /// </summary>
        public static IConfiguration GetConfiguration()
        {
            if (_config == null)
            {
                lock (_lock)
                {
                    if (_config == null)
                    {
                        var environment = Environment.GetEnvironmentVariable("CONSENTSYNC_ENVIRONMENT") ?? "Production";

                        _config = new ConfigurationBuilder()
                            .SetBasePath(AppContext.BaseDirectory)
                            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                            .AddEnvironmentVariables(prefix: "CONSENTSYNC_")
                            .Build();

                        Console.WriteLine($"✅ Configuration loaded (Environment: {environment})");
                    }
                }
            }
            return _config;
        }

        /// <summary>
        /// Reload configuration from disk
        /// </summary>
        public static void ReloadConfiguration()
        {
            lock (_lock)
            {
                _config = null;
                GetConfiguration();
            }
        }



        #region CSV Processing Configuration

        /// <summary>
        /// Get CSV Processing configuration
        /// </summary>
        public static CsvProcessingConfig GetCsvConfig()
        {
            var config = GetConfiguration();
            return new CsvProcessingConfig
            {
                InputCsvPath = config["CsvProcessing:InputCsvPath"] ?? "",
                InputCsvFileName = config["CsvProcessing:InputCsvFileName"] ?? "immunizations.csv",
                OutputCsvPath = config["CsvProcessing:OutputCsvPath"] ?? "",
                OutputCsvFileName = config["CsvProcessing:OutputCsvFileName"] ?? "immunizations_processed.csv",
                SaveProgressEveryNRecords = config.GetValue<int>("CsvProcessing:SaveProgressEveryNRecords", 5),
                DateOfBirthColumn = config["CsvProcessing:DateOfBirthColumn"] ?? "Date of Birth",
                DateFormat = config["CsvProcessing:DateFormat"] ?? "yyyy-MM-dd",
                InputDateFormats = config.GetSection("CsvProcessing:InputDateFormats").Get<string[]>()
                    ?? new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" },
                LastNameColumn = config["CsvProcessing:LastNameColumn"] ?? "Last Name",
                FirstNameColumn = config["CsvProcessing:FirstNameColumn"] ?? "First Name",
                ClientIdColumn = config["CsvProcessing:ClientIdColumn"] ?? "ClientId"
            };
        }

        /// <summary>
        /// Get full input CSV path
        /// </summary>
        public static string GetInputCsvFullPath()
        {
            var csvConfig = GetCsvConfig();
            return Path.Combine(csvConfig.InputCsvPath, csvConfig.InputCsvFileName);
        }

        /// <summary>
        /// Get full output CSV path
        /// </summary>
        public static string GetOutputCsvFullPath()
        {
            var csvConfig = GetCsvConfig();
            return Path.Combine(csvConfig.OutputCsvPath, csvConfig.OutputCsvFileName);
        }


        #endregion



        #region Phase 1 Configuration


        /// <summary>
        /// Get Phase 1 configuration
        /// </summary>
        public static Phase1Config GetPhase1Config()
        {
            var config = GetConfiguration();
            return new Phase1Config
            {
                Enabled = config.GetValue<bool>("Phase1:Enabled", true),
                Description = config["Phase1:Description"] ?? "Search PHIS by Date of Birth",
                FilterByStatus = config["Phase1:Processing:FilterByStatus"] ?? "NotProcessed",
                SaveProgressEveryNRecords = config.GetValue<int>("Phase1:Processing:SaveProgressEveryNRecords", 5),
                MaxRetries = config.GetValue<int>("Phase1:Processing:MaxRetries", 3),
                DelayBetweenRetriesMs = config.GetValue<int>("Phase1:Processing:DelayBetweenRetriesMs", 2000)
            };
        }


        #endregion




        #region Phase 2 Configuration

        /// <summary>
        /// Get Phase 2 configuration
        /// </summary>
        public static Phase2Config GetPhase2Config()
        {
            var config = GetConfiguration();
            return new Phase2Config
            {
                Enabled = config.GetValue<bool>("Phase2:Enabled", true),
                Description = config["Phase2:Description"] ?? "Download consent PDFs from Vitalite",

                VitaliteLoginUrl = config["Phase2:VitaliteWebsite:LoginUrl"] ?? "",
                VitaliteSearchUrl = config["Phase2:VitaliteWebsite:SearchUrl"] ?? "",
                VitaliteUsername = config["Phase2:VitaliteWebsite:Username"] ?? "",
                VitalitePassword = config["Phase2:VitaliteWebsite:Password"] ?? "",
                WaitAfterLoginSeconds = config.GetValue<int>("Phase2:VitaliteWebsite:WaitAfterLoginSeconds", 3),
                DownloadTimeoutSeconds = config.GetValue<int>("Phase2:VitaliteWebsite:DownloadTimeoutSeconds", 30),

                DownloadPath = config["Phase2:Download:DownloadPath"] ?? "",
                RenamedPath = config["Phase2:Download:RenamedPath"] ?? "",
                TempPath = config["Phase2:Download:TempPath"] ?? "",
                MaxDownloadRetries = config.GetValue<int>("Phase2:Download:MaxDownloadRetries", 3),
                DelayBetweenDownloadsMs = config.GetValue<int>("Phase2:Download:DelayBetweenDownloadsMs", 1000),

                ValidateNamesBeforeRename = config.GetValue<bool>("Phase2:PdfProcessing:ValidateNamesBeforeRename", true),
                SplitMultiPagePdfs = config.GetValue<bool>("Phase2:PdfProcessing:SplitMultiPagePdfs", true),
                FileRosePageThreshold = config.GetValue<int>("Phase2:PdfProcessing:FileRosePageThreshold", 1),
                DebugMode = config.GetValue<bool>("Phase2:PdfProcessing:DebugMode", false),
                DebugOutputDir = config["Phase2:PdfProcessing:DebugOutputDir"] ?? "",

                ValidationResultsCsv = config["Phase2:Output:ValidationResultsCsv"] ?? "Validation_Results.csv",
                UploadCsv = config["Phase2:Output:UploadCsv"] ?? "Upload_to_PHIS.csv",
                CurrentYear = config["Phase2:Output:CurrentYear"] ?? "2025-2026"
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

        #endregion




        #region Phase 3 Configuration

        /// <summary>
        /// Get Phase 3 configuration
        /// </summary>
        public static Phase3Config GetPhase3Config()
        {
            var config = GetConfiguration();
            return new Phase3Config
            {
                Enabled = config.GetValue<bool>("Phase3:Enabled", true),
                Description = config["Phase3:Description"] ?? "Upload consent PDFs to PHIS",

                UploadCsvPath = config["Phase3:Input:UploadCsvPath"] ?? "",
                UploadCsvFileName = config["Phase3:Input:UploadCsvFileName"] ?? "Upload_to_PHIS.csv",
                PdfPath = config["Phase3:Input:PdfPath"] ?? "",

                MaxUploadRetries = config.GetValue<int>("Phase3:Upload:MaxUploadRetries", 3),
                DelayBetweenUploadsMs = config.GetValue<int>("Phase3:Upload:DelayBetweenUploadsMs", 2000),
                WaitAfterUploadMs = config.GetValue<int>("Phase3:Upload:WaitAfterUploadMs", 1500),
                VerifyUploadSuccess = config.GetValue<bool>("Phase3:Upload:VerifyUploadSuccess", true),

                FileRoseEnabled = config.GetValue<bool>("Phase3:FileRose:FileRoseEnabled", true),
                FileRosePath = config["Phase3:FileRose:FileRosePath"] ?? "",
                UseCustomFileRosePerVaccine = config.GetValue<bool>("Phase3:FileRose:UseCustomFileRosePerVaccine", false),

                DocumentsSectionId = config["Phase3:Navigation:DocumentsSectionId"] ?? "documents-tab",
                UploadButtonId = config["Phase3:Navigation:UploadButtonId"] ?? "upload-btn",
                DocumentTitleFieldId = config["Phase3:Navigation:DocumentTitleFieldId"] ?? "doc-title",
                DocumentDescriptionFieldId = config["Phase3:Navigation:DocumentDescriptionFieldId"] ?? "doc-description",

                CompletedCsvFileName = config["Phase3:Output:CompletedCsvFileName"] ?? "Upload_to_PHIS_completed.csv"
            };
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
            return Path.Combine(phase3Config.UploadCsvPath, phase3Config.UploadCsvFileName);
        }

        #endregion




        #region PHIS Automation Configuration (Shared Phase 1 & 3)

        /// <summary>
        /// Get PHIS Automation configuration
        /// </summary>
        public static PhisConfig GetPhisConfig()
        {
            var config = GetConfiguration();
            return new PhisConfig
            {
                LoginUrl = config["PhisAutomation:LoginUrl"] ?? "",
                SearchUrl = config["PhisAutomation:SearchUrl"] ?? "",

                Username = config["PhisAutomation:Authentication:Username"] ?? "",
                Password = config["PhisAutomation:Authentication:Password"] ?? "",
                ManualLoginMode = config.GetValue<bool>("PhisAutomation:Authentication:ManualLoginMode", true),
                ManualLoginWaitSeconds = config.GetValue<int>("PhisAutomation:Authentication:ManualLoginWaitSeconds", 120),

                SessionTimeoutMinutes = config.GetValue<int>("PhisAutomation:Session:SessionTimeoutMinutes", 20),
                SessionRefreshEnabled = config.GetValue<bool>("PhisAutomation:Session:SessionRefreshEnabled", true),
                RefreshBufferMinutes = config.GetValue<int>("PhisAutomation:Session:RefreshBufferMinutes", 2),

                WebDriverWaitSeconds = config.GetValue<int>("PhisAutomation:Timing:WebDriverWaitSeconds", 10),
                DelayBetweenSearchesMs = config.GetValue<int>("PhisAutomation:Timing:DelayBetweenSearchesMs", 1000),
                PageLoadDelayMs = config.GetValue<int>("PhisAutomation:Timing:PageLoadDelayMs", 2000),
                AjaxWaitMs = config.GetValue<int>("PhisAutomation:Timing:AjaxWaitMs", 1000)
            };
        }

        /// <summary>
        /// Get PHIS column headers configuration
        /// </summary>
        public static PhisColumnHeaders GetPhisColumnHeaders()
        {
            var config = GetConfiguration();
            return new PhisColumnHeaders
            {
                ClientId = config["PhisAutomation:ColumnHeaders:ClientId"] ?? "Client ID",
                FirstName = config["PhisAutomation:ColumnHeaders:FirstName"] ?? "First Name",
                LastName = config["PhisAutomation:ColumnHeaders:LastName"] ?? "Last Name",
                Medicare = config["PhisAutomation:ColumnHeaders:Medicare"] ?? "Health Card Number",
                DateOfBirth = config["PhisAutomation:ColumnHeaders:DateOfBirth"] ?? "Date of Birth"
            };
        }

        /// <summary>
        /// Get Fuzzy Matching configuration
        /// </summary>
        public static FuzzyMatchingConfig GetFuzzyMatchingConfig()
        {
            var config = GetConfiguration();
            return new FuzzyMatchingConfig
            {
                Enabled = config.GetValue<bool>("PhisAutomation:FuzzyMatching:Enabled", true),
                SingleResultThreshold = config.GetValue<double>("PhisAutomation:FuzzyMatching:SingleResultThreshold", 75.0),
                MultipleResultsThreshold = config.GetValue<double>("PhisAutomation:FuzzyMatching:MultipleResultsThreshold", 85.0),
                ManualReviewThreshold = config.GetValue<double>("PhisAutomation:FuzzyMatching:ManualReviewThreshold", 70.0),
                LastNameWeight = config.GetValue<double>("PhisAutomation:FuzzyMatching:LastNameWeight", 0.6),
                FirstNameWeight = config.GetValue<double>("PhisAutomation:FuzzyMatching:FirstNameWeight", 0.4),
                IgnoreHyphensInComparison = config.GetValue<bool>("PhisAutomation:FuzzyMatching:IgnoreHyphensInComparison", true),
                IgnoreSpacesInComparison = config.GetValue<bool>("PhisAutomation:FuzzyMatching:IgnoreSpacesInComparison", true),
                TreatCompoundNamesAsPartialMatch = config.GetValue<bool>("PhisAutomation:FuzzyMatching:TreatCompoundNamesAsPartialMatch", true),
                UseMedicareNumberAsConfirmation = config.GetValue<bool>("PhisAutomation:FuzzyMatching:UseMedicareNumberAsConfirmation", true),
                MedicareNumberBoostScore = config.GetValue<double>("PhisAutomation:FuzzyMatching:MedicareNumberBoostScore", 20.0)
            };
        }

        #endregion



        #region Chrome Driver Configuration

        /// <summary>
        /// Get Chrome Driver configuration
        /// </summary>
        public static ChromeDriverConfig GetChromeDriverConfig()
        {
            var config = GetConfiguration();
            return new ChromeDriverConfig
            {
                UsePortableChrome = config.GetValue<bool>("ChromeDriver:UsePortableChrome", false),
                PortableChromePath = config["ChromeDriver:PortableChromePath"] ?? "",
                ChromeDriverPath = config["ChromeDriver:ChromeDriverPath"] ?? "",
                UseDebuggerMode = config.GetValue<bool>("ChromeDriver:UseDebuggerMode", false),
                DebuggerPort = config.GetValue<int>("ChromeDriver:DebuggerPort", 9222),

                StartMaximized = config.GetValue<bool>("ChromeDriver:Options:StartMaximized", true),
                DisableNotifications = config.GetValue<bool>("ChromeDriver:Options:DisableNotifications", true),
                DisablePopupBlocking = config.GetValue<bool>("ChromeDriver:Options:DisablePopupBlocking", true),
                HideAutomationIndicators = config.GetValue<bool>("ChromeDriver:Options:HideAutomationIndicators", true),
                Headless = config.GetValue<bool>("ChromeDriver:Options:Headless", false),

                DefaultDownloadDirectory = config["ChromeDriver:Download:DefaultDownloadDirectory"] ?? ""
            };
        }

        #endregion



        #region PDF Extraction Configuration



        /// <summary>
        /// Get PDF Extraction configuration (for Phase 2)
        /// </summary>
        public static PdfExtractionConfig GetPdfExtractionConfig()
        {
            var config = GetConfiguration();
            return new PdfExtractionConfig
            {
                LastNameKeywords = config.GetSection("PdfExtraction:LastNameKeywords").Get<string[]>()
                    ?? new[] { "FAMILLE", "NOM", "SURNAME" },
                FirstNameKeywords = config.GetSection("PdfExtraction:FirstNameKeywords").Get<string[]>()
                    ?? new[] { "PRÉNOM", "PRENOM", "GIVEN" },
                ExcludeKeywords = config.GetSection("PdfExtraction:ExcludeKeywords").Get<string[]>()
                    ?? new[] { "PRÉFÉRÉ", "PREFERRED", "DATE" },
                FieldLabelWords = config.GetSection("PdfExtraction:FieldLabelWords").Get<string[]>()
                    ?? Array.Empty<string>(),
                SearchRange = config.GetValue<int>("PdfExtraction:SearchRange", 15),
                MinNameLength = config.GetValue<int>("PdfExtraction:MinNameLength", 2)
            };
        }

        #endregion







    }




}
