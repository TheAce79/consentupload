using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Models
{
    public class PhisSearchResult
    {
        public string ClientId { get; set; } = string.Empty;

        public string? MedicareNumber { get; set; }

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        public string MiddleName { get; set; } = "";
        public string Gender { get; set; } = "";
        public string DateOfBirth { get; set; } = "";
        public string HealthRegion { get; set; } = "";
        public string ActiveStatus { get; set; } = "";

    }

}
