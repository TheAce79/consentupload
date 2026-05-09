using ConsentSyncCore.Services.Configuration;
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
        public async Task<bool> CheckIfDocumentExistsAsync(string documentTitle)
        {
            try
            {
                 LoggerService.LogInformation($"      🔍 Searching for existing document: '{documentTitle}'");

                // Give page time to load
                await Task.Delay(1000);

                // ✅ Use correct selector from network trace:
                // Link IDs: userDocumentListForm:docListCollapseSection:documentListDataTable:0:viewtitleLink
                // Document title is the TEXT of the link, not the ID
                var documentLinks = _driver.FindElements(By.XPath(
                    "//a[contains(@id, 'viewtitleLink')]"));

                if (documentLinks.Count == 0)
                {
                     LoggerService.LogInformation($"      ℹ️  No documents found in the list");
                    return false;
                }

                 LoggerService.LogInformation($"      📊 Found {documentLinks.Count} document(s) in the list");

                // Normalize search title for comparison
                var normalizedSearchTitle = documentTitle
                    .Replace(" ", "")
                    .Replace("_", "")
                    .ToLowerInvariant();

                foreach (var link in documentLinks)
                {
                    try
                    {
                        var docText = link.Text.Trim();
                        if (string.IsNullOrWhiteSpace(docText)) continue;

                        var normalizedDocText = docText
                            .Replace(" ", "")
                            .Replace("_", "")
                            .ToLowerInvariant();

                         LoggerService.LogInformation($"         Comparing: '{docText}' vs '{documentTitle}'");

                        // ✅ Exact match after normalization
                        if (normalizedDocText.Equals(normalizedSearchTitle, StringComparison.OrdinalIgnoreCase))
                        {
                             LoggerService.LogInformation($"      ✅ EXACT MATCH FOUND: '{docText}'");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                         LoggerService.LogInformation($"         ⚠️  Error reading document link: {ex.Message}");
                    }
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
                 LoggerService.LogInformation($"   📤 Clicking 'Add New' button to open upload form...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // The button ID from the network capture
                var addNewButtonId = "userDocumentListForm:docListCollapseSection:addDocument";

                // Wait for the button to be present and clickable
                _wait.Until(d => d.FindElements(By.Id(addNewButtonId)).Count > 0);

                var addNewButton = _driver.FindElement(By.Id(addNewButtonId));

                // Verify the button is enabled
                var isDisabled = addNewButton.GetAttribute("disabled");
                var classes = addNewButton.GetAttribute("class");

                if (!string.IsNullOrEmpty(isDisabled) || (classes != null && classes.Contains("buttonDisabled")))
                {
                     LoggerService.LogInformation($"   ⚠️  'Add New' button is disabled");
                    return false;
                }

                 LoggerService.LogInformation($"   ✅ 'Add New' button found and enabled");

                // Execute the onclick JavaScript first (folder validation)
                try
                {
                    var onClickResult = js.ExecuteScript(
                        "return checkSelectedFolder('hideUserTreeView:treeViewForm:hiddenFolderId');");

                    if (onClickResult is bool boolResult && !boolResult)
                    {
                         LoggerService.LogInformation($"   ⚠️  Folder validation failed - cannot add document");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                     LoggerService.LogInformation($"   ℹ️  Folder validation skipped: {ex.Message}");
                }

                js.ExecuteScript("arguments[0].click();", addNewButton);

                 LoggerService.LogInformation($"   ✅ 'Add New' button clicked");

                await Task.Delay(_phisConfig.PageLoadDelayMs);

                try
                {
                    _wait.Until(d =>
                    {
                        var titleElements = d.FindElements(By.Id("pageTitle"));
                        if (titleElements.Count > 0 && titleElements[0].Text.Contains("Document Management"))
                            return true;

                        var sectionHeaders = d.FindElements(By.XPath("//*[contains(text(), 'Add New Document')]"));
                        return sectionHeaders.Count > 0;
                    });

                     LoggerService.LogInformation($"   ✅ Document Management page loaded");
                    _sessionManager.UpdateActivity();
                    return true;
                }
                catch (WebDriverTimeoutException)
                {
                     LoggerService.LogInformation($"   ⚠️  Page verification timed out");

                    var fileInputs = _driver.FindElements(By.Id("addNewDocumentForm:sectionAddNewDocumentDefault:fileuploadInput"));
                    if (fileInputs.Count > 0)
                    {
                         LoggerService.LogInformation($"   ✅ Upload form found - assuming navigation successful");
                        _sessionManager.UpdateActivity();
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"   ❌ Error clicking 'Add New' button: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> UploadDocumentAsync(string pdfPath, string documentTitle, string description)
        {
            const int maxAttempts = 2;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                bool isRetry = attempt > 1;

                if (isRetry)
                {
                    LoggerService.LogWarning(
                        $"   🔄 Retry attempt {attempt}/{maxAttempts} — navigating to fresh page to reset PrimeFaces state...");

                    // ── Force a full navigation to the search page ────────────
                    // This is exactly what restarting Chrome does: it gives the
                    // PrimeFaces FileUpload component a clean ViewState/server state.
                    try
                    {
                        await EnsureOnSearchPageAsync();
                        await Task.Delay(_phisConfig.PageLoadDelayMs);
                        LoggerService.LogInformation("   ✅ Fresh page loaded — retrying upload.");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogWarning($"   ⚠️  Could not navigate to fresh page: {ex.Message}");
                        return false;
                    }
                }

                bool result = await TryUploadOnceAsync(pdfPath, documentTitle, description, attempt, maxAttempts);

                if (result)
                    return true;

                if (attempt == maxAttempts)
                {
                    LoggerService.LogWarning(
                        $"   ❌ All {maxAttempts} upload attempt(s) failed for '{documentTitle}'.\n" +
                        "      VerifStatus stays NotProcessed — re-run Phase 3 to continue.");
                    return false;
                }

                // Small cooldown before retry
                await Task.Delay(1500);
            }

            return false;
        }



        /// <summary>
        /// Single upload attempt. Called by UploadDocumentAsync — do not call directly.
        /// </summary>
        private async Task<bool> TryUploadOnceAsync(
             string pdfPath, string documentTitle, string description,
             int attempt, int maxAttempts)
        {
            string attemptLabel = maxAttempts > 1 ? $" (attempt {attempt}/{maxAttempts})" : string.Empty;

            try
            {
                LoggerService.LogInformation($"   📤 Starting document upload{attemptLabel}...");
                LoggerService.LogInformation($"      File:  {Path.GetFileName(pdfPath)}");
                LoggerService.LogInformation($"      Title: {documentTitle}");
                LoggerService.LogInformation($"      Desc:  {description}");

                // ── Guard 1: PDF must exist and not be locked ─────────────────
                if (!File.Exists(pdfPath))
                {
                    LoggerService.LogWarning($"   ❌ PDF file not found: {pdfPath}");
                    return false;
                }

                // ✅ ENHANCEMENT 3: Detect file lock (e.g. archiving still in progress)
                try
                {
                    using var probe = File.Open(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
                catch (IOException)
                {
                    LoggerService.LogWarning(
                        $"   ❌ PDF is locked by another process: {Path.GetFileName(pdfPath)}\n" +
                        "      Skipping — record stays NotProcessed for retry.");
                    return false;
                }

                // ✅ ENHANCEMENT 1: Verify session is alive before touching the form
                if (!_sessionManager.EnsureSessionValid())
                {
                    LoggerService.LogWarning(
                        $"   ❌ PHIS session expired before upload{attemptLabel} — stopping.");
                    return false;
                }

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // ── STEP 1: Set file path ─────────────────────────────────────
                const string fileInputId = "addNewDocumentForm:sectionAddNewDocumentDefault:fileuploadInput";

                _wait.Until(d => d.FindElements(By.Id(fileInputId)).Count > 0);
                var fileInput = _driver.FindElement(By.Id(fileInputId));

                js.ExecuteScript(
                    "arguments[0].style.display='block'; arguments[0].style.visibility='visible'; arguments[0].removeAttribute('disabled');",
                    fileInput);
                fileInput.SendKeys(pdfPath);
                LoggerService.LogInformation($"   ✅ File path set");

                js.ExecuteScript("onChangeFileUploadAction();");
                await Task.Delay(500);

                // ── STEP 2: Click "Upload File" ───────────────────────────────
                const string uploadBtnId = "addNewDocumentForm:sectionAddNewDocumentDefault:buttonUploadFile";

                _wait.Until(d =>
                {
                    var btn = d.FindElements(By.Id(uploadBtnId));
                    return btn.Count > 0 && string.IsNullOrEmpty(btn[0].GetAttribute("disabled"));
                });

                var uploadButton = _driver.FindElement(By.Id(uploadBtnId));
                js.ExecuteScript("arguments[0].click();", uploadButton);
                LoggerService.LogInformation($"   ✅ 'Upload File' button clicked");

                await Task.Delay(_phisConfig.PageLoadDelayMs * 2);

                // ── STEP 3: Verify server accepted the file ───────────────────
                const string progressRefresherId = "addNewDocumentForm:sectionAddNewDocumentDefault:progressRefresher";

                try
                {
                    _wait.Until(d =>
                    {
                        var refresher = d.FindElements(By.Id(progressRefresherId));
                        if (refresher.Count == 0) return false;
                        var text = refresher[0].Text;
                        return text.Contains("File uploaded", StringComparison.OrdinalIgnoreCase)
                            || text.Contains(Path.GetFileName(pdfPath), StringComparison.OrdinalIgnoreCase);
                    });
                    LoggerService.LogInformation($"   ✅ File upload confirmed by server");
                }
                catch (WebDriverTimeoutException)
                {
                    var pageErrors = _driver.FindElements(
                        By.CssSelector(".errorMessage, .ui-messages-error-detail, .sysMessages .errorMessage"));

                    if (pageErrors.Count > 0)
                    {
                        var errorText = string.Join("; ",
                            pageErrors.Select(e => e.Text.Trim()).Where(t => !string.IsNullOrEmpty(t)));
                        LoggerService.LogWarning(
                            $"   ❌ Server rejected file upload{attemptLabel} — PHIS error: {errorText}\n" +
                            "      Stale PrimeFaces component detected (invalid java.util.List).");
                        return false;
                    }

                    LoggerService.LogWarning(
                        $"   ⚠️  Upload confirmation timed out{attemptLabel} — no server error detected. Proceeding cautiously.");
                }

                // ── STEP 4: Fill Document Title ───────────────────────────────
                // ✅ ENHANCEMENT 2: Catch StaleElementReferenceException — the DOM
                //    can refresh after the server processes the file upload, making
                //    previously found elements stale. Re-locate before interacting.
                const string titleFieldId = "addNewDocumentForm:sectionAddNewDocumentDefault:newDocumentTitle";
                try
                {
                    _wait.Until(d => d.FindElements(By.Id(titleFieldId)).Count > 0);
                    var titleField = _driver.FindElement(By.Id(titleFieldId));
                    titleField.Clear();
                    titleField.SendKeys(documentTitle);
                    LoggerService.LogInformation($"   ✅ Document title filled: '{documentTitle}'");
                }
                catch (StaleElementReferenceException)
                {
                    LoggerService.LogWarning("   ⚠️  Title field went stale — re-locating and retrying...");
                    await Task.Delay(500);
                    var titleField = _driver.FindElement(By.Id(titleFieldId));
                    titleField.Clear();
                    titleField.SendKeys(documentTitle);
                    LoggerService.LogInformation($"   ✅ Document title filled (after re-locate): '{documentTitle}'");
                }

                // ── STEP 5: Fill Description ──────────────────────────────────
                const string descFieldId = "addNewDocumentForm:sectionAddNewDocumentDefault:documentDescription";
                try
                {
                    _wait.Until(d => d.FindElements(By.Id(descFieldId)).Count > 0);
                    var descField = _driver.FindElement(By.Id(descFieldId));
                    descField.Clear();
                    descField.SendKeys(description);
                    LoggerService.LogInformation($"   ✅ Description filled: '{description}'");
                }
                catch (StaleElementReferenceException)
                {
                    LoggerService.LogWarning("   ⚠️  Description field went stale — re-locating and retrying...");
                    await Task.Delay(500);
                    var descField = _driver.FindElement(By.Id(descFieldId));
                    descField.Clear();
                    descField.SendKeys(description);
                    LoggerService.LogInformation($"   ✅ Description filled (after re-locate): '{description}'");
                }

                // ── STEP 6: Click Submit ──────────────────────────────────────
                const string submitBtnId = "addNewDocumentForm:sectionAddNewDocumentDefault:cmdBtnSave2";

                _wait.Until(d =>
                {
                    var btn = d.FindElements(By.Id(submitBtnId));
                    return btn.Count > 0 && string.IsNullOrEmpty(btn[0].GetAttribute("disabled"));
                });

                var submitButton = _driver.FindElement(By.Id(submitBtnId));
                js.ExecuteScript("disableFileUpload();");
                js.ExecuteScript("arguments[0].click();", submitButton);
                LoggerService.LogInformation($"   ✅ Submit button clicked");

                // ── STEP 7: Verify success ────────────────────────────────────
                await Task.Delay(_phisConfig.PageLoadDelayMs * 2);

                try
                {
                    _wait.Until(d =>
                    {
                        var listLinks = d.FindElements(By.XPath("//a[contains(@id,'viewtitleLink')]"));
                        if (listLinks.Count > 0) return true;

                        var errorMessages = d.FindElements(
                            By.CssSelector(".errorMessage, .sysMessages .errorMessage"));
                        return errorMessages.Count == 0 && d.Title.Contains("Panorama");
                    });

                    LoggerService.LogInformation($"   ✅ Document submitted successfully{attemptLabel}!");
                    _sessionManager.UpdateActivity();
                    return true;
                }
                catch (WebDriverTimeoutException)
                {
                    var errors = _driver.FindElements(By.CssSelector(".errorMessage"));
                    if (errors.Count > 0)
                    {
                        var errorText = string.Join("; ",
                            errors.Select(e => e.Text.Trim()).Where(t => !string.IsNullOrEmpty(t)));
                        LoggerService.LogWarning($"   ❌ Submit failed{attemptLabel} – page errors: {errorText}");
                        return false;
                    }

                    // ✅ ENHANCEMENT 4: Submit verification timed out but no error shown.
                    //    PHIS sometimes accepts silently without redirecting (partial page update).
                    //    Confirm by checking the document list directly.
                    LoggerService.LogWarning(
                        $"   ⚠️  Submit verification timed out{attemptLabel} — checking document list to confirm...");

                    bool confirmedViaList = await CheckIfDocumentExistsAsync(documentTitle);
                    if (confirmedViaList)
                    {
                        LoggerService.LogInformation(
                            $"   ✅ Document confirmed in PHIS document list — upload successful.");
                        _sessionManager.UpdateActivity();
                        return true;
                    }

                    LoggerService.LogWarning(
                        "   ⚠️  Document NOT found in list after timeout — treating as failure for safety.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogWarning($"   ❌ Upload error{attemptLabel}: {ex.Message}");
                LoggerService.LogWarning($"      Stack: {ex.StackTrace}");
                return false;
            }
        }



    }
}