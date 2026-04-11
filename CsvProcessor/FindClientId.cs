using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace CsvProcessor
{
    /// <summary>
    /// Status of Client ID search for a student record
    /// </summary>
    public enum ClientIdStatus
    {
        /// <summary>Not yet searched</summary>
        NotProcessed = 0,

        /// <summary>Client ID found successfully</summary>
        Found = 1,

        /// <summary>Error occurred or no match found - needs manual review</summary>
        NeedsManualReview = 2
    }

    public class FindClientId : IDisposable
    {

        private readonly IWebDriver _driver;
        private readonly IConfiguration _config;
        private readonly string _processedCsvPath;
        private readonly WebDriverWait _wait;
        private readonly int _delayBetweenSearchesMs;
        private readonly int _pageLoadDelayMs;
        private readonly int _waitSeconds;
        private readonly string _username;
        private readonly string _password;
        private readonly bool _manualLoginMode;
        private readonly int _manualLoginWaitSeconds;

        // ✅ ADD THIS
        private readonly int _saveProgressEveryNRecords;


        // ✅ ADD SESSION TIMEOUT TRACKING
        private readonly int _sessionTimeoutMinutes;
        private readonly bool _sessionRefreshEnabled;
        private DateTime _lastSessionActivity;

        // Fuzzy matching configuration
        private readonly bool _fuzzyMatchingEnabled;
        private readonly double _singleResultThreshold;
        private readonly double _multipleResultsThreshold;
        private readonly double _manualReviewThreshold;
        private readonly double _lastNameWeight;
        private readonly double _firstNameWeight;
        private readonly bool _ignoreHyphens;
        private readonly bool _ignoreSpaces;
        private readonly bool _treatCompoundNamesAsPartialMatch;
        private readonly bool _useMedicareNumberAsConfirmation;
        private readonly double _medicareNumberBoostScore;

        // ADD nullable fields that get initialized lazily
        private int? _firstNameIdx;
        private int? _lastNameIdx;
        private int? _medicareIdx;
        private int? _clientIdIdx;
        private bool _columnIndicesInitialized = false;

        private List<StudentRecord>? _currentStudentList; // ✅ ADD THIS
        private bool _shutdownRequested = false;          // ✅ ADD THIS

        public FindClientId(IConfiguration config)
        {
            _config = config;

            // Load PHIS configuration
            _processedCsvPath = Path.Combine(
                _config["CsvProcessing:OutputCsvPath"] ?? "",
                _config["CsvProcessing:OutputCsvFileName"] ?? ""
            );


            // ✅ ADD THIS
            _saveProgressEveryNRecords = _config.GetValue<int>("CsvProcessing:SaveProgressEveryNRecords", 1);

            _username = _config["PhisAutomation:Username"] ?? "";
            _password = _config["PhisAutomation:Password"] ?? "";

            // ✅ LOAD SESSION TIMEOUT CONFIGURATION
            _sessionTimeoutMinutes = _config.GetValue<int>("PhisAutomation:SessionTimeoutMinutes", 20);
            _sessionRefreshEnabled = _config.GetValue<bool>("PhisAutomation:SessionRefreshEnabled", true);
            _lastSessionActivity = DateTime.Now;


            // Load timing configurations
            _waitSeconds = _config.GetValue<int>("PhisAutomation:WebDriverWaitSeconds", 10);
            _delayBetweenSearchesMs = _config.GetValue<int>("PhisAutomation:DelayBetweenSearchesMs", 1000);
            _pageLoadDelayMs = _config.GetValue<int>("PhisAutomation:PageLoadDelayMs", 2000);
            _manualLoginMode = _config.GetValue<bool>("PhisAutomation:ManualLoginMode", false);
            _manualLoginWaitSeconds = _config.GetValue<int>("PhisAutomation:ManualLoginWaitSeconds", 60);

            // Load fuzzy matching configuration
            _fuzzyMatchingEnabled = _config.GetValue<bool>("PhisAutomation:FuzzyMatching:Enabled", true);
            _singleResultThreshold = _config.GetValue<double>("PhisAutomation:FuzzyMatching:SingleResultThreshold", 75.0);
            _multipleResultsThreshold = _config.GetValue<double>("PhisAutomation:FuzzyMatching:MultipleResultsThreshold", 85.0);
            _manualReviewThreshold = _config.GetValue<double>("PhisAutomation:FuzzyMatching:ManualReviewThreshold", 70.0);
            _lastNameWeight = _config.GetValue<double>("PhisAutomation:FuzzyMatching:LastNameWeight", 0.6);
            _firstNameWeight = _config.GetValue<double>("PhisAutomation:FuzzyMatching:FirstNameWeight", 0.4);
            _ignoreHyphens = _config.GetValue<bool>("PhisAutomation:FuzzyMatching:IgnoreHyphensInComparison", true);
            _ignoreSpaces = _config.GetValue<bool>("PhisAutomation:FuzzyMatching:IgnoreSpacesInComparison", true);
            _treatCompoundNamesAsPartialMatch = _config.GetValue<bool>("PhisAutomation:FuzzyMatching:TreatCompoundNamesAsPartialMatch", true);
            _useMedicareNumberAsConfirmation = _config.GetValue<bool>("PhisAutomation:FuzzyMatching:UseMedicareNumberAsConfirmation", true);
            _medicareNumberBoostScore = _config.GetValue<double>("PhisAutomation:FuzzyMatching:MedicareNumberBoostScore", 20.0);

            Console.WriteLine($"⏱️  Selenium timing configuration:");
            Console.WriteLine($"   WebDriver wait: {_waitSeconds} seconds");
            Console.WriteLine($"   Delay between searches: {_delayBetweenSearchesMs} ms");
            Console.WriteLine($"   Page load delay: {_pageLoadDelayMs} ms");
            Console.WriteLine($"   Manual login mode: {(_manualLoginMode ? "ENABLED ✋" : "Disabled")}");
            if (_manualLoginMode)
            {
                Console.WriteLine($"   Manual login wait time: {_manualLoginWaitSeconds} seconds");
            }


            // ✅ DISPLAY SESSION TIMEOUT CONFIGURATION
            Console.WriteLine($"\n🔐 Session management:");
            Console.WriteLine($"   Session timeout: {_sessionTimeoutMinutes} minutes");
            Console.WriteLine($"   Auto-refresh: {(_sessionRefreshEnabled ? "Enabled ✅" : "Disabled ❌")}");


            // ✅ ADD THIS
            Console.WriteLine($"\n💾 Progress saving configuration:");
            Console.WriteLine($"   Save CSV every: {_saveProgressEveryNRecords} record(s)");
            Console.WriteLine($"   Atomic file replacement: Enabled ✅");

            Console.WriteLine($"\n🔍 Fuzzy matching configuration:");
            Console.WriteLine($"   Enabled: {_fuzzyMatchingEnabled}");
            Console.WriteLine($"   Single result threshold: {_singleResultThreshold}%");
            Console.WriteLine($"   Multiple results threshold: {_multipleResultsThreshold}%");
            Console.WriteLine($"   Manual review threshold: {_manualReviewThreshold}%");
            Console.WriteLine($"   Last name weight: {_lastNameWeight:P0}");
            Console.WriteLine($"   First name weight: {_firstNameWeight:P0}");
            Console.WriteLine($"   Ignore hyphens: {_ignoreHyphens}");
            Console.WriteLine($"   Ignore spaces: {_ignoreSpaces}");
            Console.WriteLine($"   Compound name matching: {_treatCompoundNamesAsPartialMatch}");
            Console.WriteLine($"   Use Medicare # verification: {_useMedicareNumberAsConfirmation}");
            Console.WriteLine($"   Medicare # boost score: +{_medicareNumberBoostScore}%");

            // ═══════════════════════════════════════════════════════════════
            // CHROME DRIVER CONFIGURATION (Portable Chrome Support)
            // ═══════════════════════════════════════════════════════════════

            var chromeOptions = new ChromeOptions();

            // Load Chrome configuration
            bool usePortableChrome = _config.GetValue<bool>("PhisAutomation:ChromeDriver:UsePortableChrome", false);
            string portableChromePath = _config["PhisAutomation:ChromeDriver:PortableChromePath"] ?? "";
            string chromeDriverPath = _config["PhisAutomation:ChromeDriver:ChromeDriverPath"] ?? "";
            bool useDebuggerMode = _config.GetValue<bool>("PhisAutomation:ChromeDriver:UseDebuggerMode", false);
            int debuggerPort = _config.GetValue<int>("PhisAutomation:ChromeDriver:DebuggerPort", 9222);

            Console.WriteLine($"\n🌐 Chrome Driver configuration:");
            Console.WriteLine($"   Use portable Chrome: {usePortableChrome}");

            // 1. Set portable Chrome executable path if configured
            if (usePortableChrome && !string.IsNullOrWhiteSpace(portableChromePath))
            {
                if (File.Exists(portableChromePath))
                {
                    chromeOptions.BinaryLocation = portableChromePath;
                    Console.WriteLine($"   ✅ Portable Chrome path: {portableChromePath}");
                }
                else
                {
                    Console.WriteLine($"   ⚠️  Portable Chrome not found at: {portableChromePath}");
                    Console.WriteLine($"   ℹ️  Falling back to system Chrome");
                }
            }
            else
            {
                Console.WriteLine($"   Using system Chrome installation");
            }

            // 2. Debugger mode for attaching to existing Chrome instance (advanced)
            if (useDebuggerMode)
            {
                chromeOptions.DebuggerAddress = $"127.0.0.1:{debuggerPort}";
                Console.WriteLine($"   🔌 Debugger mode enabled on port {debuggerPort}");
                Console.WriteLine($"   ℹ️  Start Chrome manually with: chrome.exe --remote-debugging-port={debuggerPort}");
            }

            // Standard Chrome options
            chromeOptions.AddArgument("--start-maximized");
            chromeOptions.AddArgument("--disable-notifications");
            chromeOptions.AddArgument("--disable-popup-blocking");
            chromeOptions.AddArgument("--disable-blink-features=AutomationControlled"); // Hide automation detection

            // Suppress DevTools listening message
            chromeOptions.AddExcludedArgument("enable-logging");

            // 3. Initialize ChromeDriver with custom path if specified
            try
            {
                if (!string.IsNullOrWhiteSpace(chromeDriverPath) && Directory.Exists(chromeDriverPath))
                {
                    Console.WriteLine($"   ChromeDriver path: {chromeDriverPath}");
                    _driver = new ChromeDriver(chromeDriverPath, chromeOptions);
                }
                else
                {
                    // Use default path (current directory or PATH environment variable)
                    string defaultPath = AppDomain.CurrentDomain.BaseDirectory;
                    Console.WriteLine($"   ChromeDriver path: {defaultPath} (default)");
                    _driver = new ChromeDriver(defaultPath, chromeOptions);
                }

                _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(_waitSeconds));
                Console.WriteLine("✅ Selenium WebDriver initialized successfully\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to initialize ChromeDriver: {ex.Message}");
                Console.WriteLine($"\n💡 TROUBLESHOOTING TIPS:");
                Console.WriteLine($"   1. Ensure chromedriver.exe is in the same folder as your .exe");
                Console.WriteLine($"   2. Download from: https://googlechromelabs.github.io/chrome-for-testing/");
                Console.WriteLine($"   3. ChromeDriver version must match your Chrome version");
                Console.WriteLine($"   4. Set ChromeDriverPath in appsettings.json if using custom location");
                throw;
            }


            // ✅ ADD THIS: Register Ctrl+C handler
            Console.CancelKeyPress += OnShutdownRequested;

            // ✅ ADD THIS: Register Ctrl+C handler
            Console.CancelKeyPress += OnShutdownRequested;

            // ✅ ADD THIS: Handle application exit
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }


        /// <summary>
        /// Initiates login - either automated or waits for manual login
        /// </summary>
        public bool InitiateLogin()
        {
            if (_manualLoginMode)
            {
                return WaitForManualLogin();
            }
            else
            {
                return Login();
            }
        }


        /// <summary>
        /// Waits for user to manually log in to PHIS
        /// </summary>
        private bool WaitForManualLogin()
        {
            try
            {
                Console.WriteLine("\n👤 MANUAL LOGIN MODE");
                Console.WriteLine("══════════════════════════════════════════════════════");

                string loginUrl = _config["PhisAutomation:LoginUrl"] ?? "https://phis.example.com/login";
                _driver.Navigate().GoToUrl(loginUrl);

                Console.WriteLine($"📌 Browser opened to: {loginUrl}");
                Console.WriteLine($"\n⏳ Please log in manually within {_manualLoginWaitSeconds} seconds...");
                Console.WriteLine("   The automation will start once you're logged in.");
                Console.WriteLine("\n💡 TIP: Navigate to the PHIS dashboard after logging in.");
                Console.WriteLine("══════════════════════════════════════════════════════\n");

                // Wait for user to complete login
                // We'll check if we're no longer on the login page
                var endTime = DateTime.Now.AddSeconds(_manualLoginWaitSeconds);
                bool loggedIn = false;

                while (DateTime.Now < endTime && !loggedIn)
                {
                    Thread.Sleep(2000); // Check every 2 seconds

                    var currentUrl = _driver.Url;

                    // Check if we've moved away from login page
                    // Adjust this condition based on your PHIS system
                    if (!currentUrl.Contains("login", StringComparison.OrdinalIgnoreCase) &&
                        !currentUrl.Contains("signin", StringComparison.OrdinalIgnoreCase))
                    {
                        loggedIn = true;
                        Console.WriteLine($"✅ Login detected! Current URL: {currentUrl}");
                        Console.WriteLine("🚀 Starting automation...\n");
                        Thread.Sleep(2000); // Give page time to fully load
                        break;
                    }

                    var remaining = (int)(endTime - DateTime.Now).TotalSeconds;
                    if (remaining % 10 == 0 && remaining > 0) // Show countdown every 10 seconds
                    {
                        Console.WriteLine($"   ⏰ {remaining} seconds remaining...");
                    }
                }

                if (!loggedIn)
                {
                    Console.WriteLine($"❌ Login timeout - no login detected within {_manualLoginWaitSeconds} seconds");
                    Console.WriteLine("   Please restart and log in more quickly, or increase ManualLoginWaitSeconds in config.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Manual login failed: {ex.Message}");
                return false;
            }
        }



        private bool Login()
        {
            try
            {
                Console.WriteLine("\n🔐 Logging into PHIS (Automated)...");

                if (string.IsNullOrWhiteSpace(_username) || string.IsNullOrWhiteSpace(_password))
                {
                    throw new InvalidOperationException("Username or password not configured for automated login");
                }

                string loginUrl = _config["PhisAutomation:LoginUrl"] ?? "https://phis.example.com/login";
                _driver.Navigate().GoToUrl(loginUrl);

                _wait.Until(d => d.FindElement(By.Id("username")));

                _driver.FindElement(By.Id("username")).SendKeys(_username);
                _driver.FindElement(By.Id("password")).SendKeys(_password);
                _driver.FindElement(By.Id("loginButton")).Click();

                Thread.Sleep(3000);

                Console.WriteLine("✅ Successfully logged in");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Login failed: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Searches for a client by DOB and returns match result
        /// Returns: (clientId, bestMatchInfo)
        /// </summary>
        /// 
        /// <summary>
        /// Searches for a client by DOB and returns match result
        /// Returns: (clientId, bestMatchInfo)
        /// </summary>
        private async Task<(string? clientId, string? bestMatchInfo)> SearchClientByDob(StudentRecord student)
        {
            try
            {
                // ✅ CHECK SESSION VALIDITY BEFORE SEARCH
                if (!EnsureSessionValid())
                {
                    Console.WriteLine($"   ❌ Session validation failed - skipping search");
                    return (null, null);
                }

                Console.WriteLine($"   🔍 Searching with DOB: {student.DateOfBirth}");
                Console.WriteLine($"      Looking for: {student.FirstName} {student.LastName}");
                if (!string.IsNullOrWhiteSpace(student.MedicareNumber))
                {
                    Console.WriteLine($"      Medicare #: {student.MedicareNumber} (available for verification)");
                }
                else
                {
                    Console.WriteLine($"      Medicare #: Not available - using name matching only");
                }

                // ✅ USE NEW METHOD TO PERFORM SEARCH AND GET RESULTS
                string xmlResponse;
                try
                {
                    xmlResponse = await PerformDobSearchAndGetResponse(student.DateOfBirth);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Session expired"))
                {
                    Console.WriteLine($"   ❌ Session expired during search");
                    return (null, null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Search failed: {ex.Message}");
                    return (null, null);
                }

                // ✅ UPDATE SESSION ACTIVITY AFTER SEARCH
                UpdateSessionActivity();

                // ✅ PARSE THE RESULTS FROM THE PAGE SOURCE
                var resultRows = _driver.FindElements(By.CssSelector("tbody[id*='dataTable_data'] tr[data-rk]"));

                if (resultRows.Count == 0)
                {
                    var infoMessages = _driver.FindElements(By.CssSelector(".ui-messages-info-detail, .ui-messages-warn-detail"));
                    if (infoMessages.Count > 0)
                    {
                        Console.WriteLine($"   ⚠️  No results found: {infoMessages[0].Text}");
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️  No results found for DOB: {student.DateOfBirth}");
                    }

                    UpdateSessionActivity();
                    return (null, null);
                }

                // ✅ INITIALIZE COLUMN INDICES ON FIRST SEARCH
                if (!_columnIndicesInitialized)
                {
                    InitializeColumnIndices();
                }

                if (resultRows.Count == 1)
                {
                    // Single result - verify with name matching (Medicare as secondary confirmation)
                    var match = ExtractResultRowData(resultRows[0]);
                    var (finalScore, nameScore, medicareMatch) = CalculateMatchScore(student, match);

                    Console.WriteLine($"   📊 Single result found:");
                    Console.WriteLine($"      PHIS: {match.FirstName} {match.LastName} | Medicare: {match.MedicareNumber ?? "N/A"}");
                    Console.WriteLine($"      CSV:  {student.FirstName} {student.LastName} | Medicare: {student.MedicareNumber ?? "N/A"}");
                    Console.WriteLine($"      Name match score: {nameScore:F2}%");

                    // Show Medicare status
                    if (medicareMatch)
                    {
                        Console.WriteLine($"      ✅ Medicare number MATCH (confidence boost: +{_medicareNumberBoostScore}%)");
                    }
                    else if (!string.IsNullOrWhiteSpace(student.MedicareNumber) && !string.IsNullOrWhiteSpace(match.MedicareNumber))
                    {
                        Console.WriteLine($"      ⚠️  Medicare number MISMATCH (possible data entry error)");
                    }
                    else if (string.IsNullOrWhiteSpace(student.MedicareNumber))
                    {
                        Console.WriteLine($"      ℹ️  Medicare # not available in CSV - relying on name match");
                    }

                    Console.WriteLine($"      Final score: {finalScore:F2}%");

                    // Format best match info
                    string bestMatchInfo = $"{match.FirstName}#{match.LastName}#{match.ClientId}#{finalScore:F1}%";

                    UpdateSessionActivity();

                    // Decision logic: Name score is primary, Medicare is confidence booster
                    if (nameScore >= _singleResultThreshold)
                    {
                        Console.WriteLine($"   ✅ Confirmed match by NAME - Client ID: {match.ClientId}");
                        return (match.ClientId, null);
                    }
                    else if (medicareMatch && nameScore >= 60)
                    {
                        Console.WriteLine($"   ✅ Confirmed match by MEDICARE (name score marginal: {nameScore:F2}%) - Client ID: {match.ClientId}");
                        return (match.ClientId, null);
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️  Score too low ({finalScore:F2}% < {_singleResultThreshold}%) - needs manual review");
                        Console.WriteLine($"   💡 Best match suggestion: {bestMatchInfo}");
                        return (null, bestMatchInfo);
                    }
                }
                else
                {
                    // Multiple results - prioritize name matching, use Medicare as tiebreaker
                    Console.WriteLine($"   ⚠️  Multiple results found ({resultRows.Count}), applying smart matching...");

                    var matches = new List<(PhisSearchResult result, double finalScore, double nameScore, bool medicareMatch)>();

                    for (int i = 0; i < Math.Min(resultRows.Count, 10); i++)
                    {
                        try
                        {
                            var result = ExtractResultRowData(resultRows[i]);
                            var (finalScore, nameScore, medicareMatch) = CalculateMatchScore(student, result);
                            matches.Add((result, finalScore, nameScore, medicareMatch));

                            string medicareStatus = medicareMatch ? "✅ Medicare Match" :
                                (!string.IsNullOrWhiteSpace(student.MedicareNumber) && !string.IsNullOrWhiteSpace(result.MedicareNumber) ? "❌ Medicare Mismatch" : "⚪ No Medicare");

                            Console.WriteLine($"      {i + 1}. ID: {result.ClientId} | {result.FirstName} {result.LastName}");
                            Console.WriteLine($"         Name Score: {nameScore:F2}% | {medicareStatus} | Final: {finalScore:F2}%");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"      ⚠️  Could not extract row {i + 1}: {ex.Message}");
                        }
                    }

                    if (matches.Count == 0)
                    {
                        Console.WriteLine($"   ⚠️  Could not extract any valid results - manual review needed");
                        UpdateSessionActivity();
                        return (null, null);
                    }

                    // Sort by NAME SCORE first (primary criteria)
                    var sortedByName = matches.OrderByDescending(m => m.nameScore).ToList();
                    var bestNameMatch = sortedByName.First();

                    // Format best match info for the top candidate
                    string bestMatchInfo = $"{bestNameMatch.result.FirstName}#{bestNameMatch.result.LastName}#{bestNameMatch.result.ClientId}#{bestNameMatch.finalScore:F1}%";

                    // Check if we have a Medicare match
                    var medicareMatches = matches.Where(m => m.medicareMatch).ToList();
                    var bestMedicareMatch = medicareMatches.OrderByDescending(m => m.nameScore).FirstOrDefault();

                    UpdateSessionActivity();

                    // Strategy 1: Strong name match (>= 85% by default)
                    if (bestNameMatch.nameScore >= _multipleResultsThreshold)
                    {
                        Console.WriteLine($"   ✅ Strong NAME match - Client ID: {bestNameMatch.result.ClientId} | Score: {bestNameMatch.nameScore:F2}%");

                        // Warn if Medicare conflicts
                        if (bestMedicareMatch.result != null &&
                            bestMedicareMatch.result.ClientId != bestNameMatch.result.ClientId)
                        {
                            Console.WriteLine($"   ⚠️  WARNING: Medicare match found for different client (ID: {bestMedicareMatch.result.ClientId})");
                            Console.WriteLine($"      This may indicate a data entry error in Medicare number");
                        }

                        return (bestNameMatch.result.ClientId, null);
                    }

                    // Strategy 2: Medicare match with decent name score (>= 70%)
                    if (bestMedicareMatch.result != null && bestMedicareMatch.nameScore >= 70)
                    {
                        Console.WriteLine($"   ✅ Medicare # confirmed match - Client ID: {bestMedicareMatch.result.ClientId}");
                        Console.WriteLine($"      Name score: {bestMedicareMatch.nameScore:F2}% (Medicare verification provides confidence)");
                        return (bestMedicareMatch.result.ClientId, null);
                    }

                    // Strategy 3: Moderate name match (>= 70% manual review threshold)
                    if (bestNameMatch.nameScore >= _manualReviewThreshold)
                    {
                        Console.WriteLine($"   ✅ Moderate NAME match - Client ID: {bestNameMatch.result.ClientId} | Score: {bestNameMatch.nameScore:F2}%");
                        return (bestNameMatch.result.ClientId, null);
                    }

                    // Strategy 4: Last resort - Medicare match even if name is weak
                    if (bestMedicareMatch.result != null)
                    {
                        Console.WriteLine($"   ⚠️  Medicare match with LOW name score - Client ID: {bestMedicareMatch.result.ClientId}");
                        Console.WriteLine($"      Name score: {bestMedicareMatch.nameScore:F2}% (possible name spelling variation)");
                        Console.WriteLine($"      Accepting based on Medicare verification - REVIEW RECOMMENDED");
                        return (bestMedicareMatch.result.ClientId, null);
                    }

                    // No confident match found - return best suggestion
                    Console.WriteLine($"   ⚠️  No confident match found (best name score: {bestNameMatch.nameScore:F2}%)");
                    Console.WriteLine($"      Manual review required");
                    Console.WriteLine($"   💡 Best match suggestion: {bestMatchInfo}");
                    return (null, bestMatchInfo);
                }
            }
            catch (NoSuchElementException ex)
            {
                if (IsSessionExpired())
                {
                    Console.WriteLine($"   ❌ Session expired during search");
                    return (null, null);
                }

                Console.WriteLine($"   ❌ Element not found: {ex.Message}");
                return (null, null);
            }
            catch (WebDriverTimeoutException ex)
            {
                if (IsSessionExpired())
                {
                    Console.WriteLine($"   ❌ Session expired (timeout)");
                    return (null, null);
                }

                Console.WriteLine($"   ❌ Timeout: {ex.Message}");
                return (null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Search error: {ex.Message}");
                return (null, null);
            }
        }




        /// <summary>
        /// Initializes column indices from the PHIS results table (called after first successful search)
        /// </summary>
        private void InitializeColumnIndices()
        {
            if (_columnIndicesInitialized) return;

            try
            {
                var phisHeaders = _config.GetSection("PhisAutomation:ColumnHeaders");

                _clientIdIdx = GetColumnIndexByName(phisHeaders["ClientId"] ?? "Client ID");
                _firstNameIdx = GetColumnIndexByName(phisHeaders["FirstName"] ?? "First Name");
                _lastNameIdx = GetColumnIndexByName(phisHeaders["LastName"] ?? "Last Name");

                // Medicare/Health Card is optional
                try
                {
                    _medicareIdx = GetColumnIndexByName(phisHeaders["Medicare"] ?? "Health Card Number");
                }
                catch
                {
                    Console.WriteLine("   ⚠️  Medicare/Health Card column not found - matching will use names only");
                    _medicareIdx = null;
                }

                _columnIndicesInitialized = true;
                Console.WriteLine($"✅ Column indices initialized: ClientID={_clientIdIdx}, FirstName={_firstNameIdx}, LastName={_lastNameIdx}, Medicare={_medicareIdx?.ToString() ?? "N/A"}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to initialize column indices: {ex.Message}");
                throw;
            }
        }




        private int GetColumnIndexByName(string columnName)
        {
            try
            {
                // Wait for the table to be present
                var table = _wait.Until(d => d.FindElement(By.Id("form:dataTable:dataTable")));

                // Find the table header row
                var headerRow = table.FindElement(By.CssSelector("thead tr"));
                var headers = headerRow.FindElements(By.TagName("th"));

                for (int i = 0; i < headers.Count; i++)
                {
                    var headerText = headers[i].Text.Trim();

                    // Match the column name (case-insensitive)
                    if (headerText.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Found column '{columnName}' at index {i}");
                        return i;
                    }
                }

                throw new Exception($"Column '{columnName}' not found in table headers");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding column index for '{columnName}': {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// Extracts data from a search result row using dynamic column indices
        /// </summary>
        private PhisSearchResult ExtractResultRowData(IWebElement row)
        {
            var cells = row.FindElements(By.TagName("td"));

            if (!_columnIndicesInitialized)
            {
                throw new InvalidOperationException("Column indices not initialized. Call InitializeColumnIndices() first.");
            }

            var result = new PhisSearchResult
            {
                ClientId = cells[_clientIdIdx!.Value].Text.Trim(),
                FirstName = cells[_firstNameIdx!.Value].Text.Trim(),
                LastName = cells[_lastNameIdx!.Value].Text.Trim()
            };

            // Extract Medicare number if column exists
            if (_medicareIdx.HasValue && _medicareIdx.Value < cells.Count)
            {
                var medicareText = cells[_medicareIdx.Value].Text.Trim();

                // Validate it looks like a Medicare number (digits, spaces, dashes only)
                if (!string.IsNullOrWhiteSpace(medicareText) &&
                    medicareText.All(c => char.IsDigit(c) || c == '-' || c == ' '))
                {
                    result.MedicareNumber = NormalizeMedicareNumber(medicareText);
                }
            }

            return result;
        }


        /// <summary>
        /// Calculates overall match score with NAME PRIORITY over Medicare number
        /// Medicare is used as:
        /// 1. Tiebreaker when multiple results have similar name scores
        /// 2. Confirmation boost for already good name matches
        /// 3. Last resort when name matching fails
        /// Returns: (finalScore, nameScore, medicareMatch)
        /// </summary>
        private (double finalScore, double nameScore, bool medicareMatch) CalculateMatchScore(StudentRecord student, PhisSearchResult phisResult)
        {
            // Calculate name similarity (this is the PRIMARY matching criteria)
            double nameScore = CalculateNameMatchScore(student, phisResult);

            // Check Medicare number match (SECONDARY criteria)
            bool medicareMatch = false;

            if (_useMedicareNumberAsConfirmation &&
                !string.IsNullOrWhiteSpace(student.MedicareNumber) &&
                !string.IsNullOrWhiteSpace(phisResult.MedicareNumber))
            {
                var csvMedicare = NormalizeMedicareNumber(student.MedicareNumber);
                var phisMedicare = NormalizeMedicareNumber(phisResult.MedicareNumber);

                medicareMatch = csvMedicare.Equals(phisMedicare, StringComparison.OrdinalIgnoreCase);
            }

            // Calculate final score
            double finalScore = nameScore;

            // Only apply Medicare boost if:
            // 1. Medicare numbers match AND
            // 2. Name score is already reasonable (>= 60%)
            // This prevents Medicare from overriding bad name matches
            if (medicareMatch && nameScore >= 60)
            {
                finalScore = Math.Min(100, nameScore + _medicareNumberBoostScore);
            }

            return (finalScore, nameScore, medicareMatch);
        }


        /// <summary>
        /// Normalizes Medicare number by removing spaces and dashes
        /// </summary>
        private string NormalizeMedicareNumber(string medicareNumber)
        {
            if (string.IsNullOrWhiteSpace(medicareNumber))
                return "";

            return medicareNumber.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
        }


        /// <summary>
        /// Calculates fuzzy name match score (0-100)
        /// </summary>
        private double CalculateNameMatchScore(StudentRecord student, PhisSearchResult phisResult)
        {
            var csvFirstName = NormalizeName(student.FirstName);
            var csvLastName = NormalizeName(student.LastName);
            var phisFirstName = NormalizeName(phisResult.FirstName);
            var phisLastName = NormalizeName(phisResult.LastName);

            double firstNameSimilarity = CalculateNameSimilarity(csvFirstName, phisFirstName);
            double lastNameSimilarity = CalculateNameSimilarity(csvLastName, phisLastName);

            double overallScore = (lastNameSimilarity * _lastNameWeight) + (firstNameSimilarity * _firstNameWeight);

            return overallScore * 100;
        }


        /// <summary>
        /// Handles compound names like "Jean-Marie" vs "Jean"
        /// </summary>
        private double CalculateNameSimilarity(string name1, string name2)
        {
            if (string.IsNullOrEmpty(name1) && string.IsNullOrEmpty(name2))
                return 1.0;

            if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
                return 0.0;

            if (name1.Equals(name2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            // Handle compound names (Jean-Marie vs Jean)
            if (_treatCompoundNamesAsPartialMatch)
            {
                var parts1 = name1.Split('-');
                var parts2 = name2.Split('-');

                if (parts1.Length == 1 && parts2.Length > 1)
                {
                    if (parts2[0].Equals(name1, StringComparison.OrdinalIgnoreCase))
                        return 0.95; // 95% match for compound name
                }
                else if (parts2.Length == 1 && parts1.Length > 1)
                {
                    if (parts1[0].Equals(name2, StringComparison.OrdinalIgnoreCase))
                        return 0.95;
                }
            }

            return CalculateStringSimilarity(name1, name2);
        }



        private string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";

            var normalized = RemoveAccents(name.Trim().ToUpperInvariant());

            if (_ignoreSpaces)
            {
                normalized = normalized.Replace(" ", "");
            }

            return normalized;
        }



        private string RemoveAccents(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }



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
                            distance[i - 1, j] + 1,
                            distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[sourceLength, targetLength];
        }


        private List<StudentRecord> ReadProcessedCsv()
        {
            var students = new List<StudentRecord>();

            try
            {
                using var reader = new StreamReader(_processedCsvPath, Encoding.UTF8);
                using var csv = new CsvHelper.CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    MissingFieldFound = null,  // Ignore missing fields (backward compatibility)
                    HeaderValidated = null,     // Ignore header validation errors
                    TrimOptions = TrimOptions.Trim
                });

                // Map CSV columns to StudentRecord properties
                csv.Context.RegisterClassMap<StudentRecordMap>();

                students = csv.GetRecords<StudentRecord>().ToList();

                Console.WriteLine($"✅ Loaded {students.Count} student records from CSV");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error reading CSV: {ex.Message}");
                throw;
            }

            return students;
        }


        /// <summary>
        /// Handles Ctrl+C gracefully by saving progress before exit
        /// </summary>
        private void OnShutdownRequested(object? sender, ConsoleCancelEventArgs e)
        {
            if (_shutdownRequested) return; // Already handling shutdown

            Console.WriteLine("\n\n⚠️  Shutdown requested (Ctrl+C detected)");
            Console.WriteLine("💾 Saving progress before exit...");

            e.Cancel = true; // Prevent immediate termination
            _shutdownRequested = true;

            // Save current progress
            if (_currentStudentList != null)
            {
                try
                {
                    UpdateCsvRecord(_currentStudentList);
                    Console.WriteLine("✅ Progress saved successfully!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to save progress: {ex.Message}");
                }
            }

            Console.WriteLine("👋 Exiting safely...");
            Environment.Exit(0);
        }



        // ✅ ADD THIS METHOD
        private void OnProcessExit(object? sender, EventArgs e)
        {
            Console.WriteLine("\n🛑 Application exiting - cleaning up ChromeDriver...");
            try
            {
                _driver?.Quit();
                _driver?.Dispose();
            }
            catch
            {
                // Ignore errors during emergency cleanup
            }
        }


        public async Task SearchAllClientsInCsvAsync()
        {
            var students = ReadProcessedCsv();
            _currentStudentList = students;

            // Filter only unprocessed records
            var unprocessedStudents = students.Where(s => s.ClientIdStatus == ClientIdStatus.NotProcessed).ToList();

            Console.WriteLine($"\n📊 Processing Summary:");
            Console.WriteLine($"   Total students: {students.Count}");
            Console.WriteLine($"   Already processed: {students.Count(s => s.ClientIdStatus == ClientIdStatus.Found)}");
            Console.WriteLine($"   Needs manual review: {students.Count(s => s.ClientIdStatus == ClientIdStatus.NeedsManualReview)}");
            Console.WriteLine($"   Not yet processed: {unprocessedStudents.Count}");

            if (unprocessedStudents.Count == 0)
            {
                Console.WriteLine("\n✅ All students have been processed!");
                return;
            }

            // ✅ ESTIMATE COMPLETION TIME AND WARN ABOUT SESSION TIMEOUT
            double estimatedMinutes = (unprocessedStudents.Count * _delayBetweenSearchesMs) / 60000.0;
            Console.WriteLine($"\n⏱️  Estimated processing time: {estimatedMinutes:F1} minutes");

            if (estimatedMinutes > _sessionTimeoutMinutes && !_sessionRefreshEnabled)
            {
                Console.WriteLine($"   ⚠️  WARNING: Processing may exceed session timeout ({_sessionTimeoutMinutes} min)");
                Console.WriteLine($"   💡 Consider enabling SessionRefreshEnabled in appsettings.json");
            }
            else if (_sessionRefreshEnabled && estimatedMinutes > _sessionTimeoutMinutes)
            {
                Console.WriteLine($"   ✅ Auto-refresh enabled - session will be kept alive");
            }

            Console.WriteLine($"\n🔍 Starting Client ID search for {unprocessedStudents.Count} students...");
            Console.WriteLine($"💡 TIP: Press Ctrl+C to save progress and exit gracefully\n");

            int successCount = 0;
            int manualReviewCount = 0;
            int errorCount = 0;
            int sessionRefreshCount = 0;

            for (int i = 0; i < unprocessedStudents.Count; i++)
            {
                if (_shutdownRequested)
                {
                    Console.WriteLine("\n⚠️  Shutdown in progress...");
                    break;
                }

                var student = unprocessedStudents[i];

                // ✅ SHOW SESSION STATUS EVERY 10 RECORDS
                if (i > 0 && i % 10 == 0)
                {
                    var timeSinceLastActivity = DateTime.Now - _lastSessionActivity;
                    var timeRemaining = TimeSpan.FromMinutes(_sessionTimeoutMinutes) - timeSinceLastActivity;
                    Console.WriteLine($"\n⏱️  Session time remaining: {timeRemaining.TotalMinutes:F1} minutes");
                }

                Console.WriteLine($"\n[{i + 1}/{unprocessedStudents.Count}] Processing: {student.FirstName} {student.LastName}");

                try
                {
                    // ✅ NOW USING ASYNC SEARCH METHOD
                    var (clientId, bestMatchInfo) = await SearchClientByDob(student);

                    if (!string.IsNullOrWhiteSpace(clientId))
                    {
                        student.ClientId = clientId;
                        student.ClientIdStatus = ClientIdStatus.Found;
                        student.BestMatch = string.Empty;
                        successCount++;
                        Console.WriteLine($"✅ Client ID found: {clientId}");
                    }
                    else
                    {
                        student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                        student.BestMatch = bestMatchInfo ?? string.Empty;
                        manualReviewCount++;
                        Console.WriteLine($"⚠️  Marked for manual review");

                        if (!string.IsNullOrWhiteSpace(bestMatchInfo))
                        {
                            Console.WriteLine($"   💡 Best match saved: {bestMatchInfo}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    student.ClientIdStatus = ClientIdStatus.NeedsManualReview;
                    student.BestMatch = string.Empty;
                    errorCount++;
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }

                // SAVE PROGRESS PERIODICALLY
                int recordsProcessed = i + 1;
                if (recordsProcessed % _saveProgressEveryNRecords == 0)
                {
                    Console.WriteLine($"\n💾 Saving progress ({recordsProcessed}/{unprocessedStudents.Count} records processed)...");
                    UpdateCsvRecord(students);
                }

                // Delay between searches
                if (i < unprocessedStudents.Count - 1)
                {
                    await Task.Delay(_delayBetweenSearchesMs);
                }
            }

            // FINAL SAVE
            Console.WriteLine($"\n💾 Performing final save...");
            UpdateCsvRecord(students);

            // Final summary
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("📊 SEARCH COMPLETE - Final Summary");
            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"✅ Successfully found: {successCount}");
            Console.WriteLine($"⚠️  Needs manual review: {manualReviewCount}");
            Console.WriteLine($"❌ Errors: {errorCount}");
            Console.WriteLine($"📝 Total processed: {successCount + manualReviewCount + errorCount}");
            if (sessionRefreshCount > 0)
            {
                Console.WriteLine($"🔄 Session refreshes: {sessionRefreshCount}");
            }
            Console.WriteLine(new string('═', 60) + "\n");
        }



        /// <summary>
        /// Updates the entire CSV file with current student records using CsvHelper
        /// Uses atomic file replacement to prevent corruption if crash occurs during write
        /// </summary>
        private void UpdateCsvRecord(List<StudentRecord> allStudents)
        {
            string tempFile = _processedCsvPath + ".tmp";

            try
            {
                // ✅ STEP 1: Write to temporary file
                using (var writer = new StreamWriter(tempFile, false, Encoding.UTF8))
                using (var csv = new CsvHelper.CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true
                }))
                {
                    // Register the class map
                    csv.Context.RegisterClassMap<StudentRecordMap>();

                    // Write all records
                    csv.WriteRecords(allStudents);
                }

                // ✅ STEP 2: Atomically replace the old file with the new one
                // This is crash-safe: if the process crashes here, the original file is still intact
                File.Move(tempFile, _processedCsvPath, overwrite: true);

                Console.WriteLine($"   ✅ CSV progress saved ({allStudents.Count} records)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Warning: Could not update CSV: {ex.Message}");

                // Clean up temporary file if it exists
                if (File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }


        public void Dispose()
        {
            // Unregister the Ctrl+C handler
            Console.CancelKeyPress -= OnShutdownRequested;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit; // ✅ ADD THIS

            // Cleanup ChromeDriver
            try
            {
                _driver?.Quit();
                _driver?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  ChromeDriver disposal error: {ex.Message}");
            }

        }



        /// <summary>
        /// Performs DOB search and reads results directly from the table
        /// Uses simple page source extraction instead of network interception
        /// </summary>
        private async Task<string> PerformDobSearchAndGetResponse(string dateOfBirth)
        {
            try
            {
                // Ensure we're on the search page
                await EnsureOnSearchPage();

                // Clear any previous search
                await ClearSearchForm();

                // Enter DOB in the search field
                var dobInput = _driver.FindElement(By.Id("form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input"));
                dobInput.Clear();
                dobInput.SendKeys(dateOfBirth);

                Console.WriteLine($"   ✏️  Entered DOB: {dateOfBirth}");

                // Click search button
                var searchButton = _driver.FindElement(By.Id("actionMenuSearch:commandButtonId"));
                searchButton.Click();

                Console.WriteLine($"   🔍 Clicked Search button, waiting for results...");

                // Wait for results table to appear or update
                await Task.Delay(2000);

                // Wait for the specific results table with actual data
                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
                wait.Until(d =>
                {
                    try
                    {
                        var tbody = d.FindElement(By.Id("form:dataTable:dataTable_data"));
                        var rows = tbody.FindElements(By.XPath(".//tr[@role='row']"));
                        return rows.Count > 0; // Wait until at least one row appears
                    }
                    catch
                    {
                        // Check for "no results" message
                        var messages = d.FindElements(By.CssSelector(".ui-messages-info, .ui-messages-warn"));
                        return messages.Count > 0; // Return true if we have a message
                    }
                });

                // Get the page source (contains the full table HTML)
                var pageSource = _driver.PageSource;

                // Wrap in XML structure for consistent parsing
                // Using CDATA to avoid escaping issues
                var wrappedXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
                <partial-response id=""j_id__v_0"">
                    <changes>
                        <update id=""form""><![CDATA[{pageSource}]]></update>
                    </changes>
                </partial-response>";

                Console.WriteLine($"   ✅ Search completed, extracted results from page");
                return wrappedXml;
            }
            catch (WebDriverTimeoutException)
            {
                Console.WriteLine($"   ⚠️  Timeout waiting for search results");

                // Check if session expired
                if (IsSessionExpired())
                {
                    Console.WriteLine($"   ❌ Session expired during search");
                    throw new InvalidOperationException("Session expired");
                }

                // Return empty result
                return CreateEmptyResponse();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error performing DOB search: {ex.Message}");
                throw;
            }
        }

        // <summary>
        /// Ensures we're on the client search page
        /// </summary>
        private async Task EnsureOnSearchPage()
        {
            try
            {
                // Check if we're already on the search page
                var searchForm = _driver.FindElements(By.Id("form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input"));

                if (searchForm.Count > 0)
                {
                    return; // Already on search page
                }

                // Navigate to search page
                Console.WriteLine($"   🔄 Navigating to search page...");

                string searchUrl = _config["PhisAutomation:SearchUrl"] ?? "https://phisisp.gnb.ca/phsdsm/ClientWeb/pages/search/clientSearch.xhtml";
                _driver.Navigate().GoToUrl(searchUrl);

                await Task.Delay(1000);

                // Wait for page to load
                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
                wait.Until(d => d.FindElements(By.Id("form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input")).Count > 0);

                Console.WriteLine($"   ✅ On search page");

                // Update session activity
                UpdateSessionActivity();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Could not navigate to search page: {ex.Message}");
                throw;
            }
        }


        // <summary>
        /// Clears the search form before entering new criteria
        /// </summary>
        private async Task ClearSearchForm()
        {
            try
            {
                // Click the Reset button to clear form
                var resetButton = _driver.FindElements(By.Id("actionMenuReset:commandButtonId"));

                if (resetButton.Count > 0)
                {
                    resetButton[0].Click();
                    await Task.Delay(1000);
                    Console.WriteLine($"   🧹 Cleared search form");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Could not clear form: {ex.Message}");
                // Don't throw - clearing is optional
            }
        }



        // <summary>
        /// Creates an empty XML response when no results are found
        /// </summary>
        private string CreateEmptyResponse()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <partial-response id=""j_id__v_0"">
                <changes>
                    <update id=""form""><![CDATA[<div>No results found</div>]]></update>
                </changes>
            </partial-response>";
        }




        /// <summary>
        /// Checks if session is about to expire and refreshes it if needed
        /// Returns true if session is valid/refreshed, false if refresh failed
        /// </summary>
        private bool EnsureSessionValid()
        {
            if (!_sessionRefreshEnabled) return true;

            var timeSinceLastActivity = DateTime.Now - _lastSessionActivity;
            var timeUntilTimeout = TimeSpan.FromMinutes(_sessionTimeoutMinutes) - timeSinceLastActivity;

            // Refresh session if less than 2 minutes remaining (safety buffer)
            if (timeUntilTimeout.TotalMinutes < 2)
            {
                Console.WriteLine($"\n⚠️  Session timeout approaching ({timeUntilTimeout.TotalMinutes:F1} minutes remaining)");
                Console.WriteLine($"🔄 Refreshing session...");

                try
                {
                    // Navigate to a simple page to keep session alive
                    string searchUrl = _config["PhisAutomation:SearchUrl"] ?? "https://phisisp.gnb.ca/phsdsm/ClientWeb/pages/search/clientSearch.xhtml";
                    _driver.Navigate().GoToUrl(searchUrl);

                    Thread.Sleep(1000); // Give page time to load

                    // Check if we're still logged in
                    if (IsSessionExpired())
                    {
                        Console.WriteLine($"❌ Session expired - attempting re-login...");

                        // Reset column indices as we might be on a different page
                        _columnIndicesInitialized = false;

                        // Re-login
                        bool loginSuccess = InitiateLogin();
                        if (!loginSuccess)
                        {
                            Console.WriteLine($"❌ Re-login failed");
                            return false;
                        }

                        Console.WriteLine($"✅ Session restored successfully");
                    }
                    else
                    {
                        Console.WriteLine($"✅ Session refreshed successfully");
                    }

                    _lastSessionActivity = DateTime.Now;
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Session refresh failed: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if the PHIS session has expired
        /// </summary>
        private bool IsSessionExpired()
        {
            try
            {
                var currentUrl = _driver.Url.ToLowerInvariant();

                // Check if we've been redirected to login page
                if (currentUrl.Contains("login") || currentUrl.Contains("signin"))
                {
                    return true;
                }

                // Try to find a common element that exists when logged in
                try
                {
                    _driver.FindElement(By.Id("form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input"));
                    return false; // Element found, session is valid
                }
                catch
                {
                    // Element not found, might be on error/timeout page
                    return true;
                }
            }
            catch
            {
                // If any error occurs, assume session is expired
                return true;
            }
        }

        /// <summary>
        /// Updates the last session activity timestamp
        /// Call this after every successful PHIS interaction
        /// </summary>
        private void UpdateSessionActivity()
        {
            _lastSessionActivity = DateTime.Now;
        }





        /// <summary>
        /// Gets the configured delay time between searches (in milliseconds)
        /// </summary>
        public int DelayBetweenSearchesMs => _delayBetweenSearchesMs;

        /// <summary>
        /// Gets the configured page load delay (in milliseconds)
        /// </summary>
        public int PageLoadDelayMs => _pageLoadDelayMs;

        /// <summary>
        /// Gets the WebDriverWait instance for element waiting
        /// </summary>
        public WebDriverWait Wait => _wait;

    }


    /// <summary>
    /// Represents a search result from PHIS (includes Medicare number)
    /// </summary>
    public class PhisSearchResult
    {
        public string ClientId { get; set; } = string.Empty;

        public string? MedicareNumber { get; set; }

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        public string MiddleName { get; set; } = "";
        public string Gender { get; set; } = "";
        public string DateOfBirth { get; set; } = "";
        public string HealthRegion { get; set; } = "";
        public string ActiveStatus { get; set; } = "";
    }


    public class StudentRecord
    {
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string School { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty; // Format: yyyy-MM-dd
        public string MedicareNumber { get; set; } = string.Empty;
        public string ConsentStatus { get; set; } = string.Empty;
        public string Tdap { get; set; } = string.Empty;
        public string HPV { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public bool IsFileRoseDefaut { get; set; }

        /// <summary>
        /// Status of Client ID search (0=NotProcessed, 1=Found, 2=NeedsManualReview)
        /// </summary>
        public ClientIdStatus ClientIdStatus { get; set; } = ClientIdStatus.NotProcessed;

        /// <summary>
        /// Best match suggestion for manual review (Format: FirstName#LastName#ClientID#Score)
        /// Only populated when ClientIdStatus = NeedsManualReview
        /// </summary>
        public string BestMatch { get; set; } = string.Empty;

    }


    /// <summary>
    /// CsvHelper mapping for StudentRecord
    /// Maps CSV column names to class properties
    /// </summary>
    public sealed class StudentRecordMap : ClassMap<StudentRecord>
    {
        public StudentRecordMap()
        {
            Map(m => m.LastName).Name("Last Name");
            Map(m => m.FirstName).Name("First Name");
            Map(m => m.School).Name("School");
            Map(m => m.Grade).Name("Grade");
            Map(m => m.DateOfBirth).Name("Date of Birth");
            Map(m => m.MedicareNumber).Name("Medicare Number");
            Map(m => m.ConsentStatus).Name("Consent Status");
            Map(m => m.Tdap).Name("Tdap");
            Map(m => m.HPV).Name("HPV");
            Map(m => m.ClientId).Name("ClientId");
            Map(m => m.IsFileRoseDefaut).Name("IsFileRoseDefaut");
            Map(m => m.ClientIdStatus).Name("ClientIdStatus")
                .TypeConverter<ClientIdStatusConverter>();
            Map(m => m.BestMatch).Name("BestMatch").Optional(); // Optional for backward compatibility
        }
    }

    /// <summary>
    /// Custom converter for ClientIdStatus enum
    /// </summary>
    public class ClientIdStatusConverter : CsvHelper.TypeConversion.DefaultTypeConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text)) return ClientIdStatus.NotProcessed;

            if (int.TryParse(text, out int value))
            {
                return (ClientIdStatus)value;
            }

            return ClientIdStatus.NotProcessed;
        }

        public override string ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
        {
            if (value is ClientIdStatus status)
            {
                return ((int)status).ToString();
            }

            return "0";
        }
    }


}
