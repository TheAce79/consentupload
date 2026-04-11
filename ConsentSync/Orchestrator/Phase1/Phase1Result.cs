using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Phase1
{
    public class Phase1Result
    {
        public int TotalStudents { get; set; }
        public int ToProcessCount { get; set; }
        public int FoundCount { get; set; }
        public int ManualReviewCount { get; set; }
        public int ErrorCount { get; set; }
        public bool HasErrors { get; set; }

        public int TotalProcessed => FoundCount + ManualReviewCount + ErrorCount;
        public double SuccessRate => ToProcessCount > 0 ? (FoundCount / (double)ToProcessCount) * 100 : 0;
    }
}
