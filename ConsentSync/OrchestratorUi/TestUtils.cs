using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.Pdf;

namespace OrchestratorUi
{
    public static class TestUtils
    {
        /// <summary>
        /// Reads all PDFs in <paramref name="pPath"/> and logs the extracted
        /// first name, last name, and page count for each file.
        /// </summary>
        public static void ReadPdfName(string pPath)
        {
            if (string.IsNullOrWhiteSpace(pPath) || !Directory.Exists(pPath))
            {
                LoggerService.LogWarning($"⚠️  ReadPdfName: directory not found → {pPath}");
                return;
            }

            var files = Directory.GetFiles(pPath, "*.pdf");

            LoggerService.LogInformation(new string('═', 60));
            LoggerService.LogInformation($"🧪 TEST — ReadPdfName: {files.Length} PDF(s) in {pPath}");
            LoggerService.LogInformation(new string('═', 60));

            if (files.Length == 0)
            {
                LoggerService.LogWarning("   No PDF files found.");
                return;
            }

            int ok = 0, failed = 0;

            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                try
                {
                    var (firstName, lastName, dateOfBirth, pageCount) =
                        PdfProcessor.ProcessSingleScannedPdf(file, debugOcr: false, debugOutputDir: null);

                    bool isUnknown = firstName is "Unknown" or "Error"
                                  || lastName is "Unknown" or "Error";

                    if (isUnknown)
                    {
                        LoggerService.LogWarning(
                            $"   ⚠️  {fileName,-45} → {firstName} {lastName}  (pages: {pageCount}   (DOB: {dateOfBirth})");
                        failed++;
                    }
                    else
                    {
                        LoggerService.LogInformation(
                            $"   ✅  {fileName,-45} → {firstName} {lastName}  (pages: {pageCount})  (DOB: {dateOfBirth})");
                        ok++;
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.LogInformation(
                        $"   ❌  {fileName,-45} → ERROR: {ex.Message}");
                    failed++;
                }
            }

            LoggerService.LogInformation(new string('─', 60));
            LoggerService.LogInformation($"   ✅ Success : {ok}   ⚠️  Failed : {failed}   📄 Total : {files.Length}");
            LoggerService.LogInformation(new string('═', 60));
        }
    }
}