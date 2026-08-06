using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

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
        private readonly string _firstNameColumn;
        private readonly string _lastNameColumn;
        private readonly Dictionary<string, object?> _additionalColumns;
        private readonly List<EncodingConfiguration> _encodingConfigs;
        private readonly ILogger<StudentCsvProcessor> _logger;

        // ── Bilingual column aliases (mirrors StudentRecordMap) ───────────────
        // The first alias that matches an actual CSV header column wins.
        private static readonly string[] LastNameAliases =
            ["Last Name", "Nom de famille", "Nom"];

        private static readonly string[] FirstNameAliases =
            ["First Name", "Prénom", "Prenom"];

        private static readonly string[] DobAliases =
            ["Date of Birth", "Date de naissance", "DOB"];

        // Tracking for diagnostics
        private readonly List<ProcessingError> _processingErrors = new();
        private int _totalInputRows = 0;
        private int _successfullyParsedRows = 0;
        private int _skippedEmptyRows = 0;
        private int _malformedRows = 0;

        public StudentCsvProcessor(IConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            var csvConfig = ConfigurationService.GetCsvConfig();

            _inputCsvPath = Path.Combine(csvConfig.InputCsvPath, csvConfig.InputCsvFileName);
            _outputCsvPath = Path.Combine(csvConfig.OutputCsvPath, csvConfig.OutputCsvFileName);
            _logger = LoggerService.GetLogger<StudentCsvProcessor>();

            LoggerService.LogInformation($"\n📁 StudentCsvProcessor Initialized:");
            LoggerService.LogInformation($"   Input Path:  {_inputCsvPath}");
            LoggerService.LogInformation($"   Output Path: {_outputCsvPath}");

            if (_inputCsvPath.Contains("{") || _outputCsvPath.Contains("{"))
            {
                LoggerService.LogInformation($"   ⚠️  WARNING: Placeholders still present!");
                throw new InvalidOperationException(
                    "Path placeholders were not resolved. Check ConfigurationService.ResolvePath()");
            }

            _dateOfBirthColumn = csvConfig.DateOfBirthColumn;
            _dateFormat = csvConfig.DateFormat;
            _inputDateFormats = csvConfig.InputDateFormats;
            _firstNameColumn = csvConfig.FirstNameColumn;
            _lastNameColumn = csvConfig.LastNameColumn;

            LoggerService.LogInformation(
                $"📅 Configured to parse input dates using {_inputDateFormats.Length} format(s): " +
                string.Join(", ", _inputDateFormats));

            _additionalColumns = new Dictionary<string, object?>();
            var additionalColumnsSection = _config.GetSection("CsvProcessing:AdditionalColumns");
            foreach (var column in additionalColumnsSection.GetChildren())
            {
                string key = column.Key;
                string? value = column.Value;

                if (value == null || value.Equals("null", StringComparison.OrdinalIgnoreCase))
                    _additionalColumns[key] = null;
                else if (bool.TryParse(value, out bool boolValue))
                    _additionalColumns[key] = boolValue;
                else
                    _additionalColumns[key] = value;
            }

            _encodingConfigs = _config.GetSection("CsvProcessing:EncodingsToTry")
                .Get<List<EncodingConfiguration>>() ?? [];

            if (_encodingConfigs.Count == 0)
            {
                LoggerService.LogInformation("⚠ No encodings configured, using defaults");
                _encodingConfigs = GetDefaultEncodingConfigurations();
            }
            else
            {
                _encodingConfigs = [.. _encodingConfigs.OrderBy(e => e.Priority)];
                LoggerService.LogInformation($"📋 Loaded {_encodingConfigs.Count} encoding configurations");
            }

            string? outputDir = Path.GetDirectoryName(_outputCsvPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                LoggerService.LogInformation($"✅ Created output directory: {outputDir}");
            }
        }

        // ── Resolve the actual column name present in the CSV header ─────────
        /// <summary>
        /// Returns the first alias from <paramref name="aliases"/> that exists as a
        /// key in <paramref name="record"/>, falling back to <paramref name="configuredName"/>
        /// (from appsettings.json) and finally the first alias.
        /// Logs a warning when the configured name is not found so it is easy to diagnose.
        /// </summary>
        private static string ResolveColumnName(
            CsvRecord record,
            string[] aliases,
            string configuredName,
            string fieldLabel)
        {
            // 1. Prefer the alias that actually exists in the record
            foreach (var alias in aliases)
                if (record.Properties.ContainsKey(alias))
                    return alias;

            // 2. Fall back to the appsettings.json configured name
            if (record.Properties.ContainsKey(configuredName))
                return configuredName;

            // 3. Nothing matched — log and return the configured name so callers
            //    get an empty string rather than throwing.
            LoggerService.LogWarning(
                $"   ⚠️  Column '{fieldLabel}' not found in CSV.\n" +
                $"      Tried aliases : {string.Join(", ", aliases)}\n" +
                $"      Configured    : {configuredName}\n" +
                $"      Available     : {string.Join(", ", record.Properties.Keys.Take(10))}...\n" +
                "      Check CsvProcessing → LastNameColumn / FirstNameColumn / DateOfBirthColumn " +
                "in appsettings.json, or add the French column header to StudentRecordMap aliases.");

            return configuredName;
        }

        public void ProcessCsv()
        {
            if (!File.Exists(_inputCsvPath))
            {
                LoggerService.LogInformation($"❌ CSV file not found: {_inputCsvPath}");
                return;
            }

            LoggerService.LogInformation($"\n📄 Processing CSV file...");
            LoggerService.LogInformation($"   Input:  {_inputCsvPath}");
            LoggerService.LogInformation($"   Output: {_outputCsvPath}");

            _processingErrors.Clear();
            _totalInputRows = _successfullyParsedRows = _skippedEmptyRows = _malformedRows = 0;

            try
            {
                var records = ReadCsvWithCsvHelper();

                if (records.Count == 0)
                {
                    LoggerService.LogInformation("❌ No valid records found in CSV");
                    PrintProcessingDiagnostics();
                    return;
                }

                LoggerService.LogInformation($"\n✅ Successfully parsed {records.Count} records");

                // ── Resolve actual bilingual column names from the first record ──
                var sample = records[0];
                string resolvedLastName = ResolveColumnName(sample, LastNameAliases, _lastNameColumn, "Last Name");
                string resolvedFirstName = ResolveColumnName(sample, FirstNameAliases, _firstNameColumn, "First Name");
                string resolvedDob = ResolveColumnName(sample, DobAliases, _dateOfBirthColumn, "Date of Birth");

                LoggerService.LogInformation(
                    $"   📋 Resolved columns → " +
                    $"LastName='{resolvedLastName}'  " +
                    $"FirstName='{resolvedFirstName}'  " +
                    $"DOB='{resolvedDob}'");

                // ── Build final header ────────────────────────────────────────
                var finalHeader = GetFinalHeader(sample);

                if (!finalHeader.Contains("IsDuplicate"))
                {
                    int insertAt = finalHeader.IndexOf("ClientIdStatus");
                    if (insertAt < 0) insertAt = finalHeader.Count;
                    finalHeader.Insert(insertAt, "IsDuplicate");
                    LoggerService.LogInformation("   ➕ Adding column: IsDuplicate");
                }

                // ── Detect duplicates using resolved column names ─────────────
                LoggerService.LogInformation("\n🔍 Detecting duplicates (FirstName + LastName + DOB)...");

                // Pass 1: count occurrences per normalised key
                var keyCounts = new Dictionary<string, int>();
                foreach (var r in records)
                {
                    string key = BuildDuplicateKey(r, resolvedLastName, resolvedFirstName, resolvedDob);
                    keyCounts[key] = keyCounts.GetValueOrDefault(key, 0) + 1;
                }

                // Pass 2: flag rows that belong to a group with count > 1
                int duplicateCount = 0;
                foreach (var r in records)
                {
                    string key = BuildDuplicateKey(r, resolvedLastName, resolvedFirstName, resolvedDob);

                    if (keyCounts[key] > 1)
                    {
                        r["IsDuplicate"] = "true";
                        duplicateCount++;
                        LoggerService.LogInformation(
                            $"   ⚠️  Duplicate: " +
                            $"{r.Properties.GetValueOrDefault(resolvedLastName)} " +
                            $"{r.Properties.GetValueOrDefault(resolvedFirstName)} " +
                            $"({r.Properties.GetValueOrDefault(resolvedDob)})");
                    }
                    else
                    {
                        r["IsDuplicate"] = "false";
                    }
                }

                LoggerService.LogInformation(
                    $"   ✅ {duplicateCount} duplicate row(s) flagged across all groups");

                // ── Transform dates using resolved DOB column ─────────────────
                TransformDates(records, resolvedDob);

                // ── Sort by resolved last-name column ─────────────────────────
                LoggerService.LogInformation($"\n🔤 Sorting by: {resolvedLastName}");
                var sortedRecords = records
                    .OrderBy(r => r.Properties.GetValueOrDefault(resolvedLastName, string.Empty),
                             StringComparer.OrdinalIgnoreCase)
                    .ToList();

                WriteCsv(sortedRecords, finalHeader);
                PrintProcessingDiagnostics();

                LoggerService.LogInformation($"\n✅ CSV processing complete!");
                LoggerService.LogInformation($"   Output file: {_outputCsvPath}");
                LoggerService.LogInformation($"   Total records: {sortedRecords.Count}");
                LoggerService.LogInformation($"   Ready for Client ID search automation");
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"\n❌ FATAL ERROR during CSV processing:");
                LoggerService.LogInformation($"   {ex.Message}");
                LoggerService.LogInformation($"   Stack trace: {ex.StackTrace}");
                PrintProcessingDiagnostics();
                throw;
            }
        }

        // ── Build the normalised duplicate key ────────────────────────────────
        private static string BuildDuplicateKey(
            CsvRecord record,
            string lastNameCol,
            string firstNameCol,
            string dobCol)
        {
            string ln = NormalizeDuplicateKey(record.Properties.GetValueOrDefault(lastNameCol, string.Empty));
            string fn = NormalizeDuplicateKey(record.Properties.GetValueOrDefault(firstNameCol, string.Empty));
            string dob = record.Properties.GetValueOrDefault(dobCol, string.Empty).Trim();
            return $"{ln}_{fn}_{dob}";
        }

        private List<CsvRecord> ReadCsvWithCsvHelper()
        {
            LoggerService.LogInformation($"\n🔍 Reading CSV with CsvHelper library...");

            var records = new List<CsvRecord>();
            Encoding? successfulEncoding = null;
            List<string>? header = null;

            foreach (var encodingConfig in _encodingConfigs)
            {
                try
                {
                    records.Clear();
                    _processingErrors.Clear();
                    _totalInputRows = _successfullyParsedRows = _skippedEmptyRows = _malformedRows = 0;

                    var targetEncoding = EncodingConfigurationService.ResolveEncoding(encodingConfig);

                    LoggerService.LogInformation($"   Trying: {encodingConfig.Name}...");

                    using var reader = new StreamReader(_inputCsvPath, targetEncoding);
                    using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        MissingFieldFound = null,
                        HeaderValidated = null,
                        TrimOptions = TrimOptions.Trim,
                        BadDataFound = context =>
                        {
                            _malformedRows++;
                            _processingErrors.Add(new ProcessingError
                            {
                                RowNumber = context.Context.Parser.Row,
                                ErrorType = "BadData",
                                Message = $"Malformed data: {context.RawRecord}"
                            });
                            LoggerService.LogInformation(
                                $"      ⚠ Row {context.Context.Parser.Row}: Bad data detected");
                        },
                        ReadingExceptionOccurred = args =>
                        {
                            _processingErrors.Add(new ProcessingError
                            {
                                RowNumber = args.Exception.Context.Parser.Row,
                                ErrorType = "ReadingException",
                                Message = args.Exception.Message
                            });
                            LoggerService.LogInformation(
                                $"      ⚠ Reading exception: {args.Exception.Message}");
                            return false;
                        }
                    });

                    csv.Read();
                    csv.ReadHeader();
                    header = csv.HeaderRecord?.ToList();

                    if (header == null || header.Count == 0)
                    {
                        LoggerService.LogInformation(
                            $"      ✗ No header found with {encodingConfig.Name}");
                        continue;
                    }

                    if (header.Any(h => h.Contains('?') || h.Contains('▯')))
                    {
                        LoggerService.LogInformation(
                            $"      ✗ Encoding issues detected in header");
                        continue;
                    }

                    LoggerService.LogInformation(
                        $"      ✓ Header looks good: {header.Count} columns");
                    LoggerService.LogInformation(
                        $"      Columns: {string.Join(", ", header.Take(5))}" +
                        (header.Count > 5 ? "..." : ""));

                    var newHeader = new List<string>(header);
                    foreach (var column in _additionalColumns.Keys)
                        if (!newHeader.Contains(column))
                            newHeader.Add(column);

                    if (!newHeader.Contains("ClientIdStatus")) newHeader.Add("ClientIdStatus");
                    if (!newHeader.Contains("BestMatch")) newHeader.Add("BestMatch");

                    int rowNumber = 1;
                    while (csv.Read())
                    {
                        rowNumber++;
                        _totalInputRows++;

                        try
                        {
                            bool isEmpty = header.All(col =>
                                string.IsNullOrWhiteSpace(csv.GetField(col)));

                            if (isEmpty)
                            {
                                _skippedEmptyRows++;
                                LoggerService.LogInformation(
                                    $"      ⚠ Row {rowNumber}: Empty row, skipping");
                                continue;
                            }

                            var record = new CsvRecord();
                            foreach (var col in header)
                                record[col] = csv.GetField(col)?.Trim() ?? string.Empty;

                            foreach (var (columnName, defaultValue) in _additionalColumns)
                                record[columnName] = defaultValue?.ToString() ?? string.Empty;

                            if (!record.Properties.ContainsKey("ClientIdStatus"))
                                record["ClientIdStatus"] = "0";
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
                            LoggerService.LogInformation(
                                $"      ⚠ Row {rowNumber}: Failed to parse - {ex.Message}");
                        }
                    }

                    successfulEncoding = targetEncoding;
                    LoggerService.LogInformation(
                        $"   ✅ Successfully read with {encodingConfig.Name}");
                    LoggerService.LogInformation(
                        $"      Total input rows: {_totalInputRows}");
                    LoggerService.LogInformation(
                        $"      Successfully parsed: {_successfullyParsedRows}");
                    LoggerService.LogInformation(
                        $"      Skipped (empty): {_skippedEmptyRows}");
                    LoggerService.LogInformation(
                        $"      Errors: {_processingErrors.Count}");
                    break;
                }
                catch (Exception ex)
                {
                    LoggerService.LogInformation(
                        $"      ✗ Failed with {encodingConfig.Name}: {ex.Message}");
                    records.Clear();
                    _processingErrors.Clear();
                    _totalInputRows = _successfullyParsedRows = _skippedEmptyRows = _malformedRows = 0;
                }
            }

            if (successfulEncoding == null)
                throw new InvalidOperationException(
                    "Failed to read CSV with any configured encoding");

            return records;
        }

        // ── Accept the resolved DOB column name rather than the field ─────────
        private void TransformDates(List<CsvRecord> records, string dobColumn)
        {
            int dateTransformCount = 0;
            int dateErrorCount = 0;
            var failedDates = new List<string>();

            LoggerService.LogInformation($"\n📅 Transforming dates in column: {dobColumn}");

            foreach (var record in records)
            {
                if (!record.Properties.ContainsKey(dobColumn)) continue;

                string originalDate = record[dobColumn];
                if (string.IsNullOrWhiteSpace(originalDate)) continue;

                bool parsed = false;

                foreach (var format in _inputDateFormats)
                {
                    if (DateTime.TryParseExact(originalDate, format,
                            CultureInfo.InvariantCulture, DateTimeStyles.None,
                            out DateTime parsedDate))
                    {
                        record[dobColumn] = parsedDate.ToString(_dateFormat);
                        dateTransformCount++;
                        parsed = true;

                        if (dateTransformCount == 1)
                        {
                            LoggerService.LogInformation($"   🔍 First date transformation:");
                            LoggerService.LogInformation($"      Original : '{originalDate}'");
                            LoggerService.LogInformation($"      Format   : '{format}'");
                            LoggerService.LogInformation(
                                $"      Result   : '{parsedDate.ToString(_dateFormat)}'");
                        }
                        break;
                    }
                }

                if (!parsed && DateTime.TryParse(originalDate,
                        CultureInfo.InvariantCulture, DateTimeStyles.None,
                        out DateTime fallback))
                {
                    record[dobColumn] = fallback.ToString(_dateFormat);
                    dateTransformCount++;
                    parsed = true;
                    LoggerService.LogInformation(
                        $"   ⚠ Used general parsing for: '{originalDate}'");
                }

                if (!parsed)
                {
                    failedDates.Add(originalDate);
                    dateErrorCount++;
                }
            }

            LoggerService.LogInformation($"\n   Transformed : {dateTransformCount} dates");
            LoggerService.LogInformation($"   Errors       : {dateErrorCount} dates");
            LoggerService.LogInformation($"   Output format: {_dateFormat}");

            if (failedDates.Count > 0)
            {
                LoggerService.LogInformation(
                    $"\n   ⚠ Failed to parse {failedDates.Count} date(s):");
                foreach (var date in failedDates.Take(10))
                    LoggerService.LogInformation($"      - '{date}'");
                if (failedDates.Count > 10)
                    LoggerService.LogInformation(
                        $"      ... and {failedDates.Count - 10} more");
                LoggerService.LogInformation(
                    "\n   💡 Add the correct format to 'InputDateFormats' in appsettings.json");
            }
        }

        private List<string> GetFinalHeader(CsvRecord? sampleRecord)
            => sampleRecord == null ? [] : [.. sampleRecord.GetColumnNames()];

        private void WriteCsv(List<CsvRecord> records, List<string> header)
        {
            LoggerService.LogInformation($"\n💾 Writing {records.Count} records to output...");

            try
            {
                var targetEncoding = EncodingConfigurationService.GetPriorityEncoding();

                using (var writer = new StreamWriter(_outputCsvPath, false, targetEncoding))
                using (var csv = new CsvWriter(writer,
                           new CsvConfiguration(CultureInfo.InvariantCulture)
                           { HasHeaderRecord = true }))
                {
                    foreach (var col in header) csv.WriteField(col);
                    csv.NextRecord();

                    foreach (var record in records)
                    {
                        foreach (var col in header) csv.WriteField(record[col]);
                        csv.NextRecord();
                    }
                }

                LoggerService.LogInformation(
                    $"   ✅ Successfully wrote {records.Count} records");

                var verifyLines = File.ReadAllLines(_outputCsvPath, targetEncoding);
                LoggerService.LogInformation(
                    $"   ✅ Verification: File contains {verifyLines.Length} lines (including header)");
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"   ❌ Write failed: {ex.Message}");
                throw;
            }
        }

        private void PrintProcessingDiagnostics()
        {
            LoggerService.LogInformation("\n" + new string('═', 70));
            LoggerService.LogInformation("📊 CSV PROCESSING DIAGNOSTICS");
            LoggerService.LogInformation(new string('═', 70));
            LoggerService.LogInformation(
                $"Total input rows (excluding header): {_totalInputRows}");
            LoggerService.LogInformation(
                $"Successfully parsed rows: {_successfullyParsedRows}");
            LoggerService.LogInformation($"Skipped empty rows: {_skippedEmptyRows}");
            LoggerService.LogInformation(
                $"Malformed/bad data rows: {_malformedRows}");
            LoggerService.LogInformation(
                $"Other errors: {_processingErrors.Count - _malformedRows}");

            int lostRows = _totalInputRows - _successfullyParsedRows;
            if (lostRows > 0)
            {
                LoggerService.LogInformation($"\n⚠️  ROWS LOST: {lostRows}");
                LoggerService.LogInformation(
                    $"   - Empty rows    : {_skippedEmptyRows}");
                LoggerService.LogInformation(
                    $"   - Malformed data: {_malformedRows}");
                LoggerService.LogInformation(
                    $"   - Other errors  : {_processingErrors.Count - _malformedRows}");
            }

            if (_processingErrors.Count > 0)
            {
                LoggerService.LogInformation(
                    $"\n❌ DETAILED ERRORS ({_processingErrors.Count} total):");
                foreach (var group in _processingErrors.GroupBy(e => e.ErrorType))
                {
                    LoggerService.LogInformation(
                        $"\n   {group.Key}: {group.Count()} occurrences");
                    foreach (var error in group.Take(5))
                        LoggerService.LogInformation(
                            $"      Row {error.RowNumber}: {error.Message}");
                    if (group.Count() > 5)
                        LoggerService.LogInformation(
                            $"      ... and {group.Count() - 5} more");
                }
            }

            LoggerService.LogInformation(new string('═', 70));
        }

        public void PreviewCsv(int maxRows = 5)
        {
            if (!File.Exists(_outputCsvPath))
            {
                LoggerService.LogInformation(
                    $"❌ Output CSV file not found: {_outputCsvPath}");
                return;
            }

            var targetEncoding = EncodingConfigurationService.GetPriorityEncoding();
            var lines = File.ReadAllLines(_outputCsvPath, targetEncoding)
                            .Take(maxRows + 1).ToList();

            LoggerService.LogInformation(
                $"\n📋 Preview of processed CSV (first {maxRows} rows):");
            LoggerService.LogInformation(new string('═', 100));

            foreach (var line in lines)
                LoggerService.LogInformation(
                    line.Length > 150 ? line[..147] + "..." : line);

            LoggerService.LogInformation(new string('═', 100));
        }

        private static string NormalizeDuplicateKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var c in value.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) !=
                    UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString()
                     .Normalize(NormalizationForm.FormC)
                     .ToUpperInvariant()
                     .Replace(" ", "")
                     .Replace("-", "")
                     .Replace("'", "");
        }

        private List<EncodingConfiguration> GetDefaultEncodingConfigurations() =>
        [
            new() { Name = "Windows-1252 (ANSI)", CodePage = "1252",       Priority = 1 },
            new() { Name = "UTF-8",               CodePage = "utf-8",      Priority = 2 },
            new() { Name = "UTF-8 with BOM",      CodePage = "utf-8",      UseBOM = true, Priority = 3 },
            new() { Name = "ISO-8859-1 (Latin-1)",CodePage = "iso-8859-1", Priority = 4 },
            new() { Name = "System Default",      CodePage = "default",    Priority = 5 },
        ];
    }

    internal class ProcessingError
    {
        public int RowNumber { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
