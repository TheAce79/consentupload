namespace Orchestrator.Phase1
{
    public class Phase1Result
    {
        public int TotalStudents { get; set; }
        public int ToProcessCount { get; set; }
        public int FoundCount { get; set; }
        public int ManualReviewCount { get; set; }
        public int ErrorCount { get; set; }
        public int DuplicatesAssigned { get; set; }
        public bool HasErrors { get; set; }

        /// <summary>
        /// True when the run was stopped because BatchSize was reached.
        /// Remaining records (ClientIdStatus = NotProcessed) will be picked up
        /// on the next run automatically.
        /// </summary>
        public bool BatchLimitReached { get; set; }

        public int TotalProcessed => FoundCount + ManualReviewCount + ErrorCount;
    }
}