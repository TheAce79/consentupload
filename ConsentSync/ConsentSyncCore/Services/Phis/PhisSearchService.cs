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

namespace ConsentSyncCore.Services.Phis
{
    public class PhisSearchService
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

                Console.WriteLine($"   🔍 Searching by DOB: {dateOfBirth}");
                if (!string.IsNullOrEmpty(expectedFirstName) && !string.IsNullOrEmpty(expectedLastName))
                {
                    Console.WriteLine($"      Looking for: {expectedFirstName} {expectedLastName}");
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
                    Console.WriteLine($"   ⚠️  No results found for DOB: {dateOfBirth}");
                    return SearchResult.NoResults();
                }

                Console.WriteLine($"   📊 Found {results.Count} result(s)");

                return SearchResult.IsSuccess(results);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Session expired"))
            {
                Console.WriteLine($"   ❌ Session expired during search");
                return SearchResult.Failed("Session expired");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Search error: {ex.Message}");
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

                Console.WriteLine($"   🔍 Searching by Client ID: {clientId}");

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
                    Console.WriteLine($"   ⚠️  Client not found: {clientId}");
                    return SearchResult.NoResults();
                }

                if (results.Count > 1)
                {
                    Console.WriteLine($"   ⚠️  WARNING: Multiple results for Client ID {clientId} (expected 1)");
                }

                Console.WriteLine($"   ✅ Found: {results[0].FirstName} {results[0].LastName}");

                return SearchResult.IsSuccess(results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Search error: {ex.Message}");
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

                Console.WriteLine($"   🔍 Searching by Name: {firstName} {lastName}");

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
                Console.WriteLine($"   ❌ Search error: {ex.Message}");
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

                Console.WriteLine($"   🔍 Searching by Medicare: {medicareNumber}");

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
                    Console.WriteLine($"   ⚠️  No results found for Medicare: {medicareNumber}");
                    return SearchResult.NoResults();
                }

                Console.WriteLine($"   📊 Found {results.Count} result(s)");

                return SearchResult.IsSuccess(results);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Session expired"))
            {
                Console.WriteLine($"   ❌ Session expired during search");
                return SearchResult.Failed("Session expired");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Search error: {ex.Message}");
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
                    Console.WriteLine($"   ❌ Invalid date format: {dateOfBirth}");
                    throw new ArgumentException($"Invalid date format: {dateOfBirth}");
                }

                Console.WriteLine($"   📅 Original DOB: {dateOfBirth}");
                Console.WriteLine($"   📅 PHIS format: {phisFormattedDate}");

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

                Console.WriteLine($"   ✏️  Entered DOB via JavaScript");

                // Give PrimeFaces time to process
                await Task.Delay(800);

                // Verify the value was actually set
                var setValue = (string)js.ExecuteScript($"return document.getElementById('{dobInputId}').value;");
                if (setValue != phisFormattedDate)
                {
                    Console.WriteLine($"   ⚠️  WARNING: DOB value mismatch!");
                    Console.WriteLine($"      Expected: {phisFormattedDate}");
                    Console.WriteLine($"      Got: {setValue}");

                    // Fallback: Try direct SendKeys
                    var dobInput = _driver.FindElement(By.Id(dobInputId));
                    dobInput.Clear();
                    dobInput.SendKeys(phisFormattedDate);
                    await Task.Delay(500);

                    // Re-verify
                    setValue = dobInput.GetAttribute("value");
                    Console.WriteLine($"      After SendKeys: {setValue}");
                }
                else
                {
                    Console.WriteLine($"   ✅ DOB value confirmed: {setValue}");
                }

                // Click search
                await ClickSearchButtonAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error in ExecuteDobSearchAsync: {ex.Message}");
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
                Console.WriteLine($"   🔍 Locating Medicare search elements...");

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

                Console.WriteLine($"   ✅ Set dropdown to 'Health Card Number' via JavaScript");
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

                Console.WriteLine($"   ✅ Entered Medicare Number: {medicareNumber}");
                await Task.Delay(500); // Brief pause to ensure value is set

                // Step 3: Verify values were set
                var dropdownValue = (string)js.ExecuteScript($"return document.getElementById('{dropdownInputId}').value;");
                var inputValue = (string)js.ExecuteScript($"return document.getElementById('{clientNumberInputId}').value;");

                Console.WriteLine($"   🔍 Verification - Dropdown: {dropdownValue}, Input: {inputValue}");

                if (dropdownValue != "HEALTH_CARD_NUMBER" || inputValue != medicareNumber)
                {
                    Console.WriteLine($"   ⚠️  WARNING: Values not set correctly!");
                    Console.WriteLine($"      Expected dropdown: HEALTH_CARD_NUMBER, Got: {dropdownValue}");
                    Console.WriteLine($"      Expected input: {medicareNumber}, Got: {inputValue}");
                }

                // Click search
                await ClickSearchButtonAsync();
            }
            catch (NoSuchElementException ex)
            {
                Console.WriteLine($"   ❌ Element not found in ExecuteMedicareSearchAsync");
                Console.WriteLine($"      Error: {ex.Message}");

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

                    Console.WriteLine($"   🔍 Input element state: {clientNumberSection}");
                }
                catch { }

                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error in ExecuteMedicareSearchAsync: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Convert date from CSV format (yyyy-MM-dd) to PHIS format (yyyy/MM/dd)
        /// </summary>
        private string ConvertDateForPhis(string csvDate)
        {
            try
            {
                // Handle null/empty
                if (string.IsNullOrWhiteSpace(csvDate))
                    return string.Empty;

                // If already in correct format, return as-is
                if (csvDate.Contains("/") && csvDate.Length == 10)
                    return csvDate;

                // Parse from CSV format (yyyy-MM-dd)
                if (DateTime.TryParseExact(csvDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime parsedDate))
                {
                    // Format for PHIS: yyyy/MM/dd
                    return parsedDate.ToString("yyyy/MM/dd");
                }

                // Try alternative formats just in case
                string[] alternativeFormats = { "yyyy-M-d", "yyyy/M/d", "dd/MM/yyyy", "MM/dd/yyyy" };
                foreach (var format in alternativeFormats)
                {
                    if (DateTime.TryParseExact(csvDate, format,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out parsedDate))
                    {
                        return parsedDate.ToString("yyyy/MM/dd");
                    }
                }

                Console.WriteLine($"   ⚠️  Failed to parse date: {csvDate}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Date conversion error: {ex.Message}");
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
                Console.WriteLine($"   🔍 Locating Client ID search elements...");

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

                Console.WriteLine($"   ✅ Set dropdown to 'Client ID' via JavaScript");
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

                Console.WriteLine($"   ✅ Entered Client ID: {clientId}");
                await Task.Delay(500); // Brief pause to ensure value is set

                // Step 3: Verify values were set
                var dropdownValue = (string)js.ExecuteScript($"return document.getElementById('{dropdownInputId}').value;");
                var inputValue = (string)js.ExecuteScript($"return document.getElementById('{clientNumberInputId}').value;");

                Console.WriteLine($"   🔍 Verification - Dropdown: {dropdownValue}, Input: {inputValue}");

                if (dropdownValue != "CLIENT_ID" || inputValue != clientId)
                {
                    Console.WriteLine($"   ⚠️  WARNING: Values not set correctly!");
                    Console.WriteLine($"      Expected dropdown: CLIENT_ID, Got: {dropdownValue}");
                    Console.WriteLine($"      Expected input: {clientId}, Got: {inputValue}");
                }

                // Click search
                await ClickSearchButtonAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error in ExecuteClientIdSearchAsync: {ex.Message}");
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
                Console.WriteLine($"   🎯 Selecting search result and setting in context...");

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

                Console.WriteLine($"   ✅ Selected first result checkbox");
                await Task.Delay(_phisConfig.AjaxWaitMs); // Wait for AJAX to process row selection

                // Step 3: Verify checkbox is checked
                var isChecked = firstRowCheckbox.GetAttribute("class").Contains("ui-state-active");
                if (!isChecked)
                {
                    Console.WriteLine($"   ⚠️  WARNING: Checkbox might not be selected, retrying...");
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

                Console.WriteLine($"   ✅ Clicked 'Set In Context' button");
                await Task.Delay(_phisConfig.PageLoadDelayMs); // Wait for page to process

                // Step 5: Verify success by checking if we're redirected or context is set
                // You might want to check for a success message or URL change here
                await Task.Delay(1000); // Extra time for page to update

                // Update session activity
                _sessionManager.UpdateActivity();

                Console.WriteLine($"   ✅ Client set in context successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error setting client in context: {ex.Message}");
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
                Console.WriteLine($"   🎯 Selecting search result via JavaScript...");

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

                Console.WriteLine($"   ✅ Row selection triggered via PrimeFaces AJAX");
                await Task.Delay(_phisConfig.AjaxWaitMs * 2); // Extra wait for AJAX

                // Step 2: Click "Set In Context" button
                var setInContextButtonId = "form:dataTable:selectButtonId:actionButtonId:commandButtonId";
                var setInContextButton = _driver.FindElement(By.Id(setInContextButtonId));

                js.ExecuteScript("arguments[0].click();", setInContextButton);

                Console.WriteLine($"   ✅ Clicked 'Set In Context' button");
                await Task.Delay(_phisConfig.PageLoadDelayMs);

                _sessionManager.UpdateActivity();

                Console.WriteLine($"   ✅ Client set in context successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error in SelectResultAndSetInContextViaJavaScriptAsync: {ex.Message}");
                return false;
            }
        }




        public async Task<bool> SearchByClientIdAndSetInContextAsync(string clientId)
        {
            try
            {
                Console.WriteLine($"   🔍 Phase 3: Searching for Client ID {clientId} and setting in context...");

                // Step 1: Search by Client ID
                var searchResult = await SearchByClientIdAsync(clientId);

                if (!searchResult.Success)
                {
                    Console.WriteLine($"   ❌ Search failed: {searchResult.ErrorMessage}");
                    return false;
                }

                if (searchResult.Results.Count == 0)
                {
                    Console.WriteLine($"   ❌ No results found for Client ID: {clientId}");
                    return false;
                }

                if (searchResult.Results.Count > 1)
                {
                    Console.WriteLine($"   ⚠️  WARNING: Found {searchResult.Results.Count} results, expected 1");
                }

                // Step 2: Select result and set in context
                bool contextSet = await SelectResultAndSetInContextAsync();

                if (!contextSet)
                {
                    Console.WriteLine($"   ⚠️  Standard method failed, trying JavaScript approach...");
                    contextSet = await SelectResultAndSetInContextViaJavaScriptAsync();
                }

                return contextSet;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error in Phase 3: {ex.Message}");
                return false;
            }
        }


        #endregion Phase 3 - Set In Context



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
                Console.WriteLine($"   ⚠️  Could not select criteria type: {ex.Message}");
            }
        }


        /// <summary>
        /// Click search button
        /// </summary>
        private async Task ClickSearchButtonAsync()
        {
            var searchButton = _driver.FindElement(By.Id("actionMenuSearch:commandButtonId"));
            searchButton.Click();

            Console.WriteLine($"   🔎 Search clicked");
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

            Console.WriteLine($"   🔄 Navigating to search page...");

            _driver.Navigate().GoToUrl(_phisConfig.SearchUrl);
            await Task.Delay(_phisConfig.PageLoadDelayMs);

            // Wait for page to load
            _wait.Until(d => d.FindElements(By.Id(
                "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input")).Count > 0);

            Console.WriteLine($"   ✅ On search page");

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
                Console.WriteLine($"   ⚠️  Could not clear form: {ex.Message}");
            }
        }


        #endregion Helper Methods











    }
}
