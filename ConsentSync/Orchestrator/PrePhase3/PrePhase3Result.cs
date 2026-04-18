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
        public bool HasErrors { get; set; }
        public List<string> ErrorMessages { get; set; } = new();
    }
}