using ConsentSyncCore.Services.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Pdf
{
    public partial class BulkPdfExtractor
    {

        public static void ProcessScannedFolder()
        {
            var config = ConfigurationService.GetConfiguration();
            var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();

            // Get paths from configuration
            string inputScannedPath = bulkConfig.GetInputScannedPath();
            string outputFolder = config["OutputFolder"] ?? "2_Output Csv";
            string baseCsvFileName = config["OutputCsvFileName"] ?? "immunizations_processed.csv";

            string schoolName = config["SchoolName"] ?? "";
            string grade = config["Grade"] ?? "";
            string namingFormat = config["NamingFormat"] ?? "{ID}_{LastName}_{FirstName}_consent";

            // Determine target CSV
            string mainCsvPath = Path.Combine(outputFolder, baseCsvFileName);
            string targetCsvPath = mainCsvPath;

            if (File.Exists(mainCsvPath))
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(baseCsvFileName);
                string extension = Path.GetExtension(baseCsvFileName);
                targetCsvPath = Path.Combine(outputFolder, $"{fileNameWithoutExt}_Scanned{extension}");
            }

            if (!Directory.Exists(inputScannedPath))
            {
                LoggerService.LogWarning($"Scanned input folder not found: {inputScannedPath}");
                return;
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            var pdfFiles = Directory.GetFiles(inputScannedPath, "*.pdf");
            LoggerService.LogInformation($"Found {pdfFiles.Length} scanned PDFs to process in {inputScannedPath}");

            // Load existing CSV data to avoid duplicates
            var existingEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool writeHeader = !File.Exists(targetCsvPath);

            if (!writeHeader)
            {
                var lines = File.ReadAllLines(targetCsvPath);
                foreach (var line in lines.Skip(1)) // Skip header
                {
                    var cols = line.Split(',');
                    if (cols.Length >= 5)
                    {
                        // Assume standard order: Last Name (0), First Name (1), DOB (4)
                        string key = $"{cols[0].Trim()}_{cols[1].Trim()}_{cols[4].Trim()}";
                        existingEntries.Add(key);
                    }
                }
            }

            string targetScannedFolder = Path.Combine(outputFolder, "Scanned");
            if (!Directory.Exists(targetScannedFolder))
            {
                Directory.CreateDirectory(targetScannedFolder);
            }

            foreach (var file in pdfFiles)
            {
                try
                {
                    var (firstName, lastName, dateOfBirth, pageCount) = PdfProcessor.ProcessSingleScannedPdf(file, debugOcr: false, debugOutputDir: null);

                    bool isInvalid = firstName is "Unknown" or "Error"
                                  || lastName is "Unknown" or "Error"
                                  || dateOfBirth is "Unknown" or "Error" or null;

                    if (!isInvalid)
                    {
                        string recordKey = $"{lastName}_{firstName}_{dateOfBirth}";

                        if (!existingEntries.Contains(recordKey))
                        {
                            // Columns: Last Name, First Name, School, Grade, Date of Birth, Medicare Number, Consent Status, Tdap, HPV, ClientId, IsFileRoseDefault, IsDuplicate, DuplicateResolved, ClientIdStatus, BestMatch
                            string csvLine = $"{EscapeCsv(lastName)},{EscapeCsv(firstName)},{EscapeCsv(schoolName)},{EscapeCsv(grade)},{EscapeCsv(dateOfBirth)},,,,,,,,0,";

                            using (var writer = new StreamWriter(targetCsvPath, append: true))
                            {
                                if (writeHeader)
                                {
                                    writer.WriteLine("Last Name,First Name,School,Grade,Date of Birth,Medicare Number,Consent Status,Tdap,HPV,ClientId,IsFileRoseDefault,IsDuplicate,DuplicateResolved,ClientIdStatus,BestMatch");
                                    writeHeader = false;
                                }
                                writer.WriteLine(csvLine);
                            }

                            existingEntries.Add(recordKey);

                            // Move and rename the file
                            string id = Guid.NewGuid().ToString("N").Substring(0, 8); // Generate short ID for {ID}
                            string newFileName = namingFormat
                                .Replace("{ID}", id)
                                .Replace("{LastName}", lastName)
                                .Replace("{FirstName}", firstName) + ".pdf";

                            string destinationPath = Path.Combine(targetScannedFolder, newFileName);

                            File.Move(file, destinationPath);
                            LoggerService.LogInformation($"✅ Processed and moved {Path.GetFileName(file)} -> {newFileName}");
                        }
                        else
                        {
                            LoggerService.LogInformation($"⏭️ Skipped {Path.GetFileName(file)}: Record already exists in CSV.");
                        }
                    }
                    else
                    {
                        LoggerService.LogWarning($"❌ Failed to extract valid data for {Path.GetFileName(file)}");
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.LogWarning($"⚠️ Error processing {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        private static string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }






    }
}
