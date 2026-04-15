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
        private static string? _baseDirectory;



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

                        // Load BaseDirectory
                        _baseDirectory = _config["BaseDirectory"] ?? "C:\\PHIS";

                        Console.WriteLine($"✅ Configuration loaded (Environment: {environment})");
                        Console.WriteLine($"📁 Base Directory: {_baseDirectory}");
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
                _baseDirectory = null;
                GetConfiguration();
            }
        }



        /// <summary>
        /// Get the base directory
        /// </summary>
        public static string GetBaseDirectory()
        {
            if (_config == null)
            {
                GetConfiguration();
            }
            return _baseDirectory ?? "C:\\PHIS";
        }


        /// <summary>
        /// Resolve path with placeholders
        /// Supported placeholders:
        ///   {BaseDirectory} - Base directory for all operations
        ///   {SchoolName} - Current school name
        ///   {Grade} - Current grade
        ///   {SchoolYear} - Current school year
        /// </summary>
        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            // Ensure configuration is loaded
            if (_config == null)
            {
                GetConfiguration();
            }

            var schoolContext = GetSchoolContextConfig();
            var baseDir = GetBaseDirectory();

            // ✅ DEBUG: Show resolution
            Console.WriteLine($"      ResolvePath Input: '{path}'");
            Console.WriteLine($"      BaseDirectory: '{baseDir}'");
            Console.WriteLine($"      SchoolName: '{schoolContext.SchoolName}'");
            Console.WriteLine($"      Grade: '{schoolContext.Grade}'");
            Console.WriteLine($"      SchoolYear: '{schoolContext.SchoolYear}'");

            var resolved = path
                .Replace("{BaseDirectory}", baseDir)
                .Replace("{SchoolName}", schoolContext.SchoolName)
                .Replace("{Grade}", schoolContext.Grade)
                .Replace("{SchoolYear}", schoolContext.SchoolYear);

            Console.WriteLine($"      ResolvePath Output: '{resolved}'");

            return resolved;
        }



        /// <summary>
        /// Resolve multiple paths with placeholders
        /// </summary>
        private static string[] ResolvePaths(string[] paths)
        {
            return paths?.Select(ResolvePath).ToArray() ?? Array.Empty<string>();
        }


        #region School Context Configuration

        /// <summary>
        /// Get School Context configuration (shared across all phases)
        /// </summary>
        public static SchoolContextConfig GetSchoolContextConfig()
        {
            var config = GetConfiguration();
            return new SchoolContextConfig
            {
                SchoolName = config["SchoolContext:SchoolName"] ?? "Unknown School",
                Grade = config["SchoolContext:Grade"] ?? "0",
                SchoolYear = config["SchoolContext:SchoolYear"] ?? "2024-2025"
            };
        }

        #endregion School Context Configuration




        #region Bulk Pdf Configuration



        /// <summary>
        /// Get Bulk PDF Extraction configuration with new folder structure
        /// </summary>
        public static BulkPdfExtractionConfig GetBulkPdfExtractionConfig()
        {
            var config = GetConfiguration();
            var section = config.GetSection("BulkPdfExtraction");

            var bulkConfig = new BulkPdfExtractionConfig
            {
                Enabled = section.GetValue<bool>("Enabled", false),
                Description = section.GetValue<string>("Description") ?? "",

                // ✅ NEW: Base path and folder structure
                BasePdfPath = section.GetValue<string>("BasePdfPath") ?? "",
                InputBulkFolder = section.GetValue<string>("InputBulkFolder") ?? "1_Input_Bulk",
                InputScannedFolder = section.GetValue<string>("InputScannedFolder") ?? "2_Input_Scanned",
                OutputReadyFolder = section.GetValue<string>("OutputReadyFolder") ?? "3_Output_Ready",
                ErrorFolder = section.GetValue<string>("ErrorFolder") ?? "4_Error",
                ArchiveFolder = section.GetValue<string>("ArchiveFolder") ?? "5_Archive",

                // Processing settings
                PagesPerConsent = section.GetValue<int>("PagesPerConsent", 1),
                StartPage = section.GetValue<int>("StartPage", 1),
                AutoDetectNames = section.GetValue<bool>("AutoDetectNames", true),
                NamingFormat = section.GetValue<string>("NamingFormat") ?? "{ID}_{LastName}_{FirstName}_consent",
                OverwriteExisting = section.GetValue<bool>("OverwriteExisting", false),
                MoveToArchiveAfterProcessing = section.GetValue<bool>("MoveToArchiveAfterProcessing", true),
                MoveErrorPdfsToErrorFolder = section.GetValue<bool>("MoveErrorPdfsToErrorFolder", true),

            };

            // ✅ Resolve BasePdfPath with placeholders using ResolvePath
            bulkConfig.BasePdfPath = ResolvePath(bulkConfig.BasePdfPath);



            return bulkConfig;
        }





        #endregion Bulk Pdf Configuration




        #region CSV Processing Configuration

        /// <summary>
        /// Get CSV Processing configuration with resolved paths
        /// </summary>
        public static CsvProcessingConfig GetCsvConfig()
        {
            var config = GetConfiguration();

            // Get raw values
            var rawInputPath = config["CsvProcessing:InputCsvPath"] ?? "";
            var rawOutputPath = config["CsvProcessing:OutputCsvPath"] ?? "";

            // ✅ DEBUG: Show before/after resolution
            Console.WriteLine($"\n🔍 DEBUG GetCsvConfig:");
            Console.WriteLine($"   Raw InputPath:  {rawInputPath}");
            Console.WriteLine($"   Raw OutputPath: {rawOutputPath}");

            var resolvedInputPath = ResolvePath(rawInputPath);
            var resolvedOutputPath = ResolvePath(rawOutputPath);

            Console.WriteLine($"   ✅ Resolved InputPath:  {resolvedInputPath}");
            Console.WriteLine($"   ✅ Resolved OutputPath: {resolvedOutputPath}");

            return new CsvProcessingConfig
            {
                InputCsvPath = resolvedInputPath,
                InputCsvFileName = config["CsvProcessing:InputCsvFileName"] ?? "immunizations.csv",
                OutputCsvPath = resolvedOutputPath,
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
        /// Get full input CSV path (resolved)
        /// </summary>
        public static string GetInputCsvFullPath()
        {
            var csvConfig = GetCsvConfig();
            var fullPath = Path.Combine(csvConfig.InputCsvPath, csvConfig.InputCsvFileName);

            // ✅ DEBUG: Show what we're resolving
            Console.WriteLine($"🔍 DEBUG GetInputCsvFullPath:");
            Console.WriteLine($"   InputCsvPath (raw): {csvConfig.InputCsvPath}");
            Console.WriteLine($"   InputCsvFileName: {csvConfig.InputCsvFileName}");
            Console.WriteLine($"   Combined Path: {fullPath}");

            return fullPath;
        }


        /// <summary>
        /// Get full output CSV path (resolved)
        /// </summary>
        public static string GetOutputCsvFullPath()
        {
            var csvConfig = GetCsvConfig();
            var fullPath = Path.Combine(csvConfig.OutputCsvPath, csvConfig.OutputCsvFileName);

            // ✅ DEBUG: Show what we're resolving
            Console.WriteLine($"🔍 DEBUG GetOutputCsvFullPath:");
            Console.WriteLine($"   OutputCsvPath (raw): {csvConfig.OutputCsvPath}");
            Console.WriteLine($"   OutputCsvFileName: {csvConfig.OutputCsvFileName}");
            Console.WriteLine($"   Combined Path: {fullPath}");

            return fullPath;
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
        /// 
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

                // ✅ Resolve paths with placeholders
                RenamedPath = ResolvePath(config["Phase2:Download:RenamedPath"] ?? ""),
                TempPath = ResolvePath(config["Phase2:Download:TempPath"] ?? ""),
                ErrorOutputDir = ResolvePath(config["Phase2:Download:ErrorOutputDir"] ?? ""),

                MaxDownloadRetries = config.GetValue<int>("Phase2:Download:MaxDownloadRetries", 3),
                DelayBetweenDownloadsMs = config.GetValue<int>("Phase2:Download:DelayBetweenDownloadsMs", 1000),

                ValidateNamesBeforeRename = config.GetValue<bool>("Phase2:PdfProcessing:ValidateNamesBeforeRename", true),
                SplitMultiPagePdfs = config.GetValue<bool>("Phase2:PdfProcessing:SplitMultiPagePdfs", true),
                FileRosePageThreshold = config.GetValue<int>("Phase2:PdfProcessing:FileRosePageThreshold", 1),
                DebugMode = config.GetValue<bool>("Phase2:PdfProcessing:DebugMode", false),
                DebugOutputDir = ResolvePath(config["Phase2:PdfProcessing:DebugOutputDir"] ?? ""),
                UseFuzzyMatching = config.GetValue<bool>("Phase2:PdfProcessing:UseFuzzyMatching", true),
                ReadNamesFromFilename = config.GetValue<bool>("Phase2:PdfProcessing:ReadNamesFromFilename", true), // ✅ Added

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

        #endregion



        #region Pre-Phase 3 Configuration


        /// <summary>
        /// Get Pre-Phase 3 configuration with resolved paths
        /// </summary>
        public static PrePhase3Config GetPrePhase3Config()
        {
            var config = GetConfiguration();
            return new PrePhase3Config
            {
                Enabled = config.GetValue<bool>("PrePhase3:Enabled", true),
                Description = config["PrePhase3:Description"] ?? "Validate and prepare PDFs for upload",

                // ✅ Resolve paths with placeholders
                ValidationCsvPath = ResolvePath(config["PrePhase3:ValidationCsvPath"] ?? ""),
                ValidationCsvFileName = config["PrePhase3:ValidationCsvFileName"] ?? "Validation_Results.csv",
               
                OutputPath = ResolvePath(config["PrePhase3:OutputPath"] ?? ""),
                MinMatchScoreToAutoAccept = config.GetValue<double>("PrePhase3:MinMatchScoreToAutoAccept", 90.0)
            };
        }



        #endregion  Pre-Phase 3 Configuration



        #region Phase 3 Configuration


        public static Phase3Config GetPhase3Config()
        {
            var config = GetConfiguration();
            var phase3Section = config.GetSection("Phase3");
            var phase3Config = new Phase3Config
            {
                Enabled = phase3Section.GetValue<bool>("Enabled"),
                Description = phase3Section.GetValue<string>("Description") ?? string.Empty,

                // Input section
                Input = new Phase3InputConfig
                {
                    UploadCsvPath = phase3Section.GetValue<string>("Input:UploadCsvPath") ?? string.Empty,
                    UploadCsvFileName = phase3Section.GetValue<string>("Input:UploadCsvFileName") ?? string.Empty,
                    PdfPath = phase3Section.GetValue<string>("Input:PdfPath") ?? string.Empty
                },

                // Upload section
                Upload = new Phase3UploadConfig
                {
                    MaxUploadRetries = phase3Section.GetValue<int>("Upload:MaxUploadRetries"),
                    DelayBetweenUploadsMs = phase3Section.GetValue<int>("Upload:DelayBetweenUploadsMs"),
                    WaitAfterUploadMs = phase3Section.GetValue<int>("Upload:WaitAfterUploadMs"),
                    VerifyUploadSuccess = phase3Section.GetValue<bool>("Upload:VerifyUploadSuccess")
                },

                // FileRose section
                FileRose = new Phase3FileRoseConfig
                {
                    FileRoseEnabled = phase3Section.GetValue<bool>("FileRose:FileRoseEnabled"),
                    FileRosePath = phase3Section.GetValue<string>("FileRose:FileRosePath") ?? string.Empty,
                    UseCustomFileRosePerVaccine = phase3Section.GetValue<bool>("FileRose:UseCustomFileRosePerVaccine")
                },

                // Navigation section - ✅ UPDATED
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

                // Output section
                Output = new Phase3OutputConfig
                {
                    CompletedCsvFileName = phase3Section.GetValue<string>("Output:CompletedCsvFileName") ?? string.Empty
                }
            };

            // Apply variable substitutions using existing ResolvePath method
            phase3Config.Input.UploadCsvPath = ResolvePath(phase3Config.Input.UploadCsvPath);
            phase3Config.Input.PdfPath = ResolvePath(phase3Config.Input.PdfPath);

            if (phase3Config.FileRose.FileRoseEnabled)
            {
                phase3Config.FileRose.FileRosePath = ResolvePath(phase3Config.FileRose.FileRosePath);
            }

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

                // ✅ ADD THESE TWO LINES - they're missing!
                TreatSpaceSeparatedNamesAsCompound = config.GetValue<bool>("PhisAutomation:FuzzyMatching:TreatSpaceSeparatedNamesAsCompound", true),
                CompoundNameMatchScore = config.GetValue<double>("PhisAutomation:FuzzyMatching:CompoundNameMatchScore", 95.0),
                MinimumCompoundMatchRatio = config.GetValue<double>("PhisAutomation:FuzzyMatching:MinimumCompoundMatchRatio", 0.5),

                UseMedicareNumberAsConfirmation = config.GetValue<bool>("PhisAutomation:FuzzyMatching:UseMedicareNumberAsConfirmation", true),
                MedicareNumberBoostScore = config.GetValue<double>("PhisAutomation:FuzzyMatching:MedicareNumberBoostScore", 20.0)
            };
        }

        #endregion



        #region Chrome Driver Configuration

        /// <summary>
        /// Get ChromeDriver configuration with resolved paths
        /// </summary>
        public static ChromeDriverConfig GetChromeDriverConfig()
        {
            var config = GetConfiguration();
            return new ChromeDriverConfig
            {
                UsePortableChrome = config.GetValue<bool>("ChromeDriver:UsePortableChrome", false),
                PortableChromePath = ResolvePath(config["ChromeDriver:PortableChromePath"] ?? ""),
                ChromeDriverPath = ResolvePath(config["ChromeDriver:ChromeDriverPath"] ?? ""),
                UseDebuggerMode = config.GetValue<bool>("ChromeDriver:UseDebuggerMode", false),
                DebuggerPort = config.GetValue<int>("ChromeDriver:DebuggerPort", 9222),

                StartMaximized = config.GetValue<bool>("ChromeDriver:Options:StartMaximized", true),
                DisableNotifications = config.GetValue<bool>("ChromeDriver:Options:DisableNotifications", true),
                DisablePopupBlocking = config.GetValue<bool>("ChromeDriver:Options:DisablePopupBlocking", true),
                HideAutomationIndicators = config.GetValue<bool>("ChromeDriver:Options:HideAutomationIndicators", true),
                Headless = config.GetValue<bool>("ChromeDriver:Options:Headless", false),

                DefaultDownloadChromeDirectory = ResolvePath(config["ChromeDriver:Download:DefaultDownloadChromeDirectory"] ?? "")
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
                    ?? new[] { "PRÉFÉRÉ", "PREFERRED", "DATE", "SEXE", "GENDER", "SEX" },
                FieldLabelWords = config.GetSection("PdfExtraction:FieldLabelWords").Get<string[]>()
                    ?? Array.Empty<string>(),
                SearchRange = config.GetValue<int>("PdfExtraction:SearchRange", 15),
                MinNameLength = config.GetValue<int>("PdfExtraction:MinNameLength", 2),

                // ✅ Add these strongly-typed patterns
                LastNamePatterns = config.GetSection("PdfExtraction:LastNamePatterns").Get<List<NamePattern>>()
                    ?? new List<NamePattern>
                    {
                new() { Words = new[] { "LAST", "NAME" }, Language = "English" },
                new() { Words = new[] { "NOM", "DE", "FAMILLE" }, Language = "French" }
                    },
                FirstNamePatterns = config.GetSection("PdfExtraction:FirstNamePatterns").Get<List<NamePattern>>()
                    ?? new List<NamePattern>
                    {
                new() { Words = new[] { "FIRST", "NAME" }, Language = "English" },
                new() { Words = new[] { "PRÉNOM" }, Language = "French" }
                    },
                PreferredNamePatterns = config.GetSection("PdfExtraction:PreferredNamePatterns").Get<List<NamePattern>>()
                    ?? new List<NamePattern>
                    {
                new() { Words = new[] { "PREFERRED", "FIRST", "NAME" }, Language = "English" },
                new() { Words = new[] { "PRÉNOM", "PRÉFÉRÉ" }, Language = "French" }
                    }
            };
        }

        #endregion





        /// <summary>
        /// Get Logging configuration
        /// </summary>
        public static LoggingConfig GetLoggingConfig()
        {
            var config = GetConfiguration();
            return new LoggingConfig
            {
                LogLevel = new LogLevelConfig
                {
                    Default = config["Logging:LogLevel:Default"] ?? "Information",
                    ConsentSync = config["Logging:LogLevel:ConsentSync"] ?? "Debug",
                    Microsoft = config["Logging:LogLevel:Microsoft"] ?? "Warning",
                    System = config["Logging:LogLevel:System"] ?? "Warning"
                },
                Console = new ConsoleLoggingConfig
                {
                    Enabled = config.GetValue<bool>("Logging:Console:Enabled", true),
                    UseColoredOutput = config.GetValue<bool>("Logging:Console:UseColoredOutput", true),
                    ShowTimestamps = config.GetValue<bool>("Logging:Console:ShowTimestamps", true),
                    TimestampFormat = config["Logging:Console:TimestampFormat"] ?? "yyyy-MM-dd HH:mm:ss"
                },
                File = new FileLoggingConfig
                {
                    Enabled = config.GetValue<bool>("Logging:File:Enabled", false),
                    LogPath = ResolvePath(config["Logging:File:LogPath"] ?? "{BaseDirectory}\\Logs"),
                    LogFileName = config["Logging:File:LogFileName"] ?? "ConsentSync_{Date}.log",
                    MaxFileSizeMB = config.GetValue<int>("Logging:File:MaxFileSizeMB", 10),
                    RetainDays = config.GetValue<int>("Logging:File:RetainDays", 30),
                    MinimumLevel = config["Logging:File:MinimumLevel"] ?? "Information"
                }
            };
        }



    }




}
