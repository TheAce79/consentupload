using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ConsentSyncCore.Models;
using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;

namespace ConsentSyncCore.Services.Excel;

public sealed class Gnb2009ExcelParserService
{
    private static readonly Regex DateTokenRegex = new(
        @"\b(?:19|20)\d{2}[-/ ][A-Za-z]{3,9}[-/ ]\d{1,2}\b|\b(?:19|20)\d{2}[-/]\d{1,2}[-/]\d{1,2}\b|\b\d{1,2}[-/]\d{1,2}[-/](?:19|20)\d{2}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] DateFormats =
    [
        "yyyy MMM dd", "yyyy MMMM dd", "yyyy-MMM-dd", "yyyy-MMMM-dd", "yyyy/MM/dd", "yyyy-MM-dd",
        "M/d/yyyy", "MM/dd/yyyy", "M-d-yyyy", "MM-dd-yyyy"
    ];

    public Dictionary<string, ClientImmunizationHistory> ExtractHistoriesFromDirectory(string criteriaFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(criteriaFolderPath);
        if (!Directory.Exists(criteriaFolderPath)) return new Dictionary<string, ClientImmunizationHistory>(StringComparer.OrdinalIgnoreCase);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var histories = new Dictionary<string, ClientImmunizationHistory>(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in Directory.EnumerateFiles(criteriaFolderPath, "*.xls", SearchOption.TopDirectoryOnly)
                     .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal)))
        {
            ExtractFromWorkbook(filePath, histories);
        }

        return histories;
    }

    public void ExportDiagnosticCsv(Dictionary<string, ClientImmunizationHistory> histories, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(histories);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(true));
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { ShouldQuote = _ => true });
        csv.WriteField("ClientId");
        csv.WriteField("Agent");
        csv.WriteField("AdministeredDate");
        csv.NextRecord();

        foreach (ClientImmunizationHistory history in histories.Values.OrderBy(history => history.ClientId, StringComparer.OrdinalIgnoreCase))
        foreach (ImmunizationRecord record in history.Records.OrderBy(record => record.AdministeredDate).ThenBy(record => record.Agent, StringComparer.OrdinalIgnoreCase))
        {
            csv.WriteField(history.ClientId);
            csv.WriteField(record.Agent);
            csv.WriteField(record.AdministeredDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            csv.NextRecord();
        }
    }

    private static void ExtractFromWorkbook(string filePath, Dictionary<string, ClientImmunizationHistory> histories)
    {
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        DataSet dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
        });

        foreach (DataTable table in dataSet.Tables)
        {
            ClientImmunizationHistory? current = null;
            bool inHistory = false;
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                string[] row = Enumerable.Range(0, table.Columns.Count).Select(col => CellText(table.Rows[rowIndex][col])).ToArray();
                string joined = string.Join(' ', row.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (joined.Length == 0) continue;

                string? clientId = ValueAfterLabel(row, "Client ID");
                if (!string.IsNullOrWhiteSpace(clientId))
                {
                    current = UpsertIdentity(histories, clientId, filePath, table.TableName, rowIndex + 1);
                    inHistory = false;
                }

                if (current is not null)
                {
                    string? name = ValueAfterLabel(row, "Client Name");
                    if (!string.IsNullOrWhiteSpace(name)) MergeIdentity(current, name, null, filePath, table.TableName, rowIndex + 1);

                    string? dob = ValueAfterLabel(row, "Date of Birth");
                    if (!string.IsNullOrWhiteSpace(dob))
                    {
                        DateTime parsedDob = ParseRequiredDate(dob, filePath, table.TableName, rowIndex + 1);
                        MergeIdentity(current, null, parsedDob, filePath, table.TableName, rowIndex + 1);
                    }
                }

                if (ContainsText(row, "Immunization Forecast"))
                {
                    inHistory = false;
                    continue;
                }

                if (ContainsText(row, "Immunization History"))
                {
                    inHistory = current is not null;
                    continue;
                }

                if (inHistory && current is not null)
                {
                    ExtractHistoryRow(current, row, filePath, table.TableName, rowIndex + 1);
                }
            }
        }
    }

    private static ClientImmunizationHistory UpsertIdentity(Dictionary<string, ClientImmunizationHistory> histories, string rawClientId, string filePath, string sheetName, int rowNumber)
    {
        string clientId = rawClientId.Trim();
        if (!Regex.IsMatch(clientId, @"^\d+$"))
            throw new FormatException($"{Path.GetFileName(filePath)}:{sheetName}: row {rowNumber}: malformed Client ID '{rawClientId}'.");

        if (!histories.TryGetValue(clientId, out ClientImmunizationHistory? history))
        {
            history = new ClientImmunizationHistory { ClientId = clientId };
            histories.Add(clientId, history);
        }

        return history;
    }

    private static void MergeIdentity(ClientImmunizationHistory history, string? fullName, DateTime? dateOfBirth, string filePath, string sheetName, int rowNumber)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            string normalized = NormalizeWhitespace(fullName);
            if (history.FullName.Length == 0) history.FullName = normalized;
            else if (!string.Equals(history.FullName, normalized, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{Path.GetFileName(filePath)}:{sheetName}: row {rowNumber}: conflicting Client Name for Client ID {history.ClientId}.");
        }

        if (dateOfBirth is DateTime dob)
        {
            if (history.DateOfBirth == default) history.DateOfBirth = dob.Date;
            else if (history.DateOfBirth.Date != dob.Date)
                throw new InvalidDataException($"{Path.GetFileName(filePath)}:{sheetName}: row {rowNumber}: conflicting Date of Birth for Client ID {history.ClientId}.");
        }
    }

    private static void ExtractHistoryRow(ClientImmunizationHistory history, string[] row, string filePath, string sheetName, int rowNumber)
    {
        if (ContainsText(row, "Immunizing Agent") || IsFootnote(row)) return;

        int firstDateColumn = Array.FindIndex(row, value => DateTokenRegex.IsMatch(value));
        if (firstDateColumn < 0)
        {
            string trailing = string.Join(' ', row.Skip(3).Where(value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(FindAgent(row)) && LooksLikeMalformedDateCell(trailing))
                throw new FormatException($"{Path.GetFileName(filePath)}:{sheetName}: row {rowNumber}: malformed immunization date '{trailing}'.");
            return;
        }

        string? agent = FindAgent(row.Take(firstDateColumn).ToArray());
        if (string.IsNullOrWhiteSpace(agent)) return;

        string dateText = string.Join(' ', row.Skip(firstDateColumn).Where(value => !string.IsNullOrWhiteSpace(value)));
        MatchCollection matches = DateTokenRegex.Matches(dateText);
        if (matches.Count == 0 && !string.IsNullOrWhiteSpace(dateText))
            throw new FormatException($"{Path.GetFileName(filePath)}:{sheetName}: row {rowNumber}: malformed immunization date '{dateText}'.");

        foreach (Match match in matches)
        {
            DateTime administered = ParseRequiredDate(match.Value, filePath, sheetName, rowNumber);
            if (!history.Records.Any(record => string.Equals(record.Agent, agent, StringComparison.OrdinalIgnoreCase) && record.AdministeredDate.Date == administered.Date))
                history.Records.Add(new ImmunizationRecord { Agent = agent, AdministeredDate = administered.Date });
        }
    }

    private static string? FindAgent(IReadOnlyList<string> cells)
    {
        foreach (string cell in cells)
        {
            string value = cell.Trim();
            if (value.Length == 0 || IsKnownNonAgent(value) || DateTokenRegex.IsMatch(value)) continue;
            return NormalizeWhitespace(value);
        }

        return null;
    }

    private static bool IsKnownNonAgent(string value) =>
        value.Equals("X", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Client ID", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Client Name", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Date of Birth", StringComparison.OrdinalIgnoreCase);

    private static bool IsFootnote(string[] row)
    {
        string first = row.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        return Regex.IsMatch(first, "^[EORX]\\s+-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeMalformedDateCell(string value) =>
        value.Contains("date", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(value, @"(?:19|20)\d{2}|\d{1,2}[-/ ]\d{1,2}", RegexOptions.CultureInvariant);

    private static string? ValueAfterLabel(string[] row, string label)
    {
        for (int index = 0; index < row.Length; index++)
        {
            if (!string.Equals(row[index].Trim(), label, StringComparison.OrdinalIgnoreCase)) continue;
            return row.Skip(index + 1).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        return null;
    }

    private static bool ContainsText(IEnumerable<string> row, string text) =>
        row.Any(value => value.Contains(text, StringComparison.OrdinalIgnoreCase));

    private static DateTime ParseRequiredDate(string value, string filePath, string sheetName, int rowNumber)
    {
        string normalized = NormalizeWhitespace(value.Replace('-', ' ').Replace('/', ' '));
        string[] candidates = [value.Trim(), normalized];
        foreach (string candidate in candidates)
        {
            if (DateTime.TryParseExact(candidate, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exact))
                return exact.Date;
            if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return parsed.Date;
        }

        throw new FormatException($"{Path.GetFileName(filePath)}:{sheetName}: row {rowNumber}: malformed date '{value}'.");
    }

    private static string CellText(object? value)
    {
        if (value is null || value is DBNull) return string.Empty;
        return value switch
        {
            DateTime date => date.ToString("yyyy MMM dd", CultureInfo.InvariantCulture),
            double number when Math.Abs(number % 1) < double.Epsilon => number.ToString("0", CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
        };
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
