using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Phase2
{
    public class Phase2Result
    {
        public int TotalPdfs { get; set; }
        public int SuccessfullyProcessed { get; set; }
        public int FailedToMatch { get; set; }
        public int FilesGenerated { get; set; }
        public bool HasErrors { get; set; }
        public List<string> ErrorMessages { get; set; } = new();


        /// <summary>
        /// The session-scoped error subfolder actually written to during this run
        /// (e.g. <c>6_Error\Error_20250419_143512</c>).
        /// <c>null</c> when no errors occurred and no folder was created.
        /// </summary>
        public string? SessionErrorDir { get; set; }
    }
}
