using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace CsvProcessor
{
    public class CsvReader
    {
        // Add this field at the top of the class
        private readonly string[] _inputDateFormats;

        private readonly IConfiguration _config;
        private readonly string _inputCsvPath;
        private readonly string _outputCsvPath;
        private readonly string _dateOfBirthColumn;
        private readonly string _dateFormat;
        private readonly string _lastNameColumn;
        private readonly Dictionary<string, object?> _additionalColumns;
        private readonly List<EncodingConfiguration> _encodingConfigs;

        public CsvReader(IConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // Load CSV processing configuration
            string csvPath = _config["CsvProcessing:InputCsvPath"]
                ?? throw new InvalidOperationException("CsvProcessing:InputCsvPath is not configured");

            string csvFileName = _config["CsvProcessing:InputCsvFileName"]
                ?? throw new InvalidOperationException("CsvProcessing:InputCsvFileName is not configured");

            _inputCsvPath = Path.Combine(csvPath, csvFileName);

            string outputPath = _config["CsvProcessing:OutputCsvPath"]
                ?? throw new InvalidOperationException("CsvProcessing:OutputCsvPath is not configured");

            string outputFileName = _config["CsvProcessing:OutputCsvFileName"]
                ?? throw new InvalidOperationException("CsvProcessing:OutputCsvFileName is not configured");

            _outputCsvPath = Path.Combine(outputPath, outputFileName);

            _dateOfBirthColumn = _config["CsvProcessing:DateOfBirthColumn"] ?? "Date of Birth";
            _dateFormat = _config["CsvProcessing:DateFormat"] ?? "yyyy-MM-dd";

            // Load input date formats from configuration
            _inputDateFormats = _config.GetSection("CsvProcessing:InputDateFormats").Get<string[]>()
                ?? new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" };

            Console.WriteLine($"📅 Configured to parse input dates using {_inputDateFormats.Length} format(s): {string.Join(", ", _inputDateFormats)}");


            _lastNameColumn = _config["CsvProcessing:LastNameColumn"] ?? "Last Name";

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

            // Load encoding configurations from appsettings.json
            _encodingConfigs = _config.GetSection("CsvProcessing:EncodingsToTry")
                .Get<List<EncodingConfiguration>>() ?? new List<EncodingConfiguration>();

            // If no encodings configured, use defaults
            if (_encodingConfigs.Count == 0)
            {
                Console.WriteLine("⚠ No encodings configured, using defaults");
                _encodingConfigs = GetDefaultEncodingConfigurations();
            }
            else
            {
                // Sort by priority
                _encodingConfigs = _encodingConfigs.OrderBy(e => e.Priority).ToList();
                Console.WriteLine($"📋 Loaded {_encodingConfigs.Count} encoding configurations from appsettings.json");
            }

            // Ensure output directory exists
            string? outputDir = Path.GetDirectoryName(_outputCsvPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                Console.WriteLine($"Created output directory: {outputDir}");
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
        /// Reads CSV with proper encoding detection using configured encodings
        /// </summary>
        private List<string> ReadCsvWithBestEncoding(string csvPath)
        {
            Console.WriteLine($"\n🔍 Reading CSV file: {csvPath}");
            Console.WriteLine($"   Trying {_encodingConfigs.Count} configured encodings...");

            foreach (var encodingConfig in _encodingConfigs)
            {
                try
                {
                    var encoding = GetEncodingFromConfig(encodingConfig);
                    var lines = File.ReadAllLines(csvPath, encoding).ToList();

                    if (lines.Count > 0 && !lines[0].Contains('?') && !lines[0].Contains('�'))
                    {
                        Console.WriteLine($"✅ Successfully read CSV with {encodingConfig.Name} (Priority: {encodingConfig.Priority})");
                        return lines;
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠ {encodingConfig.Name} produced encoding issues, trying next...");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠ Failed with {encodingConfig.Name}: {ex.Message}");
                }
            }

            Console.WriteLine("⚠ All configured encodings failed, using UTF-8 as fallback");
            return File.ReadAllLines(csvPath, Encoding.UTF8).ToList();
        }

        /// <summary>
        /// Processes the CSV: reads, transforms dates, sorts, adds columns, and writes output
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

            // Read CSV lines
            var lines = ReadCsvWithBestEncoding(_inputCsvPath);
            if (lines.Count == 0)
            {
                Console.WriteLine("❌ CSV file is empty");
                return;
            }

            // Parse header
            var header = lines[0].Split(',').Select(h => h.Trim()).ToList();
            Console.WriteLine($"\n📋 Original columns: {string.Join(", ", header)}");

            // Add additional columns to header
            var newHeader = new List<string>(header);
            foreach (var column in _additionalColumns.Keys)
            {
                if (!newHeader.Contains(column))
                {
                    newHeader.Add(column);
                    Console.WriteLine($"   ➕ Adding column: {column}");
                }
            }

            // Parse data rows
            var records = new List<CsvRecord>();
            for (int i = 1; i < lines.Count; i++)
            {
                var values = lines[i].Split(',');
                if (values.Length != header.Count)
                {
                    Console.WriteLine($"⚠ Skipping malformed row {i}: column count mismatch");
                    continue;
                }

                var record = new CsvRecord();
                for (int j = 0; j < header.Count; j++)
                {
                    record[header[j]] = values[j].Trim();
                }

                // Add additional columns with default values
                foreach (var (columnName, defaultValue) in _additionalColumns)
                {
                    record[columnName] = defaultValue?.ToString() ?? string.Empty;
                }

                records.Add(record);
            }

            Console.WriteLine($"\n✅ Parsed {records.Count} records");

            // Transform Date of Birth column
            int dateTransformCount = 0;
            int dateErrorCount = 0;
            var failedDates = new List<string>();

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
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None,
                                out parsedDate))
                            {
                                record[_dateOfBirthColumn] = parsedDate.ToString(_dateFormat);
                                dateTransformCount++;
                                parsed = true;

                                // DEBUG: Log first transformation
                                if (dateTransformCount == 1)
                                {
                                    Console.WriteLine($"\n🔍 DEBUG: First date transformation:");
                                    Console.WriteLine($"   Original: '{originalDate}'");
                                    Console.WriteLine($"   Parsed with format: '{format}'");
                                    Console.WriteLine($"   Transformed to: '{parsedDate.ToString(_dateFormat)}'");
                                }

                                break;
                            }
                        }

                        // If none of the configured formats worked, try general parsing
                        if (!parsed && DateTime.TryParse(originalDate, out parsedDate))
                        {
                            record[_dateOfBirthColumn] = parsedDate.ToString(_dateFormat);
                            dateTransformCount++;
                            parsed = true;
                        }

                        if (!parsed)
                        {
                            failedDates.Add(originalDate);
                            dateErrorCount++;
                        }
                    }
                }
            }

            Console.WriteLine($"\n📅 Date formatting:");
            Console.WriteLine($"   Transformed: {dateTransformCount} dates");
            Console.WriteLine($"   Errors: {dateErrorCount} dates");
            Console.WriteLine($"   Output format: {_dateFormat}");

            if (failedDates.Count > 0)
            {
                Console.WriteLine($"\n⚠ Failed to parse {failedDates.Count} date(s):");
                foreach (var date in failedDates.Take(5)) // Show first 5 failed dates
                {
                    Console.WriteLine($"     - '{date}'");
                }
                if (failedDates.Count > 5)
                {
                    Console.WriteLine($"     ... and {failedDates.Count - 5} more");
                }
                Console.WriteLine($"\n💡 Tip: Add the correct format to 'InputDateFormats' in appsettings.json");
            }

            // Sort by Last Name
            Console.WriteLine($"\n🔤 Sorting by: {_lastNameColumn}");
            var sortedRecords = records
                .OrderBy(r => r[_lastNameColumn], StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Write output CSV
            WriteCsv(sortedRecords, newHeader);

            Console.WriteLine($"\n✅ CSV processing complete!");
            Console.WriteLine($"   Output file: {_outputCsvPath}");
            Console.WriteLine($"   Total records: {sortedRecords.Count}");


        }


        /// <summary>
        /// Writes the processed records to a new CSV file
        /// </summary>
        private void WriteCsv(List<CsvRecord> records, List<string> header)
        {
            var lines = new List<string>
            {
                string.Join(",", header)
            };

            // DEBUG: Show first few records being written
            Console.WriteLine($"\n🔍 DEBUG: First 3 records being written:");
            int debugCount = 0;

            foreach (var record in records)
            {
                var values = record.GetValues(header).Select(v => EscapeCsvValue(v)).ToList();
                string line = string.Join(",", values);
                lines.Add(line);

                // DEBUG: Print first 3 records
                if (debugCount < 3)
                {
                    Console.WriteLine($"   Record {debugCount + 1}:");
                    Console.WriteLine($"      Full line: {line}");
                    if (record.Properties.ContainsKey(_dateOfBirthColumn))
                    {
                        Console.WriteLine($"      Date of Birth value: '{record[_dateOfBirthColumn]}'");
                    }
                    debugCount++;
                }
            }

            // Write with UTF-8 encoding for maximum compatibility
            File.WriteAllLines(_outputCsvPath, lines, new UTF8Encoding(true));
            Console.WriteLine($"💾 Wrote {lines.Count - 1} records to output file");

            // ADDITIONAL DEBUG: Read back and verify first line
            var verifyLines = File.ReadAllLines(_outputCsvPath, Encoding.UTF8);
            if (verifyLines.Length > 1)
            {
                Console.WriteLine($"\n🔍 VERIFICATION: First data line in file:");
                Console.WriteLine($"   {verifyLines[1]}");
            }
        }


        /// <summary>
        /// Escapes CSV values that contain commas, quotes, or newlines
        /// </summary>
        private string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // If value contains comma, quote, or newline, wrap in quotes and escape existing quotes
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
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
            Console.WriteLine(new string('=', 100));

            foreach (var line in lines)
            {
                Console.WriteLine(line);
            }

            Console.WriteLine(new string('=', 100));
        }
    }
}