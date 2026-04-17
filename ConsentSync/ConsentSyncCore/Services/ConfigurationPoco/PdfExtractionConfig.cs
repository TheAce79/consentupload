using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{
    /// <summary>
    /// PDF Extraction configuration
    /// </summary>
    public class PdfExtractionConfig
    {
        public string[] LastNameKeywords { get; set; } = Array.Empty<string>();
        public string[] FirstNameKeywords { get; set; } = Array.Empty<string>();
        public string[] ExcludeKeywords { get; set; } = Array.Empty<string>();
        public string[] FieldLabelWords { get; set; } = Array.Empty<string>();
        public int SearchRange { get; set; }
        public int MinNameLength { get; set; }


        // ✅ Add these strongly-typed properties
        public List<NamePattern> LastNamePatterns { get; set; } = new();
        public List<NamePattern> FirstNamePatterns { get; set; } = new();
        public List<NamePattern> PreferredNamePatterns { get; set; } = new();
    }


    /// <summary>
    /// Name pattern for PDF extraction
    /// </summary>
    public class NamePattern
    {
        public string[] Words { get; set; } = Array.Empty<string>();
        public string Language { get; set; } = string.Empty;
    }


}
