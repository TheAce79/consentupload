using ConsentSyncCore.Services.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ConsentSyncCore.Services.Phis
{
    public partial class PhisSearchService
    {
        // ── FileRose — Step B: Navigate to Context Documents ──────────────────

        /// <summary>
        /// Navigates to the client Context Documents page via direct URL.
        /// The client must already be set in context before calling this.
        /// Target: DocumentManagement/pages/DocumentMgtUserViewLayout.xhtml?DM_TYPE=DM_RECORD_CONTEXT
        /// </summary>
        public async Task<bool> NavigateToContextDocumentsAsync()
        {
            try
            {
                LoggerService.LogInformation("\n📂 STEP C: Navigating to Context Documents...");

                var baseUrl = _phisConfig.LoginUrl.Replace("/phsdsm/", "");
                var contextUrl = $"{baseUrl}/DocumentManagement/pages/" +
                                 "DocumentMgtUserViewLayout.xhtml?DM_TYPE=DM_RECORD_CONTEXT";

                LoggerService.LogInformation($"   📍 URL: {contextUrl}");
                _driver.Navigate().GoToUrl(contextUrl);
                await Task.Delay(_phisConfig.PageLoadDelayMs);

                try
                {
                    _wait.Until(d =>
                    {
                        var table = d.FindElements(By.Id(
                            "userDocumentListForm:docListCollapseSection:documentListDataTable"));
                        if (table.Count > 0) return true;

                        var addNew = d.FindElements(By.Id(
                            "userDocumentListForm:docListCollapseSection:addDocument"));
                        return addNew.Count > 0;
                    });

                    LoggerService.LogInformation("   ✅ Context Documents page loaded");
                    _sessionManager.UpdateActivity();
                    return true;
                }
                catch (WebDriverTimeoutException)
                {
                    var pageTitle = string.Empty;
                    try { pageTitle = _driver.FindElement(By.XPath("//h1")).Text; } catch { }

                    LoggerService.LogWarning(
                        "   ⚠️  Context Documents page did not load in time." +
                        (string.IsNullOrWhiteSpace(pageTitle)
                            ? string.Empty
                            : $" Current page: '{pageTitle}'"));
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError(
                    $"   ❌ NavigateToContextDocumentsAsync error: {ex.Message}", ex);
                return false;
            }
        }

        // ── FileRose — Step D: Check if document already exists ───────────────

        /// <summary>
        /// Scans the Context Documents table for a row whose Document Title
        /// matches <paramref name="documentTitle"/> (case-insensitive, ignoring
        /// spaces and underscores).
        /// </summary>
        public async Task<bool> CheckIfContextDocumentExistsAsync(string documentTitle)
        {
            try
            {
                LoggerService.LogInformation(
                    $"\n🔍 STEP D: Checking for existing document '{documentTitle}'...");

                await Task.Delay(500);

                var titleLinks = _driver.FindElements(By.XPath(
                    "//a[contains(@id," +
                    "'userDocumentListForm:docListCollapseSection:documentListDataTable') " +
                    "and contains(@id,'viewtitleLink')]"));

                if (titleLinks.Count == 0)
                {
                    LoggerService.LogInformation("   ℹ️  Document list is empty — upload required");
                    return false;
                }

                LoggerService.LogInformation(
                    $"   📊 {titleLinks.Count} document(s) found in Context Documents list");

                string Normalise(string s) =>
                    s.ToLowerInvariant()
                     .Replace(" ", string.Empty)
                     .Replace("_", string.Empty)
                     .Replace("-", string.Empty);

                var normSearch = Normalise(documentTitle);

                foreach (var link in titleLinks)
                {
                    try
                    {
                        var text = link.Text.Trim();
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        LoggerService.LogInformation($"   🔎 Comparing: '{text}' vs '{documentTitle}'");

                        if (Normalise(text).Equals(normSearch, StringComparison.Ordinal))
                        {
                            LoggerService.LogInformation(
                                $"   ✅ MATCH — document already exists: '{text}'");
                            return true;
                        }
                    }
                    catch (StaleElementReferenceException)
                    {
                        LoggerService.LogWarning("   ⚠️  Stale element — retrying check");
                        return await CheckIfContextDocumentExistsAsync(documentTitle);
                    }
                }

                LoggerService.LogInformation("   ℹ️  Document not found — upload required");
                return false;
            }
            catch (Exception ex)
            {
                LoggerService.LogError(
                    $"   ❌ CheckIfContextDocumentExistsAsync error: {ex.Message}", ex);
                return false;
            }
        }

        // ── FileRose — Step E: Click Add New ──────────────────────────────────

        /// <summary>
        /// Clicks the "Add New" button on the Context Documents page.
        ///
        /// Network payload shows the form submits:
        ///   userDocumentListForm:docListCollapseSection:addDocument = "Add New"
        ///
        /// The button's onclick calls checkSelectedFolder() which validates that
        /// a folder is selected in the hidden tree view. We bypass this by setting
        /// the hidden folder ID directly via JS before clicking.
        ///
        /// Target page after click: Add New Document (Document Management upload form)
        /// </summary>
        public async Task<bool> ClickContextDocumentAddNewAsync()
        {
            const string addNewId =
                "userDocumentListForm:docListCollapseSection:addDocument";
            const string hiddenFolderInputId =
                "hideUserTreeView:treeViewForm:hiddenFolderId";

            try
            {
                LoggerService.LogInformation("\n📤 STEP E: Clicking 'Add New' on Context Documents...");

                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;

                // ── Wait for the Add New button to be present ─────────────────
                _wait.Until(d => d.FindElements(By.Id(addNewId)).Count > 0);
                var addNewButton = _driver.FindElement(By.Id(addNewId));

                LoggerService.LogInformation("   🔍 Add New button found");
                LoggerService.LogInformation(
                    $"   📋 Button state — disabled: " +
                    $"'{addNewButton.GetAttribute("disabled")}', " +
                    $"class: '{addNewButton.GetAttribute("class")}'");

                // ── Satisfy checkSelectedFolder() by ensuring hiddenFolderId has
                //    a value. If the tree view has already selected a folder this
                //    is a no-op; if not, an empty string makes the function return
                //    true (folder validation is skipped server-side for this form).
                try
                {
                    var currentFolderVal = (string?)js.ExecuteScript(
                        $"var el = document.getElementById('{hiddenFolderInputId}');" +
                        "return el ? el.value : 'NOT_FOUND';");

                    LoggerService.LogInformation(
                        $"   📁 hiddenFolderId current value: '{currentFolderVal}'");

                    if (currentFolderVal == "NOT_FOUND" || string.IsNullOrEmpty(currentFolderVal))
                    {
                        // Set to empty string — server accepts no folder (root)
                        js.ExecuteScript(
                            $"var el = document.getElementById('{hiddenFolderInputId}');" +
                            "if(el) el.value = '';");
                        LoggerService.LogInformation(
                            "   📁 hiddenFolderId set to empty (root folder)");
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal — proceed and let the server decide
                    LoggerService.LogWarning(
                        $"   ⚠️  Could not set hiddenFolderId: {ex.Message}");
                }

                // ── Scroll button into view and click via JS ──────────────────
                js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", addNewButton);
                await Task.Delay(300);

                js.ExecuteScript("arguments[0].click();", addNewButton);
                LoggerService.LogInformation("   ✅ Add New button clicked");

                await Task.Delay(_phisConfig.PageLoadDelayMs);

                // ── Verify we landed on the upload form ───────────────────────
                try
                {
                    _wait.Until(d =>
                    {
                        // Upload form file input is present
                        var fileInput = d.FindElements(By.Id(
                            "addNewDocumentForm:sectionAddNewDocumentDefault:fileuploadInput"));
                        if (fileInput.Count > 0) return true;

                        // Fallback: page contains "Add New Document" heading
                        var heading = d.FindElements(By.XPath(
                            "//*[contains(text(),'Add New Document')]"));
                        return heading.Count > 0;
                    });

                    LoggerService.LogInformation("   ✅ Add New Document upload form loaded");
                    _sessionManager.UpdateActivity();
                    return true;
                }
                catch (WebDriverTimeoutException)
                {
                    var pageTitle = string.Empty;
                    try { pageTitle = _driver.FindElement(By.XPath("//h1")).Text; } catch { }

                    LoggerService.LogWarning(
                        "   ⚠️  Upload form did not load in time." +
                        (string.IsNullOrWhiteSpace(pageTitle)
                            ? string.Empty
                            : $" Current page: '{pageTitle}'"));
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError(
                    $"   ❌ ClickContextDocumentAddNewAsync error: {ex.Message}", ex);
                return false;
            }
        }
    }
}