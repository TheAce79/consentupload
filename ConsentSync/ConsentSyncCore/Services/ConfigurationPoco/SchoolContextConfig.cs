using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{

    /// <summary>
    /// School context configuration (shared across all phases)
    /// </summary>
    public class SchoolContextConfig
    {
        public string SchoolName { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string SchoolYear { get; set; } = string.Empty;

    }
}
