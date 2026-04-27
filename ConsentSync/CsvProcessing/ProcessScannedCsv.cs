using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
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
                    IsPdfSave = false
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




    }
}
