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

public sealed record CohortCreationResult(CohortCreationStatus Status, string Message)
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
    private const string EncounterGroupSourceId = "maintainCohortForm:EncounterGroup:pickList_source";
    private const string EncounterGroupTargetId = "maintainCohortForm:EncounterGroup:pickList_target";
    private const string EncounterGroupAddButtonId = "maintainCohortForm:EncounterGroup:pickList_button_add";
    private const string SaveButtonId = "actionMenuSave:commandButtonId";
    private const string ImmunizationEncounterGroupValue = "019969f8-35c2-49f1-9d5f-ad78d767434f";

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
            return new(CohortCreationStatus.ExistingResults, "PHIS returned existing cohort search results; no cohort was created.");
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

    private void ClickCreateCohort()
    {
        IReadOnlyList<IWebElement> candidates = _driver.FindElements(By.XPath("//*[self::a or self::button or self::input][normalize-space(.)='Create Cohort' or @value='Create Cohort' or @title='Create Cohort']"))
            .Where(element => element.Displayed && element.Enabled)
            .ToList();
        if (candidates.Count != 1)
            throw new InvalidOperationException($"Expected exactly one enabled 'Create Cohort' action after an empty search, but found {candidates.Count}.");
        candidates[0].Click();
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
        IWebElement input = WaitForVisible(CohortTypeInputId);
        if (string.Equals(input.GetAttribute("value"), "STATIC", StringComparison.OrdinalIgnoreCase)) return;

        string menuId = "maintainCohortForm:CohortType:selectOneMenu";
        WaitForVisible(menuId).Click();
        IWebElement staticOption = _wait.Until(d => d.FindElements(By.XPath("//*[contains(@class, 'ui-selectonemenu-item') and (normalize-space(.)='Static' or @data-label='Static' or @data-value='STATIC')]") )
            .SingleOrDefault(element => element.Displayed));
        staticOption.Click();
        WaitForAjaxQueue();
        input = WaitForVisible(CohortTypeInputId);
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
        if (ContainsOption(EncounterGroupTargetId, ImmunizationEncounterGroupValue, "Immunization")) return;

        IWebElement source = WaitForVisible(EncounterGroupSourceId);
        IWebElement? option = source.FindElements(By.TagName("option")).SingleOrDefault(item =>
            string.Equals(item.GetAttribute("value"), ImmunizationEncounterGroupValue, StringComparison.Ordinal) ||
            string.Equals(item.Text.Trim(), "Immunization", StringComparison.Ordinal));
        if (option is null) throw new InvalidOperationException("The Immunization encounter group is not available for selection.");
        option.Click();
        WaitForEnabled(EncounterGroupAddButtonId).Click();
        WaitForAjaxQueue();
        if (!ContainsOption(EncounterGroupTargetId, ImmunizationEncounterGroupValue, "Immunization"))
            throw new InvalidOperationException("The Immunization encounter group was not moved to Selected Encounter Groups.");
    }

    private bool ContainsOption(string selectId, string value, string text) => _driver.FindElements(By.Id(selectId))
        .Where(element => element.Displayed)
        .SelectMany(element => element.FindElements(By.TagName("option")))
        .Any(option => string.Equals(option.GetAttribute("value"), value, StringComparison.Ordinal) || string.Equals(option.Text.Trim(), text, StringComparison.Ordinal));

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
