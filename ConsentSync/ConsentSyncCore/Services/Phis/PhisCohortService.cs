using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ConsentSyncCore.Services.Phis;

public enum CohortSearchCriterion
{
    CohortId,
    ClientListName
}

public enum CohortCreationStatus
{
    ExistingResults,
    SearchResultUnavailable,
    Created,
    SaveUnverified
}

public sealed record CohortCreationResult(CohortCreationStatus Status, string Message, int? PhisCohortId = null)
{
    public bool CohortWasSaved => Status is CohortCreationStatus.Created or CohortCreationStatus.SaveUnverified;
}

/// <summary>Automates the PHIS Search Cohort page and creates a new static cohort only after an empty search.</summary>
public sealed class PhisCohortService
{
    private const string SearchCohortPath = "/phsdsm/ClientWeb/pages/cohort/searchCohort.xhtml";
    private const string CriteriaPanelId = "form:CohortSearchCriteria_Panel";
    private const string CohortIdInputId = "form:CohortSearchCriteria_QueryID:inputText";
    private const string CohortNameInputId = "form:CohortName:inputText";
    private const string SearchButtonId = "actionMenuSearch:commandButtonId";
    private const string EmptyResultsId = "form:DataTable:emptyMessageId";
    private const string MaintainCohortPath = "/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml";
    private const string MaintainCohortFormId = "maintainCohortForm";
    private const string CohortNameFieldId = "maintainCohortForm:CohortName:inputText";
    private const string CohortTypeInputId = "maintainCohortForm:CohortType:selectOneMenu_input";
    private const string SaveButtonId = "actionMenuSave:commandButtonId";
    private const string CreateCohortButtonId = "form:DataTable:CreateCohortButtonId:commandButtonId";

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly PhisSessionManager _sessionManager;
    private readonly PhisConfig _phisConfig;

    public PhisCohortService(IWebDriver driver, IConfiguration config, PhisSessionManager sessionManager)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(sessionManager);

        _driver = driver;
        _sessionManager = sessionManager;
        _phisConfig = ConfigurationService.GetPhisConfig();
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(_phisConfig.WebDriverWaitSeconds));
    }

    public bool IsOnSearchCohortPage()
    {
        try
        {
            if (!_driver.Url.Contains(SearchCohortPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return IsDisplayed(CriteriaPanelId) && IsDisplayed(CohortIdInputId) && IsDisplayed(CohortNameInputId);
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    public async Task SearchAsync(CohortSearchCriterion criterion, string value)
    {
        string searchValue = value?.Trim() ?? string.Empty;
        if (searchValue.Length == 0)
        {
            throw new ArgumentException("A non-empty cohort search value is required.", nameof(value));
        }

        if (!IsOnSearchCohortPage())
        {
            throw new InvalidOperationException("PHIS is not on the Search Cohort page.");
        }

        IWebElement cohortIdInput = WaitForVisible(CohortIdInputId);
        IWebElement cohortNameInput = WaitForVisible(CohortNameInputId);

        Clear(cohortIdInput);
        Clear(cohortNameInput);

        IWebElement selectedInput = criterion switch
        {
            CohortSearchCriterion.CohortId => cohortIdInput,
            CohortSearchCriterion.ClientListName => cohortNameInput,
            _ => throw new ArgumentOutOfRangeException(nameof(criterion))
        };
        selectedInput.SendKeys(searchValue);

        VerifyInputs(criterion, searchValue, cohortIdInput, cohortNameInput);

        WaitForAjaxQueue();
        IWebElement searchButton = _wait.Until(d =>
        {
            IWebElement button = d.FindElement(By.Id(SearchButtonId));
            return button.Displayed && button.Enabled ? button : null;
        });
        searchButton.Click();

        await Task.Delay(_phisConfig.AjaxWaitMs);
        WaitForAjaxQueue();
        _wait.Until(_ => IsOnSearchCohortPage());
        _sessionManager.UpdateActivity();
    }

    public async Task<CohortCreationResult> CreateIfSearchReturnedNoResultsAsync(string cohortName)
    {
        string name = cohortName?.Trim() ?? string.Empty;
        if (name.Length == 0) throw new ArgumentException("A non-empty cohort name is required.", nameof(cohortName));
        if (!IsOnSearchCohortPage()) throw new InvalidOperationException("PHIS is not on the Search Cohort page.");

        SearchResultState searchState = GetSearchResultState();
        if (searchState == SearchResultState.ResultsFound)
        {
            int? existingCohortId = ExtractMatchingCohortId(name);
            return existingCohortId.HasValue
                ? new(CohortCreationStatus.ExistingResults, $"PHIS returned existing cohort ID {existingCohortId} for '{name}'; no cohort was created.", existingCohortId)
                : new(CohortCreationStatus.ExistingResults, "PHIS returned existing cohort search results, but no unique exact Client List Name match was found; no cohort was created.");
        }
        if (searchState != SearchResultState.Empty)
            return new(CohortCreationStatus.SearchResultUnavailable, "PHIS search results could not be confirmed as exactly 'No search results.'; no cohort was created.");

        ClickCreateCohort();
        await WaitForAjaxAndBlockUiAsync();
        EnsureMaintainCohortPage();

        PopulateCohort(name);
        string previousLog = GetTransactionText();
        WaitForAjaxQueue();
        WaitForEnabled(SaveButtonId).Click();
        await WaitForAjaxAndBlockUiAsync();
        _sessionManager.UpdateActivity();

        string currentLog = GetTransactionText();
        string errors = GetVisibleMessages(".ui-messages-error, .ui-messages-error-detail, .ui-growl-error, #seriousMessage");
        if (!string.IsNullOrWhiteSpace(errors))
            throw new InvalidOperationException($"PHIS rejected the cohort save: {errors}");

        string newMessages = currentLog.Length > previousLog.Length ? currentLog[previousLog.Length..] : currentLog;
        if (ContainsSaveConfirmation(newMessages) || ContainsSaveConfirmation(GetVisibleMessages("#infoMessage, .ui-messages-info, .ui-growl-info")))
            return new(CohortCreationStatus.Created, $"PHIS confirmed that static cohort '{name}' was saved.");

        return new(CohortCreationStatus.SaveUnverified,
            $"PHIS accepted one save request for static cohort '{name}', but did not provide an explicit success confirmation. The browser remains on the PHIS page for review.");
    }

    private SearchResultState GetSearchResultState()
    {
        IReadOnlyList<IWebElement> emptyMessages = _driver.FindElements(By.Id(EmptyResultsId)).Where(element => element.Displayed).ToList();
        if (emptyMessages.Count == 1)
            return string.Equals(emptyMessages[0].Text.Trim(), "No search results.", StringComparison.Ordinal)
                ? SearchResultState.Empty : SearchResultState.ResultsFound;

        bool hasRows = _driver.FindElements(By.CssSelector("#form\\:DataTable tbody tr, #form\\:DataTable_data tr")).Any(row => row.Displayed);
        return hasRows ? SearchResultState.ResultsFound : SearchResultState.Unavailable;
    }

    private int? ExtractMatchingCohortId(string clientListName)
    {
        IReadOnlyList<int> matchingIds = _driver.FindElements(By.CssSelector("#form\\:DataTable\\:dataTable_data tr[role='row']"))
            .Where(row => row.Displayed)
            .Select(row => row.FindElements(By.CssSelector("td[role='gridcell']")))
            .Where(cells => cells.Count >= 4 && string.Equals(cells[3].Text.Trim(), clientListName, StringComparison.OrdinalIgnoreCase))
            .Select(cells => int.TryParse(cells[2].Text.Trim(), out int cohortId) ? (int?)cohortId : null)
            .Where(cohortId => cohortId.HasValue)
            .Select(cohortId => cohortId!.Value)
            .Distinct()
            .ToList();

        return matchingIds.Count == 1 ? matchingIds[0] : null;
    }

    private void ClickCreateCohort()
    {
        ClickReliably(WaitForEnabled(CreateCohortButtonId));
    }

    private void EnsureMaintainCohortPage()
    {
        if (!_driver.Url.Contains(MaintainCohortPath, StringComparison.OrdinalIgnoreCase) || !IsDisplayed(MaintainCohortFormId))
            throw new InvalidOperationException("PHIS did not navigate to the Create Cohort form.");
    }

    private void PopulateCohort(string cohortName)
    {
        IWebElement name = WaitForVisible(CohortNameFieldId);
        Clear(name);
        name.SendKeys(cohortName);
        if (!string.Equals(name.GetAttribute("value")?.Trim(), cohortName, StringComparison.Ordinal))
            throw new InvalidOperationException("PHIS cohort name could not be verified.");

        SelectStaticCohortType();
        VerifyRequiredDefaults();
        SelectImmunizationEncounterGroup();
    }

    private void SelectStaticCohortType()
    {
        // PrimeFaces keeps the real select inside ui-helper-hidden-accessible; it is present but never displayed.
        IWebElement input = WaitForPresent(CohortTypeInputId);
        if (string.Equals(input.GetAttribute("value"), "STATIC", StringComparison.OrdinalIgnoreCase)) return;

        string menuId = "maintainCohortForm:CohortType:selectOneMenu";
        ClickReliably(WaitForVisible(menuId));
        const string cohortTypeItemsId = "maintainCohortForm:CohortType:selectOneMenu_items";
        IWebElement staticOption = _wait.Until(d => d.FindElements(By.Id(cohortTypeItemsId))
            .SelectMany(items => items.FindElements(By.CssSelector(".ui-selectonemenu-item")))
            .FirstOrDefault(element => element.Displayed &&
                (string.Equals(element.Text.Trim(), "Static", StringComparison.Ordinal) ||
                 string.Equals(element.GetAttribute("data-label"), "Static", StringComparison.Ordinal))));
        ClickReliably(staticOption);
        WaitForAjaxQueue();
        input = WaitForPresent(CohortTypeInputId);
        if (!string.Equals(input.GetAttribute("value"), "STATIC", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PHIS cohort type could not be set to Static.");
    }

    private void VerifyRequiredDefaults()
    {
        string effectiveDate = FindFieldValueAfterLabel("Effective From:");
        if (!DateTime.TryParse(effectiveDate, out _))
            throw new InvalidOperationException("PHIS Effective From must contain a valid default date before saving.");

        string organization = FindFieldValueAfterLabel("Jurisdictional Organization:");
        if (!organization.Contains("Moncton Public Health", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PHIS Jurisdictional Organization must default to Moncton Public Health before saving.");
    }

    private string FindFieldValueAfterLabel(string label)
    {
        IWebElement field = _wait.Until(d => d.FindElements(By.XPath($"//*[normalize-space(text())='{label}']/following::input[not(@type='hidden')][1] | //*[normalize-space(text())='{label}']/following::textarea[1]")).SingleOrDefault(element => element.Displayed));
        return (field.GetAttribute("value") ?? field.Text).Trim();
    }

    private void SelectImmunizationEncounterGroup()
    {
        if (ContainsEncounterGroup(".ui-picklist-target", "Immunization")) return;

        IWebElement pickList = WaitForVisible("maintainCohortForm:EncounterGroup:pickList");
        IWebElement? item = pickList.FindElements(By.CssSelector(".ui-picklist-source .ui-picklist-item"))
            .SingleOrDefault(element => element.Displayed && string.Equals(element.Text.Trim(), "Immunization", StringComparison.Ordinal));
        if (item is null) throw new InvalidOperationException("The Immunization encounter group is not available for selection.");
        ClickReliably(item);
        IWebElement addButton = pickList.FindElements(By.CssSelector(".ui-picklist-button-add"))
            .SingleOrDefault(element => element.Displayed && element.Enabled)
            ?? throw new InvalidOperationException("PHIS did not expose the Add control for Encounter Groups.");
        ClickReliably(addButton);
        WaitForAjaxQueue();
        if (!ContainsEncounterGroup(".ui-picklist-target", "Immunization"))
            throw new InvalidOperationException("The Immunization encounter group was not moved to Selected Encounter Groups.");
    }

    private bool ContainsEncounterGroup(string listClass, string text) => _driver.FindElements(By.CssSelector($"#maintainCohortForm\\:EncounterGroup\\:pickList {listClass} .ui-picklist-item"))
        .Any(element => element.Displayed && string.Equals(element.Text.Trim(), text, StringComparison.Ordinal));

    private void ClickReliably(IWebElement element)
    {
        WaitForTransientOverlays();
        if (_driver is IJavaScriptExecutor javascript)
        {
            javascript.ExecuteScript("arguments[0].scrollIntoView({block: 'center', inline: 'center'});", element);
        }

        try
        {
            element.Click();
        }
        catch (ElementClickInterceptedException)
        {
            if (_driver is not IJavaScriptExecutor fallbackJavascript)
                throw;
            fallbackJavascript.ExecuteScript("arguments[0].click();", element);
        }
    }

    private void WaitForTransientOverlays() => _wait.Until(_ => !_driver.FindElements(By.Id("blockUI"))
        .Any(element => element.Displayed));

    private IWebElement WaitForEnabled(string id) => _wait.Until(d =>
    {
        IWebElement element = d.FindElement(By.Id(id));
        return element.Displayed && element.Enabled ? element : null;
    });

    private async Task WaitForAjaxAndBlockUiAsync()
    {
        await Task.Delay(_phisConfig.AjaxWaitMs);
        WaitForAjaxQueue();
        _wait.Until(_ => !_driver.FindElements(By.Id("blockUI")).Any(element => element.Displayed));
    }

    private string GetTransactionText() => GetVisibleMessages("#transactionLog, #transactionLogPanel");

    private string GetVisibleMessages(string selector) => string.Join("\n", _driver.FindElements(By.CssSelector(selector))
        .Where(element => element.Displayed)
        .Select(element => element.Text.Trim())
        .Where(text => text.Length > 0));

    private static bool ContainsSaveConfirmation(string message) =>
        message.Contains("cohort", StringComparison.OrdinalIgnoreCase) &&
        (message.Contains("created", StringComparison.OrdinalIgnoreCase) || message.Contains("saved", StringComparison.OrdinalIgnoreCase) || message.Contains("success", StringComparison.OrdinalIgnoreCase));

    private IWebElement WaitForVisible(string id) => _wait.Until(d =>
    {
        IWebElement element = d.FindElement(By.Id(id));
        return element.Displayed ? element : null;
    });

    private IWebElement WaitForPresent(string id) => _wait.Until(d =>
    {
        try { return d.FindElement(By.Id(id)); }
        catch (NoSuchElementException) { return null; }
    });

    private bool IsDisplayed(string id) => _driver.FindElements(By.Id(id)).Any(element => element.Displayed);

    private static void Clear(IWebElement input)
    {
        input.Clear();
        if (!string.IsNullOrEmpty(input.GetAttribute("value")))
        {
            throw new InvalidOperationException("PHIS cohort search criteria could not be cleared.");
        }
    }

    private static void VerifyInputs(
        CohortSearchCriterion criterion,
        string expectedValue,
        IWebElement cohortIdInput,
        IWebElement cohortNameInput)
    {
        string cohortId = cohortIdInput.GetAttribute("value") ?? string.Empty;
        string cohortName = cohortNameInput.GetAttribute("value") ?? string.Empty;
        bool valid = criterion switch
        {
            CohortSearchCriterion.CohortId => cohortId == expectedValue && cohortName.Length == 0,
            CohortSearchCriterion.ClientListName => cohortName == expectedValue && cohortId.Length == 0,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidOperationException("PHIS cohort search criteria could not be verified before searching.");
        }
    }

    private void WaitForAjaxQueue() => _wait.Until(_ =>
    {
        try
        {
            if (_driver is not IJavaScriptExecutor scriptExecutor)
            {
                return true;
            }
            return scriptExecutor.ExecuteScript("return !(window.PrimeFaces && PrimeFaces.ajax && PrimeFaces.ajax.Queue && typeof PrimeFaces.ajax.Queue.isEmpty === 'function') || PrimeFaces.ajax.Queue.isEmpty();") is true;
        }
        catch (WebDriverException)
        {
            return false;
        }
    });

    private enum SearchResultState { Empty, ResultsFound, Unavailable }
}
