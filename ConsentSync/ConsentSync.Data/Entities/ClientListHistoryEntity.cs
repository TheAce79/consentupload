using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSync.Data.Entities
{
    public class ClientListHistoryEntity
    {
        public int Id { get; set; }
        public int CohortContextId { get; set; }
        public string ResolvedListName { get; set; } = string.Empty;
        public int ClientCount { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    }
}
