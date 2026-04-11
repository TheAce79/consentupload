using ConsentSyncCore.Models;
using ConsentSyncCore.Services;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Matching;
using ConsentSyncCore.Services.Phis;
using CsvProcessing;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
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

        public Phase1Orchestrator(IConfiguration? config = null)
        {
            _config = config ?? ConfigurationService.GetConfiguration();
            _csvRepo = new StudentCsvRepository(_config);
            _driverFactory = new ChromeDriverFactory(_config);

            _phase1Config = ConfigurationService.GetPhase1Config();
            _phisConfig = ConfigurationService.GetPhisConfig();

            // Register Ctrl+C handler for graceful shutdown
            Console.CancelKeyPress += OnShutdownRequested;
        }



        #region Public API

        /// <summary>
        /// Run the complete Phase 1 workflow
        /// </summary>
        public async Task<Phase1Result> RunAsync()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════╗");
            Console.WriteLine("║         ConsentSync - Phase 1: Search Client IDs       ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

            var result = new Phase1Result();

            try
            {
                // Step 1: Load and validate CSV
                Console.WriteLine("📋 Step 1: Loading processed CSV...");
                if (!_csvRepo.ProcessedCsvExists())
                {
                    Console.WriteLine("❌ Processed CSV not found. Please run pre-processing first.");
                    return result;
                }

                var allStudents = _csvRepo.ReadAll();
                _currentStudentList = allStudents;
                result.TotalStudents = allStudents.Count;

                Console.WriteLine($"   ✅ Loaded {allStudents.Count} students");
                _csvRepo.DisplayStatistics();

                // Step 2: Filter unprocessed students
                var unprocessedStudents = allStudents
                    .Where(s => s.ClientIdStatus == ClientIdStatus.NotProcessed)
                    .ToList();

                if (unprocessedStudents.Count == 0)
                {
                    Console.WriteLine("\n✅ All students already processed!");
                    return result;
                }

                Console.WriteLine($"\n📊 Found {unprocessedStudents.Count} students to process");
                result.ToProcessCount = unprocessedStudents.Count;

                // Step 3: Initialize browser and services
                Console.WriteLine("\n📋 Step 2: Initializing browser and services...");
                if (!InitializeServices())
                {
                    Console.WriteLine("❌ Service initialization failed");
                    return result;
                }

                // Step 4: Login to PHIS
                Console.WriteLine("\n📋 Step 3: Logging into PHIS...");
                if (!_sessionManager!.Login())
                {
                    Console.WriteLine("❌ Login failed. Cannot proceed.");
                    return result;
                }

                Console.WriteLine("✅ Login successful");

                // Step 5: Process students
                Console.WriteLine("\n📋 Step 4: Searching for Client IDs...");
                await ProcessStudentsAsync(unprocessedStudents, result);

                // Step 6: Final save
                Console.WriteLine("\n💾 Saving final results...");
                _csvRepo.SaveAll(allStudents);

                // Step 7: Display summary
                DisplaySummary(result);

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
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

                Console.WriteLine("✅ Services initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Service initialization failed: {ex.Message}");
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
            Console.WriteLine($"\n⏱️  Estimated processing time: {estimatedMinutes:F1} minutes");

            if (estimatedMinutes > _phisConfig.SessionTimeoutMinutes && !_phisConfig.SessionRefreshEnabled)
            {
                Console.WriteLine($"   ⚠️  WARNING: May exceed session timeout ({_phisConfig.SessionTimeoutMinutes} min)");
                Console.WriteLine($"   💡 Consider enabling SessionRefreshEnabled in appsettings.json");
            }
            else if (_phisConfig.SessionRefreshEnabled && estimatedMinutes > _phisConfig.SessionTimeoutMinutes)
            {
                Console.WriteLine($"   ✅ Auto-refresh enabled - session will be kept alive");
            }

            Console.WriteLine($"\n💡 TIP: Press Ctrl+C to save progress and exit gracefully\n");

            for (int i = 0; i < students.Count; i++)
            {
                if (_shutdownRequested)
                {
                    Console.WriteLine("\n⚠️  Shutdown requested - saving progress...");
                    break;
                }

                var student = students[i];

                // Display session status every 10 records
                if (i > 0 && i % 10 == 0)
                {
                    DisplaySessionStatus();
                }

                Console.WriteLine($"\n[{i + 1}/{students.Count}] Processing: {student.FirstName} {student.LastName}");

                try
                {
                    // Search for client
                    await ProcessSingleStudentAsync(student, result);

                    // Save progress periodically
                    if ((i + 1) % _phase1Config.SaveProgressEveryNRecords == 0)
                    {
                        Console.WriteLine($"\n💾 Saving progress ({i + 1}/{students.Count} processed)...");
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
                    Console.WriteLine($"   ❌ Error: {ex.Message}");
                    student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                    result.ErrorCount++;
                }
            }
        }

        /// <summary>
        /// Process a single student record
        /// </summary>
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
                    Console.WriteLine($"   ❌ Search failed: {searchResult.ErrorMessage}");
                    student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                    result.ErrorCount++;
                    return false;
                }

                if (!searchResult.HasResults)
                {
                    Console.WriteLine($"   ⚠️  No results found");
                    student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                    result.ManualReviewCount++;
                    return false;
                }

                // Find best match using fuzzy matcher
                var (bestMatch, score, suggestion) = _fuzzyMatcher!.FindBestMatch(student, searchResult.Results);

                if (bestMatch == null)
                {
                    Console.WriteLine($"   ⚠️  No confident match found");
                    student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                    student.BestMatch = suggestion ?? "";
                    result.ManualReviewCount++;
                    return false;
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
                    Console.WriteLine($"   ✅ Client ID found: {bestMatch.ClientId} (score: {score:F2}%)");
                    return true;
                }
                else
                {
                    Console.WriteLine($"   ⚠️  Score too low: {score:F2}% (threshold: {threshold}%)");
                    student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                    student.BestMatch = suggestion ?? "";
                    result.ManualReviewCount++;

                    if (!string.IsNullOrEmpty(suggestion))
                    {
                        Console.WriteLine($"   💡 Best match saved: {suggestion}");
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Processing error: {ex.Message}");
                student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                result.ErrorCount++;
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

            Console.WriteLine($"\n⏱️  Session Status:");
            Console.WriteLine($"   Time remaining: {timeRemaining.TotalMinutes:F1} minutes");
            Console.WriteLine($"   Health: {stats.PercentageRemaining:F1}%");

            if (stats.IsAboutToExpire)
            {
                Console.WriteLine($"   ⚠️  Session expiring soon!");
            }
        }

        /// <summary>
        /// Display final summary
        /// </summary>
        private void DisplaySummary(Phase1Result result)
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("📊 PHASE 1 COMPLETE - Final Summary");
            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"Total students: {result.TotalStudents}");
            Console.WriteLine($"To process: {result.ToProcessCount}");
            Console.WriteLine($"✅ Client IDs found: {result.FoundCount}");
            Console.WriteLine($"⚠️  Needs manual review: {result.ManualReviewCount}");
            Console.WriteLine($"❌ Errors: {result.ErrorCount}");
            Console.WriteLine($"📝 Total processed: {result.TotalProcessed}");
            Console.WriteLine(new string('═', 60));

            if (result.ManualReviewCount > 0)
            {
                Console.WriteLine($"\n⚠️  ACTION REQUIRED:");
                Console.WriteLine($"   {result.ManualReviewCount} students need manual Client ID assignment");
                Console.WriteLine($"   Review the CSV and fill in missing Client IDs");
                Console.WriteLine($"   Then proceed to Phase 2");
            }
            else
            {
                Console.WriteLine($"\n✅ All Client IDs found! Ready for Phase 2");
            }

            // Display updated statistics
            Console.WriteLine();
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

            Console.WriteLine("\n\n⚠️  Shutdown requested (Ctrl+C detected)");
            Console.WriteLine("💾 Saving progress before exit...");

            e.Cancel = true; // Prevent immediate termination
            _shutdownRequested = true;

            // Save current progress
            if (_currentStudentList != null)
            {
                try
                {
                    _csvRepo.SaveAll(_currentStudentList);
                    Console.WriteLine("✅ Progress saved successfully!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to save progress: {ex.Message}");
                }
            }

            Console.WriteLine("👋 Exiting safely...");
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
                Console.WriteLine("✅ ChromeDriver disposed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Cleanup warning: {ex.Message}");
            }
        }


        #endregion

    }
}
