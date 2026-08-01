namespace ConsentSyncCore.Models
{
    public class MassImmsExportResult
    {
        public bool Success { get; private set; }
        public int ExportedCount { get; private set; }
        public string? ErrorMessage { get; private set; }

        public static MassImmsExportResult IsSuccess(int exportedCount)
        {
            return new MassImmsExportResult
            {
                Success = true,
                ExportedCount = exportedCount
            };
        }

        public static MassImmsExportResult Failed(string errorMessage)
        {
            return new MassImmsExportResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
