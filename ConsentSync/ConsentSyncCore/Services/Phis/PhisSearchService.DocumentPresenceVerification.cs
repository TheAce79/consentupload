using OpenQA.Selenium;

namespace ConsentSyncCore.Services.Phis;

public enum PhisDocumentLookupStatus
{
    Found,
    NotFound,
    Error
}

public sealed class PhisDocumentLookupResult
{
    public PhisDocumentLookupStatus Status { get; init; }
    public string? MatchedTitle { get; init; }
    public string? ErrorMessage { get; init; }

    public static PhisDocumentLookupResult Found(string title) => new() { Status = PhisDocumentLookupStatus.Found, MatchedTitle = title };
    public static PhisDocumentLookupResult NotFound() => new() { Status = PhisDocumentLookupStatus.NotFound };
    public static PhisDocumentLookupResult Error(string message) => new() { Status = PhisDocumentLookupStatus.Error, ErrorMessage = message };
}

public partial class PhisSearchService
{
    public async Task<PhisDocumentLookupResult> CheckIfDocumentExistsDetailedAsync(string documentTitle)
    {
        return await InspectDocumentTitlesDetailedAsync(documentTitle, false);
    }

    public async Task<PhisDocumentLookupResult> CheckIfContextDocumentExistsDetailedAsync(string documentTitle)
    {
        return await InspectDocumentTitlesDetailedAsync(documentTitle, true);
    }

    private async Task<PhisDocumentLookupResult> InspectDocumentTitlesDetailedAsync(string documentTitle, bool isContextRoute)
    {
        try
        {
            await Task.Delay(isContextRoute ? 500 : 1000);
            const string tableId = "userDocumentListForm:docListCollapseSection:documentListDataTable";
            var tables = _driver.FindElements(By.Id(tableId));
            if (tables.Count != 1)
                return PhisDocumentLookupResult.Error("The PHIS document list could not be confirmed.");

            if (HasUninspectedPagination())
                return PhisDocumentLookupResult.Error("The PHIS document list has additional pages that could not be inspected reliably.");

            string linkXPath = isContextRoute
                ? "//a[contains(@id,'userDocumentListForm:docListCollapseSection:documentListDataTable') and contains(@id,'viewtitleLink')]"
                : "//a[contains(@id, 'viewtitleLink')]";
            var links = _driver.FindElements(By.XPath(linkXPath));
            if (links.Count == 0)
                return IsConfirmedEmptyDocumentList(tables[0])
                    ? PhisDocumentLookupResult.NotFound()
                    : PhisDocumentLookupResult.Error("The PHIS document list was empty but its empty state could not be confirmed.");

            string expected = isContextRoute
                ? NormalizeContextPresenceTitle(documentTitle)
                : NormalizeConsentPresenceTitle(documentTitle);

            foreach (var link in links)
            {
                string title;
                try { title = link.Text?.Trim() ?? string.Empty; }
                catch (Exception ex) { return PhisDocumentLookupResult.Error($"A PHIS document title could not be read: {ex.Message}"); }

                if (string.IsNullOrWhiteSpace(title))
                    return PhisDocumentLookupResult.Error("A PHIS document title was blank or unreadable.");

                string actual = isContextRoute
                    ? NormalizeContextPresenceTitle(title)
                    : NormalizeConsentPresenceTitle(title);
                if (actual.Equals(expected, StringComparison.Ordinal))
                    return PhisDocumentLookupResult.Found(title);
            }

            return PhisDocumentLookupResult.NotFound();
        }
        catch (Exception ex)
        {
            return PhisDocumentLookupResult.Error($"PHIS document-list inspection failed: {ex.Message}");
        }
    }

    private bool HasUninspectedPagination()
    {
        var paginator = _driver.FindElements(By.CssSelector(".ui-paginator, [id*='paginator']"));
        if (paginator.Count == 0) return false;
        return paginator.Any(element =>
        {
            try
            {
                return element.FindElements(By.CssSelector(".ui-paginator-page:not(.ui-state-active), a[aria-label*='Page']")).Count > 0;
            }
            catch { return true; }
        });
    }

    private static bool IsConfirmedEmptyDocumentList(IWebElement table)
    {
        string text;
        try { text = table.Text ?? string.Empty; }
        catch { return false; }
        return text.Contains("No records", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("No documents", StringComparison.OrdinalIgnoreCase) ||
               table.FindElements(By.CssSelector("tr")).Count == 0;
    }

    private static string NormalizeConsentPresenceTitle(string value) =>
        (value ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();

    private static string NormalizeContextPresenceTitle(string value) =>
        (value ?? string.Empty).ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
}
