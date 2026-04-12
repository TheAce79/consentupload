using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsvProcessing
{
    public class StudentCsvProcessor
    {
        private readonly string[] _inputDateFormats;
        private readonly IConfiguration _config;
        private readonly string _inputCsvPath;
        private readonly string _outputCsvPath;
        private readonly string _dateOfBirthColumn;
        private readonly string _dateFormat;
        private readonly string _lastNameColumn;
        private readonly Dictionary<string, object?> _additionalColumns;
        private readonly List<EncodingConfiguration> _encodingConfigs;

        // Tracking for diagnostics
        private readonly List<ProcessingError> _processingErrors = new();
        private int _totalInputRows = 0;
        private int _successfullyParsedRows = 0;
        private int _skippedEmptyRows = 0;
        private int _malformedRows = 0;

        public StudentCsvProcessor(IConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // Use ConfigurationService to get resolved paths
            var csvConfig = ConsentSyncCore.Services.ConfigurationService.GetCsvConfig();

            // Paths are now pre-resolved by ConfigurationService
            _inputCsvPath = Path.Combine(csvConfig.InputCsvPath, csvConfig.InputCsvFileName);
            _outputCsvPath = Path.Combine(csvConfig.OutputCsvPath, csvConfig.OutputCsvFileName);

            // DEBUG: Verify paths are resolved
            Console.WriteLine($"\n📁 StudentCsvProcessor Initialized:");
            Console.WriteLine($"   Input Path:  {_inputCsvPath}");
            Console.WriteLine($"   Output Path: {_outputCsvPath}");

            if (_inputCsvPath.Contains("{") || _outputCsvPath.Contains("{"))
            {
                Console.WriteLine($"   ⚠️  WARNING: Placeholders still present!");
                throw new InvalidOperationException("Path placeholders were not resolved. Check ConfigurationService.ResolvePath()");
            }

            _dateOfBirthColumn = csvConfig.DateOfBirthColumn;
            _dateFormat = csvConfig.DateFormat;
            _inputDateFormats = csvConfig.InputDateFormats;
            _lastNameColumn = csvConfig.LastNameColumn;

            Console.WriteLine($"📅 Configured to parse input dates using {_inputDateFormats.Length} format(s): {string.Join(", ", _inputDateFormats)}");

            // Load additional columns from configuration
            _additionalColumns = new Dictionary<string, object?>();
            var additionalColumnsSection = _config.GetSection("CsvProcessing:AdditionalColumns");
            foreach (var column in additionalColumnsSection.GetChildren())
            {
                string key = column.Key;
                string? value = column.Value;

                // Parse the value based on type
                if (value == null || value.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    _additionalColumns[key] = null;
                }
                else if (bool.TryParse(value, out bool boolValue))
                {
                    _additionalColumns[key] = boolValue;
                }
                else
                {
                    _additionalColumns[key] = value;
                }
            }

            // Load encoding configurations
            _encodingConfigs = _config.GetSection("CsvProcessing:EncodingsToTry")
                .Get<List<EncodingConfiguration>>() ?? new List<EncodingConfiguration>();

            if (_encodingConfigs.Count == 0)
            {
                Console.WriteLine("⚠ No encodings configured, using defaults");
                _encodingConfigs = GetDefaultEncodingConfigurations();
            }
            else
            {
                _encodingConfigs = _encodingConfigs.OrderBy(e => e.Priority).ToList();
                Console.WriteLine($"📋 Loaded {_encodingConfigs.Count} encoding configurations");
            }

            // Ensure output directory exists
            string? outputDir = Path.GetDirectoryName(_outputCsvPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                Console.WriteLine($"✅ Created output directory: {outputDir}");
            }
        }

        /// <summary>
        /// Gets default encoding configurations as fallback
        /// </summary>
        private List<EncodingConfiguration> GetDefaultEncodingConfigurations()
        {
            return new List<EncodingConfiguration>
            {
                new EncodingConfiguration { Name = "UTF-8", CodePage = "utf-8", UseBOM = false, Priority = 1 },
                new EncodingConfiguration { Name = "UTF-8 with BOM", CodePage = "utf-8", UseBOM = true, Priority = 2 },
                new EncodingConfiguration { Name = "Windows-1252 (ANSI)", CodePage = "1252", Priority = 3 },
                new EncodingConfiguration { Name = "ISO-8859-1 (Latin-1)", CodePage = "iso-8859-1", Priority = 4 },
                new EncodingConfiguration { Name = "System Default", CodePage = "default", Priority = 5 }
            };
        }

        /// <summary>
        /// Converts encoding configuration to actual Encoding object
        /// </summary>
        private Encoding GetEncodingFromConfig(EncodingConfiguration config)
        {
            try
            {
                if (config.CodePage.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    return Encoding.Default;
                }
                else if (config.CodePage.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                {
                    return config.UseBOM ? new UTF8Encoding(true) : Encoding.UTF8;
                }
                else if (int.TryParse(config.CodePage, out int codePage))
                {
                    return Encoding.GetEncoding(codePage);
                }
                else
                {
                    return Encoding.GetEncoding(config.CodePage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Failed to load encoding '{config.Name}' (CodePage: {config.CodePage}): {ex.Message}");
                return Encoding.UTF8; // Fallback
            }
        }

        /// <summary>
        /// Processes the CSV: reads, transforms dates, sorts, adds columns, and writes output
        /// Uses CsvHelper for robust CSV parsing
        /// </summary>
        public void ProcessCsv()
        {
            if (!File.Exists(_inputCsvPath))
            {
                Console.WriteLine($"❌ CSV file not found: {_inputCsvPath}");
                return;
            }

            Console.WriteLine($"\n📄 Processing CSV file...");
            Console.WriteLine($"   Input:  {_inputCsvPath}");
            Console.WriteLine($"   Output: {_outputCsvPath}");

            // Reset diagnostics
            _processingErrors.Clear();
            _totalInputRows = 0;
            _successfullyParsedRows = 0;
            _skippedEmptyRows = 0;
            _malformedRows = 0;

            try
            {
                // Read and process CSV using CsvHelper
                var records = ReadCsvWithCsvHelper();

                if (records.Count == 0)
                {
                    Console.WriteLine("❌ No valid records found in CSV");
                    PrintProcessingDiagnostics();
                    return;
                }

                Console.WriteLine($"\n✅ Successfully parsed {records.Count} records");

                // Transform Date of Birth column
                TransformDates(records);

                // Sort by Last Name
                Console.WriteLine($"\n🔤 Sorting by: {_lastNameColumn}");
                var sortedRecords = records
                    .OrderBy(r => r[_lastNameColumn], StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Get final header (original + additional columns)
                var finalHeader = GetFinalHeader(records.FirstOrDefault());

                // Write output CSV
                WriteCsv(sortedRecords, finalHeader);

                // Print comprehensive diagnostics
                PrintProcessingDiagnostics();

                Console.WriteLine($"\n✅ CSV processing complete!");
                Console.WriteLine($"   Output file: {_outputCsvPath}");
                Console.WriteLine($"   Total records: {sortedRecords.Count}");
                Console.WriteLine($"   Ready for Client ID search automation");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ FATAL ERROR during CSV processing:");
                Console.WriteLine($"   {ex.Message}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
                PrintProcessingDiagnostics();
                throw;
            }
        }

        /// <summary>
        /// Reads CSV using CsvHelper library for robust parsing
        /// Handles quoted fields, embedded commas, and multi-line values correctly
        /// </summary>
        private List<CsvRecord> ReadCsvWithCsvHelper()
        {
            Console.WriteLine($"\n🔍 Reading CSV with CsvHelper library...");

            var records = new List<CsvRecord>();
            Encoding? successfulEncoding = null;
            List<string>? header = null;

            // Try each encoding configuration
            foreach (var encodingConfig in _encodingConfigs)
            {
                try
                {
                    var encoding = GetEncodingFromConfig(encodingConfig);
                    Console.WriteLine($"   Trying: {encodingConfig.Name}...");

                    using var reader = new StreamReader(_inputCsvPath, encoding);
                    using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        MissingFieldFound = null,
                        HeaderValidated = null,
                        TrimOptions = TrimOptions.Trim,
                        // ✅ FIX: Use correct property for CsvHelper API
                        BadDataFound = context =>
                        {
                            _malformedRows++;
                            _processingErrors.Add(new ProcessingError
                            {
                                RowNumber = context.Context.Parser.Row,
                                ErrorType = "BadData",
                                Message = $"Malformed data: {context.RawRecord}"
                            });
                            Console.WriteLine($"      ⚠ Row {context.Context.Parser.Row}: Bad data detected");
                        },
                        // ✅ FIX: Use correct event handler signature
                        ReadingExceptionOccurred = args =>
                        {
                            _processingErrors.Add(new ProcessingError
                            {
                                RowNumber = args.Exception.Context.Parser.Row,
                                ErrorType = "ReadingException",
                                Message = args.Exception.Message
                            });
                            Console.WriteLine($"      ⚠ Reading exception: {args.Exception.Message}");
                            return false; // Don't throw, skip the row
                        }
                    });

                    // Read header
                    csv.Read();
                    csv.ReadHeader();
                    header = csv.HeaderRecord?.ToList();

                    if (header == null || header.Count == 0)
                    {
                        Console.WriteLine($"      ✗ No header found with {encodingConfig.Name}");
                        continue;
                    }

                    // Check for encoding issues in header
                    if (header.Any(h => h.Contains('?') || h.Contains('�')))
                    {
                        Console.WriteLine($"      ✗ Encoding issues detected in header");
                        continue;
                    }

                    Console.WriteLine($"      ✓ Header looks good: {header.Count} columns");
                    Console.WriteLine($"      Columns: {string.Join(", ", header.Take(5))}{(header.Count > 5 ? "..." : "")}");

                    // Add additional columns to header
                    var newHeader = new List<string>(header);
                    foreach (var column in _additionalColumns.Keys)
                    {
                        if (!newHeader.Contains(column))
                        {
                            newHeader.Add(column);
                        }
                    }

                    // Ensure Client ID tracking columns exist
                    if (!newHeader.Contains("ClientIdStatus"))
                        newHeader.Add("ClientIdStatus");
                    if (!newHeader.Contains("BestMatch"))
                        newHeader.Add("BestMatch");

                    // Read data rows
                    int rowNumber = 1; // Header is row 0
                    while (csv.Read())
                    {
                        rowNumber++;
                        _totalInputRows++;

                        try
                        {
                            // Check if row is empty
                            bool isEmpty = true;
                            foreach (var col in header)
                            {
                                var value = csv.GetField(col);
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    isEmpty = false;
                                    break;
                                }
                            }

                            if (isEmpty)
                            {
                                _skippedEmptyRows++;
                                Console.WriteLine($"      ⚠ Row {rowNumber}: Empty row, skipping");
                                continue;
                            }

                            // Create record
                            var record = new CsvRecord();

                            // Read original columns
                            foreach (var col in header)
                            {
                                record[col] = csv.GetField(col)?.Trim() ?? string.Empty;
                            }

                            // Add additional columns with default values
                            foreach (var (columnName, defaultValue) in _additionalColumns)
                            {
                                record[columnName] = defaultValue?.ToString() ?? string.Empty;
                            }

                            // Initialize Client ID tracking columns
                            if (!record.Properties.ContainsKey("ClientIdStatus"))
                                record["ClientIdStatus"] = "0"; // NotProcessed

                            if (!record.Properties.ContainsKey("BestMatch"))
                                record["BestMatch"] = string.Empty;

                            records.Add(record);
                            _successfullyParsedRows++;
                        }
                        catch (Exception ex)
                        {
                            _processingErrors.Add(new ProcessingError
                            {
                                RowNumber = rowNumber,
                                ErrorType = "RowParsingError",
                                Message = ex.Message
                            });
                            Console.WriteLine($"      ⚠ Row {rowNumber}: Failed to parse - {ex.Message}");
                        }
                    }

                    // If we got here successfully, use this encoding
                    successfulEncoding = encoding;
                    Console.WriteLine($"   ✅ Successfully read with {encodingConfig.Name}");
                    Console.WriteLine($"      Total input rows: {_totalInputRows}");
                    Console.WriteLine($"      Successfully parsed: {_successfullyParsedRows}");
                    Console.WriteLine($"      Skipped (empty): {_skippedEmptyRows}");
                    Console.WriteLine($"      Errors: {_processingErrors.Count}");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      ✗ Failed with {encodingConfig.Name}: {ex.Message}");
                    records.Clear(); // Clear any partial results
                    _totalInputRows = 0;
                    _successfullyParsedRows = 0;
                }
            }

            if (successfulEncoding == null)
            {
                throw new InvalidOperationException("Failed to read CSV with any configured encoding");
            }

            return records;
        }

        /// <summary>
        /// Transform Date of Birth column to configured format
        /// </summary>
        private void TransformDates(List<CsvRecord> records)
        {
            int dateTransformCount = 0;
            int dateErrorCount = 0;
            var failedDates = new List<string>();

            Console.WriteLine($"\n📅 Transforming dates in column: {_dateOfBirthColumn}");

            foreach (var record in records)
            {
                if (record.Properties.ContainsKey(_dateOfBirthColumn))
                {
                    string originalDate = record[_dateOfBirthColumn];
                    if (!string.IsNullOrWhiteSpace(originalDate))
                    {
                        DateTime parsedDate;
                        bool parsed = false;

                        // Try parsing with configured input formats
                        foreach (var format in _inputDateFormats)
                        {
                            if (DateTime.TryParseExact(originalDate, format,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out parsedDate))
                            {
                                record[_dateOfBirthColumn] = parsedDate.ToString(_dateFormat);
                                dateTransformCount++;
                                parsed = true;

                                // DEBUG: Log first transformation
                                if (dateTransformCount == 1)
                                {
                                    Console.WriteLine($"   🔍 First date transformation:");
                                    Console.WriteLine($"      Original: '{originalDate}'");
                                    Console.WriteLine($"      Format used: '{format}'");
                                    Console.WriteLine($"      Result: '{parsedDate.ToString(_dateFormat)}'");
                                }

                                break;
                            }
                        }

                        // If configured formats failed, try general parsing
                        if (!parsed && DateTime.TryParse(originalDate, out parsedDate))
                        {
                            record[_dateOfBirthColumn] = parsedDate.ToString(_dateFormat);
                            dateTransformCount++;
                            parsed = true;
                            Console.WriteLine($"   ⚠ Used general parsing for: '{originalDate}'");
                        }

                        if (!parsed)
                        {
                            failedDates.Add(originalDate);
                            dateErrorCount++;
                        }
                    }
                }
            }

            Console.WriteLine($"\n   Transformed: {dateTransformCount} dates");
            Console.WriteLine($"   Errors: {dateErrorCount} dates");
            Console.WriteLine($"   Output format: {_dateFormat}");

            if (failedDates.Count > 0)
            {
                Console.WriteLine($"\n   ⚠ Failed to parse {failedDates.Count} date(s):");
                foreach (var date in failedDates.Take(10))
                {
                    Console.WriteLine($"      - '{date}'");
                }
                if (failedDates.Count > 10)
                {
                    Console.WriteLine($"      ... and {failedDates.Count - 10} more");
                }
                Console.WriteLine($"\n   💡 Add the correct format to 'InputDateFormats' in appsettings.json");
            }
        }

        /// <summary>
        /// Get final header including all columns
        /// </summary>
        private List<string> GetFinalHeader(CsvRecord? sampleRecord)
        {
            if (sampleRecord == null)
                return new List<string>();

            return sampleRecord.GetColumnNames().ToList();
        }



        /// <summary>
        /// Writes the processed records to output CSV using CsvHelper
        /// </summary>
        private void WriteCsv(List<CsvRecord> records, List<string> header)
        {
            Console.WriteLine($"\n💾 Writing {records.Count} records to output...");

            try
            {
                // ✅ FIX: Wrap in using block and ensure disposal before verification
                using (var writer = new StreamWriter(_outputCsvPath, false, new UTF8Encoding(true)))
                using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true
                }))
                {
                    // Write header
                    foreach (var col in header)
                    {
                        csv.WriteField(col);
                    }
                    csv.NextRecord();

                    // Write data rows
                    foreach (var record in records)
                    {
                        foreach (var col in header)
                        {
                            csv.WriteField(record[col]);
                        }
                        csv.NextRecord();
                    }
                } // ✅ File handles are released HERE when using block exits

                Console.WriteLine($"   ✅ Successfully wrote {records.Count} records");

                // ✅ NOW it's safe to verify - file is closed
                var verifyLines = File.ReadAllLines(_outputCsvPath, Encoding.UTF8);
                Console.WriteLine($"   ✅ Verification: File contains {verifyLines.Length} lines (including header)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Write failed: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// Print comprehensive processing diagnostics
        /// </summary>
        private void PrintProcessingDiagnostics()
        {
            Console.WriteLine("\n" + new string('═', 70));
            Console.WriteLine("📊 CSV PROCESSING DIAGNOSTICS");
            Console.WriteLine(new string('═', 70));

            Console.WriteLine($"Total input rows (excluding header): {_totalInputRows}");
            Console.WriteLine($"Successfully parsed rows: {_successfullyParsedRows}");
            Console.WriteLine($"Skipped empty rows: {_skippedEmptyRows}");
            Console.WriteLine($"Malformed/bad data rows: {_malformedRows}");
            Console.WriteLine($"Other errors: {_processingErrors.Count - _malformedRows}");

            int lostRows = _totalInputRows - _successfullyParsedRows;
            if (lostRows > 0)
            {
                Console.WriteLine($"\n⚠️  ROWS LOST: {lostRows}");
                Console.WriteLine($"   Breakdown:");
                Console.WriteLine($"   - Empty rows: {_skippedEmptyRows}");
                Console.WriteLine($"   - Malformed data: {_malformedRows}");
                Console.WriteLine($"   - Other errors: {_processingErrors.Count - _malformedRows}");
            }

            if (_processingErrors.Count > 0)
            {
                Console.WriteLine($"\n❌ DETAILED ERRORS ({_processingErrors.Count} total):");
                var errorGroups = _processingErrors.GroupBy(e => e.ErrorType);
                foreach (var group in errorGroups)
                {
                    Console.WriteLine($"\n   {group.Key}: {group.Count()} occurrences");
                    foreach (var error in group.Take(5))
                    {
                        Console.WriteLine($"      Row {error.RowNumber}: {error.Message}");
                    }
                    if (group.Count() > 5)
                    {
                        Console.WriteLine($"      ... and {group.Count() - 5} more");
                    }
                }
            }

            Console.WriteLine(new string('═', 70));
        }

        /// <summary>
        /// Displays a preview of the processed data
        /// </summary>
        public void PreviewCsv(int maxRows = 5)
        {
            if (!File.Exists(_outputCsvPath))
            {
                Console.WriteLine($"❌ Output CSV file not found: {_outputCsvPath}");
                return;
            }

            var lines = File.ReadAllLines(_outputCsvPath, Encoding.UTF8).Take(maxRows + 1).ToList();

            Console.WriteLine($"\n📋 Preview of processed CSV (first {maxRows} rows):");
            Console.WriteLine(new string('═', 100));

            foreach (var line in lines)
            {
                // Truncate very long lines for display
                if (line.Length > 150)
                {
                    Console.WriteLine(line.Substring(0, 147) + "...");
                }
                else
                {
                    Console.WriteLine(line);
                }
            }

            Console.WriteLine(new string('═', 100));
        }
    }

    /// <summary>
    /// Represents a processing error during CSV reading
    /// </summary>
    internal class ProcessingError
    {
        public int RowNumber { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}