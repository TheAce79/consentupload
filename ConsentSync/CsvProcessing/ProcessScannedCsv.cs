using ConsentSyncCore.Services.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsvProcessing
{
    public static class ProcessScannedCsv
    {


        /// <summary>
        /// For a scanned PDF whose ClientId was manually entered in the CSV:
        /// renames and moves the PDF to ScannedOK\{clientId}.pdf, then updates
        /// the CSV row with ClientIdStatus=Found, IsScanPdfReady=true and the new PdfName.
        /// </summary>
        public static (bool success, string message) AssignClientIdToScannedPdf(string originalPdfPath)
        {
            if (!File.Exists(originalPdfPath))
                return (false, $"File not found: {originalPdfPath}");

            string originalFileName = Path.GetFileName(originalPdfPath);

            // ── 1. Find the CSV row by PdfName ────────────────────────────────
            var config = ConfigurationService.GetConfiguration();
            var repo = new CsvProcessing.StudentCsvRepository(config);

            if (!repo.ProcessedCsvExists())
                return (false, "Processed CSV not found.");

            var students = repo.ReadAll();

            var row = students.FirstOrDefault(s =>
                string.Equals(s.PdfName, originalFileName, StringComparison.OrdinalIgnoreCase));

            if (row == null)
                return (false, $"No CSV row found with PdfName = '{originalFileName}'.");

            // ── 2. ClientId must already be filled in by the user ─────────────
            if (string.IsNullOrWhiteSpace(row.ClientId))
                return (false,
                    $"ClientId is empty in the CSV for '{originalFileName}'.\n" +
                    "Open the CSV, fill in the ClientId column for this row, then try again.");

            string clientId = row.ClientId.Trim();
            string newPdfName = $"{clientId}.pdf";

            // ── 3. Move + rename the PDF to ScannedOK ─────────────────────────
            var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            string scannedOkFolder = Path.Combine(bulkConfig.GetOutputReadyPath(), "ScannedOK");

            if (!Directory.Exists(scannedOkFolder))
                Directory.CreateDirectory(scannedOkFolder);

            string destinationPath = Path.Combine(scannedOkFolder, newPdfName);
            File.Move(originalPdfPath, destinationPath, overwrite: true);

            LoggerService.LogInformation(
                $"📁 Moved: {originalFileName} → ScannedOK\\{newPdfName}");

            // ── 4. Update the CSV row ─────────────────────────────────────────
            row.ClientIdStatus = ConsentSyncCore.Models.ClientIdStatus.Found;  // 1
            row.IsScanPdfReady = true;
            row.PdfName = newPdfName;

            repo.SaveAll(students);

            LoggerService.LogInformation(
                $"✅ CSV updated → ClientId={clientId}, " +
                $"ClientIdStatus=Found, IsScanPdfReady=true, PdfName={newPdfName}");

            return (true, $"ClientId={clientId}  |  PDF={newPdfName}");
        }

    }
}
