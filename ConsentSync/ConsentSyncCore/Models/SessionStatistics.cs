using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Models
{
    /// <summary>
    /// Session statistics
    /// </summary>
    public class SessionStatistics
    {
        public bool IsLoggedIn { get; set; }
        public DateTime LastActivityTime { get; set; }
        public TimeSpan TimeSinceLastActivity { get; set; }
        public TimeSpan TimeUntilTimeout { get; set; }
        public int SessionTimeoutMinutes { get; set; }
        public bool AutoRefreshEnabled { get; set; }

        public bool IsAboutToExpire => TimeUntilTimeout.TotalMinutes < 2;
        public double PercentageRemaining => (TimeUntilTimeout.TotalMinutes / SessionTimeoutMinutes) * 100;
    }
}
