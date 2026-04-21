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
                LoggerService.LogInformation("\n📂 STEP B: Navigating to Context Documents...");

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
                        // Primary: document list table is present
                        var table = d.FindElements(By.Id(
                            "userDocumentListForm:docListCollapseSection:documentListDataTable"));
                        if (table.Count > 0) return true;

                        // Fallback: "Add New" button visible
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

        // ── FileRose — Step C: Check if document already exists ───────────────

        /// <summary>
        /// Scans the Context Documents table for a row whose Document Title
        /// matches <paramref name="documentTitle"/> (case-insensitive, ignoring
        /// spaces and underscores).
        ///
        /// HTML anchor pattern:
        ///   id="userDocumentListForm:docListCollapseSection:documentListDataTable:{n}:viewtitleLink"
        ///   text = document title (e.g. "125804_suiviscolaire_2025-2026")
        /// </summary>
        public async Task<bool> CheckIfContextDocumentExistsAsync(string documentTitle)
        {
            try
            {
                LoggerService.LogInformation(
                    $"\n🔍 STEP C: Checking for existing document '{documentTitle}'...");

                await Task.Delay(500); // brief stabilisation

                // All title links in the Context Documents table share this id fragment
                var titleLinks = _driver.FindElements(By.XPath(
                    "//a[contains(@id," +
                    "'userDocumentListForm:docListCollapseSection:documentListDataTable') " +
                    "and contains(@id,'viewtitleLink')]"));

                if (titleLinks.Count == 0)
                {
                    LoggerService.LogInformation(
                        "   ℹ️  Document list is empty — upload required");
                    return false;
                }

                LoggerService.LogInformation(
                    $"   📊 {titleLinks.Count} document(s) found in Context Documents list");

                // Normalise: lower-case, strip spaces and underscores for fuzzy compare
                string Normalise(string s) =>
                    s.ToLowerInvariant()
                     .Replace(" ", string.Empty)
                     .Replace("_", string.Empty);

                var normSearch = Normalise(documentTitle);

                foreach (var link in titleLinks)
                {
                    try
                    {
                        var text = link.Text.Trim();
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        LoggerService.LogInformation(
                            $"   🔎 Comparing: '{text}' vs '{documentTitle}'");

                        if (Normalise(text).Equals(normSearch, StringComparison.Ordinal))
                        {
                            LoggerService.LogInformation(
                                $"   ✅ MATCH — document already exists: '{text}'");
                            return true;
                        }
                    }
                    catch (StaleElementReferenceException)
                    {
                        LoggerService.LogWarning(
                            "   ⚠️  Stale link reference — DOM refreshed mid-scan, retrying");
                        return await CheckIfContextDocumentExistsAsync(documentTitle);
                    }
                }

                LoggerService.LogInformation(
                    $"   ℹ️  Document not found — upload required");
                return false;
            }
            catch (Exception ex)
            {
                LoggerService.LogError(
                    $"   ❌ CheckIfContextDocumentExistsAsync error: {ex.Message}", ex);
                return false;
            }
        }
    }
}