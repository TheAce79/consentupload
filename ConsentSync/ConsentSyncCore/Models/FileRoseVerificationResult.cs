using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Models
{
    public class FileRoseVerificationResult
    {

        /// <summary>Total records eligible for the check (ClientId set + ClientIdStatus == Found).</summary>
        public int EligibleRecords { get; set; }

        /// <summary>Number of records where a FileRose PDF was found.</summary>
        public int Found { get; set; }

        /// <summary>Number of eligible records where no matching PDF was located.</summary>
        public int NotFound { get; set; }

        /// <summary>Records that were skipped (ClientId empty or status != Found).</summary>
        public int Skipped { get; set; }

        /// <summary>Full path of the directory that was scanned.</summary>
        public string ScannedDirectory { get; set; } = string.Empty;

        /// <summary>Per-record detail: ClientId → matched file name (or null if not found).</summary>
        public Dictionary<string, string?> Details { get; set; } = new();

    }
}
