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
        private const double AutoMatchThreshold = 85.0;
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

            string studentFullName = BuildStudentFullName(student);
            var candidates = BuildCandidates(student, studentFullName);

            var nameMatch = TryMatchByName(candidates);
            if (nameMatch.Matched)
            {
                return nameMatch;
            }

            var exactDobMatch = TryMatchByExactDob(student, candidates);
            if (exactDobMatch.Matched)
            {
                return exactDobMatch;
            }

            var invertedDobMatch = TryMatchByInvertedDob(student, candidates);
            if (invertedDobMatch.Matched)
            {
                return invertedDobMatch;
            }

            return BuildSuggestionResult(candidates);
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

        public bool TryInvertDate(string rawDate, out DateTime invertedDate)
        {
            invertedDate = default;

            DateTime? parsedDate = ParseStudentDob(rawDate);
            if (!parsedDate.HasValue)
            {
                return false;
            }

            if (parsedDate.Value.Month == parsedDate.Value.Day)
            {
                return false;
            }

            try
            {
                invertedDate = new DateTime(
                    parsedDate.Value.Year,
                    parsedDate.Value.Day,
                    parsedDate.Value.Month);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        public bool NameTokensMatch(string name1, string name2)
        {
            var tokens1 = TokenizeName(name1);
            var tokens2 = TokenizeName(name2);

            if (tokens1.Length == 0 || tokens2.Length == 0)
            {
                return false;
            }

            var set1 = new HashSet<string>(tokens1.Where(token => token.Length >= 2), StringComparer.Ordinal);
            var set2 = new HashSet<string>(tokens2.Where(token => token.Length >= 2), StringComparer.Ordinal);
            if (set1.Count == 0 || set2.Count == 0)
            {
                return false;
            }

            return set1.Overlaps(set2);
        }

        public double CalculateFuzzyScore(string studentFullName, string rosterClientName)
        {
            var (rosterFirstName, rosterLastName) = SplitRosterName(rosterClientName);
            var (studentFirstName, studentLastName) = SplitStudentName(studentFullName);

            double pairScore = _fuzzyMatcher.CalculateNameMatchScore(
                studentFirstName,
                studentLastName,
                rosterFirstName,
                rosterLastName);

            string[] studentTokens = TokenizeName(studentFullName);
            string[] rosterTokens = TokenizeName(rosterClientName);
            double tokenScore = CalculateTokenJaccard(studentTokens, rosterTokens) * 100.0;

            return Math.Max(pairScore, tokenScore);
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

        private static (string firstName, string lastName) SplitStudentName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (string.Empty, string.Empty);
            }

            var tokens = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
            string matchMethod = DetermineSuggestionMatchMethod(candidate);
            return $"{candidate.Row.ClientName} # {candidate.Row.ClientId} # {matchMethod} ({candidate.NameScore:F0}%)";
        }

        private static string BuildStudentFullName(StudentRecord student)
        {
            return $"{student.LastName} {student.FirstName}".Trim();
        }

        private List<RosterCandidate> BuildCandidates(StudentRecord student, string studentFullName)
        {
            var candidates = new List<RosterCandidate>(_roster.Count);
            DateTime? studentDob = ParseStudentDob(student.DateOfBirth);
            bool hasInvertedDob = TryInvertDate(student.DateOfBirth, out DateTime invertedDob);

            foreach (var row in _roster)
            {
                DateTime? rosterDob = ParseRosterDob(row.DateOfBirth);

                candidates.Add(new RosterCandidate
                {
                    Row = row,
                    NameScore = CalculateFuzzyScore(studentFullName, row.ClientName),
                    TokensMatch = NameTokensMatch(studentFullName, row.ClientName),
                    RosterDob = rosterDob,
                    ExactDobMatch = rosterDob.HasValue &&
                                    studentDob.HasValue &&
                                    rosterDob.Value.Date == studentDob.Value.Date,
                    InvertedDobMatch = rosterDob.HasValue &&
                                       hasInvertedDob &&
                                       rosterDob.Value.Date == invertedDob.Date
                });
            }

            return candidates;
        }

        private LocalRosterMatchResult TryMatchByName(List<RosterCandidate> candidates)
        {
            var ordered = candidates
                .OrderByDescending(candidate => candidate.NameScore)
                .ToList();

            var bestCandidate = ordered.FirstOrDefault();
            if (bestCandidate == null || bestCandidate.NameScore < AutoMatchThreshold)
            {
                return new LocalRosterMatchResult();
            }

            var secondCandidate = ordered.Skip(1).FirstOrDefault();
            bool isUniqueBest =
                secondCandidate == null ||
                bestCandidate.NameScore - secondCandidate.NameScore >= UniqueMatchGapThreshold;

            if (!isUniqueBest)
            {
                return new LocalRosterMatchResult();
            }

            return BuildMatchedResult(bestCandidate, "MassCSV_NameMatch");
        }

        private LocalRosterMatchResult TryMatchByExactDob(StudentRecord student, List<RosterCandidate> candidates)
        {
            DateTime? studentDob = ParseStudentDob(student.DateOfBirth);
            if (!studentDob.HasValue)
            {
                return new LocalRosterMatchResult();
            }

            var matches = candidates
                .Where(candidate => candidate.ExactDobMatch)
                .OrderByDescending(candidate => candidate.NameScore)
                .ToList();

            if (matches.Count == 1)
            {
                return BuildMatchedResult(matches[0], "MassCSV_DOBMatch");
            }

            var tokenMatches = matches
                .Where(candidate => candidate.TokensMatch)
                .OrderByDescending(candidate => candidate.NameScore)
                .ToList();

            if (tokenMatches.Count == 1)
            {
                return BuildMatchedResult(tokenMatches[0], "MassCSV_DOBPlusNameMatch");
            }

            return new LocalRosterMatchResult();
        }

        private LocalRosterMatchResult TryMatchByInvertedDob(StudentRecord student, List<RosterCandidate> candidates)
        {
            if (!TryInvertDate(student.DateOfBirth, out DateTime invertedDate))
            {
                return new LocalRosterMatchResult();
            }

            var match = candidates
                .Where(candidate => candidate.InvertedDobMatch && candidate.TokensMatch)
                .OrderByDescending(candidate => candidate.NameScore)
                .FirstOrDefault();

            return match != null
                ? BuildMatchedResult(match, "MassCSV_InvertedDOBMatch")
                : new LocalRosterMatchResult();
        }

        private LocalRosterMatchResult BuildSuggestionResult(List<RosterCandidate> candidates)
        {
            var suggestionCandidate = candidates
                .OrderByDescending(candidate => candidate.NameScore)
                .FirstOrDefault();

            if (suggestionCandidate == null)
            {
                return new LocalRosterMatchResult();
            }

            return new LocalRosterMatchResult
            {
                Suggestion = BuildSuggestion(suggestionCandidate),
                NameScore = suggestionCandidate.NameScore
            };
        }

        private static string DetermineSuggestionMatchMethod(RosterCandidate candidate)
        {
            if (candidate.ExactDobMatch && candidate.TokensMatch)
            {
                return "MassCSV_DOBPlusNameMatch";
            }

            if (candidate.ExactDobMatch)
            {
                return "MassCSV_DOBMatch";
            }

            if (candidate.InvertedDobMatch && candidate.TokensMatch)
            {
                return "MassCSV_InvertedDOBMatch";
            }

            return "MassCSV_NameMatch";
        }

        private static LocalRosterMatchResult BuildMatchedResult(RosterCandidate candidate, string matchMethod)
        {
            return new LocalRosterMatchResult
            {
                Matched = true,
                ClientId = candidate.Row.ClientId,
                MatchMethod = matchMethod,
                NameScore = candidate.NameScore
            };
        }

        private sealed class RosterCandidate
        {
            public required MassImmunisationRosterRecord Row { get; init; }
            public double NameScore { get; init; }
            public bool TokensMatch { get; init; }
            public DateTime? RosterDob { get; init; }
            public bool ExactDobMatch { get; init; }
            public bool InvertedDobMatch { get; init; }
        }
    }
}
