using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{

    /// <summary>
    /// Shared working directory for the Phase 2 → PrePhase 3 → Phase 3 pipeline.
    ///   Phis\
    ///     ├── 1_To_Upload\
    ///     │     ├── 1 Consent Upload\   ← consent PDFs uploaded in Phase 3
    ///     │     │    
    ///     │     └── 2 File Rose Upload\ ← file rose PDFs
    ///     │       ──3 Consent Archive  ← consent PDFs will move here after successful upload (Phase 3)
    ///              ──4 Rose Archive  ← file rose PDFs will move here after successful upload (Phase 3)
    ///     │
    ///     ├── 2_Error\ ← PDFs that failed matching
    ///     └── 3_Csv\   ← Validation_Results.csv, Upload_to_PHIS.csv
    /// </summary>
    public class PhisWorkspaceConfig
    {
        public string BasePath { get; set; } = string.Empty;
        public string ToUploadFolder { get; set; } = "1_To_Upload";
        public string ErrorFolder { get; set; } = "2_Error";
        public string CsvFolder { get; set; } = "3_Csv";

        // Subfolders inside ToUploadFolder
        public string ConsentUploadSubFolder { get; set; } = "1 Consent Upload";

        private string ConsentArchiveSubFolder { get; set; } = "3 Consent Archive";

        public string FileRoseUploadSubFolder { get; set; } = "2 File Rose Upload";

        private string FileRoseArchiveSubFolder { get; set; } = "4 Rose Archive";

        // ── Computed path helpers ──────────────────────────────────────────────
        public string GetToUploadPath() => Path.Combine(BasePath, ToUploadFolder);
        public string GetErrorPath() => Path.Combine(BasePath, ErrorFolder);
        public string GetCsvPath() => Path.Combine(BasePath, CsvFolder);
        public string GetConsentUploadPath() => Path.Combine(GetToUploadPath(), ConsentUploadSubFolder);

        // ✅ Add the path helper
        /// <summary>
        /// Path to the "3 Consent Archive" folder where consent PDFs will be moved after successful upload in Phase 3.
        /// </summary>
        public string GetConsentArchivePath() => Path.Combine(GetToUploadPath(), ConsentArchiveSubFolder);



        public string GetFileRoseUploadPath() => Path.Combine(GetToUploadPath(), FileRoseUploadSubFolder);

        // ✅ Add the path helper
        /// <summary>
        /// Path to the "4 Rose Archive" folder where file rose PDFs will be moved after successful upload in Phase 3.
        /// </summary>
        public string GetFileRoseArchivePath() => Path.Combine(GetToUploadPath(), FileRoseArchiveSubFolder);


    }

}
