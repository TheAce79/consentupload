using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{




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


        /// <summary>
        /// Maximum number of records to process per run (Phase 1 search + Phase 3 upload).
        /// Once the batch is exhausted the run stops cleanly so the user can verify
        /// results before continuing. Set to 0 to disable batching (process all).
        /// Default: 60.
        /// </summary>
        public int BatchSize { get; set; } = 60;
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






}
