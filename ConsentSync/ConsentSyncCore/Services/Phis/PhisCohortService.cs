using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System.Globalization;
using System.Diagnostics;

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

public sealed record PhisClientListResult(int ClientListId, int ClientCount);

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
    private const string CohortTypeMenuId = "maintainCohortForm:CohortType:selectOneMenu";
    private const string CohortTypeItemsId = "maintainCohortForm:CohortType:selectOneMenu_items";
    private const string CohortTypeLabelId = "maintainCohortForm:CohortType:selectOneMenu_label";
    private const string EffectiveFromInputId = "maintainCohortForm:EffectiveDateRange:fromDateTime:dateInput_input";
    private const string EffectiveToInputId = "maintainCohortForm:EffectiveDateRange:toDateTime:dateInput_input";
    private const string OrganizationInputId = "maintainCohortForm:orgFinder:orgNameAutoComplete:autoComplete_input";
    private const string EncounterGroupPickListId = "maintainCohortForm:EncounterGroup:pickList";
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
        _wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
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
        string traceId = Guid.NewGuid().ToString("N")[..8];
        Stopwatch stopwatch = Stopwatch.StartNew();
        string name = cohortName?.Trim() ?? string.Empty;
        Trace(traceId, "start", $"cohort creation requested; nameLength={name.Length}");
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
        if (!string.IsNullOrWhiteSpace(WaitForVisible(CohortIdInputId).GetAttribute("value")))
            return new(CohortCreationStatus.SearchResultUnavailable, "The stored PHIS Cohort ID was not found; no replacement cohort was created.");

        try
        {
            ClickCreateCohort();
            Trace(traceId, "create-form", "Create Cohort clicked; waiting for PHIS AJAX and overlays.");
            await WaitForAjaxAndBlockUiAsync();
            EnsureMaintainCohortPage();

            PopulateCohort(name, traceId);
            VerifyPreSaveState(traceId);
            string previousLog = GetTransactionText();
            string previousInfo = GetVisibleMessages("#infoMessage, .ui-messages-info, .ui-growl-info");
            WaitForAjaxQueue();
            Trace(traceId, "save", "all pre-save checks passed; clicking Save once.");
            WaitForEnabled(SaveButtonId).Click();
            await WaitForAjaxAndBlockUiAsync();
            _sessionManager.UpdateActivity();

            string currentLog = GetTransactionText();
            string errors = GetVisibleMessages(".ui-messages-error, .ui-messages-error-detail, .ui-growl-error, #seriousMessage");
            if (!string.IsNullOrWhiteSpace(errors))
                throw new InvalidOperationException($"PHIS rejected the cohort save: {errors}");

            int? savedId = ReadCohortHeaderId(name);
            if (savedId.HasValue)
                return new(CohortCreationStatus.Created, "PHIS saved cohort identity verified.", savedId);

            string newMessages = currentLog.Length > previousLog.Length ? currentLog[previousLog.Length..] : string.Empty;
            string currentInfo = GetVisibleMessages("#infoMessage, .ui-messages-info, .ui-growl-info");
            string newInfo = currentInfo.Length > previousInfo.Length ? currentInfo[previousInfo.Length..] : string.Empty;
            if (ContainsSaveConfirmation(newMessages) || ContainsSaveConfirmation(newInfo))
            {
                Trace(traceId, "complete", $"PHIS confirmed save; elapsedMs={stopwatch.ElapsedMilliseconds}");
                return new(CohortCreationStatus.SaveUnverified, $"PHIS confirmed that static cohort '{name}' was saved, but its ID could not be read. Upload was not started; enter the ID using Save Db and retry.");
            }

            Trace(traceId, "complete", $"Save confirmation missing after one Save click; elapsedMs={stopwatch.ElapsedMilliseconds}");
            return new(CohortCreationStatus.SaveUnverified,
                $"PHIS accepted one save request for static cohort '{name}', but did not provide an explicit success confirmation. The browser remains on the PHIS page for review.");
        }
        catch (Exception ex)
        {
            Trace(traceId, "failed", $"elapsedMs={stopwatch.ElapsedMilliseconds}; {ex.GetType().Name}: {ex.Message}", warning: true);
            throw;
        }
    }

    public int? ReadCohortHeaderId(string expectedName)
    {
        if (!_driver.Url.Contains(MaintainCohortPath, StringComparison.OrdinalIgnoreCase)) return null;
        string name = _driver.FindElements(By.Id(CohortNameFieldId)).FirstOrDefault()?.GetAttribute("value") ?? "";
        if (!string.Equals(name.Trim(), expectedName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The opened PHIS cohort name does not match the active list.");
        var values = _driver.FindElements(By.XPath("//*[@id='uiContextHeaderCCId:uiContextHeaderList_content']//*[contains(@class,'phsdsm-ui-labeledgroup')][div/span[normalize-space(.)='Cohort ID:']]/div[contains(@class,'phsdsm-ui-labeledgroup-contentarea')]"))
            .Where(e => e.Displayed).Select(e => e.Text.Trim()).ToList();
        return values.Count == 1 && int.TryParse(values[0], out int id) && id > 0 ? id : null;
    }

    public async Task OpenExistingCohortAsync(int id, string name)
    {
        var rows = _driver.FindElements(By.CssSelector("#form\\:DataTable\\:dataTable_data tr[role='row']"))
            .Where(row => row.Displayed).Where(row =>
            {
                var cells = row.FindElements(By.CssSelector("td[role='gridcell']"));
                return cells.Count >= 4 && cells[2].Text.Trim() == id.ToString() &&
                    string.Equals(cells[3].Text.Trim(), name, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        if (rows.Count != 1) throw new InvalidOperationException("No unique matching cohort row is available for Update.");
        ClickReliably(rows[0].FindElement(By.CssSelector(".ui-radiobutton-box")));
        await WaitForAjaxAndBlockUiAsync();
        WaitForEnabled("form:DataTable:UpdateCohortButtonId:actionButtonId:commandButtonId").Click();
        await WaitForAjaxAndBlockUiAsync();
        EnsureMaintainCohortPage();
        if (ReadCohortHeaderId(name) != id) throw new InvalidOperationException("PHIS opened a different cohort than requested.");
    }

    public async Task<PhisClientListResult> UploadClientListAsync(int cohortId, string name, string filePath)
    {
        string traceId = Guid.NewGuid().ToString("N")[..8];
        if (ReadCohortHeaderId(name) != cohortId) throw new InvalidOperationException("Cohort identity could not be verified before upload.");
        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length == 0 || file.Length > 1000000 || !file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The client-ID text file must exist, be nonempty, and be no larger than 1,000,000 bytes.");
        // Inspect list identity only to choose the upload destination; always send the entire file.
        var existingList = ReadAttachedClientList(cohortId, name);
        Trace(traceId, "upload", $"cohortId={cohortId}; file={file.Name}; bytes={file.Length}");
        WaitForEnabled("maintainCohortForm:clientListDataTable:UploadClientIDListButtonId:commandButtonId").Click();
        await WaitForAjaxAndBlockUiAsync();
        var input = WaitForPresent("maintainCohortForm:fileUpload").FindElement(By.CssSelector("input[type='file']"));
        input.SendKeys(file.FullName);
        _wait.Until(_ => _driver.FindElements(By.CssSelector("#maintainCohortForm\\:fileUpload .ui-datagrid td"))
            .Any(e => e.Displayed && e.Text.Trim() == file.Name));
        await WaitForAjaxAndBlockUiAsync();
        bool useExistingList = existingList is not null;
        await EnsureUploadDestinationModeAsync(useExistingList, traceId);
        if (existingList is not null)
        {
            SelectExistingUploadList(existingList.ClientListId, name);
            VerifyExistingUploadListSelection(existingList.ClientListId, name);
            Trace(traceId, "upload-destination", $"Existing Client List selected; id={existingList.ClientListId}");
        }
        else
        {
            var nameField = WaitForVisible("maintainCohortForm:clientListRadio:newListName:inputText");
            Clear(nameField);
            nameField.SendKeys(name);
            if (nameField.GetAttribute("value") != name) throw new InvalidOperationException("Upload client-list name could not be verified.");
            if (!IsUploadDestinationModeReady(existing: false))
                throw new InvalidOperationException("PHIS upload destination verification failed: New Client List controls are contradictory.");
            Trace(traceId, "upload-destination", "New Client List selected; name verified.");
        }
        Trace(traceId, "upload-save", "Attachment verified; clicking upload-panel Save once.");
        WaitForEnabled("maintainCohortForm:saveButtonId:commandButtonId").Click();
        await WaitForAjaxAndBlockUiAsync();
        string errors = GetVisibleMessages(".ui-messages-error, .ui-growl-error, #seriousMessage");
        if (!string.IsNullOrWhiteSpace(errors)) throw new InvalidOperationException($"PHIS rejected the client list: {errors}");
        _wait.Until(_ => !_driver.FindElements(By.Id("maintainCohortForm:uploadClientListPanel")).Any(e => e.Displayed));
        WaitForTransientOverlays();
        Trace(traceId, "upload-main-save", "Upload panel saved; clicking main page Save once to persist the client list.");
        WaitForEnabled(SaveButtonId).Click();
        await WaitForAjaxAndBlockUiAsync();
        errors = GetVisibleMessages(".ui-messages-error, .ui-growl-error, #seriousMessage");
        if (!string.IsNullOrWhiteSpace(errors)) throw new InvalidOperationException($"PHIS rejected the main page save: {errors}");
        if (ReadCohortHeaderId(name) != cohortId) throw new InvalidOperationException("Cohort identity changed after the main page save.");
        try
        {
            var result = _wait.Until(_ => ReadAttachedClientList(cohortId, name));
            if (existingList is not null && result.ClientListId != existingList.ClientListId)
                throw new InvalidOperationException("The saved client-list ID differs from the selected upload destination.");
            Trace(traceId, "upload-complete", $"clientListId={result.ClientListId}; clients={result.ClientCount}");
            return result;
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new InvalidOperationException("Upload-panel Save and main page Save were each attempted once, but the attached list could not be verified. Review PHIS before retrying.", ex);
        }
    }

    private void SelectExistingUploadList(int listId, string name)
    {
        const string menuId = "maintainCohortForm:clientListRadio:existingLists:selectOneMenu";
        string expectedLabel = $"{listId}, {name}";
        var options = WaitForPresent(menuId + "_input").FindElements(By.TagName("option"))
            .Where(e => e.GetAttribute("value") == listId.ToString() &&
                string.Equals(NormalizeLabel(e.GetDomProperty("textContent")), NormalizeLabel(expectedLabel), StringComparison.OrdinalIgnoreCase)).ToList();
        if (options.Count != 1) throw new InvalidOperationException("The expected existing client list is not uniquely available in the upload dropdown.");
        TraceUploadDestination("dropdown-open", $"existing list id={listId}");
        ClickReliably(WaitForVisible(menuId).FindElement(By.CssSelector(".ui-selectonemenu-trigger")));
        IWebElement panel;
        try { panel = WaitForVisible(menuId + "_panel"); }
        catch (WebDriverTimeoutException ex) { throw new InvalidOperationException("PHIS upload dropdown did not open.", ex); }
        var filter = panel.FindElements(By.CssSelector(".ui-selectonemenu-filter")).FirstOrDefault();
        if (filter is not null && filter.Enabled) Clear(filter);
        var matches = panel.FindElements(By.CssSelector(".ui-selectonemenu-item"))
            .Where(e => e.Displayed && string.Equals(NormalizeLabel(e.Text), NormalizeLabel(expectedLabel), StringComparison.OrdinalIgnoreCase)).ToList();
        TraceUploadDestination("dropdown-option-match", $"existing list id={listId}; visibleMatches={matches.Count}");
        if (matches.Count != 1) throw new InvalidOperationException("The expected existing client list is not uniquely visible in the upload dropdown.");
        IWebElement option = matches[0];
        ClickReliably(option);
        WaitForAjaxQueue();
        try { _wait.Until(_ => IsExistingUploadListSelectionVerified(listId, expectedLabel)); }
        catch (WebDriverTimeoutException ex) { throw new InvalidOperationException("PHIS upload dropdown selected value could not be verified.", ex); }
    }

    private async Task EnsureUploadDestinationModeAsync(bool existing, string traceId)
    {
        if (IsUploadDestinationModeReady(existing))
        {
            Trace(traceId, "upload-destination-ready", existing ? "Existing Client List already selected." : "New Client List already selected.");
            return;
        }
        string optionId = $"maintainCohortForm:clientListRadio:option{(existing ? 2 : 1)}";
        Trace(traceId, "upload-destination-click", existing ? "Selecting Existing Client List." : "Selecting New Client List.");
        ClickReliably(WaitForPresent(optionId).FindElement(By.CssSelector(".ui-radiobutton-box")));
        await WaitForAjaxAndBlockUiAsync();
        try { _wait.Until(_ => IsUploadDestinationModeReady(existing)); }
        catch (WebDriverTimeoutException ex)
        {
            throw new InvalidOperationException($"PHIS upload destination did not become ready for {(existing ? "Existing Client List" : "New Client List")}. {DescribeUploadDestinationState()}", ex);
        }
    }

    private void VerifyExistingUploadListSelection(int listId, string name)
    {
        if (!IsUploadDestinationModeReady(existing: true) || !IsExistingUploadListSelectionVerified(listId, $"{listId}, {name}"))
            throw new InvalidOperationException($"PHIS upload destination verification failed for Existing Client List. {DescribeUploadDestinationState()}");
    }

    private bool IsUploadDestinationModeReady(bool existing)
    {
        try
        {
            string radioBaseId = "maintainCohortForm:clientListRadio:selectOneRadio:" + (existing ? "1" : "0");
            bool radioChecked = _driver.FindElements(By.Id(radioBaseId)).Concat(_driver.FindElements(By.Id(radioBaseId + "_clone")))
                .Any(element => element.Selected);
            IWebElement? newName = _driver.FindElements(By.Id("maintainCohortForm:clientListRadio:newListName:inputText")).FirstOrDefault();
            IWebElement? existingMenu = _driver.FindElements(By.Id("maintainCohortForm:clientListRadio:existingLists:selectOneMenu")).FirstOrDefault();
            if (newName is null || existingMenu is null) return false;
            return radioChecked && (existing ? !IsDisabled(existingMenu) && IsDisabled(newName) : !IsDisabled(newName) && IsDisabled(existingMenu));
        }
        catch (WebDriverException) { return false; }
    }

    private bool IsExistingUploadListSelectionVerified(int listId, string expectedLabel)
    {
        const string menuId = "maintainCohortForm:clientListRadio:existingLists:selectOneMenu";
        try
        {
            string value = _driver.FindElements(By.Id(menuId + "_input")).FirstOrDefault()?.GetAttribute("value") ?? string.Empty;
            string label = _driver.FindElements(By.Id(menuId + "_label")).FirstOrDefault(e => e.Displayed)?.Text ?? string.Empty;
            return value == listId.ToString() && string.Equals(NormalizeLabel(label), NormalizeLabel(expectedLabel), StringComparison.OrdinalIgnoreCase);
        }
        catch (WebDriverException) { return false; }
    }

    private string DescribeUploadDestinationState()
    {
        try
        {
            bool originalNew = IsRadioChecked("0");
            bool cloneNew = IsRadioChecked("0_clone");
            bool originalExisting = IsRadioChecked("1");
            bool cloneExisting = IsRadioChecked("1_clone");
            bool newDisabled = IsElementDisabled("maintainCohortForm:clientListRadio:newListName:inputText");
            bool existingDisabled = IsElementDisabled("maintainCohortForm:clientListRadio:existingLists:selectOneMenu");
            return $"radio checked (new original={originalNew}, new clone={cloneNew}, existing original={originalExisting}, existing clone={cloneExisting}); controls disabled (new name={newDisabled}, existing list={existingDisabled}).";
        }
        catch (WebDriverException) { return "radio and destination control state could not be read after PHIS updated the upload panel."; }
    }
    private bool IsRadioChecked(string suffix) => _driver.FindElements(By.Id("maintainCohortForm:clientListRadio:selectOneRadio:" + suffix)).Any(element => element.Selected);
    private bool IsElementDisabled(string id)
    {
        IWebElement? element = _driver.FindElements(By.Id(id)).FirstOrDefault();
        return element is null || IsDisabled(element);
    }
    private static bool IsDisabled(IWebElement element) => !element.Enabled || element.GetAttribute("disabled") is not null ||
        string.Equals(element.GetAttribute("aria-disabled"), "true", StringComparison.OrdinalIgnoreCase) ||
        (element.GetAttribute("class")?.Contains("ui-state-disabled", StringComparison.OrdinalIgnoreCase) ?? false);
    private static string NormalizeLabel(string? value) => string.Join(" ", (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static void TraceUploadDestination(string stage, string detail) => LoggerService.LogInformation($"PHIS upload destination {stage}: {detail}");

    private PhisClientListResult? ReadAttachedClientList(int cohortId, string name)
    {
        var table = WaitForPresent("maintainCohortForm:clientListDataTable");
        var matches = table.FindElements(By.CssSelector("tbody.ui-datatable-data tr[role='row']"))
            .Select(row => row.FindElements(By.CssSelector("td[role='gridcell']")))
            .Where(cells => cells.Count >= 4 && string.Equals(cells[2].Text.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1) throw new InvalidOperationException("Multiple attached client lists match the requested name.");
        if (matches.Count == 0) return null;
        if (!int.TryParse(matches[0][1].Text.Trim(), out int listId) || listId <= 0 ||
            !int.TryParse(matches[0][3].Text.Trim(), out int count) || count < 0)
            throw new InvalidOperationException("Attached client-list ID or client count is invalid.");
        foreach (var link in _driver.FindElements(By.CssSelector("#uiContextHeaderCCId\\:uiContextHeaderList_content a[href*='type=resultSet']")))
        {
            if (!link.Text.Trim().StartsWith(name + " /", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(link.GetAttribute("href"), UriKind.Absolute, out var uri))
                throw new InvalidOperationException("Client-list header link is invalid.");
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (query["contextId"] != cohortId.ToString() || query["id"] != listId.ToString())
                throw new InvalidOperationException("Client-list header link and attached list disagree.");
            listId = int.Parse(query["id"]!);
        }
        return new(listId, count);
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

    private void PopulateCohort(string cohortName, string traceId)
    {
        IWebElement name = WaitForVisible(CohortNameFieldId);
        Clear(name);
        name.SendKeys(cohortName);
        if (!string.Equals(name.GetAttribute("value")?.Trim(), cohortName, StringComparison.Ordinal))
            throw new InvalidOperationException("PHIS cohort name could not be verified.");

        VerifyRequiredDefaults();
        SelectImmunizationEncounterGroup(traceId);
        // The Encounter Group picklist refreshes the form and clears Cohort Type in PHIS.
        // Select Static only after that AJAX update has finished so the value survives until Save.
        SelectStaticCohortType(traceId);
    }

    private void SelectStaticCohortType(string traceId)
    {
        // PrimeFaces keeps the real select inside ui-helper-hidden-accessible; it is present but never displayed.
        IWebElement input = WaitForPresent(CohortTypeInputId);
        if (IsStaticCohortTypeSelected())
        {
            Trace(traceId, "cohort-type", "Static was already selected.");
            return;
        }

        ClickReliably(WaitForVisible(CohortTypeMenuId));
        IWebElement staticOption = _wait.Until(d => d.FindElements(By.Id(CohortTypeItemsId))
            .SelectMany(items => items.FindElements(By.CssSelector(".ui-selectonemenu-item")))
            .FirstOrDefault(element => element.Displayed &&
                (string.Equals(element.Text.Trim(), "Static", StringComparison.Ordinal) ||
                 string.Equals(element.GetAttribute("data-label"), "Static", StringComparison.Ordinal))));
        ClickReliably(staticOption);
        WaitForAjaxQueue();
        _wait.Until(d => d.FindElements(By.Id(CohortTypeMenuId))
            .All(menu => !string.Equals(menu.GetAttribute("aria-expanded"), "true", StringComparison.OrdinalIgnoreCase)));
        input = WaitForPresent(CohortTypeInputId);
        if (!IsStaticCohortTypeSelected())
            throw new InvalidOperationException("PHIS cohort type could not be set to Static.");
        Trace(traceId, "cohort-type", "Static selected and visible label verified.");
    }

    private void VerifyRequiredDefaults()
    {
        string effectiveDate = ReadVisibleFieldValue(EffectiveFromInputId, "Effective From");
        if (!DateTime.TryParseExact(effectiveDate, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidOperationException("PHIS Effective From must contain a valid default date before saving.");

        string effectiveTo = ReadVisibleFieldValue(EffectiveToInputId, "To date");
        if (!string.IsNullOrWhiteSpace(effectiveTo))
            throw new InvalidOperationException("PHIS To date must be empty before creating a static cohort.");

        string organization = ReadVisibleFieldValue(OrganizationInputId, "Jurisdictional Organization");
        if (!organization.Contains("Moncton Public Health", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PHIS Jurisdictional Organization must default to Moncton Public Health before saving.");
    }

    private string ReadVisibleFieldValue(string id, string fieldName)
    {
        IReadOnlyList<IWebElement> fields;
        try
        {
            fields = _wait.Until(d =>
            {
                IReadOnlyList<IWebElement> matches = d.FindElements(By.Id(id)).Where(element => element.Displayed).ToList();
                return matches.Count > 0 ? matches : null;
            });
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new InvalidOperationException($"PHIS {fieldName} control was not available on the Create Cohort form.", ex);
        }

        if (fields.Count != 1)
            throw new InvalidOperationException($"PHIS {fieldName} control was ambiguous on the Create Cohort form.");

        IWebElement field = fields[0];
        return (field.GetAttribute("value") ?? field.Text).Trim();
    }

    private void SelectImmunizationEncounterGroup(string traceId)
    {
        if (ContainsEncounterGroup(".ui-picklist-target", "Immunization"))
        {
            Trace(traceId, "encounter-group", "Immunization was already selected.");
            return;
        }

        IWebElement pickList = WaitForVisible(EncounterGroupPickListId);
        IReadOnlyList<IWebElement> items = pickList.FindElements(By.CssSelector(".ui-picklist-source .ui-picklist-item"))
            .Where(element => element.Displayed && string.Equals(element.Text.Trim(), "Immunization", StringComparison.Ordinal))
            .ToList();
        if (items.Count != 1)
            throw new InvalidOperationException($"PHIS {(items.Count == 0 ? "did not provide" : "provided more than one")} Immunization encounter group for selection.");

        IWebElement item = items[0];
        ClickReliably(item);
        IReadOnlyList<IWebElement> addButtons = WaitForVisible(EncounterGroupPickListId)
            .FindElements(By.CssSelector(".ui-picklist-button-add"))
            .Where(element => element.Displayed && element.Enabled)
            .ToList();
        if (addButtons.Count != 1)
            throw new InvalidOperationException($"PHIS {(addButtons.Count == 0 ? "did not expose" : "exposed more than one")} Add control for Encounter Groups.");

        IWebElement addButton = addButtons[0];
        ClickReliably(addButton);
        WaitForAjaxQueue();
        try
        {
            _wait.Until(_ => ContainsEncounterGroup(".ui-picklist-target", "Immunization"));
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new InvalidOperationException("The Immunization encounter group was not moved to Selected Encounter Groups.", ex);
        }
        Trace(traceId, "encounter-group", "Immunization transfer verified in Selected Encounter Groups.");
    }

    private void VerifyPreSaveState(string traceId)
    {
        VerifyRequiredDefaults();
        if (!IsStaticCohortTypeSelected())
            throw new InvalidOperationException("PHIS cohort type is not visibly set to Static before saving.");
        if (!ContainsEncounterGroup(".ui-picklist-target", "Immunization"))
            throw new InvalidOperationException("PHIS Immunization encounter group is not selected before saving.");
        Trace(traceId, "pre-save", "Static label/value, required defaults, and Immunization selection verified.");
    }

    private bool IsStaticCohortTypeSelected()
    {
        string value = WaitForPresent(CohortTypeInputId).GetAttribute("value")?.Trim() ?? string.Empty;
        string label = _driver.FindElements(By.Id(CohortTypeLabelId)).Where(element => element.Displayed)
            .Select(element => element.Text.Trim()).SingleOrDefault() ?? string.Empty;
        return string.Equals(value, "STATIC", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(label, "Static", StringComparison.OrdinalIgnoreCase);
    }

    private static void Trace(string traceId, string stage, string detail, bool warning = false)
    {
        string message = $"PHIS cohort [{traceId}] {stage}: {detail}";
        if (warning) LoggerService.LogWarning(message); else LoggerService.LogInformation(message);
    }

    private bool ContainsEncounterGroup(string listClass, string text)
    {
        try
        {
            return _driver.FindElements(By.CssSelector($"#maintainCohortForm\\:EncounterGroup\\:pickList {listClass} .ui-picklist-item"))
                .Any(element => element.Displayed && string.Equals(element.Text.Trim(), text, StringComparison.Ordinal));
        }
        catch (StaleElementReferenceException)
        {
            return false;
        }
    }

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
