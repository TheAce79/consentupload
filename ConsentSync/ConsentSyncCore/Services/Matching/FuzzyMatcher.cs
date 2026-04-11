using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using ConsentSyncCore.Models;
using ConsentSyncCore.Services;


namespace ConsentSyncCore.Services.Matching
{
    /// <summary>
    /// Fuzzy name matching service for student records
    /// Used in Phase 1 (Client ID search) and Phase 2 (PDF validation)
    /// Supports compound names, accents, hyphens, and Medicare number verification
    /// </summary>
    public class FuzzyMatcher
    {
        private readonly FuzzyMatchingConfig _config;

        // Thresholds
        public double SingleResultThreshold => _config.SingleResultThreshold;
        public double MultipleResultsThreshold => _config.MultipleResultsThreshold;
        public double ManualReviewThreshold => _config.ManualReviewThreshold;

        public FuzzyMatcher(FuzzyMatchingConfig? config = null)
        {
            _config = config ?? ConfigurationService.GetFuzzyMatchingConfig();

            // Log configuration on startup
            Console.WriteLine($"\n🔍 Fuzzy Matcher Configuration:");
            Console.WriteLine($"   Compound name matching: {_config.TreatCompoundNamesAsPartialMatch}");
            Console.WriteLine($"   Space-separated compounds: {_config.TreatSpaceSeparatedNamesAsCompound}");
            Console.WriteLine($"   Compound match score: {_config.CompoundNameMatchScore}%");
            Console.WriteLine($"   Min compound ratio: {_config.MinimumCompoundMatchRatio:P0}");
        }


        #region Public API


            /// <summary>
            /// Calculate match score between a student and a PHIS search result
            /// Returns: (finalScore, nameScore, medicareMatch)
            /// </summary>
        public (double finalScore, double nameScore, bool medicareMatch) CalculateMatchScore(
            StudentRecord student,
            PhisSearchResult phisResult)
        {
            // Calculate name similarity (PRIMARY matching criteria)
            double nameScore = CalculateNameMatchScore(student.FirstName, student.LastName, phisResult);

            // Check Medicare number match (SECONDARY criteria)
            bool medicareMatch = false;

            if (_config.UseMedicareNumberAsConfirmation &&
                !string.IsNullOrWhiteSpace(student.MedicareNumber) &&
                !string.IsNullOrWhiteSpace(phisResult.MedicareNumber))
            {
                var csvMedicare = NormalizeMedicareNumber(student.MedicareNumber);
                var phisMedicare = NormalizeMedicareNumber(phisResult.MedicareNumber);

                medicareMatch = csvMedicare.Equals(phisMedicare, StringComparison.OrdinalIgnoreCase);
            }

            // Calculate final score
            double finalScore = nameScore;

            // Apply Medicare boost only if:
            // 1. Medicare numbers match AND
            // 2. Name score is already reasonable (>= 60%)
            if (medicareMatch && nameScore >= 60)
            {
                finalScore = Math.Min(100, nameScore + _config.MedicareNumberBoostScore);
            }

            return (finalScore, nameScore, medicareMatch);
        }




        /// <summary>
        /// Find best match from multiple PHIS results
        /// </summary>
        public (PhisSearchResult? bestMatch, double score, string? suggestion) FindBestMatch(
            StudentRecord student,
            List<PhisSearchResult> results)
        {
            if (results.Count == 0)
            {
                return (null, 0, null);
            }

            var matches = results
                .Select(r =>
                {
                    var (finalScore, nameScore, medicareMatch) = CalculateMatchScore(student, r);
                    return new { Result = r, FinalScore = finalScore, NameScore = nameScore, MedicareMatch = medicareMatch };
                })
                .OrderByDescending(m => m.FinalScore)
                .ToList();

            var best = matches.First();

            // Create suggestion string
            string suggestion = $"{best.Result.FirstName}#{best.Result.LastName}#{best.Result.ClientId}#{best.FinalScore:F1}%";

            return (best.Result, best.FinalScore, suggestion);
        }





        #endregion Public API




        #region Name Matching Logic


        /// <summary>
        /// Calculate fuzzy name match score (0-100) for StudentRecord vs PhisSearchResult
        /// </summary>
        private double CalculateNameMatchScore(
            string csvFirstName,
            string csvLastName,
            PhisSearchResult phisResult)
        {
            return CalculateNameMatchScore(csvFirstName, csvLastName, phisResult.FirstName, phisResult.LastName);
        }


        /// <summary>
        /// Calculate fuzzy name match score (0-100) between two name pairs
        /// </summary>
        private double CalculateNameMatchScore(
            string firstName1,
            string lastName1,
            string firstName2,
            string lastName2)
        {
            var normalizedFirstName1 = NormalizeName(firstName1);
            var normalizedLastName1 = NormalizeName(lastName1);
            var normalizedFirstName2 = NormalizeName(firstName2);
            var normalizedLastName2 = NormalizeName(lastName2);

            double firstNameSimilarity = CalculateNameSimilarity(normalizedFirstName1, normalizedFirstName2);
            double lastNameSimilarity = CalculateNameSimilarity(normalizedLastName1, normalizedLastName2);

            double overallScore = (lastNameSimilarity * _config.LastNameWeight) +
                                 (firstNameSimilarity * _config.FirstNameWeight);

            return overallScore * 100;
        }





        /// <summary>
        /// Calculate similarity between two names
        /// Handles compound names (hyphenated OR space-separated based on config)
        /// Examples:
        ///   - "Jean-Marie" vs "Jean" → 95%
        ///   - "Mohammad Jaabir" vs "Jaabir" → 95%
        ///   - "Emile André" vs "Emile" → 95%
        /// </summary>
        /// 
        private double CalculateNameSimilarity(string name1, string name2)
        {
            if (string.IsNullOrEmpty(name1) && string.IsNullOrEmpty(name2))
                return 1.0;

            if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
                return 0.0;

            if (name1.Equals(name2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            // Handle compound names (if enabled in config)
            if (_config.TreatCompoundNamesAsPartialMatch)
            {
                var parts1 = SplitCompoundName(name1);
                var parts2 = SplitCompoundName(name2);

                // Case 1: "Mohammad Jaabir" vs "Jaabir"
                if (parts1.Length > 1 && parts2.Length == 1)
                {
                    foreach (var part in parts1)
                    {
                        if (part.Equals(name2, StringComparison.OrdinalIgnoreCase))
                        {
                            var score = _config.CompoundNameMatchScore / 100.0;
                            Console.WriteLine($"      Compound match: '{name1}' contains '{name2}' → {score * 100:F1}%");
                            return score;
                        }
                    }
                }

                // Case 2: "Jaabir" vs "Mohammad Jaabir"
                if (parts2.Length > 1 && parts1.Length == 1)
                {
                    foreach (var part in parts2)
                    {
                        if (part.Equals(name1, StringComparison.OrdinalIgnoreCase))
                        {
                            var score = _config.CompoundNameMatchScore / 100.0;
                            Console.WriteLine($"      Compound match: '{name2}' contains '{name1}' → {score * 100:F1}%");
                            return score;
                        }
                    }
                }

                // Case 3: Both are compound - check overlap
                if (parts1.Length > 1 && parts2.Length > 1)
                {
                    int matchCount = 0;
                    int minParts = Math.Min(parts1.Length, parts2.Length);

                    for (int i = 0; i < minParts; i++)
                    {
                        if (parts1[i].Equals(parts2[i], StringComparison.OrdinalIgnoreCase))
                        {
                            matchCount++;
                        }
                    }

                    if (matchCount > 0)
                    {
                        double matchRatio = (double)matchCount / Math.Max(parts1.Length, parts2.Length);

                        if (matchRatio >= _config.MinimumCompoundMatchRatio)
                        {
                            var score = matchRatio * (_config.CompoundNameMatchScore / 100.0);
                            Console.WriteLine($"      Partial compound: {matchCount}/{Math.Max(parts1.Length, parts2.Length)} parts → {score * 100:F1}%");
                            return score;
                        }
                    }
                }
            }

            // Fallback to Levenshtein distance
            return CalculateStringSimilarity(name1, name2);
        }



        /// <summary>
        /// Split compound name by hyphens, spaces, or both
        /// Examples:
        ///   "Jean-Marie" → ["Jean", "Marie"]
        ///   "Mohammad Jaabir" → ["Mohammad", "Jaabir"]
        ///   "Marie-Claire Louise" → ["Marie", "Claire", "Louise"]
        /// </summary>
        /// <summary>
        /// Split compound name by hyphens and/or spaces (based on config)
        /// </summary>
        private string[] SplitCompoundName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Array.Empty<string>();

            // Determine delimiters based on configuration
            char[] delimiters;

            if (_config.TreatSpaceSeparatedNamesAsCompound)
            {
                // Split by both hyphens AND spaces
                delimiters = new[] { '-', ' ' };
            }
            else
            {
                // Split by hyphens only (original behavior)
                delimiters = new[] { '-' };
            }

            return name.Split(delimiters, StringSplitOptions.RemoveEmptyEntries)
                       .Select(part => part.Trim())
                       .Where(part => !string.IsNullOrEmpty(part))
                       .ToArray();
        }






        #endregion Name Matching Logic





        #region Normalization


        /// <summary>
        /// Normalize name for comparison
        /// </summary>
        private string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";

            var normalized = RemoveAccents(name.Trim().ToUpperInvariant());

            if (_config.IgnoreHyphensInComparison)
            {
                // Only remove hyphens if configured, but keep them for compound name detection
                // This is done AFTER compound name check
            }

            if (_config.IgnoreSpacesInComparison)
            {
                normalized = normalized.Replace(" ", "");
            }

            return normalized;
        }



        /// <summary>
        /// Remove accents from text (e.g., é → e)
        /// </summary>
        private string RemoveAccents(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }





        /// <summary>
        /// Normalize Medicare number by removing spaces and dashes
        /// </summary>
        private string NormalizeMedicareNumber(string medicareNumber)
        {
            if (string.IsNullOrWhiteSpace(medicareNumber))
                return "";

            return medicareNumber.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
        }





        #endregion Normalization




        #region String Similarity (Levenshtein Distance)


        /// <summary>
        /// Calculate string similarity using Levenshtein distance
        /// Returns value between 0.0 (completely different) and 1.0 (identical)
        /// </summary>
        private double CalculateStringSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(target))
                return 1.0;

            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0.0;

            int distance = CalculateLevenshteinDistance(source, target);
            int maxLength = Math.Max(source.Length, target.Length);

            return 1.0 - ((double)distance / maxLength);
        }


        /// <summary>
        /// Calculate Levenshtein distance between two strings
        /// </summary>
        private int CalculateLevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
                return target?.Length ?? 0;

            if (string.IsNullOrEmpty(target))
                return source.Length;

            int sourceLength = source.Length;
            int targetLength = target.Length;

            var distance = new int[sourceLength + 1, targetLength + 1];

            for (int i = 0; i <= sourceLength; distance[i, 0] = i++) { }
            for (int j = 0; j <= targetLength; distance[0, j] = j++) { }

            for (int i = 1; i <= sourceLength; i++)
            {
                for (int j = 1; j <= targetLength; j++)
                {
                    int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                    distance[i, j] = Math.Min(
                        Math.Min(
                            distance[i - 1, j] + 1,      // Deletion
                            distance[i, j - 1] + 1),     // Insertion
                        distance[i - 1, j - 1] + cost);  // Substitution
                }
            }

            return distance[sourceLength, targetLength];
        }









        #endregion String Similarity (Levenshtein Distance)







    }
}
