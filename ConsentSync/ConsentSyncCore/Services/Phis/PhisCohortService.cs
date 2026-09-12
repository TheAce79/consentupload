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

/// <summary>Automates the PHIS Search Cohort page without creating or changing a cohort.</summary>
public sealed class PhisCohortService
{
    private const string SearchCohortPath = "/phsdsm/ClientWeb/pages/cohort/searchCohort.xhtml";
    private const string CriteriaPanelId = "form:CohortSearchCriteria_Panel";
    private const string CohortIdInputId = "form:CohortSearchCriteria_QueryID:inputText";
    private const string CohortNameInputId = "form:CohortName:inputText";
    private const string SearchButtonId = "actionMenuSearch:commandButtonId";

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
}
