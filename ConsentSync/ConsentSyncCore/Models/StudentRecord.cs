using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Models
{
    /// <summary>
    /// Status of Client ID search for a student record
    /// </summary>
    public enum ClientIdStatus
    {
        /// <summary>Not yet searched</summary>
        NotProcessed = 0,

        /// <summary>Client ID found successfully</summary>
        Found = 1,

        /// <summary>Error occurred or no match found - needs manual review</summary>
        NeedsManualReview = 2
    }
    public class StudentRecord
    {
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string School { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty; // Format: yyyy-MM-dd
        public string MedicareNumber { get; set; } = string.Empty;
        public string ConsentStatus { get; set; } = string.Empty;
        public string Tdap { get; set; } = string.Empty;
        public string HPV { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public bool IsFileRoseDefaut { get; set; }

        /// <summary>
        /// Status of Client ID search (0=NotProcessed, 1=Found, 2=NeedsManualReview)
        /// </summary>
        public ClientIdStatus ClientIdStatus { get; set; } = ClientIdStatus.NotProcessed;

        /// <summary>
        /// Best match suggestion for manual review (Format: FirstName#LastName#ClientID#Score)
        /// Only populated when ClientIdStatus = NeedsManualReview
        /// </summary>
        public string BestMatch { get; set; } = string.Empty;
    }

   
   
}
