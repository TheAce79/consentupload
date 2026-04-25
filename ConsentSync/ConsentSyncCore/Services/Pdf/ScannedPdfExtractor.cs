

using ConsentSyncCore.Services.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConsentSyncCore.Services.Pdf
{
    public partial class BulkPdfExtractor
    {
        public static void ProcessScannedFolder()
        {
            var config = ConfigurationService.GetConfiguration();
            var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            var csvWsConfig = ConfigurationService.GetCsvWorkspaceConfig();
            var csvConfig = ConfigurationService.GetCsvConfig();

            string inputScannedPath = bulkConfig.GetInputScannedPath();
            string outputCsvFolder = csvWsConfig.GetProcessedCsvPath();
            string baseCsvFileName = csvConfig.OutputCsvFileName ?? "immunizations_processed.csv";

            string schoolName = config["SchoolContext:SchoolName"] ?? config["SchoolName"] ?? "";
            string grade = config["SchoolContext:Grade"] ?? config["Grade"] ?? "";
            string namingFormat = config["BulkPdfExtraction:NamingFormat"] ?? config["NamingFormat"] ?? "{ID}_{LastName}_{FirstName}_consent";

            string mainCsvPath = Path.Combine(outputCsvFolder, baseCsvFileName);
            string targetCsvPath = mainCsvPath;
            string successfulPdfPath = bulkConfig.GetOutputReadyPath();

            if (File.Exists(mainCsvPath))
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(baseCsvFileName);
                string extension = Path.GetExtension(baseCsvFileName);
                targetCsvPath = Path.Combine(outputCsvFolder, $"{fileNameWithoutExt}_Scanned{extension}");
            }

            if (!Directory.Exists(inputScannedPath))
            {
                LoggerService.LogWarning($"Scanned input folder not found: {inputScannedPath}");
                return;
            }

            if (!Directory.Exists(outputCsvFolder))
                Directory.CreateDirectory(outputCsvFolder);

            var pdfFiles = Directory.GetFiles(inputScannedPath, "*.pdf");
            LoggerService.LogInformation($"Found {pdfFiles.Length} scanned PDFs to process in {inputScannedPath}");

            if (pdfFiles.Length == 0) return;

            // ── Load existing entries to guard against duplicates ─────────────
            // Key for valid rows   → "lastName_firstName_dob"
            // Key for error rows   → "pdfname:filename.pdf"  (PdfName already in CSV)
            var existingNameDobKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingPdfNameKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool writeHeader = !File.Exists(targetCsvPath);

            if (!writeHeader)
            {
                var existingLines = File.ReadAllLines(targetCsvPath);
                var headers = existingLines[0].Split(',');

                int idxLast = Array.IndexOf(headers, "Last Name");
                int idxFirst = Array.IndexOf(headers, "First Name");
                int idxDob = Array.IndexOf(headers, "Date of Birth");
                int idxPdfName = Array.IndexOf(headers, "PdfName");

                foreach (var line in existingLines.Skip(1))
                {
                    var cols = line.Split(',');

                    if (idxLast >= 0 && idxFirst >= 0 && idxDob >= 0 && cols.Length > Math.Max(idxLast, Math.Max(idxFirst, idxDob)))
                    {
                        string nameDobKey = $"{cols[idxLast].Trim()}_{cols[idxFirst].Trim()}_{cols[idxDob].Trim()}";
                        if (!string.IsNullOrWhiteSpace(cols[idxLast].Trim()))
                            existingNameDobKeys.Add(nameDobKey);
                    }

                    if (idxPdfName >= 0 && cols.Length > idxPdfName)
                    {
                        string pdfName = cols[idxPdfName].Trim();
                        if (!string.IsNullOrWhiteSpace(pdfName))
                            existingPdfNameKeys.Add(pdfName);
                    }
                }
            }

            string targetScannedFolder = Path.Combine(successfulPdfPath, "ScannedOK");
            if (!Directory.Exists(targetScannedFolder))
                Directory.CreateDirectory(targetScannedFolder);

            foreach (var file in pdfFiles)
            {
                string pdfFileName = Path.GetFileName(file);

                try
                {
                    var (firstName, lastName, dateOfBirth, pageCount) =
                        PdfProcessor.ProcessSingleScannedPdf(file, debugOcr: false, debugOutputDir: null);

                    // Normalize: treat "Unknown"/"Error" as empty for the CSV field
                    string safeLast = lastName is "Unknown" or "Error" ? "" : lastName ?? "";
                    string safeFirst = firstName is "Unknown" or "Error" ? "" : firstName ?? "";
                    string safeDob = dateOfBirth is "Unknown" or "Error" or null ? "" : dateOfBirth;

                    bool fullyExtracted = !string.IsNullOrEmpty(safeLast)
                                       && !string.IsNullOrEmpty(safeFirst)
                                       && !string.IsNullOrEmpty(safeDob);

                    // ── Duplicate guard ───────────────────────────────────────
                    // For valid rows: skip if lastName+firstName+DOB already exists
                    // For error rows: skip if the PdfName is already recorded
                    if (fullyExtracted)
                    {
                        string nameDobKey = $"{safeLast}_{safeFirst}_{safeDob}";
                        if (existingNameDobKeys.Contains(nameDobKey))
                        {
                            LoggerService.LogInformation($"⏭️ Skipped {pdfFileName}: Record already exists in CSV.");
                            continue;
                        }
                    }
                    else
                    {
                        if (existingPdfNameKeys.Contains(pdfFileName))
                        {
                            LoggerService.LogInformation($"⏭️ Skipped {pdfFileName}: PDF error row already recorded in CSV.");
                            continue;
                        }
                    }

                    // ── Build CSV line ────────────────────────────────────────
                    // Columns: Last Name, First Name, School, Grade, Date of Birth,
                    //          Medicare Number, Consent Status, Tdap, HPV, ClientId,
                    //          IsFileRoseDefault, IsDuplicate, DuplicateResolved,
                    //          ClientIdStatus, BestMatch, IsScanPdf, PdfName, IsScanPdfReady
                    string csvLine =
                        $"{EscapeCsv(safeLast)}," +
                        $"{EscapeCsv(safeFirst)}," +
                        $"{EscapeCsv(schoolName)}," +
                        $"{EscapeCsv(grade)}," +
                        $"{EscapeCsv(safeDob)}," +
                        $",,,,,," +            // Medicare Number, Consent Status, Tdap, HPV, ClientId, IsFileRoseDefault
                        $"False," +            // IsDuplicate
                        $"False," +            // DuplicateResolved
                        $"0," +                // ClientIdStatus = NotProcessed
                        $"," +                 // BestMatch
                        $"True," +             // IsScanPdf = true (reading from scanned folder)
                        $"{EscapeCsv(pdfFileName)}," +
                        $"{(fullyExtracted ? "True" : "False")}"; // IsScanPdfReady

                    using (var writer = new StreamWriter(targetCsvPath, append: true))
                    {
                        if (writeHeader)
                        {
                            writer.WriteLine(
                                "Last Name,First Name,School,Grade,Date of Birth," +
                                "Medicare Number,Consent Status,Tdap,HPV,ClientId," +
                                "IsFileRoseDefault,IsDuplicate,DuplicateResolved," +
                                "ClientIdStatus,BestMatch,IsScanPdf,PdfName,IsScanPdfReady");
                            writeHeader = false;
                        }
                        writer.WriteLine(csvLine);
                    }

                    // Track to avoid re-inserting within the same run
                    if (fullyExtracted)
                        existingNameDobKeys.Add($"{safeLast}_{safeFirst}_{safeDob}");
                    else
                        existingPdfNameKeys.Add(pdfFileName);

                    // ── Move fully extracted PDFs to ScannedOK ────────────────
                    if (fullyExtracted)
                    {
                        string id = Guid.NewGuid().ToString("N")[..8];
                        string newFileName = namingFormat
                            .Replace("{ID}", id)
                            .Replace("{LastName}", safeLast)
                            .Replace("{FirstName}", safeFirst) + ".pdf";

                        string destinationPath = Path.Combine(targetScannedFolder, newFileName);
                        File.Move(file, destinationPath, overwrite: true);
                        LoggerService.LogInformation($"✅ Processed and moved {pdfFileName} → {newFileName}");
                    }
                    else
                    {
                        LoggerService.LogWarning(
                            $"⚠️ Partial extraction for {pdfFileName}: " +
                            $"Last={safeLast.OrNull()} First={safeFirst.OrNull()} DOB={safeDob.OrNull()} " +
                            $"— row added to CSV, PDF left in scanned folder.");
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.LogWarning($"⚠️ Error processing {pdfFileName}: {ex.Message}");
                }
            }
        }



        private static string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
                return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }
    }

    // ── Small local helper — avoids pulling in a dependency just for null display ──
    internal static class StringDisplayExtensions
    {
        public static string OrNull(this string s) => string.IsNullOrEmpty(s) ? "(empty)" : s;
    }
}