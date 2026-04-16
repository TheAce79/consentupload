using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Phis
{
    public partial class PhisSearchService
    {

        /// <summary>
        /// Click the Documents button after selecting a consent directive
        /// This navigates to the Context Documents page
        /// </summary>
        public async Task<bool> ClickDocumentsButtonAsync()
        {
            try
            {
                 LoggerService.LogInformation($"   📄 Clicking Documents button...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Wait for Documents button to be enabled
                var documentsButtonId = "consentForm:ConsentDataTable:DocumentsButton:actionButtonId:commandButtonId";

                _wait.Until(d =>
                {
                    var button = d.FindElements(By.Id(documentsButtonId));
                    if (button.Count > 0)
                    {
                        var isDisabled = button[0].GetAttribute("disabled");
                        var classes = button[0].GetAttribute("class");
                        return string.IsNullOrEmpty(isDisabled) && !classes.Contains("ui-state-disabled");
                    }
                    return false;
                });

                var documentsButton = _driver.FindElement(By.Id(documentsButtonId));

                // Click using JavaScript
                js.ExecuteScript("arguments[0].click();", documentsButton);

                 LoggerService.LogInformation($"   ✅ Documents button clicked");

                // Wait for Context Documents page to load
                await Task.Delay(_phisConfig.PageLoadDelayMs);

                // Verify we're on the Context Documents page
                try
                {
                    _wait.Until(d => d.Title.Contains("Panorama") ||
                                   d.FindElements(By.XPath("//*[contains(text(), 'Context Documents')]")).Count > 0);

                     LoggerService.LogInformation($"   ✅ Context Documents page loaded");
                    _sessionManager.UpdateActivity();
                    return true;
                }
                catch (WebDriverTimeoutException)
                {
                     LoggerService.LogInformation($"   ⚠️  Page verification timed out");

                    // Check if we can find the document list table as fallback
                    var docList = _driver.FindElements(By.XPath("//*[contains(text(), 'Document List')]"));
                    if (docList.Count > 0)
                    {
                         LoggerService.LogInformation($"   ✅ Document List found - assuming navigation successful");
                        _sessionManager.UpdateActivity();
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error clicking Documents button: {ex.Message}");
                return false;
            }
        }



        /// <summary>
        /// Search for a document by title on the Context Documents page
        /// Returns true if document already exists
        /// </summary>
        /// <param name="documentTitle">The document title to search for (e.g., "1066457_consentHPV9_2025-2026")</param>
        public async Task<bool> CheckIfDocumentExistsAsync(string documentTitle)
        {
            try
            {
                 LoggerService.LogInformation($"      🔍 Searching for existing document: '{documentTitle}'");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Wait for the document list table to be present
                await Task.Delay(1000); // Give page time to load

                // First, check if there are any documents in the list
                var documentRows = _driver.FindElements(By.XPath("//table//a[contains(@id, '_consent')]"));

                if (documentRows.Count == 0)
                {
                     LoggerService.LogInformation($"      ℹ️  No documents found in the list");
                    return false;
                }

                 LoggerService.LogInformation($"      📊 Found {documentRows.Count} document(s) in the list");

                // Search through visible documents for exact or partial match
                foreach (var docLink in documentRows)
                {
                    try
                    {
                        var docText = docLink.Text.Trim();

                        // Remove spaces and underscores for comparison
                        var normalizedDocText = docText.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        var normalizedSearchTitle = documentTitle.Replace(" ", "").Replace("_", "").ToLowerInvariant();

                         LoggerService.LogInformation($"         Comparing: '{docText}' vs '{documentTitle}'");

                        // Check for exact match (after normalization)
                        if (normalizedDocText.Equals(normalizedSearchTitle, StringComparison.OrdinalIgnoreCase))
                        {
                             LoggerService.LogInformation($"      ✅ EXACT MATCH FOUND: '{docText}'");
                            return true;
                        }

                        // Check if the document title contains the key parts (ClientID + consent type + year)
                        // Example: searching for "1066457_consentHPV9_2025-2026"
                        // Should match "1066457_consent HPV9_2025-2026" or similar variations

                        var parts = documentTitle.Split('_');
                        if (parts.Length >= 2)
                        {
                            var clientId = parts[0]; // e.g., "1066457"
                            var consentType = parts[1].Replace("consent", "").ToLowerInvariant(); // e.g., "hpv9"

                            if (normalizedDocText.Contains(clientId.ToLowerInvariant()) &&
                                normalizedDocText.Contains(consentType))
                            {
                                 LoggerService.LogInformation($"      ⚠️  PARTIAL MATCH FOUND: '{docText}'");
                                 LoggerService.LogInformation($"         This may be the same document with different naming");
                                // You might want to return true here if partial matches should count
                                // For now, we'll continue searching for exact match
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                         LoggerService.LogInformation($"         ⚠️  Error reading document: {ex.Message}");
                    }
                }

                // If we want to use the search functionality on the page
                 LoggerService.LogInformation($"      🔍 Trying page search functionality...");
                bool foundViaSearch = await SearchDocumentViaPageSearchAsync(documentTitle);

                if (foundViaSearch)
                {
                     LoggerService.LogInformation($"      ✅ Document found via page search");
                    return true;
                }

                 LoggerService.LogInformation($"      ❌ Document not found: '{documentTitle}'");
                return false;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"      ❌ Error checking for document: {ex.Message}");
                return false;
            }
        }





        /// <summary>
        /// Use the page's search functionality to search for a document
        /// </summary>
        private async Task<bool> SearchDocumentViaPageSearchAsync(string documentTitle)
        {
            try
            {
                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // Find the search input box
                var searchInputs = _driver.FindElements(By.XPath("//input[contains(@placeholder, 'search') or @type='text']"));

                if (searchInputs.Count == 0)
                {
                     LoggerService.LogInformation($"         ℹ️  Search box not found");
                    return false;
                }

                var searchBox = searchInputs[0];

                // Clear and enter search term
                searchBox.Clear();
                searchBox.SendKeys(documentTitle);

                // Find and click Search button
                var searchButtons = _driver.FindElements(By.XPath("//input[@value='Search' or @value='search']"));

                if (searchButtons.Count > 0)
                {
                    js.ExecuteScript("arguments[0].click();", searchButtons[0]);
                    await Task.Delay(_phisConfig.AjaxWaitMs * 2);

                    // Check if results show the document
                    var resultRows = _driver.FindElements(By.XPath($"//a[contains(text(), '{documentTitle}')]"));

                    if (resultRows.Count > 0)
                    {
                         LoggerService.LogInformation($"         ✅ Found in search results");
                        return true;
                    }
                }

                 LoggerService.LogInformation($"         ℹ️  Not found in search results");
                return false;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"         ⚠️  Search error: {ex.Message}");
                return false;
            }
        }





        /// <summary>
        /// Navigate back to Consent Directives page from Context Documents
        /// </summary>
        public async Task<bool> NavigateBackToSearchPagesAsync()
        {
            try
            {
                 LoggerService.LogInformation($"      🔙 Navigating back to Consent Directives...");

                // Navigate to search page
                await EnsureOnSearchPageAsync();

                // Press the reset button to clear the form
                var resetButton = _driver.FindElements(By.Id("actionMenuReset:commandButtonId"));
                if (resetButton.Count > 0)
                {
                    resetButton[0].Click();
                    await Task.Delay(1000);
                     LoggerService.LogInformation($"      🧹 Reset button clicked");
                }

                // Verify we're on the search page with reset form
                var searchForm = _driver.FindElements(By.Id(
                    "form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input"));

                if (searchForm.Count > 0)
                {
                     LoggerService.LogInformation($"      ✅ Ready for next search");
                    _sessionManager.UpdateActivity();
                    return true;
                }

                 LoggerService.LogInformation($"      ⚠️  Search form not found after reset");
                return false;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"      ❌ Error navigating back: {ex.Message}");
                return false;
            }
        }





        /// <summary>
        /// Click the "Add New" button to navigate to the document upload page
        /// This is the final step before actually uploading the document
        /// </summary>
        public async Task<bool> ClickAddNewDocumentButtonAsync()
        {
            try
            {
                Console.WriteLine($"   📤 Clicking 'Add New' button to open upload form...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // The button ID from the network capture
                var addNewButtonId = "userDocumentListForm:docListCollapseSection:addDocument";

                // Wait for the button to be present and clickable
                _wait.Until(d => d.FindElements(By.Id(addNewButtonId)).Count > 0);

                var addNewButton = _driver.FindElement(By.Id(addNewButtonId));

                // Verify the button is enabled
                var isDisabled = addNewButton.GetAttribute("disabled");
                var classes = addNewButton.GetAttribute("class");

                if (!string.IsNullOrEmpty(isDisabled) || classes.Contains("buttonDisabled"))
                {
                    Console.WriteLine($"   ⚠️  'Add New' button is disabled");
                    return false;
                }

                Console.WriteLine($"   ✅ 'Add New' button found and enabled");

                // Execute the onclick JavaScript first (folder validation)
                // From network capture: onclick="return checkSelectedFolder('hideUserTreeView:treeViewForm:hiddenFolderId');"
                var onClickResult = js.ExecuteScript(
                    "return checkSelectedFolder('hideUserTreeView:treeViewForm:hiddenFolderId');");

                if (onClickResult is bool boolResult && !boolResult)
                {
                    Console.WriteLine($"   ⚠️  Folder validation failed - cannot add document");
                    return false;
                }

                // Click the button using JavaScript for reliability
                js.ExecuteScript("arguments[0].click();", addNewButton);

                Console.WriteLine($"   ✅ 'Add New' button clicked");

                // Wait for the Document Management page to load
                await Task.Delay(_phisConfig.PageLoadDelayMs);

                // Verify we're on the "Add New Document" page
                try
                {
                    _wait.Until(d =>
                    {
                        // Check for the page title
                        var titleElements = d.FindElements(By.Id("pageTitle"));
                        if (titleElements.Count > 0 && titleElements[0].Text.Contains("Document Management"))
                        {
                            return true;
                        }

                        // Alternative: Check for the "Add New Document" section header
                        var sectionHeaders = d.FindElements(By.XPath("//*[contains(text(), 'Add New Document')]"));
                        return sectionHeaders.Count > 0;
                    });

                    Console.WriteLine($"   ✅ Document Management page loaded");
                    Console.WriteLine($"   📄 Ready to upload document");

                    _sessionManager.UpdateActivity();
                    return true;
                }
                catch (WebDriverTimeoutException)
                {
                    Console.WriteLine($"   ⚠️  Page verification timed out");

                    // Fallback: Check for the file upload input field
                    var fileInputs = _driver.FindElements(By.Id("addNewDocumentForm:sectionAddNewDocumentDefault:fileuploadInput"));
                    if (fileInputs.Count > 0)
                    {
                        Console.WriteLine($"   ✅ Upload form found - assuming navigation successful");
                        _sessionManager.UpdateActivity();
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error clicking 'Add New' button: {ex.Message}");
                return false;
            }
        }



    }
}
