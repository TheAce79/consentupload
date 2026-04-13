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
        public int UploadReadyRecords { get; set; }
        public int SkippedNotReady { get; set; }
        public int SuccessfulUploads { get; set; }
        public int FailedUploads { get; set; }
        public int PdfsUploaded { get; set; }
        public bool HasErrors { get; set; }
        public List<string> ErrorMessages { get; set; } = new();

        public bool IsSuccessful => !HasErrors && FailedUploads == 0;

        public string Summary => $"Total: {TotalRecords}, Clients: {UploadReadyRecords}, " +
                                $"Success: {SuccessfulUploads}, Failed: {FailedUploads}";

    }
}
