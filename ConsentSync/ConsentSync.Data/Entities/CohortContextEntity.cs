using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSync.Data.Entities
{
    public class CohortContextEntity
    {
        public int CohortContextId { get; set; }
        public int? PhisCohortId { get; set; }      // e.g., 24182
        public int? PhisClientListId { get; set; }  // e.g., 24111
        public string Prefix { get; set; } = "CIP";
        public string Location { get; set; } = "MONCTON";
        public string Type { get; set; } = "SP";
        public string Jurisdiction { get; set; } = "Moncton Public Health, Moncton, New Brunswick";
        public string EncounterGroup { get; set; } = "Immunization";
        public string ClientListName { get; set; } = string.Empty;
        public DateTime CohortDate { get; set; } = DateTime.Today;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public string ResolvedListName => ClientListName;

    }
}
