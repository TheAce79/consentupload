using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;

namespace ConsentSyncCore.Services.Configuration
{
    public partial class ConfigurationService
    {
        /// <summary>
        /// Get the CSV workspace directory config (per-grade folder structure)
        /// </summary>
        public static CsvWorkspaceConfig GetCsvWorkspaceConfig()
        {
            var config = GetConfiguration();
            var section = config.GetSection("CsvWorkspace");

            var ws = new CsvWorkspaceConfig
            {
                BaseCsvPath = section.GetValue<string>("BaseCsvPath") ?? "{BaseDirectory}\\Csv",
                InputFolder = section.GetValue<string>("InputFolder") ?? "1_Input Csv",
                OutputFolder = section.GetValue<string>("OutputFolder") ?? "2_Output Csv",
                ConsentCsvSubFolder = section.GetValue<string>("ConsentCsvSubFolder") ?? "1 Consent Csv",
                ProcessedCsvSubFolder = section.GetValue<string>("ProcessedCsvSubFolder") ?? "1 Processed Csv",
                UploadCsvSubFolder = section.GetValue<string>("UploadCsvSubFolder") ?? "2 Upload Csv",
            };

            ws.BaseCsvPath = ResolvePath(ws.BaseCsvPath);
            return ws;
        }


        /// <summary>
        /// Get CSV Processing configuration — paths derived from CsvWorkspace
        /// </summary>
        public static CsvProcessingConfig GetCsvConfig()
        {
            var config = GetConfiguration();
            var ws = GetCsvWorkspaceConfig();

            return new CsvProcessingConfig
            {
                // ✅ Paths come entirely from CsvWorkspace — nothing hardcoded
                InputCsvPath = ws.GetConsentCsvPath(),
                OutputCsvPath = ws.GetProcessedCsvPath(),

                InputCsvFileName = config["CsvProcessing:InputCsvFileName"] ?? "immunizations.csv",
                OutputCsvFileName = config["CsvProcessing:OutputCsvFileName"] ?? "immunizations_processed.csv",

                SaveProgressEveryNRecords = config.GetValue<int>("CsvProcessing:SaveProgressEveryNRecords", 5),
                DateOfBirthColumn = config["CsvProcessing:DateOfBirthColumn"] ?? "Date of Birth",
                DateFormat = config["CsvProcessing:DateFormat"] ?? "yyyy-MM-dd",
                InputDateFormats = config.GetSection("CsvProcessing:InputDateFormats").Get<string[]>()
                                        ?? new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" },
                LastNameColumn = config["CsvProcessing:LastNameColumn"] ?? "Last Name",
                FirstNameColumn = config["CsvProcessing:FirstNameColumn"] ?? "First Name",
                ClientIdColumn = config["CsvProcessing:ClientIdColumn"] ?? "ClientId",
            };
        }


        /// <summary>Get full input CSV path (resolved)</summary>
        public static string GetInputCsvFullPath()
        {
            var c = GetCsvConfig();
            return Path.Combine(c.InputCsvPath, c.InputCsvFileName);
        }

        /// <summary>Get full output (processed) CSV path (resolved)</summary>
        public static string GetOutputCsvFullPath()
        {
            var c = GetCsvConfig();
            return Path.Combine(c.OutputCsvPath, c.OutputCsvFileName);
        }
    }
}