using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Models
{
    public class UploadRecord
    {

        public string ClientID { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string DocumentTitle { get; set; } = string.Empty; // Filename without extension
        public string Description { get; set; } = string.Empty;   // ConsentHPV9, ConsentTdap, etc.

        public string PhisAntigen { get; set; } = string.Empty;   // ✅ NEW: HPV-9, Tetanus (T), Men-C-ACYW-135

        public bool IsFeuilleRose { get; set; } = false;
        public string Status { get; set; } = string.Empty;
        public bool IsFeuilleRoseUpload { get; set; } = false;


    }


    /// <summary>
    /// CSV mapping for UploadRecord
    /// </summary>
    public sealed class UploadRecordMap : ClassMap<UploadRecord>
    {
        public UploadRecordMap()
        {
            Map(m => m.ClientID).Name("ClientID");
            Map(m => m.LastName).Name("Last Name");
            Map(m => m.FirstName).Name("First Name");
            Map(m => m.DocumentTitle).Name("Document Title");
            Map(m => m.Description).Name("Description");
            Map(m => m.PhisAntigen).Name("PhisAntigen"); // ✅ NEW
            Map(m => m.IsFeuilleRose).Name("IsFeuilleRose");
            Map(m => m.Status).Name("Status");
            Map(m => m.IsFeuilleRoseUpload).Name("IsFeuilleRoseUpload");
        }
    }



}
