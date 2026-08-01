using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;

namespace ConsentSyncCore.Services.Matching
{
    public class LocalRosterMatcher
    {
        private const double AutoMatchThreshold = 95.0;
        private const double SuggestionThreshold = 85.0;
        private const double UniqueMatchGapThreshold = 5.0;
        private const string RosterDateFormat = "yyyy MMM dd";

        private readonly List<MassImmunisationRosterRecord> _roster = new();
        private readonly string[] _studentDateFormats;
        private readonly FuzzyMatcher _fuzzyMatcher;

        public LocalRosterMatcher(IConfiguration? config = null)
        {
            config ??= ConfigurationService.GetConfiguration();
            _fuzzyMatcher = new FuzzyMatcher();

            var csvConfig = ConfigurationService.GetCsvConfig();
            _studentDateFormats = csvConfig.InputDateFormats
                .Concat(new[] { csvConfig.DateFormat, "yyyy/MM/dd" })
                .Where(format => !string.IsNullOrWhiteSpace(format))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            LoadRoster();
        }

        public bool HasRoster => _roster.Count > 0;
        public int RosterCount => _roster.Count;

        public LocalRosterMatchResult MatchStudent(StudentRecord student)
        {
            if (!HasRoster)
            {
                return new LocalRosterMatchResult();
            }

            DateTime? studentDob = ParseStudentDob(student.DateOfBirth);
            if (!studentDob.HasValue)
            {
                return new LocalRosterMatchResult();
            }

            string[] inputTokens = TokenizeName($"{student.LastName} {student.FirstName}");

            var candidates = new List<RosterCandidate>();

            foreach (var row in _roster)
            {
                DateTime? rosterDob = ParseRosterDob(row.DateOfBirth);
                bool dobMatch = rosterDob.HasValue && IsExactOrInvertedDobMatch(studentDob.Value, rosterDob.Value);

                var (rosterFirstName, rosterLastName) = SplitRosterName(row.ClientName);
                double pairScore = _fuzzyMatcher.CalculateNameMatchScore(
                    student.FirstName,
                    student.LastName,
                    rosterFirstName,
                    rosterLastName);

                string[] rosterTokens = TokenizeName(row.ClientName);
                double tokenScore = CalculateTokenJaccard(inputTokens, rosterTokens) * 100.0;
                double nameScore = Math.Max(pairScore, tokenScore);

                candidates.Add(new RosterCandidate
                {
                    Row = row,
                    NameScore = nameScore,
                    DobMatch = dobMatch
                });
            }

            var dobCandidates = candidates
                .Where(candidate => candidate.DobMatch)
                .OrderByDescending(candidate => candidate.NameScore)
                .ToList();

            var bestDobCandidate = dobCandidates.FirstOrDefault();
            if (bestDobCandidate != null && bestDobCandidate.NameScore >= AutoMatchThreshold)
            {
                var secondDobCandidate = dobCandidates.Skip(1).FirstOrDefault();
                bool isUniqueBest =
                    secondDobCandidate == null ||
                    bestDobCandidate.NameScore - secondDobCandidate.NameScore >= UniqueMatchGapThreshold;

                if (isUniqueBest)
                {
                    return new LocalRosterMatchResult
                    {
                        Matched = true,
                        ClientId = bestDobCandidate.Row.ClientId,
                        NameScore = bestDobCandidate.NameScore
                    };
                }
            }

            var suggestionCandidate = bestDobCandidate
                ?? candidates.OrderByDescending(candidate => candidate.NameScore).FirstOrDefault();

            if (suggestionCandidate != null && suggestionCandidate.NameScore >= SuggestionThreshold)
            {
                return new LocalRosterMatchResult
                {
                    Suggestion = BuildSuggestion(suggestionCandidate),
                    NameScore = suggestionCandidate.NameScore
                };
            }

            return new LocalRosterMatchResult();
        }

        private void LoadRoster()
        {
            string filePath = ConfigurationService.GetMassImmunisationCsvFullPath();
            if (!File.Exists(filePath))
            {
                LoggerService.LogInformation($"ℹ️  Local roster not found: {filePath}");
                return;
            }

            try
            {
                var targetEncoding = EncodingConfigurationService.GetPriorityEncoding();
                using var reader = new StreamReader(filePath, targetEncoding);
                using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    MissingFieldFound = null,
                    HeaderValidated = null,
                    TrimOptions = TrimOptions.Trim
                });

                var records = csv.GetRecords<MassImmunisationRosterRecord>()
                    .Where(record =>
                        !string.IsNullOrWhiteSpace(record.ClientId) &&
                        !string.IsNullOrWhiteSpace(record.ClientName))
                    .ToList();

                _roster.AddRange(records);

                if (_roster.Count > 0)
                {
                    LoggerService.LogInformation($"📋 Loaded {_roster.Count} Mass Imms roster row(s) from mass_immunisation.csv");
                }
                else
                {
                    LoggerService.LogInformation("ℹ️  Mass Imms roster file is empty; Phase 1 will use live PHIS search.");
                }
            }
            catch (Exception ex)
            {
                _roster.Clear();
                LoggerService.LogWarning($"⚠️  Could not load mass_immunisation.csv: {ex.Message}");
            }
        }

        private DateTime? ParseStudentDob(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            foreach (string format in _studentDateFormats)
            {
                if (DateTime.TryParseExact(
                    value.Trim(),
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsed))
                {
                    return parsed.Date;
                }
            }

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fallback)
                ? fallback.Date
                : null;
        }

        private static DateTime? ParseRosterDob(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTime.TryParseExact(
                value.Trim(),
                RosterDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed)
                ? parsed.Date
                : null;
        }

        private static bool IsExactOrInvertedDobMatch(DateTime studentDob, DateTime rosterDob)
        {
            if (studentDob.Date == rosterDob.Date)
            {
                return true;
            }

            return studentDob.Year == rosterDob.Year &&
                   studentDob.Month == rosterDob.Day &&
                   studentDob.Day == rosterDob.Month;
        }

        private static (string firstName, string lastName) SplitRosterName(string clientName)
        {
            if (string.IsNullOrWhiteSpace(clientName))
            {
                return (string.Empty, string.Empty);
            }

            var parts = clientName.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return (parts[1], parts[0]);
            }

            var tokens = clientName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 1)
            {
                return (tokens[0], string.Empty);
            }

            return (string.Join(' ', tokens.Skip(1)), tokens[0]);
        }

        private static string[] TokenizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            var normalized = RemoveAccents(value.ToUpperInvariant());
            var buffer = new StringBuilder(normalized.Length);

            foreach (char c in normalized)
            {
                buffer.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }

            return buffer.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static double CalculateTokenJaccard(string[] leftTokens, string[] rightTokens)
        {
            if (leftTokens.Length == 0 || rightTokens.Length == 0)
            {
                return 0.0;
            }

            var left = new HashSet<string>(leftTokens, StringComparer.Ordinal);
            var right = new HashSet<string>(rightTokens, StringComparer.Ordinal);

            int intersection = left.Intersect(right, StringComparer.Ordinal).Count();
            int union = left.Union(right, StringComparer.Ordinal).Count();

            return union == 0 ? 0.0 : (double)intersection / union;
        }

        private static string RemoveAccents(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string BuildSuggestion(RosterCandidate candidate)
        {
            return $"{candidate.Row.ClientName}#{candidate.Row.ClientId}#{candidate.NameScore:F1}%";
        }

        private sealed class RosterCandidate
        {
            public required MassImmunisationRosterRecord Row { get; init; }
            public double NameScore { get; init; }
            public bool DobMatch { get; init; }
        }
    }
}
