using ConsentSyncCore.Services.Configuration;
using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Phis
{
    public partial class PhisSearchService
    {

        // ── FileRose — Step B: Navigate to Context Documents ──────────────────

        /// <summary>
        /// Navigates to the client Context Documents page via direct URL.
        /// The client must already be set in context before calling this.
        ///
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

                // ── Verify we landed on Context Documents ─────────────────────
                try
                {
                    _wait.Until(d =>
                    {
                        // Page title h1 reads "Context Documents"
                        var h1 = d.FindElements(By.XPath("//h1[normalize-space()='Context Documents']"));
                        if (h1.Count > 0) return true;

                        // Fallback: "Add New" button in the Document List is present
                        var addNew = d.FindElements(By.XPath(
                            "//input[@value='Add New'] | //button[normalize-space()='Add New']"));
                        return addNew.Count > 0;
                    });

                    LoggerService.LogInformation("   ✅ Context Documents page loaded");
                    _sessionManager.UpdateActivity();
                    return true;
                }
                catch (WebDriverTimeoutException)
                {
                    // Log the actual page title to help diagnose redirects
                    var actualTitle = string.Empty;
                    try
                    {
                        actualTitle = _driver.FindElement(
                            By.XPath("//h1 | //div[@id='pageTitle']")).Text;
                    }
                    catch { /* non-fatal */ }

                    LoggerService.LogWarning(
                        "   ⚠️  Context Documents page did not load in time. " +
                        (string.IsNullOrWhiteSpace(actualTitle)
                            ? "No page title found."
                            : $"Current page title: '{actualTitle}'"));

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

    }
}
