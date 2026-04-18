using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Phase3
{
    public class Phase3Result
    {


        public int TotalRecords { get; set; }
        public int SuccessfulUploads { get; set; }
        public bool HasErrors { get; set; }
        public bool IsSuccessful => !HasErrors;

        /// <summary>
        /// True when the run was stopped because BatchSize was reached.
        /// Records with VerifStatus = NotProcessed will be picked up
        /// automatically on the next run.
        /// </summary>
        public bool BatchLimitReached { get; set; }

        public List<string> ErrorMessages { get; set; } = new();

    }
}
