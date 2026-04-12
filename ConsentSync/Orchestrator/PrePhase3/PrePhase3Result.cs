using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.PrePhase3
{
    public class PrePhase3Result
    {
        public int TotalRecords { get; set; }
        public int ValidatedRecords { get; set; }
        public int PdfsProcessed { get; set; }
        public int FilesGenerated { get; set; }
        public int UploadRecordsCreated { get; set; }
        public int SkippedNotValidated { get; set; }
        public int SkippedMissingPdf { get; set; }
        public bool HasErrors { get; set; }
        public List<string> ErrorMessages { get; set; } = new();
    }
}
