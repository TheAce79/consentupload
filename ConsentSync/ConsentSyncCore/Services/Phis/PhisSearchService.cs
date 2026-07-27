using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Matching;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ConsentSyncCore.Services.ConfigurationPoco;
using ConsentSyncCore.Services.Configuration;

namespace ConsentSyncCore.Services.Phis
{
    public partial class PhisSearchService
    {
        private readonly IWebDriver _driver;
        private readonly IConfiguration _config;
        private readonly WebDriverWait _wait;
        private readonly PhisResultExtractor _resultExtractor;
        private readonly PhisSessionManager _sessionManager;
        private readonly PhisConfig _phisConfig;


        // Constructor with dependency injection
        public PhisSearchService(
            IWebDriver driver,
            IConfiguration config,
            PhisResultExtractor resultExtractor,
            PhisSessionManager sessionManager)
        {
            _driver = driver;
            _config = config;
            _resultExtractor = resultExtractor;
            _sessionManager = sessionManager;
            _phisConfig = ConfigurationService.GetPhisConfig();

            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(_phisConfig.WebDriverWaitSeconds));
        }





        #region Public API



        /// <summary>
        /// Search by Date of Birth (Phase 1)
        /// Returns all matching results for fuzzy matching
        /// </summary>
        public async Task<SearchResult> SearchByDobAsync(
            string dateOfBirth,
            string? expectedFirstName = null,
            string? expectedLastName = null,
            string? expectedMedicare = null)
        {
            try
            {
                // Check session validity
                if (!_sessionManager.EnsureSessionValid())
                {
                    return SearchResult.Failed("Session validation failed");
                }

                 LoggerService.LogInformation($"   🔍 Searching by DOB: {dateOfBirth}");
                if (!string.IsNullOrEmpty(expectedFirstName) && !string.IsNullOrEmpty(expectedLastName))
                {
                     LoggerService.LogInformation($"      Looking for: {expectedFirstName} {expectedLastName}");
                }

                // Navigate to search page
                await EnsureOnSearchPageAsync();

                // Clear previous search
                await ClearSearchFormAsync();

                // Perform DOB search
                await ExecuteDobSearchAsync(dateOfBirth);

                // Wait for results
                await WaitForSearchResultsAsync();

                // Extract all results
                var results = _resultExtractor.ExtractAllResults(_driver);

                // Update session activity
                _sessionManager.UpdateActivity();

                if (results.Count == 0)
                {
                     LoggerService.LogInformation($"   ⚠️  No results found for DOB: {dateOfBirth}");
                    return SearchResult.NoResults();
                }

                 LoggerService.LogInformation($"   📊 Found {results.Count} result(s)");

                return SearchResult.IsSuccess(results);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Session expired"))
            {
                 LoggerService.LogInformation($"   ❌ Session expired during search");
                return SearchResult.Failed("Session expired");
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Search error: {ex.Message}");
                return SearchResult.Failed(ex.Message);
            }
        }




        /// <summary>
        /// Search by Client ID (Phase 3)
        /// Expected to return single result
        /// </summary>
        public async Task<SearchResult> SearchByClientIdAsync(string clientId)
        {
            try
            {
                // Check session validity
                if (!_sessionManager.EnsureSessionValid())
                {
                    return SearchResult.Failed("Session validation failed");
                }

                 LoggerService.LogInformation($"   🔍 Searching by Client ID: {clientId}");

                // Navigate to search page
                await EnsureOnSearchPageAsync();

                // Clear previous search
                await ClearSearchFormAsync();

                // Perform Client ID search
                await ExecuteClientIdSearchAsync(clientId);

                // Wait for results
                await WaitForSearchResultsAsync();

                // Extract results (should be 1)
                var results = _resultExtractor.ExtractAllResults(_driver);

                // Update session activity
                _sessionManager.UpdateActivity();

                if (results.Count == 0)
                {
                     LoggerService.LogInformation($"   ⚠️  Client not found: {clientId}");
                    return SearchResult.NoResults();
                }

                if (results.Count > 1)
                {
                     LoggerService.LogInformation($"   ⚠️  WARNING: Multiple results for Client ID {clientId} (expected 1)");
                }

                 LoggerService.LogInformation($"   ✅ Found: {results[0].FirstName} {results[0].LastName}");

                return SearchResult.IsSuccess(results);
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Search error: {ex.Message}");
                return SearchResult.Failed(ex.Message);
            }
        }



        /// <summary>
        /// Search by Name (optional - for future use)
        /// </summary>
        public async Task<SearchResult> SearchByNameAsync(string firstName, string lastName)
        {
            try
            {
                if (!_sessionManager.EnsureSessionValid())
                {
                    return SearchResult.Failed("Session validation failed");
                }

                 LoggerService.LogInformation($"   🔍 Searching by Name: {firstName} {lastName}");

                await EnsureOnSearchPageAsync();
                await ClearSearchFormAsync();
                await ExecuteNameSearchAsync(firstName, lastName);
                await WaitForSearchResultsAsync();

                var results = _resultExtractor.ExtractAllResults(_driver);
                _sessionManager.UpdateActivity();

                if (results.Count == 0)
                {
                    return SearchResult.NoResults();
                }

                return SearchResult.IsSuccess(results);
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Search error: {ex.Message}");
                return SearchResult.Failed(ex.Message);
            }
        }



        /// <summary>
        /// Search by Medicare Number
        /// Returns matching results for fuzzy matching
        /// </summary>
        public async Task<SearchResult> SearchByMedicareAsync(string medicareNumber)
        {
            try
            {
                // Check session validity
                if (!_sessionManager.EnsureSessionValid())
                {
                    return SearchResult.Failed("Session validation failed");
                }

                 LoggerService.LogInformation($"   🔍 Searching by Medicare: {medicareNumber}");

                // Navigate to search page
                await EnsureOnSearchPageAsync();

                // Clear previous search
                await ClearSearchFormAsync();

                // Perform Medicare search
                await ExecuteMedicareSearchAsync(medicareNumber);

                // Wait for results
                await WaitForSearchResultsAsync();

                // Extract all results
                var results = _resultExtractor.ExtractAllResults(_driver);

                // Update session activity
                _sessionManager.UpdateActivity();

                if (results.Count == 0)
                {
                     LoggerService.LogInformation($"   ⚠️  No results found for Medicare: {medicareNumber}");
                    return SearchResult.NoResults();
                }

                 LoggerService.LogInformation($"   📊 Found {results.Count} result(s)");

                return SearchResult.IsSuccess(results);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Session expired"))
            {
                 LoggerService.LogInformation($"   ❌ Session expired during search");
                return SearchResult.Failed("Session expired");
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Search error: {ex.Message}");
                return SearchResult.Failed(ex.Message);
            }
        }


        #endregion Public API




        #region Search Execution Methods



        /// <summary>
        /// Execute DOB search - now with correct date format (yyyy/MM/dd)
        /// </summary>
        private async Task ExecuteDobSearchAsync(string dateOfBirth)
        {
            try
            {
                // Select DOB radio button (if needed)
                await SelectSearchCriteriaTypeAsync("DOB");

                // Convert date from CSV format (yyyy-MM-dd) to PHIS format (yyyy/MM/dd)
                string phisFormattedDate = ConvertDateForPhis(dateOfBirth);

                if (string.IsNullOrEmpty(phisFormattedDate))
                {
                     LoggerService.LogInformation($"   ❌ Invalid date format: {dateOfBirth}");
                    throw new ArgumentException($"Invalid date format: {dateOfBirth}");
                }

                 LoggerService.LogInformation($"   📅 Original DOB: {dateOfBirth}");
                 LoggerService.LogInformation($"   📅 PHIS format: {phisFormattedDate}");

                // Find and fill DOB input using JavaScript (PrimeFaces calendar requires this)
                var dobInputId = "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input";

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Use JavaScript to set the value and trigger PrimeFaces events
                js.ExecuteScript($@"
            var input = document.getElementById('{dobInputId}');
            if (input) {{
                input.value = '{phisFormattedDate}';
                
                // Trigger PrimeFaces calendar events
                var changeEvent = new Event('change', {{ bubbles: true }});
                input.dispatchEvent(changeEvent);
                
                var inputEvent = new Event('input', {{ bubbles: true }});
                input.dispatchEvent(inputEvent);
                
                // Trigger blur to ensure PrimeFaces processes the value
                var blurEvent = new Event('blur', {{ bubbles: true }});
                input.dispatchEvent(blurEvent);
            }}
        ");

                 LoggerService.LogInformation($"   ✏️  Entered DOB via JavaScript");

                // Give PrimeFaces time to process
                await Task.Delay(800);

                // Verify the value was actually set
                var setValue = (string)js.ExecuteScript($"return document.getElementById('{dobInputId}').value;");
                if (setValue != phisFormattedDate)
                {
                     LoggerService.LogInformation($"   ⚠️  WARNING: DOB value mismatch!");
                     LoggerService.LogInformation($"      Expected: {phisFormattedDate}");
                     LoggerService.LogInformation($"      Got: {setValue}");

                    // Fallback: Try direct SendKeys
                    var dobInput = _driver.FindElement(By.Id(dobInputId));
                    dobInput.Clear();
                    dobInput.SendKeys(phisFormattedDate);
                    await Task.Delay(500);

                    // Re-verify
                    setValue = dobInput.GetAttribute("value");
                     LoggerService.LogInformation($"      After SendKeys: {setValue}");
                }
                else
                {
                     LoggerService.LogInformation($"   ✅ DOB value confirmed: {setValue}");
                }

                // Click search
                await ClickSearchButtonAsync();
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error in ExecuteDobSearchAsync: {ex.Message}");
                throw;
            }
        }



        /// <summary>
        /// Execute Medicare search
        /// </summary>
        private async Task ExecuteMedicareSearchAsync(string medicareNumber)
        {
            try
            {
                 LoggerService.LogInformation($"   🔍 Locating Medicare search elements...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Step 1: Set the dropdown value using JavaScript (PrimeFaces dropdowns often need this)
                var dropdownInputId = "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_ClientNumberType:selectOneMenu_input";

                // Wait for dropdown to be present
                _wait.Until(d => d.FindElements(By.Id(dropdownInputId)).Count > 0);

                // Set dropdown value via JavaScript
                js.ExecuteScript($@"
            var dropdown = document.getElementById('{dropdownInputId}');
            if (dropdown) {{
                dropdown.value = 'HEALTH_CARD_NUMBER';
                
                // Trigger change event for PrimeFaces
                var changeEvent = new Event('change', {{ bubbles: true }});
                dropdown.dispatchEvent(changeEvent);
                
                console.log('Dropdown set to HEALTH_CARD_NUMBER');
            }}
        ");

                 LoggerService.LogInformation($"   ✅ Set dropdown to 'Health Card Number' via JavaScript");
                await Task.Delay(_phisConfig.AjaxWaitMs); // Wait for PrimeFaces to process

                // Step 2: Enter Medicare number using JavaScript
                var clientNumberInputId = "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_ClientNumber:inputText";

                // Wait for input to be present
                _wait.Until(d => d.FindElements(By.Id(clientNumberInputId)).Count > 0);

                // Set input value via JavaScript
                js.ExecuteScript($@"
            var input = document.getElementById('{clientNumberInputId}');
            if (input) {{
                input.value = '{medicareNumber}';
                
                // Trigger input events
                var inputEvent = new Event('input', {{ bubbles: true }});
                input.dispatchEvent(inputEvent);
                
                var changeEvent = new Event('change', {{ bubbles: true }});
                input.dispatchEvent(changeEvent);
                
                console.log('Medicare number entered: {medicareNumber}');
            }}
        ");

                 LoggerService.LogInformation($"   ✅ Entered Medicare Number: {medicareNumber}");
                await Task.Delay(500); // Brief pause to ensure value is set

                // Step 3: Verify values were set
                var dropdownValue = (string)js.ExecuteScript($"return document.getElementById('{dropdownInputId}').value;");
                var inputValue = (string)js.ExecuteScript($"return document.getElementById('{clientNumberInputId}').value;");

                 LoggerService.LogInformation($"   🔍 Verification - Dropdown: {dropdownValue}, Input: {inputValue}");

                if (dropdownValue != "HEALTH_CARD_NUMBER" || inputValue != medicareNumber)
                {
                     LoggerService.LogInformation($"   ⚠️  WARNING: Values not set correctly!");
                     LoggerService.LogInformation($"      Expected dropdown: HEALTH_CARD_NUMBER, Got: {dropdownValue}");
                     LoggerService.LogInformation($"      Expected input: {medicareNumber}, Got: {inputValue}");
                }

                // Click search
                await ClickSearchButtonAsync();
            }
            catch (NoSuchElementException ex)
            {
                 LoggerService.LogInformation($"   ❌ Element not found in ExecuteMedicareSearchAsync");
                 LoggerService.LogInformation($"      Error: {ex.Message}");

                // Debug: Log page source snippet
                try
                {
                    IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;
                    var clientNumberSection = js.ExecuteScript(@"
                var elem = document.getElementById('form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_ClientNumber:inputText');
                if (elem) {
                    return {
                        id: elem.id,
                        visible: elem.offsetParent !== null,
                        enabled: !elem.disabled,
                        readonly: elem.readOnly,
                        display: window.getComputedStyle(elem).display,
                        visibility: window.getComputedStyle(elem).visibility
                    };
                }
                return null;
            ");

                     LoggerService.LogInformation($"   🔍 Input element state: {clientNumberSection}");
                }
                catch { }

                throw;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error in ExecuteMedicareSearchAsync: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Convert date from CSV format (yyyy-MM-dd) to PHIS format (yyyy/MM/dd)
        /// </summary>
        /// 
        /// <summary>
        /// Converts a date from any known CSV format to PHIS format (yyyy/MM/dd).
        /// Handles: yyyy-MM-dd, yyyy/MM/dd, M/d/yyyy, MM/dd/yyyy, d/M/yyyy, dd/MM/yyyy, etc.
        /// </summary>
        private string ConvertDateForPhis(string csvDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(csvDate))
                    return string.Empty;

                // ── Try every format we may encounter in the processed CSV ────
                // Order matters: most specific / unambiguous formats first.
                string[] knownFormats =
                {
                    // Standard output format written by StudentCsvProcessor
                    "yyyy-MM-dd",

                    // Input CSV native format (Windows-1252 ANSI export)
                    "yyyy/MM/dd",

                    // Culture-aware fallback formats that TryParse may have written
                    "M/d/yyyy",
                    "MM/dd/yyyy",
                    "d/M/yyyy",
                    "dd/MM/yyyy",

                    // Extra variants just in case
                    "yyyy-M-d",
                    "yyyy/M/d",
                    "M-d-yyyy",
                    "MM-dd-yyyy",
                };

                if (DateTime.TryParseExact(
                        csvDate,
                        knownFormats,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out DateTime parsed))
                {
                    return parsed.ToString("yyyy/MM/dd"); // PHIS calendar format
                }

                // Last resort: let .NET try with invariant culture
                if (DateTime.TryParse(csvDate,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out parsed))
                {
                    LoggerService.LogInformation($"   ⚠️  Date parsed via general fallback: '{csvDate}' → '{parsed:yyyy/MM/dd}'");
                    return parsed.ToString("yyyy/MM/dd");
                }

                LoggerService.LogInformation($"   ⚠️  Failed to parse date: {csvDate}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"   ❌ Date conversion error: {ex.Message}");
                return string.Empty;
            }
        }


        /// <summary>
        /// Execute Client ID search
        /// </summary>
        private async Task ExecuteClientIdSearchAsync(string clientId)
        {
            try
            {
                 LoggerService.LogInformation($"   🔍 Locating Client ID search elements...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Step 1: Set the dropdown value to CLIENT_ID using JavaScript
                var dropdownInputId = "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_ClientNumberType:selectOneMenu_input";

                // Wait for dropdown to be present
                _wait.Until(d => d.FindElements(By.Id(dropdownInputId)).Count > 0);

                // Set dropdown value via JavaScript
                js.ExecuteScript($@"
            var dropdown = document.getElementById('{dropdownInputId}');
            if (dropdown) {{
                dropdown.value = 'CLIENT_ID';
                
                // Trigger change event for PrimeFaces
                var changeEvent = new Event('change', {{ bubbles: true }});
                dropdown.dispatchEvent(changeEvent);
                
                console.log('Dropdown set to CLIENT_ID');
            }}
        ");

                 LoggerService.LogInformation($"   ✅ Set dropdown to 'Client ID' via JavaScript");
                await Task.Delay(_phisConfig.AjaxWaitMs); // Wait for PrimeFaces to process

                // Step 2: Enter Client ID using JavaScript
                var clientNumberInputId = "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_ClientNumber:inputText";

                // Wait for input to be present
                _wait.Until(d => d.FindElements(By.Id(clientNumberInputId)).Count > 0);

                // Set input value via JavaScript
                js.ExecuteScript($@"
            var input = document.getElementById('{clientNumberInputId}');
            if (input) {{
                input.value = '{clientId}';
                
                // Trigger input events
                var inputEvent = new Event('input', {{ bubbles: true }});
                input.dispatchEvent(inputEvent);
                
                var changeEvent = new Event('change', {{ bubbles: true }});
                input.dispatchEvent(changeEvent);
                
                console.log('Client ID entered: {clientId}');
            }}
        ");

                 LoggerService.LogInformation($"   ✅ Entered Client ID: {clientId}");
                await Task.Delay(500); // Brief pause to ensure value is set

                // Step 3: Verify values were set
                var dropdownValue = (string)js.ExecuteScript($"return document.getElementById('{dropdownInputId}').value;");
                var inputValue = (string)js.ExecuteScript($"return document.getElementById('{clientNumberInputId}').value;");

                 LoggerService.LogInformation($"   🔍 Verification - Dropdown: {dropdownValue}, Input: {inputValue}");

                if (dropdownValue != "CLIENT_ID" || inputValue != clientId)
                {
                     LoggerService.LogInformation($"   ⚠️  WARNING: Values not set correctly!");
                     LoggerService.LogInformation($"      Expected dropdown: CLIENT_ID, Got: {dropdownValue}");
                     LoggerService.LogInformation($"      Expected input: {clientId}, Got: {inputValue}");
                }

                // Click search
                await ClickSearchButtonAsync();
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error in ExecuteClientIdSearchAsync: {ex.Message}");
                throw;
            }
        }




        /// <summary>
        /// Execute Name search
        /// </summary>
        private async Task ExecuteNameSearchAsync(string firstName, string lastName)
        {
            // Enter last name
            var lastNameInput = _driver.FindElement(By.Id(
                "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_lastName"));
            lastNameInput.Clear();
            lastNameInput.SendKeys(lastName);

            // Enter first name
            var firstNameInput = _driver.FindElement(By.Id(
                "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_firstName"));
            firstNameInput.Clear();
            firstNameInput.SendKeys(firstName);

            // Click search
            await ClickSearchButtonAsync();
        }








        #endregion Search Execution Methods






        #region Phase 3 - Set In Context


        /// <summary>
        /// Select the first search result and click "Set In Context"
        /// Used in Phase 3 after searching by Client ID
        /// </summary>
        public async Task<bool> SelectResultAndSetInContextAsync()
        {
            try
            {
                 LoggerService.LogInformation($"   🎯 Selecting search result and setting in context...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Step 1: Wait for search results table to be present
                _wait.Until(d => d.FindElements(By.Id("form:dataTable:dataTable_data")).Count > 0);

                // Step 2: Check the checkbox for the first result (row 0)
                var checkboxId = "form:dataTable:dataTable:0:j_idt198"; // This might vary, we'll use a more robust selector

                // More reliable: find checkbox in first row
                var firstRowCheckbox = _driver.FindElement(By.CssSelector(
                    "#form\\:dataTable\\:dataTable_data tr[data-ri='0'] .ui-chkbox-box"));

                // Click checkbox using JavaScript to ensure it works
                js.ExecuteScript("arguments[0].click();", firstRowCheckbox);

                 LoggerService.LogInformation($"   ✅ Selected first result checkbox");
                await Task.Delay(_phisConfig.AjaxWaitMs); // Wait for AJAX to process row selection

                // Step 3: Verify checkbox is checked
                var isChecked = firstRowCheckbox.GetAttribute("class").Contains("ui-state-active");
                if (!isChecked)
                {
                     LoggerService.LogInformation($"   ⚠️  WARNING: Checkbox might not be selected, retrying...");
                    js.ExecuteScript("arguments[0].click();", firstRowCheckbox);
                    await Task.Delay(500);
                }

                // Step 4: Click "Set In Context" button
                var setInContextButtonId = "form:dataTable:selectButtonId:actionButtonId:commandButtonId";

                // Wait for button to be clickable
                _wait.Until(d => d.FindElements(By.Id(setInContextButtonId)).Count > 0);

                var setInContextButton = _driver.FindElement(By.Id(setInContextButtonId));

                // Click using JavaScript for reliability
                js.ExecuteScript("arguments[0].click();", setInContextButton);

                 LoggerService.LogInformation($"   ✅ Clicked 'Set In Context' button");
                await Task.Delay(_phisConfig.PageLoadDelayMs); // Wait for page to process

                // Step 5: Verify success by checking if we're redirected or context is set
                // You might want to check for a success message or URL change here
                await Task.Delay(1000); // Extra time for page to update

                // Update session activity
                _sessionManager.UpdateActivity();

                 LoggerService.LogInformation($"   ✅ Client set in context successfully");
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error setting client in context: {ex.Message}");
                return false;
            }
        }




       

        /// <summary>
        /// Alternative method using network trace approach (more reliable for PrimeFaces)
        /// </summary>
        public async Task<bool> SelectResultAndSetInContextViaJavaScriptAsync()
        {
            try
            {
                 LoggerService.LogInformation($"   🎯 Selecting search result via JavaScript...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Step 1: Trigger the checkbox selection event via JavaScript
                // Based on network trace: rowSelectCheckbox event on row 0
                js.ExecuteScript(@"
            // Simulate PrimeFaces checkbox selection
            PrimeFaces.ajax.Request.handle({
                source: 'form:dataTable:dataTable',
                process: 'form:dataTable:dataTable',
                update: 'form:dataTable:rowActionsPanel',
                params: [
                    {name: 'javax.faces.behavior.event', value: 'rowSelectCheckbox'},
                    {name: 'javax.faces.partial.event', value: 'rowSelectCheckbox'},
                    {name: 'form:dataTable:dataTable_instantSelectedRowKey', value: '0'},
                    {name: 'form:dataTable:dataTable_selection', value: '0'}
                ],
                oncomplete: function() {
                    console.log('Row selected');
                }
            });
        ");

                 LoggerService.LogInformation($"   ✅ Row selection triggered via PrimeFaces AJAX");
                await Task.Delay(_phisConfig.AjaxWaitMs * 2); // Extra wait for AJAX

                // Step 2: Click "Set In Context" button
                var setInContextButtonId = "form:dataTable:selectButtonId:actionButtonId:commandButtonId";
                var setInContextButton = _driver.FindElement(By.Id(setInContextButtonId));

                js.ExecuteScript("arguments[0].click();", setInContextButton);

                 LoggerService.LogInformation($"   ✅ Clicked 'Set In Context' button");
                await Task.Delay(_phisConfig.PageLoadDelayMs);

                _sessionManager.UpdateActivity();

                 LoggerService.LogInformation($"   ✅ Client set in context successfully");
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error in SelectResultAndSetInContextViaJavaScriptAsync: {ex.Message}");
                return false;
            }
        }




        public async Task<bool> SearchByClientIdAndSetInContextAsync(string clientId)
        {
            try
            {
                 LoggerService.LogInformation($"   🔍 Phase 3: Searching for Client ID {clientId} and setting in context...");

                // Step 1: Search by Client ID
                var searchResult = await SearchByClientIdAsync(clientId);

                if (!searchResult.Success)
                {
                     LoggerService.LogInformation($"   ❌ Search failed: {searchResult.ErrorMessage}");
                    return false;
                }

                if (searchResult.Results.Count == 0)
                {
                     LoggerService.LogInformation($"   ❌ No results found for Client ID: {clientId}");
                    return false;
                }

                if (searchResult.Results.Count > 1)
                {
                     LoggerService.LogInformation($"   ⚠️  WARNING: Found {searchResult.Results.Count} results, expected 1");
                }

                // Step 2: Select result and set in context
                bool contextSet = await SelectResultAndSetInContextAsync();

                if (!contextSet)
                {
                     LoggerService.LogInformation($"   ⚠️  Standard method failed, trying JavaScript approach...");
                    contextSet = await SelectResultAndSetInContextViaJavaScriptAsync();
                }

                return contextSet;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error in Phase 3: {ex.Message}");
                return false;
            }
        }



        // Update the NavigateToImmunizationServiceAsync method to use config values:

        /// <summary>
        /// Navigate to Consent Directives > Immunization Service page
        /// Called after setting client in context
        /// </summary>
        public async Task<bool> NavigateToImmunizationServiceAsync()
        {
            try
            {
                 LoggerService.LogInformation($"   🧭 Navigating to Consent Directives > Immunization Service...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // ✅ Get navigation config
                var phase3Config = ConfigurationService.GetPhase3Config();
                var navConfig = phase3Config.Navigation;

                // Method 1: Direct URL navigation (most reliable)
                var baseUrl = _phisConfig.LoginUrl.Replace("/phsdsm/", "");
                var immunizationUrl = $"{baseUrl}{navConfig.ImmunizationServiceUrl}";

                 LoggerService.LogInformation($"   📍 Navigating to: {immunizationUrl}");
                _driver.Navigate().GoToUrl(immunizationUrl);

                // Wait for page to load
                await Task.Delay(_phisConfig.PageLoadDelayMs);

                // ✅ Wait for the page title to match expected value from config
                try
                {
                    _wait.Until(d =>
                    {
                        // Check if we're on the Immunization Service page
                        var titleElement = d.FindElements(By.Id(navConfig.PageTitleElementId));
                        if (titleElement.Count > 0)
                        {
                            var actualTitle = titleElement[0].Text;
                            var expectedTitle = navConfig.ImmunizationServicePageTitle;

                            bool titleMatches = actualTitle.Equals(expectedTitle, StringComparison.OrdinalIgnoreCase);

                            if (!titleMatches)
                            {
                                 LoggerService.LogInformation($"   ⚠️  Title mismatch:");
                                 LoggerService.LogInformation($"      Expected: '{expectedTitle}'");
                                 LoggerService.LogInformation($"      Got: '{actualTitle}'");
                            }

                            return titleMatches;
                        }
                        return false;
                    });

                     LoggerService.LogInformation($"   ✅ Page title verified: '{navConfig.ImmunizationServicePageTitle}'");
                     LoggerService.LogInformation($"   ✅ Successfully navigated to Immunization Service page");

                    // Update session activity
                    _sessionManager.UpdateActivity();

                    return true;
                }
                catch (WebDriverTimeoutException)
                {
                     LoggerService.LogInformation($"   ⚠️  Page title verification timed out");

                    // Check if we can find the consent table as fallback verification
                    var consentTable = _driver.FindElements(By.CssSelector("table[role='grid']"));
                    if (consentTable.Count > 0)
                    {
                         LoggerService.LogInformation($"   ✅ Found consent table - assuming navigation successful");
                        _sessionManager.UpdateActivity();
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error navigating to Immunization Service: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Alternative method: Navigate using menu clicks (if direct URL doesn't work)
        /// </summary>
        public async Task<bool> NavigateToImmunizationServiceViaMenuAsync()
        {
            try
            {
                 LoggerService.LogInformation($"   🧭 Navigating via menu: Consent Directives > Immunization Service...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Step 1: Find and expand "Consent Directives" menu
                 LoggerService.LogInformation($"   📂 Expanding 'Consent Directives' menu...");

                var consentDirectivesMenuId = "menu:comibmpdcephsimmunization_ConsentDirectives";

                // Wait for menu to be present
                _wait.Until(d => d.FindElements(By.Id(consentDirectivesMenuId)).Count > 0);

                // Check if menu is already expanded
                var consentDirectivesMenu = _driver.FindElement(By.Id(consentDirectivesMenuId));
                var isExpanded = consentDirectivesMenu.GetAttribute("class").Contains("layout-menubar-subfolder-open");

                if (!isExpanded)
                {
                    // Click to expand
                    var menuToggle = consentDirectivesMenu.FindElement(By.CssSelector("a[onclick*='toggleSubMenu']"));
                    js.ExecuteScript("arguments[0].click();", menuToggle);

                     LoggerService.LogInformation($"   ✅ Expanded 'Consent Directives' menu");
                    await Task.Delay(500); // Wait for menu animation
                }
                else
                {
                     LoggerService.LogInformation($"   ℹ️  'Consent Directives' menu already expanded");
                }

                // Step 2: Click "Immunization Service" submenu item
                 LoggerService.LogInformation($"   🎯 Clicking 'Immunization Service'...");

                var immunizationServiceMenuId = "menu:comibmpdcephsimmunization_ImmunizationService";
                var immunizationServiceLink = _driver.FindElement(By.CssSelector($"#{immunizationServiceMenuId} a"));

                js.ExecuteScript("arguments[0].click();", immunizationServiceLink);

                 LoggerService.LogInformation($"   ✅ Clicked 'Immunization Service'");
                await Task.Delay(_phisConfig.PageLoadDelayMs);

                // Step 3: Verify we're on the correct page
                _wait.Until(d =>
                {
                    var titleElement = d.FindElements(By.Id("layout-toolbar-title"));
                    if (titleElement.Count > 0)
                    {
                        var title = titleElement[0].Text;
                        return title.Contains("Consent Directives") && title.Contains("Immunization Service");
                    }
                    return false;
                });

                 LoggerService.LogInformation($"   ✅ Successfully navigated to Immunization Service page via menu");

                // Update session activity
                _sessionManager.UpdateActivity();

                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error navigating via menu: {ex.Message}");
                return false;
            }
        }



        #endregion Phase 3 - Set In Context





        #region Phase 3 - Consent Directive Selection





        /// <summary>
        /// Find and select the consent directive row matching the specified antigen
        /// Used after navigating to Immunization Service page
        /// </summary>
        /// <param name="phisAntigen">The antigen name to search for (e.g., "HPV-9", "Tetanus (T)", "Men-C-ACYW-135")</param>
        /// <returns>True if row found and selected, false otherwise</returns>
        public async Task<bool> SelectConsentDirectiveByAntigenAsync(string phisAntigen)
        {
            try
            {
                 LoggerService.LogInformation($"   🔍 Searching for consent directive with antigen: '{phisAntigen}'");

                // Wait for the page to fully load after navigation
                await Task.Delay(2000); // Give time for the table to load

                // Expand the table so rows beyond page 1 are visible before searching.
                await EnsureAllConsentRowsDisplayedAsync();

                // First attempt: Search with current filters (Active only by default)
                int matchingRowIndex = await FindConsentDirectiveRowAsync(phisAntigen);

                if (matchingRowIndex == -1)
                {
                     LoggerService.LogInformation($"   ⚠️  Not found in Active records (or table is empty)");
                     LoggerService.LogInformation($"   🔧 Applying Inactive filter to show all records...");

                    // Apply filter to show both Active and Inactive
                    bool filterApplied = await ApplyActiveInactiveFilterAsync();

                    if (!filterApplied)
                    {
                         LoggerService.LogInformation($"   ❌ Failed to apply Active/Inactive filter");
                        return false;
                    }

                     LoggerService.LogInformation($"   ✅ Filter applied successfully");

                    await EnsureAllConsentRowsDisplayedAsync();

                    // Second attempt: Search with Active + Inactive filters
                    matchingRowIndex = await FindConsentDirectiveRowAsync(phisAntigen);

                    if (matchingRowIndex == -1)
                    {
                         LoggerService.LogInformation($"   ❌ No consent directive found for antigen: '{phisAntigen}' (even with Inactive filter)");
                        return false;
                    }
                }

                // Select the checkbox for the matching row
                 LoggerService.LogInformation($"   🎯 Found at row {matchingRowIndex}, selecting checkbox...");

                return await SelectConsentDirectiveCheckboxAsync(matchingRowIndex);
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error selecting consent directive: {ex.Message}");
                return false;
            }
        }



        /// <summary>
        /// Find the row index for a consent directive matching the specified antigen
        /// Returns -1 if not found
        /// </summary>
        private async Task<int> FindConsentDirectiveRowAsync(string phisAntigen)
        {
            try
            {
                // Wait for the consent directives table to be present
                var tableId = "consentForm:ConsentDataTable:dataTable_data";

                try
                {
                    _wait.Until(d => d.FindElements(By.Id(tableId)).Count > 0);
                }
                catch (WebDriverTimeoutException)
                {
                     LoggerService.LogInformation($"      ⚠️  Consent directives table not found");
                    return -1;
                }

                 LoggerService.LogInformation($"      📊 Table loaded, searching for antigen...");

                // Find all rows in the table
                var tbody = _driver.FindElement(By.Id(tableId));
                var rows = tbody.FindElements(By.XPath(".//tr[@role='row']"));

                if (rows.Count == 0)
                {
                     LoggerService.LogInformation($"      ⚠️  Table is empty (no consent directives found)");
                    return -1;
                }

                 LoggerService.LogInformation($"      📊 Found {rows.Count} consent directive(s) in table");

                // Search for the row with matching antigen
                for (int i = 0; i < rows.Count; i++)
                {
                    try
                    {
                        // Get all cells in the row
                        var cells = rows[i].FindElements(By.TagName("td"));

                        // Based on your HTML, the structure is:
                        // [0: Checkbox] [1: Toggle] [2: Icon] [3: Status] [4: Instruction] [5: Directive Type] [6: Antigen] [7: Active] [8: Effective From] [9: Effective To]
                        if (cells.Count > 6)
                        {
                            var antigenText = cells[6].Text.Trim(); // Antigen is at index 6

                             LoggerService.LogInformation($"         Row {i}: Antigen = '{antigenText}' (data-ri='{rows[i].GetAttribute("data-ri")}')");

                            if (antigenText.Equals(phisAntigen, StringComparison.OrdinalIgnoreCase))
                            {
                                 LoggerService.LogInformation($"         ✅ MATCH FOUND at row {i}!");
                                return i;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                         LoggerService.LogInformation($"         ⚠️  Error reading row {i}: {ex.Message}");
                    }
                }

                 LoggerService.LogInformation($"      ❌ Antigen '{phisAntigen}' not found in {rows.Count} row(s)");
                return -1; // Not found
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"      ❌ Error searching for consent directive: {ex.Message}");
                return -1;
            }
        }




        /// <summary>
        /// Apply Active/Inactive filter via AJAX request (fallback method)
        /// </summary>
        private async Task<bool> ApplyActiveInactiveFilterViaAjaxAsync()
        {
            try
            {
                 LoggerService.LogInformation($"      🔄 Trying AJAX method to apply filter...");
                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                var ajaxScript = @"
            try {
                console.log('Sending AJAX filter request...');

                var appendParam = function(params, name, value) {
                    params.push({ name: name, value: value == null ? '' : value });
                };
                var getValueById = function(id) {
                    var element = document.getElementById(id);
                    return element ? element.value || '' : '';
                };
                var getCheckedValues = function(prefix) {
                    return Array.from(document.querySelectorAll('input[id^=""' + prefix + '""]:checked'))
                        .map(function(input) { return input.value || input.getAttribute('value') || ''; })
                        .filter(function(value) { return !!value; });
                };

                var viewState = document.querySelector('input[name=""javax.faces.ViewState""]')?.value || '';
                var rowsSelect = document.querySelector('select[name=""consentForm:ConsentDataTable:dataTable_rppDD""]');
                var rowsValue = rowsSelect && rowsSelect.value ? rowsSelect.value : '130';
                var activeFilters = getCheckedValues('consentForm:ConsentDataTable:dataTable:activeFilter:');

                if (activeFilters.indexOf('Active') === -1) {
                    activeFilters.unshift('Active');
                }
                if (activeFilters.indexOf('Inactive') === -1) {
                    activeFilters.push('Inactive');
                }

                var params = [];
                appendParam(params, 'javax.faces.partial.ajax', 'true');
                appendParam(params, 'javax.faces.source', 'consentForm:ConsentDataTable:dataTable');
                appendParam(params, 'primefaces.ignoreautoupdate', 'true');
                appendParam(params, 'javax.faces.partial.execute', 'consentForm:ConsentDataTable:dataTable');
                appendParam(params, 'javax.faces.partial.render', 'consentForm:ConsentDataTable:rowActionsPanel consentForm:ConsentDataTable:dataTable');
                appendParam(params, 'javax.faces.behavior.event', 'filter');
                appendParam(params, 'javax.faces.partial.event', 'filter');
                appendParam(params, 'consentForm:ConsentDataTable:dataTable_filtering', 'true');
                appendParam(params, 'consentForm:ConsentDataTable:dataTable_encodeFeature', 'true');
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:statusFilter_focus', getValueById('consentForm:ConsentDataTable:dataTable:statusFilter_focus'));
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:statusFilter', getValueById('consentForm:ConsentDataTable:dataTable:statusFilter'));
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:instructionFilter_focus', getValueById('consentForm:ConsentDataTable:dataTable:instructionFilter_focus'));
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:antigenFilter_focus', getValueById('consentForm:ConsentDataTable:dataTable:antigenFilter_focus'));
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:activeFilter_focus', getValueById('consentForm:ConsentDataTable:dataTable:activeFilter_focus'));

                activeFilters.forEach(function(value) {
                    appendParam(params, 'consentForm:ConsentDataTable:dataTable:activeFilter', value);
                });

                appendParam(params, 'consentForm:ConsentDataTable:dataTable_rppDD', rowsValue);
                appendParam(params, 'consentForm:ConsentDataTable:dataTable_selection', getValueById('consentForm:ConsentDataTable:dataTable_selection'));
                appendParam(params, 'javax.faces.ViewState', viewState);

                if (typeof PrimeFaces !== 'undefined' && PrimeFaces.ajax) {
                    PrimeFaces.ajax.Request.handle({
                        source: 'consentForm:ConsentDataTable:dataTable',
                        process: 'consentForm:ConsentDataTable:dataTable',
                        update: 'consentForm:ConsentDataTable:rowActionsPanel consentForm:ConsentDataTable:dataTable',
                        formId: 'consentForm',
                        params: params,
                        oncomplete: function(xhr, status, args) {
                            console.log('AJAX filter complete. Status:', status);
                        }
                    });
                    return true;
                }

                console.error('PrimeFaces not available');
                return false;
            } catch (error) {
                console.error('AJAX error:', error);
                return false;
            }
        ";

                var ajaxResult = js.ExecuteScript(ajaxScript);

                if (ajaxResult is bool ajaxBoolResult && !ajaxBoolResult)
                {
                     LoggerService.LogInformation($"      ❌ AJAX method failed");
                    return await ApplyActiveInactiveFilterViaClickAsync();
                }

                 LoggerService.LogInformation($"      ✅ AJAX filter request sent");
                await Task.Delay(_phisConfig.AjaxWaitMs * 3);

                _sessionManager.UpdateActivity();
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"      ❌ AJAX method error: {ex.Message}");
                return await ApplyActiveInactiveFilterViaClickAsync();
            }
        }











        /// <summary>
        /// Apply Active and Inactive filter to show all consent directives
        /// Based on network trace and PrimeFaces SelectCheckboxMenu widget
        /// </summary>
        private async Task<bool> ApplyActiveInactiveFilterAsync()
        {
            try
            {
                 LoggerService.LogInformation($"      🔧 Applying Active + Inactive filter...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Method 1: Use PrimeFaces widget to check the Inactive checkbox
                var widgetScript = @"
            try {
                console.log('Step 1: Checking if Inactive checkbox is already checked...');
                
                // Get the Inactive checkbox element
                var inactiveCheckbox = document.getElementById('consentForm:ConsentDataTable:dataTable:activeFilter:1');
                
                if (!inactiveCheckbox) {
                    console.error('Inactive checkbox not found!');
                    return false;
                }
                
                // Check if it's already checked
                if (inactiveCheckbox.checked) {
                    console.log('Inactive checkbox is already checked');
                    return true;
                }
                
                console.log('Step 2: Checking the Inactive checkbox...');
                
                // Check the checkbox
                inactiveCheckbox.checked = true;
                
                // Trigger the onchange event to make PrimeFaces filter
                var changeEvent = new Event('change', { bubbles: true });
                inactiveCheckbox.dispatchEvent(changeEvent);
                
                // Also trigger the PrimeFaces widget filter directly
                if (typeof PF === 'function' && PF('widget_consentForm_ConsentDataTable_dataTable')) {
                    console.log('Step 3: Triggering PrimeFaces filter...');
                    PF('widget_consentForm_ConsentDataTable_dataTable').filter();
                }
                
                console.log('Filter applied successfully!');
                return true;
                
            } catch (error) {
                console.error('Error applying filter:', error);
                return false;
            }
        ";

                var result = js.ExecuteScript(widgetScript);

                if (result is bool boolResult && !boolResult)
                {
                     LoggerService.LogInformation($"      ⚠️  Widget method failed, trying AJAX method...");
                    return await ApplyActiveInactiveFilterViaAjaxAsync();
                }

                 LoggerService.LogInformation($"      ✅ Inactive filter checkbox checked");

                // Wait for the filter to apply and table to refresh
                 LoggerService.LogInformation($"      ⏳ Waiting for table to refresh...");
                await Task.Delay(_phisConfig.AjaxWaitMs * 3);

                // Verify the filter was applied by checking if table has more rows
                var tableId = "consentForm:ConsentDataTable:dataTable_data";
                var tbody = _driver.FindElements(By.Id(tableId));

                if (tbody.Count > 0)
                {
                    var rows = tbody[0].FindElements(By.XPath(".//tr[@role='row']"));
                     LoggerService.LogInformation($"      ✅ Table now has {rows.Count} row(s)");
                }

                _sessionManager.UpdateActivity();
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"      ❌ Error applying filter: {ex.Message}");
                return await ApplyActiveInactiveFilterViaAjaxAsync();
            }
        }




        /// <summary>
        /// Apply Active/Inactive filter by directly clicking UI elements (last resort fallback)
        /// </summary>
        private async Task<bool> ApplyActiveInactiveFilterViaClickAsync()
        {
            try
            {
                 LoggerService.LogInformation($"      🖱️  Trying direct click method...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Step 1: Find the Inactive checkbox and check it
                var inactiveCheckboxId = "consentForm:ConsentDataTable:dataTable:activeFilter:1";

                _wait.Until(d => d.FindElements(By.Id(inactiveCheckboxId)).Count > 0);

                var inactiveCheckbox = _driver.FindElement(By.Id(inactiveCheckboxId));

                // Check if already checked
                bool isChecked = inactiveCheckbox.Selected || inactiveCheckbox.GetAttribute("checked") == "checked";

                if (isChecked)
                {
                     LoggerService.LogInformation($"      ℹ️  Inactive checkbox is already checked");
                    return true;
                }

                // Click the checkbox using JavaScript
                js.ExecuteScript(@"
            var checkbox = arguments[0];
            checkbox.checked = true;
            checkbox.dispatchEvent(new Event('change', { bubbles: true }));
        ", inactiveCheckbox);

                 LoggerService.LogInformation($"      ✅ Clicked Inactive checkbox");

                // Wait for filter to apply
                await Task.Delay(_phisConfig.AjaxWaitMs * 3);

                _sessionManager.UpdateActivity();
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"      ❌ Direct click failed: {ex.Message}");
                return false;
            }
        }



        /// <summary>
        /// Select the checkbox for a specific consent directive row
        /// </summary>
        private async Task<bool> SelectConsentDirectiveCheckboxAsync(int rowIndex)
        {
            try
            {
                var tableId = "consentForm:ConsentDataTable:dataTable_data";
                var tbody = _driver.FindElement(By.Id(tableId));
                var rows = tbody.FindElements(By.XPath(".//tr[@role='row']"));

                if (rowIndex >= rows.Count)
                {
                     LoggerService.LogInformation($"   ❌ Row index {rowIndex} out of bounds (total rows: {rows.Count})");
                    return false;
                }

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;
                var targetRow = rows[rowIndex];

                 LoggerService.LogInformation($"   🎯 Attempting to select row {rowIndex}...");

                // Method 1: Try direct checkbox click first (most reliable for visual feedback)
                try
                {
                     LoggerService.LogInformation($"   🖱️  Method 1: Direct checkbox click...");

                    // Find the checkbox in the row
                    var checkboxCell = targetRow.FindElement(By.CssSelector(".ui-chkbox"));
                    var checkboxBox = checkboxCell.FindElement(By.CssSelector(".ui-chkbox-box"));
                    var checkboxInput = checkboxCell.FindElement(By.CssSelector("input[type='checkbox']"));

                    // Scroll to make checkbox visible
                    js.ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", checkboxBox);
                    await Task.Delay(300);

                    // Get the data-rk attribute (row key) for the AJAX call
                    var rowKey = targetRow.GetAttribute("data-rk");
                    var dataRi = targetRow.GetAttribute("data-ri");

                     LoggerService.LogInformation($"      Row key: {rowKey}, Data-ri: {dataRi}");

                    // Click the checkbox box (visual element)
                    js.ExecuteScript("arguments[0].click();", checkboxBox);

                     LoggerService.LogInformation($"      ✅ Checkbox clicked");
                    await Task.Delay(_phisConfig.AjaxWaitMs);

                    // Verify the checkbox is visually checked
                    var checkboxClasses = checkboxBox.GetAttribute("class") ?? string.Empty;
                    var isVisuallyChecked = checkboxClasses.Contains("ui-state-active");
                    var isInputChecked = checkboxInput.Selected || checkboxInput.GetAttribute("checked") == "checked";

                     LoggerService.LogInformation($"      📊 Visual check: {isVisuallyChecked}, Input checked: {isInputChecked}");

                    if (isVisuallyChecked || isInputChecked)
                    {
                         LoggerService.LogInformation($"   ✅ Checkbox is visually checked!");

                        // Verify Documents button is enabled
                        bool buttonsEnabled = await VerifyRowSelectionAsync();

                        if (buttonsEnabled)
                        {
                             LoggerService.LogInformation($"   ✅ Documents button is enabled - selection confirmed!");
                            _sessionManager.UpdateActivity();
                            return true;
                        }
                        else
                        {
                             LoggerService.LogInformation($"   ⚠️  Checkbox checked but Documents button not enabled yet");
                            await Task.Delay(1000); // Give more time

                            // Check again
                            buttonsEnabled = await VerifyRowSelectionAsync();
                            if (buttonsEnabled)
                            {
                                 LoggerService.LogInformation($"   ✅ Documents button now enabled!");
                                _sessionManager.UpdateActivity();
                                return true;
                            }
                        }
                    }
                    else
                    {
                         LoggerService.LogInformation($"   ⚠️  Checkbox not visually checked after click, trying AJAX method...");
                    }
                }
                catch (Exception ex)
                {
                     LoggerService.LogInformation($"   ⚠️  Direct click failed: {ex.Message}");
                }

                // Method 2: Use PrimeFaces AJAX as fallback
                 LoggerService.LogInformation($"   🔄 Method 2: Using PrimeFaces AJAX...");

                var rowKey2 = targetRow.GetAttribute("data-rk");
                var dataRi2 = targetRow.GetAttribute("data-ri");

                bool ajaxSuccess = await SelectConsentDirectiveViaAjaxAsync(rowIndex, rowKey2, dataRi2);

                if (ajaxSuccess)
                {
                    // Wait for UI to update
                    await Task.Delay(1500);

                    // Refresh the row element (DOM may have changed)
                    tbody = _driver.FindElement(By.Id(tableId));
                    rows = tbody.FindElements(By.XPath(".//tr[@role='row']"));

                    if (rowIndex < rows.Count)
                    {
                        var updatedRow = rows[rowIndex];
                        var checkboxes = updatedRow.FindElements(By.CssSelector(".ui-chkbox-box.ui-state-active"));

                        if (checkboxes.Count > 0)
                        {
                             LoggerService.LogInformation($"   ✅ Checkbox is now visually checked after AJAX!");
                        }
                        else
                        {
                             LoggerService.LogInformation($"   ⚠️  AJAX completed but checkbox still not visually checked");
                        }
                    }

                    // Verify Documents button
                    bool buttonsEnabled = await VerifyRowSelectionAsync();

                    if (buttonsEnabled)
                    {
                         LoggerService.LogInformation($"   ✅ Row selection confirmed via Documents button!");
                        _sessionManager.UpdateActivity();
                        return true;
                    }
                    else
                    {
                         LoggerService.LogInformation($"   ⚠️  Documents button not enabled - selection may have failed");
                        return false;
                    }
                }

                 LoggerService.LogInformation($"   ❌ All selection methods failed");
                return false;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error selecting checkbox: {ex.Message}");
                 LoggerService.LogInformation($"      Stack trace: {ex.StackTrace}");
                return false;
            }
        }




        /// <summary>
        /// Select consent directive row using PrimeFaces AJAX request
        /// </summary>
        private async Task<bool> SelectConsentDirectiveViaAjaxAsync(int rowIndex, string? rowKey, string? dataRi)
        {
            try
            {
                 LoggerService.LogInformation($"   📡 Sending PrimeFaces AJAX selection...");
                 LoggerService.LogInformation($"      Row index: {rowIndex}, Row key: {rowKey}, Data-ri: {dataRi}");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Use rowKey if available, otherwise use rowIndex
                var selectionKey = !string.IsNullOrEmpty(rowKey) ? rowKey : rowIndex.ToString();

                var ajaxScript = $@"
            try {{
                console.log('Sending row selection AJAX for row index {rowIndex}, key: {selectionKey}');

                var appendParam = function(params, name, value) {{
                    params.push({{ name: name, value: value == null ? '' : value }});
                }};
                var getValueById = function(id) {{
                    var element = document.getElementById(id);
                    return element ? element.value || '' : '';
                }};
                var getCheckedValues = function(prefix) {{
                    return Array.from(document.querySelectorAll('input[id^=""' + prefix + '""]:checked'))
                        .map(function(input) {{ return input.value || input.getAttribute('value') || ''; }})
                        .filter(function(value) {{ return !!value; }});
                }};

                var viewState = document.querySelector('input[name=""javax.faces.ViewState""]')?.value || '';
                var rowsSelect = document.querySelector('select[name=""consentForm:ConsentDataTable:dataTable_rppDD""]');
                var rowsValue = rowsSelect && rowsSelect.value ? rowsSelect.value : '130';
                var activeFilters = getCheckedValues('consentForm:ConsentDataTable:dataTable:activeFilter:');

                if (activeFilters.length === 0) {{
                    activeFilters.push('Active');
                }}

                var params = [];
                appendParam(params, 'javax.faces.partial.ajax', 'true');
                appendParam(params, 'javax.faces.source', 'consentForm:ConsentDataTable:dataTable');
                appendParam(params, 'javax.faces.partial.execute', 'consentForm:ConsentDataTable:dataTable');
                appendParam(params, 'javax.faces.partial.render', 'consentForm:ConsentDataTable:rowActionsPanel');
                appendParam(params, 'javax.faces.behavior.event', 'rowSelectCheckbox');
                appendParam(params, 'javax.faces.partial.event', 'rowSelectCheckbox');
                appendParam(params, 'consentForm:ConsentDataTable:dataTable_instantSelectedRowKey', '{selectionKey}');
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:statusFilter_focus', getValueById('consentForm:ConsentDataTable:dataTable:statusFilter_focus'));
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:statusFilter', getValueById('consentForm:ConsentDataTable:dataTable:statusFilter'));
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:instructionFilter_focus', getValueById('consentForm:ConsentDataTable:dataTable:instructionFilter_focus'));
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:antigenFilter_focus', getValueById('consentForm:ConsentDataTable:dataTable:antigenFilter_focus'));
                appendParam(params, 'consentForm:ConsentDataTable:dataTable:activeFilter_focus', getValueById('consentForm:ConsentDataTable:dataTable:activeFilter_focus'));

                activeFilters.forEach(function(value) {{
                    appendParam(params, 'consentForm:ConsentDataTable:dataTable:activeFilter', value);
                }});

                appendParam(params, 'consentForm:ConsentDataTable:dataTable_checkbox', 'on');
                appendParam(params, 'consentForm:ConsentDataTable:dataTable_rppDD', rowsValue);
                appendParam(params, 'consentForm:ConsentDataTable:dataTable_selection', '{selectionKey}');
                appendParam(params, 'javax.faces.ViewState', viewState);

                PrimeFaces.ajax.Request.handle({{
                    source: 'consentForm:ConsentDataTable:dataTable',
                    process: 'consentForm:ConsentDataTable:dataTable',
                    update: 'consentForm:ConsentDataTable:rowActionsPanel',
                    params: params,
                    formId: 'consentForm',
                    oncomplete: function(xhr, status, args) {{
                        console.log('AJAX complete. Status:', status);
                    }}
                }});
                
                return true;
            }} catch (error) {{
                console.error('AJAX error:', error);
                return false;
            }}
        ";

                var result = js.ExecuteScript(ajaxScript);

                if (result is bool boolResult && boolResult)
                {
                     LoggerService.LogInformation($"   ✅ AJAX request sent successfully");
                    await Task.Delay(_phisConfig.AjaxWaitMs * 2);
                    _sessionManager.UpdateActivity();
                    return true;
                }
                else
                {
                     LoggerService.LogInformation($"   ❌ AJAX request failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ AJAX selection error: {ex.Message}");
                return false;
            }
        }









        /// <summary>
        /// Verify that a row is selected by checking if the Documents button is enabled
        /// </summary>
        private async Task<bool> VerifyRowSelectionAsync()
        {
            try
            {
                await Task.Delay(500);

                var documentsButtonId = "consentForm:ConsentDataTable:DocumentsButton:actionButtonId:commandButtonId";
                var documentsButton = _driver.FindElements(By.Id(documentsButtonId));

                if (documentsButton.Count > 0)
                {
                    var isDisabled = documentsButton[0].GetAttribute("disabled");
                    var classes = documentsButton[0].GetAttribute("class") ?? string.Empty;

                    bool isEnabled = string.IsNullOrEmpty(isDisabled) && !classes.Contains("ui-state-disabled");

                    if (isEnabled)
                    {
                         LoggerService.LogInformation($"      ✅ Documents button is enabled");
                        return true;
                    }
                    else
                    {
                         LoggerService.LogInformation($"      ℹ️  Documents button is disabled");
                        return false;
                    }
                }

                 LoggerService.LogInformation($"      ⚠️  Documents button not found");

                // Fallback: check for checked checkboxes
                var checkedBoxes = _driver.FindElements(By.CssSelector(
                    "#consentForm\\:ConsentDataTable\\:dataTable_data .ui-chkbox-box.ui-state-active"));

                var count = checkedBoxes.Count;
                 LoggerService.LogInformation($"      ℹ️  Found {count} checked checkbox(es)");

                return count > 0;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"      ⚠️  Verification error: {ex.Message}");
                return false;
            }
        }



        private async Task EnsureAllConsentRowsDisplayedAsync()
        {
            const string rowsPerPageName = "consentForm:ConsentDataTable:dataTable_rppDD";
            const string tableId = "consentForm:ConsentDataTable:dataTable_data";

            try
            {
                _wait.Until(d => d.FindElements(By.Id(tableId)).Count > 0);

                var rowsPerPageElement = _driver.FindElements(By.Name(rowsPerPageName)).FirstOrDefault();
                if (rowsPerPageElement == null)
                {
                     LoggerService.LogInformation($"      ⚠️  Consent rows-per-page selector not found; continuing with visible rows only");
                    return;
                }

                var currentRowsValue = GetSelectedConsentRowsPerPageValue(rowsPerPageElement);
                var maxRowsValue = GetMaxConsentRowsPerPageValue(rowsPerPageElement);
                var currentRowCount = GetConsentDirectiveRowCount();

                if (string.Equals(currentRowsValue, maxRowsValue, StringComparison.OrdinalIgnoreCase))
                {
                     LoggerService.LogInformation($"      ℹ️  Consent rows already set to max ({maxRowsValue}); visible row count: {currentRowCount}");
                    return;
                }

                 LoggerService.LogInformation($"      ⚙️  Expanding consent rows-per-page from {currentRowsValue} to {maxRowsValue}...");

                bool widgetChangedRows = false;

                try
                {
                    IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;
                    var jsResult = js.ExecuteScript(@"
                var targetValue = arguments[0];
                try {
                    var tableWidget = typeof PF === 'function'
                        ? PF('widget_consentForm_ConsentDataTable_dataTable')
                        : (window.PrimeFaces && PrimeFaces.widgets
                            ? PrimeFaces.widgets['widget_consentForm_ConsentDataTable_dataTable']
                            : null);
                    var rowsSelect = document.querySelector('select[name=""consentForm:ConsentDataTable:dataTable_rppDD""]');

                    if (tableWidget && tableWidget.getPaginator && tableWidget.getPaginator()) {
                        tableWidget.getPaginator().setRows(parseInt(targetValue, 10));
                        return true;
                    }

                    if (rowsSelect) {
                        rowsSelect.value = targetValue;
                        rowsSelect.dispatchEvent(new Event('change', { bubbles: true }));
                        return true;
                    }

                    return false;
                } catch (error) {
                    console.error('Could not expand consent rows-per-page', error);
                    return false;
                }", maxRowsValue);

                    widgetChangedRows = jsResult is bool boolResult && boolResult;
                }
                catch (Exception ex)
                {
                     LoggerService.LogInformation($"      ⚠️  PrimeFaces rows-per-page update failed: {ex.Message}");
                }

                if (!widgetChangedRows)
                {
                    try
                    {
                        var select = new SelectElement(rowsPerPageElement);
                        select.SelectByValue(maxRowsValue);
                    }
                    catch (NoSuchElementException)
                    {
                        var select = new SelectElement(rowsPerPageElement);
                        select.SelectByText(maxRowsValue);
                    }
                }

                await WaitForConsentTableExpansionAsync(maxRowsValue);

                var expandedRowCount = GetConsentDirectiveRowCount();
                 LoggerService.LogInformation($"      ✅ Consent rows-per-page now {maxRowsValue}; visible row count: {expandedRowCount}");
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"      ⚠️  Could not ensure all consent rows are displayed: {ex.Message}");
            }
        }



        private async Task WaitForConsentTableExpansionAsync(string expectedRowsValue)
        {
            const string rowsPerPageName = "consentForm:ConsentDataTable:dataTable_rppDD";
            const string tableId = "consentForm:ConsentDataTable:dataTable_data";

            _wait.Until(d =>
            {
                var rowsPerPageElement = d.FindElements(By.Name(rowsPerPageName)).FirstOrDefault();
                if (rowsPerPageElement == null)
                {
                    return false;
                }

                var selectedValue = GetSelectedConsentRowsPerPageValue(rowsPerPageElement);
                if (!string.Equals(selectedValue, expectedRowsValue, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return d.FindElements(By.Id(tableId)).Count > 0;
            });

            await Task.Delay(_phisConfig.AjaxWaitMs);
            await Task.Delay(250);
        }



        private int GetConsentDirectiveRowCount()
        {
            var tbody = _driver.FindElements(By.Id("consentForm:ConsentDataTable:dataTable_data")).FirstOrDefault();
            if (tbody == null)
            {
                return 0;
            }

            return tbody.FindElements(By.XPath(".//tr[@role='row']")).Count;
        }



        private static string GetSelectedConsentRowsPerPageValue(IWebElement rowsPerPageElement)
        {
            try
            {
                var select = new SelectElement(rowsPerPageElement);
                var selectedOption = select.SelectedOption;
                var selectedValue = selectedOption?.GetAttribute("value");

                if (!string.IsNullOrWhiteSpace(selectedValue))
                {
                    return selectedValue.Trim();
                }

                if (!string.IsNullOrWhiteSpace(selectedOption?.Text))
                {
                    return selectedOption.Text.Trim();
                }
            }
            catch
            {
                // Fall through to element value handling.
            }

            return rowsPerPageElement.GetAttribute("value")?.Trim() ?? "130";
        }



        private static string GetMaxConsentRowsPerPageValue(IWebElement rowsPerPageElement)
        {
            const string fallbackValue = "130";

            try
            {
                var select = new SelectElement(rowsPerPageElement);
                var numericOptionValues = select.Options
                    .Select(option => option.GetAttribute("value")?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => int.TryParse(value, out var parsedValue)
                        ? new KeyValuePair<string, int>(value!, parsedValue)
                        : (KeyValuePair<string, int>?)null)
                    .Where(option => option.HasValue)
                    .Select(option => option!.Value)
                    .ToList();

                if (numericOptionValues.Count > 0)
                {
                    return numericOptionValues
                        .OrderByDescending(option => option.Value)
                        .First()
                        .Key;
                }

                var lastOptionValue = select.Options
                    .Select(option => option.GetAttribute("value")?.Trim())
                    .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (!string.IsNullOrWhiteSpace(lastOptionValue))
                {
                    return lastOptionValue;
                }
            }
            catch
            {
                // Fall back to the known PHIS "ALL" value.
            }

            return fallbackValue;
        }



        #endregion Phase 3 - Consent Directive Selection



        #region Helper Methods


        /// <summary>
        /// Select search criteria type (DOB, Client ID, etc.)
        /// </summary>
        private async Task SelectSearchCriteriaTypeAsync(string criteriaType)
        {
            try
            {
                var radioButton = _driver.FindElement(By.CssSelector(
                    $"input[name='form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:selectOneRadio'][value='{criteriaType}']"));

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;
                js.ExecuteScript("arguments[0].click();", radioButton);

                await Task.Delay(_phisConfig.AjaxWaitMs);
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ⚠️  Could not select criteria type: {ex.Message}");
            }
        }


        /// <summary>
        /// Click search button
        /// </summary>
        private async Task ClickSearchButtonAsync()
        {
            var searchButton = _driver.FindElement(By.Id("actionMenuSearch:commandButtonId"));
            searchButton.Click();

             LoggerService.LogInformation($"   🔎 Search clicked");
            await Task.Delay(_phisConfig.PageLoadDelayMs);
        }




        /// <summary>
        /// Wait for search results to appear
        /// </summary>
        private async Task WaitForSearchResultsAsync()
        {
            _wait.Until(d =>
            {
                try
                {
                    // Check for results table
                    var tbody = d.FindElement(By.Id("form:dataTable:dataTable_data"));
                    var rows = tbody.FindElements(By.XPath(".//tr[@role='row']"));
                    return rows.Count > 0;
                }
                catch
                {
                    // Check for "no results" message
                    var messages = d.FindElements(By.CssSelector(".ui-messages-info, .ui-messages-warn"));
                    return messages.Count > 0;
                }
            });

            await Task.Delay(500); // Extra stability delay
        }



        /// <summary>
        /// Ensure we're on the search page
        /// </summary>
        private async Task EnsureOnSearchPageAsync()
        {
            var searchForm = _driver.FindElements(By.Id(
                "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input"));

            if (searchForm.Count > 0) return; // Already on search page

             LoggerService.LogInformation($"   🔄 Navigating to search page...");

            _driver.Navigate().GoToUrl(_phisConfig.SearchUrl);
            await Task.Delay(_phisConfig.PageLoadDelayMs);

            // Wait for page to load
            _wait.Until(d => d.FindElements(By.Id(
                "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input")).Count > 0);

             LoggerService.LogInformation($"   ✅ On search page");

            _sessionManager.UpdateActivity();
        }




        /// <summary>
        /// Clear search form
        /// </summary>
        private async Task ClearSearchFormAsync()
        {
            try
            {
                var resetButton = _driver.FindElements(By.Id("actionMenuReset:commandButtonId"));
                if (resetButton.Count > 0)
                {
                    resetButton[0].Click();
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ⚠️  Could not clear form: {ex.Message}");
            }
        }


        #endregion Helper Methods











    }
}
