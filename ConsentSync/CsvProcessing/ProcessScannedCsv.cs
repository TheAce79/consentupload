using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.Pdf;
using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsvProcessing
{
    public static class ProcessScannedCsv
    {





        public static List<StudentRecord> FinalizeAndPromoteScannedPdfs(string inputScannedPath)
        {
            var config = ConfigurationService.GetConfiguration();
            var repo = new CsvProcessing.StudentCsvRepository(config);


            // We target the Scanned version of the CSV specifically
            // Assuming your naming convention: immunizations_processed_Scanned.csv
            if (!repo.ProcessedCsvExists()) return new List<StudentRecord>();

            var students = repo.ReadAll();
            var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();

            string scannedOkFolder = bulkConfig.GetScannedOkPath();
            var finalizedThisRun = new List<StudentRecord>();

            foreach (var student in students.Where(s => s.IsScanPdf && !string.IsNullOrWhiteSpace(s.ClientId) && s.IsScanPdfReady))
            {
                // Try to find the file in the input scanned folder OR ScannedOK
                string currentPath = Path.Combine(inputScannedPath, student.PdfName);
                if (!File.Exists(currentPath))
                    currentPath = Path.Combine(scannedOkFolder, student.PdfName);

                if (File.Exists(currentPath))
                {
                    string finalName = $"{student.ClientId.Trim()}.pdf";
                    string destinationPath = Path.Combine(scannedOkFolder, finalName);

                    try
                    {
                        File.Move(currentPath, destinationPath, overwrite: true);

                        student.PdfName = finalName;
                        student.IsScanPdfReady = true;
                        student.ClientIdStatus = ClientIdStatus.Found; // Set to 1

                        finalizedThisRun.Add(student);
                        LoggerService.LogInformation($"✨ Promoted: {student.FirstName} {student.LastName} -> {finalName}");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogError($"Error moving {student.PdfName}: {ex.Message}");
                    }
                }
            }

            if (finalizedThisRun.Count > 0)
            {
                repo.SaveAll(students); // Saves the _Scanned.csv
                AppendToMasterValidation(finalizedThisRun); // Pushes to the production file
            }

            return finalizedThisRun;
        }


        private static void AppendToMasterValidation(List<StudentRecord> students)
        {
            var phase2Config = ConfigurationService.GetPhase2Config();
            string masterPath = Path.Combine(phase2Config.ValidationCsvPath, phase2Config.ValidationResultsCsv);

            // ✅ Use the centralized service for priority encoding
            var targetEncoding = EncodingConfigurationService.GetPriorityEncoding();

            List<ValidationRecord> masterRecords = new List<ValidationRecord>();

            // 1. Read existing using Priority Encoding
            if (File.Exists(masterPath))
            {
                try
                {
                    using var reader = new StreamReader(masterPath, targetEncoding);
                    using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        MissingFieldFound = null,
                        HeaderValidated = null
                    });
                    csv.Context.RegisterClassMap<ValidationRecordMap>();
                    masterRecords = csv.GetRecords<ValidationRecord>().ToList();
                }
                catch (Exception ex)
                {
                    LoggerService.LogError($"⚠️ Error reading master validation CSV: {ex.Message}");
                }
            }

            var existingIds = masterRecords.Select(r => r.ClientId).ToHashSet();

            // 2. Map and Add New Records
            foreach (var s in students)
            {
                if (existingIds.Contains(s.ClientId)) continue;


                ///when ClientId is found, we can be confident enough to mark it as "Found" (1).
                ///if ClientIdStatus is not set, file rose will not be able to update it to "Found" (1) later, which will cause issues 
                ///for the file rose extraction process that relies 
                ///on this status to identify which records are ready for extraction.
               
                int clientIdStatus = string.IsNullOrWhiteSpace(s.ClientId)
                    ? (int)ConsentSyncCore.Models.ClientIdStatus.NeedsManualReview
                    : (int)ConsentSyncCore.Models.ClientIdStatus.Found;


                masterRecords.Add(new ValidationRecord
                {
                    ClientId = s.ClientId,
                    LastName = s.LastName,
                    FirstName = s.FirstName,
                    School = s.School,
                    Grade = s.Grade,
                    DateOfBirth = s.DateOfBirth,
                    FileFound = true,
                    IsMatch = true,
                    IsScanPdf = true,
                    IsScanPdfReady = true,
                    PdfName = s.PdfName,
                    MatchScore = 100.0,
                    IsPdfSave = false,
                    ClientIdStatus = clientIdStatus // assign the enum value directly
                });
            }

            // 3. Write back using the SAME Priority Encoding
            try
            {
                using var writer = new StreamWriter(masterPath, false, targetEncoding);
                using var csvOut = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
                csvOut.Context.RegisterClassMap<ValidationRecordMap>();
                csvOut.WriteRecords(masterRecords);

                LoggerService.LogInformation($"✅ Successfully appended {students.Count} records to {Path.GetFileName(masterPath)} using {targetEncoding.EncodingName}");
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"❌ Failed to write to master validation CSV: {ex.Message}");
            }
        }




        public static void ProcessScannedFolder(bool isClientIdAsFileName)
        {
            var config = ConfigurationService.GetConfiguration();
            var bulkConfig = ConfigurationService.GetBulkPdfExtractionConfig();
            var csvWsConfig = ConfigurationService.GetCsvWorkspaceConfig();
            var csvConfig = ConfigurationService.GetCsvConfig();
            var targetEncoding = EncodingConfigurationService.GetPriorityEncoding();

            string inputScannedPath = bulkConfig.GetInputScannedPath();
            string outputCsvFullPath = Path.Combine(csvWsConfig.GetProcessedCsvPath(), csvConfig.OutputCsvFileName ?? "immunizations_processed.csv");

            // 1. Load existing data to perform the merge check
            var repo = new CsvProcessing.StudentCsvRepository(config);
            var existingStudents = File.Exists(outputCsvFullPath) ? repo.ReadAll() : new List<StudentRecord>();

            // Prepare lookup sets for fast duplicate checking
            var existingIdentityKeys = existingStudents
                .Select(s => $"{s.LastName?.Trim()}_{s.FirstName?.Trim()}_{s.DateOfBirth?.Trim()}".ToLowerInvariant())
                .ToHashSet();

            var existingClientIds = existingStudents
                .Where(s => !string.IsNullOrEmpty(s.ClientId))
                .Select(s => s.ClientId.Trim().ToLowerInvariant())
                .ToHashSet();

            var pdfFiles = Directory.GetFiles(inputScannedPath, "*.pdf");
            bool hasNewChanges = false;

            foreach (var file in pdfFiles)
            {
                string stem = Path.GetFileNameWithoutExtension(file).Trim();
                string safeLast = "", safeFirst = "", safeDob = "", clientId = "";
                bool fullyExtracted = false;

                // ── Extraction Logic (Bypass or OCR) ──
                if (isClientIdAsFileName && IsValidClientId(stem))
                {
                    clientId = stem;
                    fullyExtracted = true;
                }
                else
                {
                    var (fn, ln, dob, _) = PdfProcessor.ProcessSingleScannedPdf(file, false, null);
                    safeLast = ln is "Unknown" or "Error" ? "" : ln ?? "";
                    safeFirst = fn is "Unknown" or "Error" ? "" : fn ?? "";
                    safeDob = dob is "Unknown" or "Error" or null ? "" : dob;
                    fullyExtracted = !string.IsNullOrEmpty(safeLast) && !string.IsNullOrEmpty(safeFirst) && !string.IsNullOrEmpty(safeDob);
                }

                // ── The Merge/Duplicate Check ──
                string currentIdentityKey = $"{safeLast}_{safeFirst}_{safeDob}".ToLowerInvariant();

                // Skip if Name+DOB exists
                if (fullyExtracted && existingIdentityKeys.Contains(currentIdentityKey)) continue;

                // Skip if ClientId is already defined in the file
                if (!string.IsNullOrEmpty(clientId) && existingClientIds.Contains(clientId.ToLowerInvariant())) continue;

                // ── If we are here, it's a NEW row — Create it ──
                var newStudent = new StudentRecord
                {
                    LastName = safeLast,
                    FirstName = safeFirst,
                    DateOfBirth = safeDob,
                    School = config["SchoolContext:SchoolName"] ?? "",
                    Grade = config["SchoolContext:Grade"] ?? "",
                    IsScanPdf = true,
                    IsScanPdfReady = fullyExtracted,
                    PdfName = Path.GetFileName(file), // Will be renamed later
                    ClientId = clientId,
                    ClientIdStatus = !string.IsNullOrEmpty(clientId) ? ClientIdStatus.Found : ClientIdStatus.NotProcessed
                };

                // Determine Final Filename and Move PDF
                if (fullyExtracted)
                {
                    string finalPdfName = !string.IsNullOrEmpty(clientId)
                        ? $"{clientId}.pdf"
                        : $"{Guid.NewGuid().ToString("N")[..8]}_{safeLast}_{safeFirst}.pdf";

                    string destinationPath = Path.Combine(bulkConfig.GetScannedOkPath(), finalPdfName);
                    Directory.CreateDirectory(bulkConfig.GetScannedOkPath());
                    File.Move(file, destinationPath, overwrite: true);

                    newStudent.PdfName = finalPdfName;
                }

                existingStudents.Add(newStudent);
                hasNewChanges = true;
            }

            // 2. Save the unified list back to the main file
            if (hasNewChanges)
            {
                repo.SaveAll(existingStudents);
                LoggerService.LogInformation($"✅ Merged new scanned records into {Path.GetFileName(outputCsvFullPath)}");
            }
        }


        private static string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
                return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }


        private static bool IsValidClientId(string stem)
        {
            // Basic check: Is it numeric and long enough to be a PHIS ID?
            return stem.Length >= 4 && long.TryParse(stem, out _);
        }



    }



    // ── Small local helper — avoids pulling in a dependency just for null display ──
    internal static class StringDisplayExtensions
    {
        public static string OrNull(this string s) => string.IsNullOrEmpty(s) ? "(empty)" : s;
    }








}
