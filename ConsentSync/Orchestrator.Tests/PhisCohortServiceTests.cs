using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.Phis;
using OpenQA.Selenium;
using System.Collections.ObjectModel;
using System.Reflection;
using Xunit;

namespace Orchestrator.Tests;

public sealed class PhisCohortServiceTests
{
    [Fact]
    public void IsOnSearchCohortPage_RequiresExpectedPathAndVisibleCriteria()
    {
        var page = new FakeSearchCohortPage();
        var service = page.CreateService();

        Assert.True(service.IsOnSearchCohortPage());

        page.Url = "https://phis.example/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml";
        Assert.False(service.IsOnSearchCohortPage());

        page.Url = FakeSearchCohortPage.SearchUrl;
        page.CohortNameDisplayed = false;
        Assert.False(service.IsOnSearchCohortPage());
    }

    [Fact]
    public void IsOnSearchCohortPage_ReturnsFalseWhenBrowserIsDisconnected()
    {
        var page = new FakeSearchCohortPage { ThrowOnUrlRead = true };

        Assert.False(page.CreateService().IsOnSearchCohortPage());
    }

    [Fact]
    public async Task SearchAsync_WithCohortId_ClearsNameAndClicksOnce()
    {
        var page = new FakeSearchCohortPage { CohortNameValue = "stale list" };

        await page.CreateService().SearchAsync(CohortSearchCriterion.CohortId, " 001234 ");

        Assert.Equal("001234", page.CohortIdValue);
        Assert.Equal(string.Empty, page.CohortNameValue);
        Assert.Equal(1, page.SearchClicks);
    }

    [Fact]
    public async Task SearchAsync_WithClientListName_ClearsCohortIdAndClicksOnce()
    {
        var page = new FakeSearchCohortPage { CohortIdValue = "24182" };

        await page.CreateService().SearchAsync(CohortSearchCriterion.ClientListName, "CIPMONCTONSP20260917");

        Assert.Equal(string.Empty, page.CohortIdValue);
        Assert.Equal("CIPMONCTONSP20260917", page.CohortNameValue);
        Assert.Equal(1, page.SearchClicks);
    }

    private sealed class FakeSearchCohortPage
    {
        public const string SearchUrl = "https://phis.example/phsdsm/ClientWeb/pages/cohort/searchCohort.xhtml?tab=review";
        private const string CriteriaPanelId = "form:CohortSearchCriteria_Panel";
        private const string CohortIdInputId = "form:CohortSearchCriteria_QueryID:inputText";
        private const string CohortNameInputId = "form:CohortName:inputText";
        private const string SearchButtonId = "actionMenuSearch:commandButtonId";

        public string Url { get; set; } = SearchUrl;
        public string CohortIdValue { get; set; } = string.Empty;
        public string CohortNameValue { get; set; } = string.Empty;
        public bool CohortNameDisplayed { get; set; } = true;
        public bool ThrowOnUrlRead { get; set; }
        public int SearchClicks { get; private set; }

        public PhisCohortService CreateService()
        {
            IWebDriver driver = SeleniumDispatchProxy.Create<IWebDriver>((method, arguments) =>
            {
                if (method.Name == "get_Url")
                {
                    if (ThrowOnUrlRead) throw new WebDriverException("Browser disconnected.");
                    return Url;
                }
                if (method.Name == "FindElements")
                {
                    string id = ExtractId(arguments![0]!);
                    IWebElement? element = FindElement(id);
                    return new ReadOnlyCollection<IWebElement>(element is null ? [] : [element]);
                }
                if (method.Name == "FindElement")
                {
                    return FindElement(ExtractId(arguments![0]!)) ?? throw new NoSuchElementException();
                }
                if (method.Name == "ExecuteScript") return true;
                if (method.Name == "Dispose" || method.Name == "Quit") return null;
                throw new NotSupportedException(method.Name);
            });
            var session = new PhisSessionManager(driver, ConfigurationService.GetConfiguration());
            return new PhisCohortService(driver, ConfigurationService.GetConfiguration(), session);
        }

        private IWebElement? FindElement(string id) => id switch
        {
            CriteriaPanelId => CreateElement(displayed: true),
            CohortIdInputId => CreateElement(value: () => CohortIdValue, setValue: value => CohortIdValue = value),
            CohortNameInputId => CreateElement(displayed: CohortNameDisplayed, value: () => CohortNameValue, setValue: value => CohortNameValue = value),
            SearchButtonId => CreateElement(onClick: () => SearchClicks++),
            _ => null
        };

        private static string ExtractId(object argument)
        {
            string value = argument.ToString() ?? string.Empty;
            const string prefix = "By.Id: ";
            return value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
        }

        private static IWebElement CreateElement(
            bool displayed = true,
            Func<string>? value = null,
            Action<string>? setValue = null,
            Action? onClick = null) => SeleniumDispatchProxy.Create<IWebElement>((method, arguments) => method.Name switch
        {
            "get_Displayed" => displayed,
            "get_Enabled" => true,
            "GetAttribute" => arguments![0] as string == "value" ? value?.Invoke() ?? string.Empty : null,
            "Clear" => ClearValue(setValue),
            "SendKeys" => SetValue(setValue, arguments![0] as string ?? string.Empty),
            "Click" => Click(onClick),
            _ => throw new NotSupportedException(method.Name)
        });

        private static object? ClearValue(Action<string>? setValue) { setValue?.Invoke(string.Empty); return null; }
        private static object? SetValue(Action<string>? setValue, string value) { setValue?.Invoke(value); return null; }
        private static object? Click(Action? onClick) { onClick?.Invoke(); return null; }
    }

    private class SeleniumDispatchProxy : DispatchProxy
    {
        private Func<System.Reflection.MethodInfo, object?[]?, object?> _handler = null!;

        public static T Create<T>(Func<System.Reflection.MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, SeleniumDispatchProxy>();
            ((SeleniumDispatchProxy)(object)proxy)._handler = handler;
            return proxy;
        }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            _handler(targetMethod ?? throw new InvalidOperationException(), args);
    }
}
