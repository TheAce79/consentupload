using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsvProcessing
{
    public class StudentCsvRepository
    {
        private readonly IConfiguration _config;
        private readonly CsvProcessingConfig _csvConfig;
        private readonly string _inputCsvFullPath;
        private readonly string _outputCsvFullPath;
        private readonly ILogger<StudentCsvRepository> _logger;

        // Constructor with optional IConfiguration parameter for flexibility


        // Constructor with optional IConfiguration parameter for flexibility
        public StudentCsvRepository(IConfiguration? config = null)
        {
            // Use provided config or get from ConfigurationService
            _config = config ?? ConfigurationService.GetConfiguration();
            _csvConfig = ConfigurationService.GetCsvConfig();

            _inputCsvFullPath = ConfigurationService.GetInputCsvFullPath();
            _outputCsvFullPath = ConfigurationService.GetOutputCsvFullPath();

                _logger = LoggerService.GetLogger<StudentCsvRepository>();

            // ✅ DEBUG: Verify paths are resolved
             LoggerService.LogInformation($"\n📁 StudentCsvRepository Initialized:");
             LoggerService.LogInformation($"   Input Path:  {_inputCsvFullPath}");
             LoggerService.LogInformation($"   Output Path: {_outputCsvFullPath}");

            // ✅ Check if placeholders are still present
            if (_outputCsvFullPath.Contains("{") || _outputCsvFullPath.Contains("}"))
            {
                 LoggerService.LogInformation($"   ⚠️  WARNING: Placeholders not resolved in output path!");
                 LoggerService.LogInformation($"   ⚠️  Raw path: {_outputCsvFullPath}");
            }
        }





        #region Pre-Processing (Uses existing StudentCsvProcessor)

        /// <summary>
        /// Process raw CSV using existing StudentCsvProcessor
        /// This adds columns, formats dates, and sorts by last name
        /// </summary>
        public void ProcessRawCsv()
        {
             LoggerService.LogInformation("📄 PRE-PROCESSING: Running StudentCsvProcessor...");

            var processor = new StudentCsvProcessor(_config);
            processor.ProcessCsv();

             LoggerService.LogInformation("✅ Pre-processing complete\n");
        }


        /// <summary>
        /// Check if processed CSV already exists
        /// </summary>
        public bool ProcessedCsvExists()
        {
            return File.Exists(_outputCsvFullPath);
        }


        /// <summary>
        /// Show preview of processed CSV
        /// </summary>
        public void PreviewProcessedCsv(int maxRows = 5)
        {
            if (!ProcessedCsvExists())
            {
                 LoggerService.LogInformation($"❌ Processed CSV not found: {_outputCsvFullPath}");
                return;
            }

            var processor = new StudentCsvProcessor(_config);
            processor.PreviewCsv(maxRows);
        }


        #endregion




        #region Read Operations (StudentRecord)


        /// <summary>
        /// Read all students from processed CSV as StudentRecord objects
        /// </summary>
        public List<StudentRecord> ReadAll()
        {
            if (!ProcessedCsvExists())
            {
                throw new FileNotFoundException($"Processed CSV not found: {_outputCsvFullPath}");
            }

             LoggerService.LogInformation($"📖 Reading students from: {_outputCsvFullPath}");

            using var reader = new StreamReader(_outputCsvFullPath, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                HeaderValidated = null,
                TrimOptions = TrimOptions.Trim
            });

            csv.Context.RegisterClassMap<StudentRecordMap>();
            var students = csv.GetRecords<StudentRecord>().ToList();

             LoggerService.LogInformation($"✅ Loaded {students.Count} student records\n");
            return students;
        }




        /// <summary>
        /// Read students with a specific status filter
        /// </summary>
        public List<StudentRecord> ReadByStatus(ClientIdStatus status)
        {
            var allStudents = ReadAll();
            var filtered = allStudents.Where(s => s.ClientIdStatus == status).ToList();

             LoggerService.LogInformation($"📊 Filtered {filtered.Count} students with status: {status}");
            return filtered;
        }

        /// <summary>
        /// Read only unprocessed students (for Phase 1)
        /// </summary>
        public List<StudentRecord> ReadUnprocessed()
        {
            return ReadByStatus(ClientIdStatus.NotProcessed);
        }

        /// <summary>
        /// Read students with Client IDs (for Phase 2)
        /// </summary>
        public List<StudentRecord> ReadWithClientIds()
        {
            var allStudents = ReadAll();
            var withClientIds = allStudents
                .Where(s => !string.IsNullOrWhiteSpace(s.ClientId))
                .ToList();

             LoggerService.LogInformation($"📊 Found {withClientIds.Count} students with Client IDs");
            return withClientIds;
        }



        #endregion


        #region Write Operations (StudentRecord)



        /// <summary>
        /// Save all students back to CSV
        /// Uses atomic file replacement for crash-safety
        /// </summary>
        public void SaveAll(List<StudentRecord> students)
        {
            string tempFile = _outputCsvFullPath + ".tmp";

            try
            {
                 LoggerService.LogInformation($"💾 Saving {students.Count} students to: {_outputCsvFullPath}");

                // Write to temporary file first
                using (var writer = new StreamWriter(tempFile, false, Encoding.UTF8))
                using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true
                }))
                {
                    csv.Context.RegisterClassMap<StudentRecordMap>();
                    csv.WriteRecords(students);
                }

                // Atomically replace the old file
                File.Move(tempFile, _outputCsvFullPath, overwrite: true);

                 LoggerService.LogInformation($"✅ Saved successfully\n");
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"❌ Save failed: {ex.Message}");

                // Clean up temp file
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }

                throw;
            }
        }



        /// Update a single student record
        /// </summary>
        public void UpdateStudent(StudentRecord student)
        {
            var allStudents = ReadAll();

            // Find and update by matching on Name + DOB (unique identifier)
            var index = allStudents.FindIndex(s =>
                s.FirstName.Equals(student.FirstName, StringComparison.OrdinalIgnoreCase) &&
                s.LastName.Equals(student.LastName, StringComparison.OrdinalIgnoreCase) &&
                s.DateOfBirth == student.DateOfBirth);

            if (index >= 0)
            {
                allStudents[index] = student;
                SaveAll(allStudents);
                 LoggerService.LogInformation($"✅ Updated: {student.FirstName} {student.LastName}");
            }
            else
            {
                 LoggerService.LogInformation($"⚠️  Student not found: {student.FirstName} {student.LastName}");
            }
        }



        #endregion



        #region Legacy CsvRecord Support (for compatibility)

        /// <summary>
        /// Read all records as CsvRecord objects (legacy compatibility)
        /// </summary>
        public List<CsvRecord> ReadAllAsCsvRecords()
        {
            if (!ProcessedCsvExists())
            {
                throw new FileNotFoundException($"Processed CSV not found: {_outputCsvFullPath}");
            }

            var lines = File.ReadAllLines(_outputCsvFullPath, Encoding.UTF8);
            if (lines.Length < 2)
            {
                return new List<CsvRecord>();
            }

            var header = lines[0].Split(',').Select(h => h.Trim()).ToArray();
            var records = new List<CsvRecord>();

            for (int i = 1; i < lines.Length; i++)
            {
                var values = lines[i].Split(',');
                var record = new CsvRecord();

                for (int j = 0; j < header.Length && j < values.Length; j++)
                {
                    record[header[j]] = values[j].Trim();
                }

                records.Add(record);
            }

            return records;
        }

        /// <summary>
        /// Save CsvRecord objects (legacy compatibility)
        /// </summary>
        public void SaveAllCsvRecords(List<CsvRecord> records)
        {
            if (records.Count == 0)
            {
                 LoggerService.LogInformation("⚠️  No records to save");
                return;
            }

            var header = records[0].GetColumnNames().ToList();
            var lines = new List<string> { string.Join(",", header) };

            foreach (var record in records)
            {
                var values = record.GetValues(header).Select(EscapeCsvValue);
                lines.Add(string.Join(",", values));
            }

            File.WriteAllLines(_outputCsvFullPath, lines, Encoding.UTF8);
             LoggerService.LogInformation($"✅ Saved {records.Count} CSV records");
        }



        #endregion



        #region Conversion Helpers

        /// <summary>
        /// Convert StudentRecord to CsvRecord
        /// </summary>
        public static CsvRecord ToCsvRecord(StudentRecord student)
        {
            var record = new CsvRecord();
            record["Last Name"] = student.LastName;
            record["First Name"] = student.FirstName;
            record["School"] = student.School;
            record["Grade"] = student.Grade;
            record["Date of Birth"] = student.DateOfBirth;
            record["Medicare Number"] = student.MedicareNumber;
            record["Consent Status"] = student.ConsentStatus;
            record["Tdap"] = student.Tdap;
            record["HPV"] = student.HPV;
            record["ClientId"] = student.ClientId;
            record["IsFileRoseDefault"] = student.IsFileRoseDefault.ToString();
            record["ClientIdStatus"] = ((int)student.ClientIdStatus).ToString();
            record["BestMatch"] = student.BestMatch;
            return record;
        }

        /// <summary>
        /// Convert CsvRecord to StudentRecord
        /// </summary>
        public static StudentRecord FromCsvRecord(CsvRecord record)
        {
            return new StudentRecord
            {
                LastName = record["Last Name"],
                FirstName = record["First Name"],
                School = record["School"],
                Grade = record["Grade"],
                DateOfBirth = record["Date of Birth"],
                MedicareNumber = record["Medicare Number"],
                ConsentStatus = record["Consent Status"],
                Tdap = record["Tdap"],
                HPV = record["HPV"],
                ClientId = record["ClientId"],
                IsFileRoseDefault = bool.TryParse(record["IsFileRoseDefault"], out var val) && val,
                ClientIdStatus = Enum.TryParse<ClientIdStatus>(record["ClientIdStatus"], out var status)
                    ? status
                    : ClientIdStatus.NotProcessed,
                BestMatch = record["BestMatch"]
            };
        }

        #endregion


        #region Helper Methods

        /// <summary>
        /// Escape CSV values that contain commas, quotes, or newlines
        /// </summary>
        private static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        /// <summary>
        /// Get statistics about the current CSV
        /// </summary>
        public CsvStatistics GetStatistics()
        {
            var allStudents = ReadAll();

            return new CsvStatistics
            {
                TotalRecords = allStudents.Count,
                NotProcessed = allStudents.Count(s => s.ClientIdStatus == ClientIdStatus.NotProcessed),
                Found = allStudents.Count(s => s.ClientIdStatus == ClientIdStatus.Found),
                NeedsManualReview = allStudents.Count(s => s.ClientIdStatus == ClientIdStatus.NeedsManualReview),
                WithClientIds = allStudents.Count(s => !string.IsNullOrWhiteSpace(s.ClientId)),
                WithFileRose = allStudents.Count(s => s.IsFileRoseDefault)
            };
        }

        /// <summary>
        /// Display CSV statistics
        /// </summary>
        public void DisplayStatistics()
        {
            var stats = GetStatistics();

             LoggerService.LogInformation("\n📊 CSV Statistics:");
             LoggerService.LogInformation($"   Total records: {stats.TotalRecords}");
             LoggerService.LogInformation($"   Not processed: {stats.NotProcessed}");
             LoggerService.LogInformation($"   Found: {stats.Found}");
             LoggerService.LogInformation($"   Needs manual review: {stats.NeedsManualReview}");
             LoggerService.LogInformation($"   With Client IDs: {stats.WithClientIds}");
             LoggerService.LogInformation($"   Multi-page (File Rose): {stats.WithFileRose}\n");
        }

        #endregion


    }



    /// <summary>
    /// CSV statistics summary
    /// </summary>
    public class CsvStatistics
    {
        public int TotalRecords { get; set; }
        public int NotProcessed { get; set; }
        public int Found { get; set; }
        public int NeedsManualReview { get; set; }
        public int WithClientIds { get; set; }
        public int WithFileRose { get; set; }
    }



}
