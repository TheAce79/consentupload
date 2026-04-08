using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace CsvProcessor
{
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

        public FindClientId(IConfiguration config)
        {
            _config = config;

            // Load PHIS configuration
            _processedCsvPath = Path.Combine(
                _config["CsvProcessing:OutputCsvPath"] ?? "",
                _config["CsvProcessing:OutputCsvFileName"] ?? ""
            );

            _username = _config["PhisAutomation:Username"] ?? "";
            _password = _config["PhisAutomation:Password"] ?? "";

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


        private string? SearchClientByDob(StudentRecord student)
        {
            try
            {
                // Navigate to client search page
                string searchUrl = _config["PhisAutomation:SearchUrl"] ?? "https://phisisp.gnb.ca/phsdsm/ClientWeb/pages/search/clientSearch.xhtml";
                _driver.Navigate().GoToUrl(searchUrl);

                // Wait for the DOB input field
                _wait.Until(d => d.FindElement(By.Id("form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input")));

                // Select DOB radio button
                try
                {
                    var dobRadioButton = _driver.FindElement(By.CssSelector("input[name='form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:selectOneRadio'][value='DOB']"));
                    if (!dobRadioButton.Selected)
                    {
                        dobRadioButton.Click();
                        Thread.Sleep(500);
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("   ℹ️  DOB radio button handling skipped");
                }

                // Enter DOB
                var dobField = _driver.FindElement(By.Id("form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input"));
                dobField.Clear();
                dobField.SendKeys(student.DateOfBirth);

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

                // Click search button
                var searchButton = _driver.FindElement(By.Id("actionMenuSearch:commandButtonId"));
                searchButton.Click();

                // Wait for AJAX response
                Thread.Sleep(_pageLoadDelayMs);

                try
                {
                    _wait.Until(d =>
                        d.FindElements(By.CssSelector("tbody[id*='dataTable_data'] tr[data-rk]")).Count > 0 ||
                        d.FindElements(By.CssSelector(".ui-messages-error, .ui-messages-info, .ui-messages-warn")).Count > 0
                    );
                }
                catch (WebDriverTimeoutException)
                {
                    Console.WriteLine("   ⚠️  Timeout waiting for search results");
                }

                // Get results
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
                    return null;
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

                    // Decision logic: Name score is primary, Medicare is confidence booster
                    if (nameScore >= _singleResultThreshold)
                    {
                        Console.WriteLine($"   ✅ Confirmed match by NAME - Client ID: {match.ClientId}");
                        return match.ClientId;
                    }
                    else if (medicareMatch && nameScore >= 60)
                    {
                        Console.WriteLine($"   ✅ Confirmed match by MEDICARE (name score marginal: {nameScore:F2}%) - Client ID: {match.ClientId}");
                        return match.ClientId;
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️  Score too low ({finalScore:F2}% < {_singleResultThreshold}%) - needs manual review");
                        return null;
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
                        return null;
                    }

                    // ═══════════════════════════════════════════════════════════════
                    // MATCHING STRATEGY (Priority Order):
                    // 1. Best name match (>= multipleResultsThreshold)
                    // 2. Medicare match with reasonable name score (>= 70%)
                    // 3. Name match above manual review threshold
                    // 4. Medicare match as last resort (if name scores all low)
                    // ═══════════════════════════════════════════════════════════════

                    // Sort by NAME SCORE first (primary criteria)
                    var sortedByName = matches.OrderByDescending(m => m.nameScore).ToList();
                    var bestNameMatch = sortedByName.First();

                    // Check if we have a Medicare match
                    var medicareMatches = matches.Where(m => m.medicareMatch).ToList();
                    var bestMedicareMatch = medicareMatches.OrderByDescending(m => m.nameScore).FirstOrDefault();

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

                        return bestNameMatch.result.ClientId;
                    }

                    // Strategy 2: Medicare match with decent name score (>= 70%)
                    if (bestMedicareMatch.result != null && bestMedicareMatch.nameScore >= 70)
                    {
                        Console.WriteLine($"   ✅ Medicare # confirmed match - Client ID: {bestMedicareMatch.result.ClientId}");
                        Console.WriteLine($"      Name score: {bestMedicareMatch.nameScore:F2}% (Medicare verification provides confidence)");
                        return bestMedicareMatch.result.ClientId;
                    }

                    // Strategy 3: Moderate name match (>= 70% manual review threshold)
                    if (bestNameMatch.nameScore >= _manualReviewThreshold)
                    {
                        Console.WriteLine($"   ✅ Moderate NAME match - Client ID: {bestNameMatch.result.ClientId} | Score: {bestNameMatch.nameScore:F2}%");
                        return bestNameMatch.result.ClientId;
                    }

                    // Strategy 4: Last resort - Medicare match even if name is weak
                    if (bestMedicareMatch.result != null)
                    {
                        Console.WriteLine($"   ⚠️  Medicare match with LOW name score - Client ID: {bestMedicareMatch.result.ClientId}");
                        Console.WriteLine($"      Name score: {bestMedicareMatch.nameScore:F2}% (possible name spelling variation)");
                        Console.WriteLine($"      Accepting based on Medicare verification - REVIEW RECOMMENDED");
                        return bestMedicareMatch.result.ClientId;
                    }

                    // No confident match found
                    Console.WriteLine($"   ⚠️  No confident match found (best name score: {bestNameMatch.nameScore:F2}%)");
                    Console.WriteLine($"      Manual review required");
                    return null;
                }
            }
            catch (NoSuchElementException ex)
            {
                Console.WriteLine($"   ❌ Element not found: {ex.Message}");
                return null;
            }
            catch (WebDriverTimeoutException ex)
            {
                Console.WriteLine($"   ❌ Timeout: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Search error: {ex.Message}");
                return null;
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
            var lines = File.ReadAllLines(_processedCsvPath, Encoding.UTF8);

            if (lines.Length == 0) return students;

            var header = lines[0].Split(',');
            var colMap = header.Select((name, index) => new { name, index })
                               .ToDictionary(x => x.name, x => x.index);

            for (int i = 1; i < lines.Length; i++)
            {
                var values = lines[i].Split(',');

                students.Add(new StudentRecord
                {
                    LastName = values[colMap["Last Name"]],
                    FirstName = values[colMap["First Name"]],
                    School = values[colMap["School"]],
                    Grade = values[colMap["Grade"]],
                    DateOfBirth = values[colMap["Date of Birth"]],
                    MedicareNumber = values[colMap["Medicare Number"]],
                    ConsentStatus = values[colMap["Consent Status"]],
                    Tdap = values[colMap["Tdap"]],
                    HPV = values[colMap["HPV"]],
                    ClientId = values[colMap["ClientId"]],
                    IsFileRoseDefaut = bool.Parse(values[colMap["IsFileRoseDefaut"]])
                });
            }

            return students;
        }

        public void Dispose()
        {
            _driver?.Quit();
            _driver?.Dispose();
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
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? MedicareNumber { get; set; }
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
    }
}
