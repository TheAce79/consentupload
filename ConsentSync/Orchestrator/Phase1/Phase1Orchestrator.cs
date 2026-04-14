using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Matching;
using ConsentSyncCore.Services.Phis;
using CsvProcessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using Orchestrator.Phase3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Phase1
{
    /// <summary>
    /// Phase 1 Orchestrator: Search PHIS for Client IDs
    /// Coordinates all services to search by DOB and match with fuzzy logic
    /// </summary>
    public class Phase1Orchestrator : IDisposable
    {
        private readonly IConfiguration _config;
        private readonly StudentCsvRepository _csvRepo;
        private readonly ChromeDriverFactory _driverFactory;
        private IWebDriver? _driver;
        private PhisSessionManager? _sessionManager;
        private PhisSearchService? _searchService;
        private PhisResultExtractor? _resultExtractor;
        private FuzzyMatcher? _fuzzyMatcher;

        private readonly Phase1Config _phase1Config;
        private readonly PhisConfig _phisConfig;

        private bool _shutdownRequested = false;
        private List<StudentRecord>? _currentStudentList;
        private readonly ILogger<Phase1Orchestrator> _logger;

        public Phase1Orchestrator(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _csvRepo = new StudentCsvRepository(_config);
            _driverFactory = new ChromeDriverFactory(_config);

            _phase1Config = ConfigurationService.GetPhase1Config();
            _phisConfig = ConfigurationService.GetPhisConfig();
            _logger = LoggerService.GetLogger<Phase1Orchestrator>();

            // Register Ctrl+C handler for graceful shutdown
            Console.CancelKeyPress += OnShutdownRequested;
        }


        /// <summary>
        /// Get the WebDriver instance for reuse in other phases
        /// </summary>
        public IWebDriver? GetDriver()
        {
            return _driver;
        }

        /// <summary>
        /// Get the PhisSessionManager instance for reuse in other phases
        /// </summary>
        public PhisSessionManager? GetSessionManager()
        {
            return _sessionManager;
        }

        /// <summary>
        /// Get the PhisSearchService instance for reuse in other phases
        /// </summary>
        public PhisSearchService? GetSearchService()
        {
            return _searchService;
        }


        #region Public API

        /// <summary>
        /// Run the complete Phase 1 workflow
        /// </summary>
        public async Task<Phase1Result> RunAsync()
        {
             LoggerService.LogInformation("╔════════════════════════════════════════════════════════╗");
             LoggerService.LogInformation("║         ConsentSync - Phase 1: Search Client IDs       ║");
             LoggerService.LogInformation("╚════════════════════════════════════════════════════════╝\n");

            var result = new Phase1Result();

            try
            {
                // Step 1: Load and validate CSV
                 LoggerService.LogInformation("📋 Step 1: Loading processed CSV...");
                if (!_csvRepo.ProcessedCsvExists())
                {
                     LoggerService.LogInformation("❌ Processed CSV not found. Please run  first.");
                    return result;
                }

                var allStudents = _csvRepo.ReadAll();
                _currentStudentList = allStudents;
                result.TotalStudents = allStudents.Count;

                 LoggerService.LogInformation($"   ✅ Loaded {allStudents.Count} students");
                _csvRepo.DisplayStatistics();

                // Step 2: Filter unprocessed students
                var unprocessedStudents = allStudents
                    .Where(s => s.ClientIdStatus == ClientIdStatus.NotProcessed)
                    .ToList();

                if (unprocessedStudents.Count == 0)
                {
                     LoggerService.LogInformation("\n✅ All students already processed!");
                    return result;
                }

                 LoggerService.LogInformation($"\n📊 Found {unprocessedStudents.Count} students to process");
                result.ToProcessCount = unprocessedStudents.Count;

                // Step 3: Initialize browser and services
                 LoggerService.LogInformation("\n📋 Step 2: Initializing browser and services...");
                if (!InitializeServices())
                {
                     LoggerService.LogInformation("❌ Service initialization failed");
                    return result;
                }

                // Step 4: Login to PHIS
                 LoggerService.LogInformation("\n📋 Step 3: Logging into PHIS...");
                if (!_sessionManager!.Login())
                {
                     LoggerService.LogInformation("❌ Login failed. Cannot proceed.");
                    return result;
                }

                 LoggerService.LogInformation("✅ Login successful");

                // Step 5: Process students
                 LoggerService.LogInformation("\n📋 Step 4: Searching for Client IDs...");
                await ProcessStudentsAsync(unprocessedStudents, result);

                // Step 6: Final save
                 LoggerService.LogInformation("\n💾 Saving final results...");
                _csvRepo.SaveAll(allStudents);

                // Step 7: Display summary
                DisplaySummary(result);

                return result;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"\n❌ ERROR: {ex.Message}");
                 LoggerService.LogInformation($"Stack trace: {ex.StackTrace}");
                result.HasErrors = true;
                return result;
            }
        }

        #endregion Public API





        #region Initialization

        /// <summary>
        /// Initialize browser and all services
        /// </summary>
        private bool InitializeServices()
        {
            try
            {
                // Create WebDriver
                _driver = _driverFactory.CreateDriver();

                // Create PHIS services
                _sessionManager = new PhisSessionManager(_driver, _config);
                _resultExtractor = new PhisResultExtractor(_config);
                _searchService = new PhisSearchService(_driver, _config, _resultExtractor, _sessionManager);

                // Create fuzzy matcher
                _fuzzyMatcher = new FuzzyMatcher();

                 LoggerService.LogInformation("✅ Services initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"❌ Service initialization failed: {ex.Message}");
                return false;
            }
        }

        #endregion Initialization


        #region Student Processing

        /// <summary>
        /// Process all unprocessed students
        /// </summary>
        private async Task ProcessStudentsAsync(List<StudentRecord> students, Phase1Result result)
        {
            // Estimate completion time
            double estimatedMinutes = (students.Count * _phisConfig.DelayBetweenSearchesMs) / 60000.0;
             LoggerService.LogInformation($"\n⏱️  Estimated processing time: {estimatedMinutes:F1} minutes");

            if (estimatedMinutes > _phisConfig.SessionTimeoutMinutes && !_phisConfig.SessionRefreshEnabled)
            {
                 LoggerService.LogInformation($"   ⚠️  WARNING: May exceed session timeout ({_phisConfig.SessionTimeoutMinutes} min)");
                 LoggerService.LogInformation($"   💡 Consider enabling SessionRefreshEnabled in appsettings.json");
            }
            else if (_phisConfig.SessionRefreshEnabled && estimatedMinutes > _phisConfig.SessionTimeoutMinutes)
            {
                 LoggerService.LogInformation($"   ✅ Auto-refresh enabled - session will be kept alive");
            }

             LoggerService.LogInformation($"\n💡 TIP: Press Ctrl+C to save progress and exit gracefully\n");

            for (int i = 0; i < students.Count; i++)
            {
                if (_shutdownRequested)
                {
                     LoggerService.LogInformation("\n⚠️  Shutdown requested - saving progress...");
                    break;
                }

                var student = students[i];

                // Display session status every 10 records
                if (i > 0 && i % 10 == 0)
                {
                    DisplaySessionStatus();
                }

                 LoggerService.LogInformation($"\n[{i + 1}/{students.Count}] Processing: {student.FirstName} {student.LastName}");

                try
                {
                    // Search for client
                    await ProcessSingleStudentAsync(student, result);

                    // Save progress periodically
                    if ((i + 1) % _phase1Config.SaveProgressEveryNRecords == 0)
                    {
                         LoggerService.LogInformation($"\n💾 Saving progress ({i + 1}/{students.Count} processed)...");
                        _csvRepo.SaveAll(_currentStudentList!);
                    }

                    // Delay between searches
                    if (i < students.Count - 1)
                    {
                        await Task.Delay(_phisConfig.DelayBetweenSearchesMs);
                    }
                }
                catch (Exception ex)
                {
                     LoggerService.LogInformation($"   ❌ Error: {ex.Message}");
                    student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                    result.ErrorCount++;
                }
            }
        }




        private async Task<bool> ProcessSingleStudentAsync(StudentRecord student, Phase1Result result)
        {
            try
            {
                // Search PHIS by DOB
                var searchResult = await _searchService!.SearchByDobAsync(
                    student.DateOfBirth,
                    student.FirstName,
                    student.LastName,
                    student.MedicareNumber);

                if (!searchResult.Success)
                {
                     LoggerService.LogInformation($"   ❌ Search failed: {searchResult.ErrorMessage}");
                    student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                    result.ErrorCount++;
                    return false;
                }

                // If no results found, try fallback searches
                if (!searchResult.HasResults)
                {
                     LoggerService.LogInformation($"   ⚠️  No results found");
                    return await TryFallbackSearchesAsync(student, result);
                }

                // Find best match using fuzzy matcher
                var (bestMatch, score, suggestion) = _fuzzyMatcher!.FindBestMatch(student, searchResult.Results);

                if (bestMatch == null)
                {
                     LoggerService.LogInformation($"   ⚠️  No confident match found");
                    return await TryFallbackSearchesAsync(student, result);
                }

                // Check if score meets threshold
                var threshold = searchResult.IsSingleResult
                    ? _fuzzyMatcher.SingleResultThreshold
                    : _fuzzyMatcher.MultipleResultsThreshold;

                if (score >= threshold)
                {
                    student.ClientId = bestMatch.ClientId;
                    student.ClientIdStatus = ClientIdStatus.Found;
                    student.BestMatch = string.Empty;
                    result.FoundCount++;
                     LoggerService.LogInformation($"   ✅ Client ID found: {bestMatch.ClientId} (score: {score:F2}%)");
                    return true;
                }
                else
                {
                     LoggerService.LogInformation($"   ⚠️  Score too low: {score:F2}% (threshold: {threshold}%)");
                    // ✅ Pass original best match to fallback searches
                    return await TryFallbackSearchesAsync(student, result, suggestion, bestMatch, score);
                }
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Processing error: {ex.Message}");
                student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                result.ErrorCount++;
                return false;
            }
        }





        /// <summary>
        /// Try fallback search strategies in order:
        /// 1. Medicare number search (if available)
        /// 2. Inverted date search (if no Medicare number)
        /// </summary>
        private async Task<bool> TryFallbackSearchesAsync(
            StudentRecord student,
            Phase1Result result,
            string? originalSuggestion = null,
            PhisSearchResult? originalBestMatch = null,
            double originalBestScore = 0.0)
        {
            // Strategy 1: Try Medicare search if available
            if (!string.IsNullOrWhiteSpace(student.MedicareNumber))
            {
                 LoggerService.LogInformation($"   🔄 Trying Medicare search...");
                var medicareSuccess = await TryMedicareSearchAsync(student, result);
                if (medicareSuccess)
                {
                    return true;
                }
            }

            // Strategy 2: Try inverted date - NOW passing original best match!
             LoggerService.LogInformation($"   🔄 Trying inverted date search...");
            var invertedSuccess = await TryInvertedDateSearchAsync(student, result, originalBestMatch, originalBestScore);
            if (invertedSuccess)
            {
                return true;
            }

            // All fallback strategies failed - mark for manual review
            student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
            student.BestMatch = originalSuggestion ?? "";
            result.ManualReviewCount++;

            if (!string.IsNullOrEmpty(originalSuggestion))
            {
                 LoggerService.LogInformation($"   💡 Best match saved: {originalSuggestion}");
            }

            return false;
        }



        /// <summary>
        /// Try searching by Medicare number as fallback
        /// </summary>
        private async Task<bool> TryMedicareSearchAsync(StudentRecord student, Phase1Result result)
        {
            try
            {
                var medicareResult = await _searchService!.SearchByMedicareAsync(student.MedicareNumber!);

                if (!medicareResult.Success)
                {
                     LoggerService.LogInformation($"   ❌ Medicare search failed: {medicareResult.ErrorMessage}");
                    return false;
                }

                if (!medicareResult.HasResults)
                {
                     LoggerService.LogInformation($"   ⚠️  No results found by Medicare number");
                    return false;
                }

                // Medicare search should return exact matches
                if (medicareResult.IsSingleResult)
                {
                    var match = medicareResult.FirstResult!;

                    // Verify name similarity for safety
                    var nameScore = _fuzzyMatcher!.CalculateNameMatchScore(
                        student.FirstName,
                        student.LastName,
                        match.FirstName,
                        match.LastName);

                    // Use a lower threshold for Medicare matches since the number itself is a strong identifier
                    if (nameScore >= 50.0) // 50% threshold for Medicare-based matches
                    {
                        student.ClientId = match.ClientId;
                        student.ClientIdStatus = ClientIdStatus.Found;
                        student.BestMatch = string.Empty;
                        result.FoundCount++;
                         LoggerService.LogInformation($"   ✅ Client ID found via Medicare: {match.ClientId} (name match: {nameScore:F2}%)");
                        return true;
                    }
                    else
                    {
                         LoggerService.LogInformation($"   ⚠️  Medicare match found but name mismatch (score: {nameScore:F2}%)");
                        return false;
                    }
                }
                else
                {
                     LoggerService.LogInformation($"   ⚠️  Multiple results ({medicareResult.Results.Count}) found by Medicare");
                    return false;
                }
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Medicare search error: {ex.Message}");
                return false;
            }
        }





        /// <summary>
        /// Try searching with inverted date (MM/DD swapped to DD/MM)
        /// Handles cases where date was incorrectly entered (e.g., 2012-12-10 should be 2012-10-12)
        /// NOW: Selects HIGHEST confidence match from BOTH original and inverted searches
        /// </summary>
        private async Task<bool> TryInvertedDateSearchAsync(
            StudentRecord student,
            Phase1Result result,
            PhisSearchResult? originalBestMatch = null,
            double originalBestScore = 0.0)
        {
            try
            {
                // Parse the original date
                if (!DateTime.TryParseExact(student.DateOfBirth, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime originalDate))
                {
                     LoggerService.LogInformation($"   ⚠️  Cannot parse date for inversion: {student.DateOfBirth}");
                    return false;
                }

                // Create inverted date by swapping day and month
                var invertedDate = new DateTime(originalDate.Year, originalDate.Day, originalDate.Month);
                var invertedDateString = invertedDate.ToString("yyyy-MM-dd");

                // Don't search if inverted date is the same (e.g., 2012-10-10)
                if (invertedDateString == student.DateOfBirth)
                {
                     LoggerService.LogInformation($"   ℹ️  Date is symmetric, skipping inversion");
                    return false;
                }

                 LoggerService.LogInformation($"   📅 Original: {student.DateOfBirth} → Inverted: {invertedDateString}");

                // Search with inverted date
                var searchResult = await _searchService!.SearchByDobAsync(
                    invertedDateString,
                    student.FirstName,
                    student.LastName,
                    student.MedicareNumber);

                if (!searchResult.Success || !searchResult.HasResults)
                {
                     LoggerService.LogInformation($"   ⚠️  No results with inverted date");
                    return false;
                }

                 LoggerService.LogInformation($"   📊 Found {searchResult.Results.Count} result(s) with inverted date");

                // ✅ KEY FIX: Evaluate ALL results from inverted search
                var invertedMatches = searchResult.Results
                    .Select(r =>
                    {
                        var (finalScore, nameScore, medicareMatch) = _fuzzyMatcher!.CalculateMatchScore(student, r);
                        return new { Result = r, FinalScore = finalScore, NameScore = nameScore, MedicareMatch = medicareMatch };
                    })
                    .OrderByDescending(m => m.FinalScore)
                    .ToList();

                var bestInvertedMatch = invertedMatches.FirstOrDefault();

                if (bestInvertedMatch == null)
                {
                     LoggerService.LogInformation($"   ⚠️  No confident match with inverted date");
                    return false;
                }

                 LoggerService.LogInformation($"   🔍 Best inverted match: {bestInvertedMatch.Result.FirstName} {bestInvertedMatch.Result.LastName}");
                 LoggerService.LogInformation($"      Score: {bestInvertedMatch.FinalScore:F2}% (Name: {bestInvertedMatch.NameScore:F2}%)");

                // ✅ CRITICAL: Compare with original search's best match
                double compareScore = originalBestScore;
                PhisSearchResult? compareMatch = originalBestMatch;

                 LoggerService.LogInformation($"\n   📊 Comparing results:");
                 LoggerService.LogInformation($"      Original date best: {(compareMatch != null ? $"{compareMatch.FirstName} {compareMatch.LastName} - {compareScore:F2}%" : "None")}");
                 LoggerService.LogInformation($"      Inverted date best: {bestInvertedMatch.Result.FirstName} {bestInvertedMatch.Result.LastName} - {bestInvertedMatch.FinalScore:F2}%");

                // Select the HIGHEST confidence match regardless of which search found it
                var threshold = searchResult.IsSingleResult
                    ? _fuzzyMatcher.SingleResultThreshold
                    : _fuzzyMatcher.MultipleResultsThreshold;

                // Use the better of the two matches
                if (bestInvertedMatch.FinalScore > compareScore)
                {
                     LoggerService.LogInformation($"   ✅ Inverted date match is BETTER ({bestInvertedMatch.FinalScore:F2}% vs {compareScore:F2}%)");

                    if (bestInvertedMatch.FinalScore >= threshold)
                    {
                        student.ClientId = bestInvertedMatch.Result.ClientId;
                        student.ClientIdStatus = ClientIdStatus.Found;
                        student.BestMatch = string.Empty;
                        result.FoundCount++;
                         LoggerService.LogInformation($"   ✅ Client ID found with INVERTED date: {bestInvertedMatch.Result.ClientId} (score: {bestInvertedMatch.FinalScore:F2}%)");
                         LoggerService.LogInformation($"   ⚠️  NOTE: Date in CSV may be incorrect! Original: {student.DateOfBirth}, Worked: {invertedDateString}");
                        return true;
                    }
                    else
                    {
                         LoggerService.LogInformation($"   ⚠️  Score still too low: {bestInvertedMatch.FinalScore:F2}% (threshold: {threshold}%)");

                        // Save best match info for manual review
                        string suggestion = $"{bestInvertedMatch.Result.FirstName}#{bestInvertedMatch.Result.LastName}#{bestInvertedMatch.Result.ClientId}#{bestInvertedMatch.FinalScore:F1}%";
                        student.BestMatch = suggestion;
                         LoggerService.LogInformation($"   💡 Best match saved: {suggestion}");
                        return false;
                    }
                }
                else
                {
                     LoggerService.LogInformation($"   ℹ️  Original date match is better ({compareScore:F2}% vs {bestInvertedMatch.FinalScore:F2}%)");

                    // Original was better, but did it meet the threshold?
                    if (compareMatch != null && compareScore >= threshold)
                    {
                        student.ClientId = compareMatch.ClientId;
                        student.ClientIdStatus = ClientIdStatus.Found;
                        student.BestMatch = string.Empty;
                        result.FoundCount++;
                         LoggerService.LogInformation($"   ✅ Using original date match: {compareMatch.ClientId} (score: {compareScore:F2}%)");
                        return true;
                    }
                    else
                    {
                         LoggerService.LogInformation($"   ⚠️  Neither match meets threshold ({threshold}%)");

                        // Save the better match for manual review
                        if (compareMatch != null)
                        {
                            string suggestion = $"{compareMatch.FirstName}#{compareMatch.LastName}#{compareMatch.ClientId}#{compareScore:F1}%";
                            student.BestMatch = suggestion;
                             LoggerService.LogInformation($"   💡 Best match saved: {suggestion}");
                        }
                        return false;
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid date combination (e.g., trying to create Feb 30th)
                 LoggerService.LogInformation($"   ⚠️  Date inversion creates invalid date");
                return false;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Inverted date search error: {ex.Message}");
                return false;
            }
        }





        #endregion Student Processing



        #region Display & Status

        /// <summary>
        /// Display session status
        /// </summary>
        private void DisplaySessionStatus()
        {
            if (_sessionManager == null) return;

            var stats = _sessionManager.GetStatistics();
            var timeRemaining = stats.TimeUntilTimeout;

             LoggerService.LogInformation($"\n⏱️  Session Status:");
             LoggerService.LogInformation($"   Time remaining: {timeRemaining.TotalMinutes:F1} minutes");
             LoggerService.LogInformation($"   Health: {stats.PercentageRemaining:F1}%");

            if (stats.IsAboutToExpire)
            {
                 LoggerService.LogInformation($"   ⚠️  Session expiring soon!");
            }
        }

        /// <summary>
        /// Display final summary
        /// </summary>
        private void DisplaySummary(Phase1Result result)
        {
             LoggerService.LogInformation("\n" + new string('═', 60));
             LoggerService.LogInformation("📊 PHASE 1 COMPLETE - Final Summary");
             LoggerService.LogInformation(new string('═', 60));
             LoggerService.LogInformation($"Total students: {result.TotalStudents}");
             LoggerService.LogInformation($"To process: {result.ToProcessCount}");
             LoggerService.LogInformation($"✅ Client IDs found: {result.FoundCount}");
             LoggerService.LogInformation($"⚠️  Needs manual review: {result.ManualReviewCount}");
             LoggerService.LogInformation($"❌ Errors: {result.ErrorCount}");
             LoggerService.LogInformation($"📝 Total processed: {result.TotalProcessed}");
             LoggerService.LogInformation(new string('═', 60));

            if (result.ManualReviewCount > 0)
            {
                 LoggerService.LogInformation($"\n⚠️  ACTION REQUIRED:");
                 LoggerService.LogInformation($"   {result.ManualReviewCount} students need manual Client ID assignment");
                 LoggerService.LogInformation($"   Review the CSV and fill in missing Client IDs");
                 LoggerService.LogInformation($"   Then proceed to Phase 2");
            }
            else
            {
                 LoggerService.LogInformation($"\n✅ All Client IDs found! Ready for Phase 2");
            }

            // Display updated statistics
             LoggerService.LogInformation("========Display updated statistics==========");
            _csvRepo.DisplayStatistics();
        }

        #endregion Display & Status



        #region Shutdown Handling

        /// <summary>
        /// Handle Ctrl+C gracefully
        /// </summary>
        /// 

        private void OnShutdownRequested(object? sender, ConsoleCancelEventArgs e)
        {
            if (_shutdownRequested) return;

             LoggerService.LogInformation("\n\n⚠️  Shutdown requested (Ctrl+C detected)");
             LoggerService.LogInformation("💾 Saving progress before exit...");

            e.Cancel = true; // Prevent immediate termination
            _shutdownRequested = true;

            // Save current progress
            if (_currentStudentList != null)
            {
                try
                {
                    _csvRepo.SaveAll(_currentStudentList);
                     LoggerService.LogInformation("✅ Progress saved successfully!");
                }
                catch (Exception ex)
                {
                     LoggerService.LogInformation($"❌ Failed to save progress: {ex.Message}");
                }
            }

             LoggerService.LogInformation("👋 Exiting safely...");
            Dispose();
            Environment.Exit(0);
        }


        #endregion



        #region IDisposable

        public void Dispose()
        {
            Console.CancelKeyPress -= OnShutdownRequested;

            try
            {
                _driver?.Quit();
                _driver?.Dispose();
                 LoggerService.LogInformation("✅ ChromeDriver disposed");
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"⚠️  Cleanup warning: {ex.Message}");
            }
        }


        #endregion

    }
}
