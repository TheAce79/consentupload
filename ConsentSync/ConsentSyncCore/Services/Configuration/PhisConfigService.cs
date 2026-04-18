using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;

namespace ConsentSyncCore.Services.Configuration
{
    public partial class ConfigurationService
    {
        /// <summary>Get PHIS Automation configuration.</summary>
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
                AjaxWaitMs = config.GetValue<int>("PhisAutomation:Timing:AjaxWaitMs", 1000),

                // ✅ How many records to process per run before stopping for user review.
                // Set to 0 to process everything in one run.
                BatchSize = config.GetValue<int>("PhisAutomation:BatchSize", 60)
            };
        }

        /// <summary>Get PHIS column headers configuration.</summary>
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

        /// <summary>Get Fuzzy Matching configuration.</summary>
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
                TreatSpaceSeparatedNamesAsCompound = config.GetValue<bool>("PhisAutomation:FuzzyMatching:TreatSpaceSeparatedNamesAsCompound", true),
                CompoundNameMatchScore = config.GetValue<double>("PhisAutomation:FuzzyMatching:CompoundNameMatchScore", 95.0),
                MinimumCompoundMatchRatio = config.GetValue<double>("PhisAutomation:FuzzyMatching:MinimumCompoundMatchRatio", 0.5),
                UseMedicareNumberAsConfirmation = config.GetValue<bool>("PhisAutomation:FuzzyMatching:UseMedicareNumberAsConfirmation", true),
                MedicareNumberBoostScore = config.GetValue<double>("PhisAutomation:FuzzyMatching:MedicareNumberBoostScore", 20.0)
            };
        }
    }
}