using ConsentSyncCore.Models;
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


        #endregion Public API




        #region Search Execution Methods



        /// <summary>
        /// Execute DOB search
        /// </summary>
        private async Task ExecuteDobSearchAsync(string dateOfBirth)
        {
            // Select DOB radio button (if needed)
            await SelectSearchCriteriaTypeAsync("DOB");

            // Find and fill DOB input
            var dobInput = _driver.FindElement(By.Id(
                "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input"));

            dobInput.Clear();
            dobInput.SendKeys(dateOfBirth);

            Console.WriteLine($"   ✏️  Entered DOB");

            // Click search
            await ClickSearchButtonAsync();
        }



        /// <summary>
        /// Execute Client ID search
        /// </summary>
        private async Task ExecuteClientIdSearchAsync(string clientId)
        {
            // Find Client ID input field
            var clientIdInput = _driver.FindElement(By.Id(
                "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_clientID"));

            clientIdInput.Clear();
            clientIdInput.SendKeys(clientId);

            Console.WriteLine($"   ✏️  Entered Client ID");

            // Click search
            await ClickSearchButtonAsync();
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
