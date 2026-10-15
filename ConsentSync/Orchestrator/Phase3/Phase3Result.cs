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
        /// <summary>Records actually started during this run.</summary>
        public int AttemptedRecords { get; set; }
        public int SuccessfulUploads { get; set; }
        public int AlreadyExistingDocuments { get; set; }
        public int FailedUploads { get; set; }
        public bool HasErrors { get; set; }

        // ✅ BUG 3 FIX: IsSuccessful now means uploads actually completed,
        //    not just "no exception was thrown".
        public bool IsSuccessful => !HasErrors && !BatchLimitReached && !AlreadyComplete &&
                                    !NoRecordsFound && TotalRecords > 0 &&
                                    AttemptedRecords == TotalRecords && FailedUploads == 0;

        public bool BatchLimitReached { get; set; }

        /// <summary>True when all records were already verified — nothing was uploaded this run.</summary>
        public bool AlreadyComplete { get; set; }

        /// <summary>True when the upload CSV contains no records at all.</summary>
        public bool NoRecordsFound { get; set; }

        public List<string> ErrorMessages { get; set; } = new();

    }
}
