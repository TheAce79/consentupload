using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Configuration
{
    public partial class ConfigurationService
    {





        /// <summary>
        /// Get PDF Extraction configuration (for Phase 2)
        /// </summary>
        public static PdfExtractionConfig GetPdfExtractionConfig()
        {
            var config = GetConfiguration();
            return new PdfExtractionConfig
            {
                LastNameKeywords = config.GetSection("PdfExtraction:LastNameKeywords").Get<string[]>()
                    ?? new[] { "FAMILLE", "NOM", "SURNAME" },
                FirstNameKeywords = config.GetSection("PdfExtraction:FirstNameKeywords").Get<string[]>()
                    ?? new[] { "PRÉNOM", "PRENOM", "GIVEN" },
                ExcludeKeywords = config.GetSection("PdfExtraction:ExcludeKeywords").Get<string[]>()
                    ?? new[] { "PRÉFÉRÉ", "PREFERRED", "DATE", "SEXE", "GENDER", "SEX" },
                FieldLabelWords = config.GetSection("PdfExtraction:FieldLabelWords").Get<string[]>()
                    ?? Array.Empty<string>(),
                SearchRange = config.GetValue<int>("PdfExtraction:SearchRange", 15),
                MinNameLength = config.GetValue<int>("PdfExtraction:MinNameLength", 2),

                // ✅ Add these strongly-typed patterns
                LastNamePatterns = config.GetSection("PdfExtraction:LastNamePatterns").Get<List<NamePattern>>()
                    ?? new List<NamePattern>
                    {
                new() { Words = new[] { "LAST", "NAME" }, Language = "English" },
                new() { Words = new[] { "NOM", "DE", "FAMILLE" }, Language = "French" }
                    },
                FirstNamePatterns = config.GetSection("PdfExtraction:FirstNamePatterns").Get<List<NamePattern>>()
                    ?? new List<NamePattern>
                    {
                new() { Words = new[] { "FIRST", "NAME" }, Language = "English" },
                new() { Words = new[] { "PRÉNOM" }, Language = "French" }
                    },
                PreferredNamePatterns = config.GetSection("PdfExtraction:PreferredNamePatterns").Get<List<NamePattern>>()
                    ?? new List<NamePattern>
                    {
                new() { Words = new[] { "PREFERRED", "FIRST", "NAME" }, Language = "English" },
                new() { Words = new[] { "PRÉNOM", "PRÉFÉRÉ" }, Language = "French" }
                    }
            };
        }






    }
}
