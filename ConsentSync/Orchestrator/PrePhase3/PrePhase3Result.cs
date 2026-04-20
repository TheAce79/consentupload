namespace Orchestrator.PrePhase3
{
    public class PrePhase3Result
    {
        public int TotalRecords { get; set; }
        public int ValidatedRecords { get; set; }
        public int SkippedNotValidated { get; set; }
        public int PdfsProcessed { get; set; }
        public int FilesGenerated { get; set; }

        /// <summary>FileRose rows appended to Upload_to_PHIS.csv (IsFeuilleRose = true).</summary>
        public int FileRoseRecordsCreated { get; set; }

        public int UploadRecordsCreated { get; set; }
        public int SkippedMissingPdf { get; set; }
        public int DuplicatesMerged { get; set; }

        /// <summary>
        /// True when 3_Output_Ready was already empty AND the Upload CSV already
        /// existed — nothing to do, user should be told it is already processed.
        /// </summary>
        public bool AlreadyProcessed { get; set; }


        public bool HasErrors { get; set; }
        public List<string> ErrorMessages { get; set; } = new();

        /// <summary>
        /// PDFs still sitting in 3_Output_Ready after processing —
        /// unmatched files the user needs to rename to {ClientId}.pdf.
        /// </summary>
        public List<string> RemainingUnmatchedPdfs { get; set; } = new();
    }
}