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

    [Fact]
    public async Task CreateIfSearchReturnedNoResultsAsync_DoesNotCreateWhenSearchHasResults()
    {
        var page = new FakeSearchCohortPage { EmptyResultText = "1 result found" };

        CohortCreationResult result = await page.CreateService().CreateIfSearchReturnedNoResultsAsync("CIPMONCTONSP20260917");

        Assert.Equal(CohortCreationStatus.ExistingResults, result.Status);
        Assert.False(result.CohortWasSaved);
    }

    [Fact]
    public async Task CreateIfSearchReturnedNoResultsAsync_DoesNotCreateWhenEmptyStateIsMissing()
    {
        var page = new FakeSearchCohortPage();

        CohortCreationResult result = await page.CreateService().CreateIfSearchReturnedNoResultsAsync("CIPMONCTONSP20260917");

        Assert.Equal(CohortCreationStatus.SearchResultUnavailable, result.Status);
        Assert.False(result.CohortWasSaved);
    }

    [Fact]
    public async Task CreateIfSearchReturnedNoResultsAsync_ExtractsTheUniqueExactClientListMatch()
    {
        var page = new FakeSearchCohortPage
        {
            SearchResultRows = [new(24250, "CIPMONCTONSP20260917")]
        };

        CohortCreationResult result = await page.CreateService().CreateIfSearchReturnedNoResultsAsync("CIPMONCTONSP20260917");

        Assert.Equal(CohortCreationStatus.ExistingResults, result.Status);
        Assert.Equal(24250, result.PhisCohortId);
    }

    [Fact]
    public void VerifyRequiredDefaults_UsesExactControlsAndAllowsAnEmptyToDate()
    {
        var page = new FakeSearchCohortPage();

        InvokeVerifyRequiredDefaults(page.CreateService());
    }

    [Fact]
    public void VerifyRequiredDefaults_RejectsAPopulatedToDate()
    {
        var page = new FakeSearchCohortPage { EffectiveToDateValue = "2026/09/14" };

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => InvokeVerifyRequiredDefaults(page.CreateService()));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("To date must be empty", exception.InnerException!.Message);
    }

    [Fact]
    public void StaticCohortType_RequiresBothUnderlyingValueAndVisibleLabel()
    {
        var page = new FakeSearchCohortPage { CohortTypeValue = "STATIC", CohortTypeLabel = "" };
        Assert.False(InvokeIsStaticCohortTypeSelected(page.CreateService()));

        page.CohortTypeLabel = "Static";
        Assert.True(InvokeIsStaticCohortTypeSelected(page.CreateService()));
    }

    [Fact]
    public async Task StoredIdNotFound_DoesNotCreateReplacement()
    {
        var page = new FakeSearchCohortPage { CohortIdValue = "24260", EmptyResultText = "No search results." };
        Assert.Equal(CohortCreationStatus.SearchResultUnavailable,
            (await page.CreateService().CreateIfSearchReturnedNoResultsAsync("LIST")).Status);
    }

    [Theory]
    [InlineData("24260", 24260)]
    [InlineData("", null)]
    [InlineData("-1", null)]
    [InlineData("n/a", null)]
    public void HeaderId_RequiresPositiveIdOnMatchingCohort(string headerId, int? expected)
    {
        var page = new FakeSearchCohortPage
        {
            Url = "https://phis.example/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml",
            CohortNameValue = "LIST", HeaderId = headerId
        };
        Assert.Equal(expected, page.CreateService().ReadCohortHeaderId("LIST"));
        Assert.Throws<InvalidOperationException>(() => page.CreateService().ReadCohortHeaderId("OTHER"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Upload_ChoosesDestinationAndSavesPanelThenMain(bool existing)
    {
        var page = new FakeSearchCohortPage
        {
            Url = "https://phis.example/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml",
            CohortNameValue = "LIST", HeaderId = "24260", AttachedListId = existing ? "24189" : null
        };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "123\n456\n123\n");
        try
        {
            var result = await page.CreateService().UploadClientListAsync(24260, "LIST", path);
            Assert.Equal(24189, result.ClientListId);
            Assert.Equal(22, result.ClientCount);
            Assert.Equal(path, page.UploadedPath);
            Assert.Equal("123\n456\n123\n", await File.ReadAllTextAsync(path));
            Assert.Equal(1, page.UploadClicks);
            Assert.Equal(1, page.UploadSaveClicks);
            Assert.Equal(new[] { "panel", "main" }, page.SaveOrder);
            Assert.Equal(existing, page.ExistingSelected);
            Assert.Equal(existing ? "24189" : "", page.SelectedUploadList);
            Assert.Equal(existing ? "" : "LIST", page.UploadName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Upload_UsesCheckedCloneWhenAjaxReplacementLeavesOriginalRadioUnchecked()
    {
        var page = new FakeSearchCohortPage
        {
            Url = "https://phis.example/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml",
            CohortNameValue = "LIST", HeaderId = "24260", AttachedListId = "24189", UseRadioCloneForExisting = true
        };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "123\n");
        try
        {
            await page.CreateService().UploadClientListAsync(24260, "LIST", path);
            Assert.Equal(new[] { "panel", "main" }, page.SaveOrder);
            Assert.Equal("24189", page.SelectedUploadList);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Upload_FirstClientListWithoutExistingDropdown_UsesSelectedNewRadioAndSaves(bool useNewRadioClone)
    {
        var page = new FakeSearchCohortPage
        {
            Url = "https://phis.example/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml",
            CohortNameValue = "LIST", HeaderId = "24260", AttachedListId = null,
            ExistingMenuPresent = false, UseRadioCloneForNew = useNewRadioClone
        };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "123\n");
        try
        {
            await page.CreateService().UploadClientListAsync(24260, "LIST", path);

            Assert.Equal("LIST", page.UploadName);
            Assert.Equal(0, page.NewDestinationClicks);
            Assert.Equal(new[] { "panel", "main" }, page.SaveOrder);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(InvalidNewClientListLayouts))]
    public void NewClientListDestination_InvalidLayoutIsRejectedBeforeSave(Action<FakeSearchCohortPage> arrange)
    {
        var page = new FakeSearchCohortPage();
        arrange(page);

        bool ready = (bool)typeof(PhisCohortService)
            .GetMethod("IsUploadDestinationModeReady", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page.CreateService(), [false])!;

        Assert.False(ready);
        Assert.Empty(page.SaveOrder);
    }

    public static IEnumerable<object[]> InvalidNewClientListLayouts()
    {
        yield return [new Action<FakeSearchCohortPage>(page => page.NewNamePresent = false)];
        yield return [new Action<FakeSearchCohortPage>(page => page.NewNameDisplayed = false)];
        yield return [new Action<FakeSearchCohortPage>(page => page.NewNameEnabled = false)];
        yield return [new Action<FakeSearchCohortPage>(page => page.NewSelected = false)];
        yield return [new Action<FakeSearchCohortPage>(page => page.ExistingMenuEnabled = true)];
    }

    [Theory]
    [InlineData("MISSING")]
    [InlineData("DUPLICATE")]
    public async Task Upload_InvalidExistingDropdownMatch_DoesNotSave(string dropdownState)
    {
        var page = new FakeSearchCohortPage
        {
            Url = "https://phis.example/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml",
            CohortNameValue = "LIST", HeaderId = "24260", AttachedListId = "24189", ExistingDropdownState = dropdownState
        };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "123\n");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => page.CreateService().UploadClientListAsync(24260, "LIST", path));
            Assert.Empty(page.SaveOrder);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExistingDestination_ContradictoryControlsAreRejectedBeforeSave()
    {
        var page = new FakeSearchCohortPage { ExistingSelected = true, DestinationControlsContradictory = true };
        var error = Assert.Throws<TargetInvocationException>(() => typeof(PhisCohortService)
            .GetMethod("VerifyExistingUploadListSelection", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page.CreateService(), [24189, "LIST"]));

        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Empty(page.SaveOrder);
    }

    [Fact]
    public void AttachedList_RejectsHeaderLinkForAnotherCohort()
    {
        var page = new FakeSearchCohortPage
        {
            Url = "https://phis.example/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml",
            CohortNameValue = "LIST", HeaderId = "24260", AttachedListId = "24189", LinkCohortId = "999"
        };
        var error = Assert.Throws<TargetInvocationException>(() => typeof(PhisCohortService)
            .GetMethod("ReadAttachedClientList", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page.CreateService(), [24260, "LIST"]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [Theory]
    [InlineData("panel")]
    [InlineData("main")]
    public async Task Upload_SaveRejectionStopsWithoutRetry(string failingStage)
    {
        var page = new FakeSearchCohortPage
        {
            Url = "https://phis.example/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml",
            CohortNameValue = "LIST", HeaderId = "24260", FailSaveStage = failingStage
        };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "123\n");
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => page.CreateService().UploadClientListAsync(24260, "LIST", path));
            Assert.Contains("rejected", error.Message);
            Assert.Equal(failingStage == "panel" ? new[] { "panel" } : new[] { "panel", "main" }, page.SaveOrder);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AdminSummary_ListsAddedAndRemovedClients()
    {
        var comparison = new PhisUploadComparison(false, [new("101", "Ada Lovelace")], [new("102", "Grace Hopper")]);
        string existing = PhisAdminSummary.Format(24260, "LIST", new(24189, 22), 25, comparison);
        Assert.Contains("LIST / 24189", existing);
        Assert.Contains("Added since previous successful upload: 1", existing);
        Assert.Contains("101 | Ada Lovelace", existing);
        Assert.Contains("No longer in current payload: 1", existing);
        Assert.Contains("102 | Grace Hopper", existing);
        Assert.Contains("Client IDs exported: 25", existing);
        Assert.Contains("Clients in PHIS list (verified): 22", existing);
        Assert.Contains("Full exported list uploaded", existing);
    }

    [Fact]
    public void UploadComparison_IgnoresOrderDuplicatesWhitespaceAndNameOnlyChanges()
    {
        var prior = new[] { new PhisUploadClient(" 1 ", "Old name"), new PhisUploadClient("2", "Two") };
        var current = new[] { new PhisUploadClient("2", "Renamed"), new PhisUploadClient("1", "One"), new PhisUploadClient("1", "") };

        PhisUploadComparison comparison = PhisUploadComparer.Compare(current, prior);

        Assert.False(comparison.IsInitialUpload);
        Assert.False(comparison.HasMembershipChanges);
    }

    [Fact]
    public void UploadComparison_ReportsInitialAndMembershipChanges()
    {
        var initial = PhisUploadComparer.Compare([new PhisUploadClient("1", "One")], null);
        var changed = PhisUploadComparer.Compare([new PhisUploadClient("2", "Two"), new PhisUploadClient("3", "")], [new PhisUploadClient("1", "One"), new PhisUploadClient("2", "Old two")]);

        Assert.True(initial.IsInitialUpload);
        Assert.Equal("1", initial.Added.Single().ClientId);
        Assert.Equal(["3"], changed.Added.Select(x => x.ClientId));
        Assert.Equal(["1"], changed.Removed.Select(x => x.ClientId));
        Assert.Equal("One", changed.Removed.Single().FullName);
    }

    [Fact]
    public async Task MissingUploadFile_StopsBeforeOpeningUploadPanel()
    {
        var page = new FakeSearchCohortPage
        {
            Url = "https://phis.example/phsdsm/ClientWeb/pages/cohort/maintainCohort.xhtml",
            CohortNameValue = "LIST", HeaderId = "24260"
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            page.CreateService().UploadClientListAsync(24260, "LIST", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt")));
        Assert.Contains("must exist", error.Message);
    }

    private static void InvokeVerifyRequiredDefaults(PhisCohortService service) =>
        typeof(PhisCohortService).GetMethod("VerifyRequiredDefaults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, null);

    private static bool InvokeIsStaticCohortTypeSelected(PhisCohortService service) =>
        (bool)typeof(PhisCohortService).GetMethod("IsStaticCohortTypeSelected", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, null)!;

    public sealed class FakeSearchCohortPage
    {
        public const string SearchUrl = "https://phis.example/phsdsm/ClientWeb/pages/cohort/searchCohort.xhtml?tab=review";
        private const string CriteriaPanelId = "form:CohortSearchCriteria_Panel";
        private const string CohortIdInputId = "form:CohortSearchCriteria_QueryID:inputText";
        private const string CohortNameInputId = "form:CohortName:inputText";
        private const string SearchButtonId = "actionMenuSearch:commandButtonId";
        private const string EmptyResultsId = "form:DataTable:emptyMessageId";
        private const string EffectiveFromInputId = "maintainCohortForm:EffectiveDateRange:fromDateTime:dateInput_input";
        private const string EffectiveToInputId = "maintainCohortForm:EffectiveDateRange:toDateTime:dateInput_input";
        private const string OrganizationInputId = "maintainCohortForm:orgFinder:orgNameAutoComplete:autoComplete_input";
        private const string CohortTypeInputId = "maintainCohortForm:CohortType:selectOneMenu_input";
        private const string CohortTypeLabelId = "maintainCohortForm:CohortType:selectOneMenu_label";

        public string Url { get; set; } = SearchUrl;
        public string CohortIdValue { get; set; } = string.Empty;
        public string CohortNameValue { get; set; } = string.Empty;
        public bool CohortNameDisplayed { get; set; } = true;
        public bool ThrowOnUrlRead { get; set; }
        public string? EmptyResultText { get; set; }
        public string EffectiveFromDateValue { get; set; } = "2026/09/13";
        public string EffectiveToDateValue { get; set; } = string.Empty;
        public string OrganizationValue { get; set; } = "Moncton Public Health, Moncton, New Brunswick";
        public string CohortTypeValue { get; set; } = string.Empty;
        public string CohortTypeLabel { get; set; } = string.Empty;
        public string HeaderId { get; set; } = "";
        public string? AttachedListId { get; set; }
        public string LinkCohortId { get; set; } = "24260";
        public string UploadedPath { get; set; } = "";
        public string UploadName { get; set; } = "";
        public int UploadClicks { get; set; }
        public int UploadSaveClicks { get; set; }
        public int NewDestinationClicks { get; set; }
        public List<string> SaveOrder { get; } = [];
        public bool ExistingSelected { get; set; }
        public bool NewSelected { get; set; } = true;
        public bool UseRadioCloneForNew { get; set; }
        public bool UseRadioCloneForExisting { get; set; }
        public bool NewNamePresent { get; set; } = true;
        public bool NewNameDisplayed { get; set; } = true;
        public bool? NewNameEnabled { get; set; }
        public bool ExistingMenuPresent { get; set; } = true;
        public bool? ExistingMenuEnabled { get; set; }
        public bool DestinationControlsContradictory { get; set; }
        public bool DropdownOpen { get; set; }
        public string? ExistingDropdownState { get; set; }
        public string SelectedUploadList { get; set; } = "";
        public string? FailSaveStage { get; set; }
        public List<(int CohortId, string CohortName)> SearchResultRows { get; set; } = [];
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
                    if (id.Contains("DataTable", StringComparison.Ordinal) && id.Contains("tr", StringComparison.Ordinal))
                    {
                        return new ReadOnlyCollection<IWebElement>(SearchResultRows.Select(row => CreateResultRow(row.CohortId, row.CohortName)).ToList());
                    }
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
            EmptyResultsId when EmptyResultText is not null => CreateElement(text: () => EmptyResultText),
            EffectiveFromInputId => CreateElement(value: () => EffectiveFromDateValue),
            EffectiveToInputId => CreateElement(value: () => EffectiveToDateValue),
            OrganizationInputId => CreateElement(value: () => OrganizationValue),
            CohortTypeInputId => CreateElement(displayed: false, value: () => CohortTypeValue),
            CohortTypeLabelId => CreateElement(text: () => CohortTypeLabel),
            "maintainCohortForm:CohortName:inputText" => CreateElement(value: () => CohortNameValue),
            "maintainCohortForm:clientListDataTable:UploadClientIDListButtonId:commandButtonId" => CreateElement(onClick: () => UploadClicks++),
            "maintainCohortForm:saveButtonId:commandButtonId" => CreateElement(onClick: () => { UploadSaveClicks++; SaveOrder.Add("panel"); }),
            "actionMenuSave:commandButtonId" => CreateElement(onClick: () => { SaveOrder.Add("main"); AttachedListId ??= "24189"; }),
            "maintainCohortForm:clientListRadio:newListName:inputText" when !NewNamePresent => null,
            "maintainCohortForm:clientListRadio:newListName:inputText" => CreateElement(displayed: NewNameDisplayed, enabled: NewNameEnabled ?? (DestinationControlsContradictory || !ExistingSelected), value: () => UploadName, setValue: v => UploadName = v),
            "maintainCohortForm:clientListRadio:selectOneRadio:0" => CreateRadio(() => NewSelected && !UseRadioCloneForNew),
            "maintainCohortForm:clientListRadio:selectOneRadio:0_clone" => CreateRadio(() => NewSelected),
            "maintainCohortForm:clientListRadio:selectOneRadio:1" => CreateRadio(() => ExistingSelected && !UseRadioCloneForExisting),
            "maintainCohortForm:clientListRadio:selectOneRadio:1_clone" => CreateRadio(() => ExistingSelected),
            "maintainCohortForm:clientListRadio:option1" => SeleniumDispatchProxy.Create<IWebElement>((m, a) => m.Name == "FindElement"
                ? CreateElement(onClick: () => { NewDestinationClicks++; ExistingSelected = false; NewSelected = true; }) : throw new NotSupportedException(m.Name)),
            "maintainCohortForm:clientListRadio:option2" => SeleniumDispatchProxy.Create<IWebElement>((m, a) => m.Name == "FindElement"
                ? CreateElement(onClick: () => { ExistingSelected = true; NewSelected = false; }) : throw new NotSupportedException(m.Name)),
            "maintainCohortForm:clientListRadio:existingLists:selectOneMenu_input" when !ExistingMenuPresent => null,
            "maintainCohortForm:clientListRadio:existingLists:selectOneMenu_input" => SeleniumDispatchProxy.Create<IWebElement>((m, a) => m.Name switch
            {
                "GetAttribute" => SelectedUploadList,
                "FindElements" => new ReadOnlyCollection<IWebElement>(GetDropdownOptions().Select(label => CreateElement(value: () => label.StartsWith("24189", StringComparison.Ordinal) ? "24189" : "123", text: () => string.Empty, domText: () => label)).ToList()),
                _ => throw new NotSupportedException(m.Name)
            }),
            "maintainCohortForm:clientListRadio:existingLists:selectOneMenu" when !ExistingMenuPresent => null,
            "maintainCohortForm:clientListRadio:existingLists:selectOneMenu" => SeleniumDispatchProxy.Create<IWebElement>((m, a) => m.Name switch
            {
                "get_Displayed" => true,
                "get_Enabled" => ExistingMenuEnabled ?? (ExistingSelected && !DestinationControlsContradictory),
                "FindElement" => CreateElement(onClick: () => DropdownOpen = true),
                "GetAttribute" => null,
                _ => throw new NotSupportedException(m.Name)
            }),
            "maintainCohortForm:clientListRadio:existingLists:selectOneMenu_panel" => SeleniumDispatchProxy.Create<IWebElement>((m, a) => m.Name switch
            {
                "get_Displayed" => DropdownOpen,
                "FindElements" => new ReadOnlyCollection<IWebElement>(CreatePanelElements(a![0]!.ToString()!).ToList()),
                _ => throw new NotSupportedException(m.Name)
            }),
            "maintainCohortForm:clientListRadio:existingLists:selectOneMenu_label" => CreateElement(text: () => SelectedUploadList == "24189" ? "24189, LIST" : ""),
            _ when id.Contains(".ui-messages-error") && FailSaveStage is not null && SaveOrder.LastOrDefault() == FailSaveStage => CreateElement(text: () => "Save rejected"),
            "maintainCohortForm:fileUpload" => SeleniumDispatchProxy.Create<IWebElement>((m, a) => m.Name == "FindElement"
                ? CreateElement(setValue: v => UploadedPath = v) : throw new NotSupportedException(m.Name)),
            _ when id.Contains(".ui-datagrid td") && UploadedPath.Length > 0 => CreateElement(text: () => Path.GetFileName(UploadedPath)),
            "maintainCohortForm:clientListDataTable" => SeleniumDispatchProxy.Create<IWebElement>((method, args) =>
            {
                if (method.Name == "get_Text") return "No records found.";
                if (method.Name == "FindElements")
                {
                    string selector = args![0]!.ToString()!;
                    if (AttachedListId is null || !selector.Contains("tbody")) return new ReadOnlyCollection<IWebElement>([]);
                    var row = SeleniumDispatchProxy.Create<IWebElement>((m, a) => new ReadOnlyCollection<IWebElement>(
                        new[] { "", AttachedListId, CohortNameValue, "22" }.Select(s => CreateElement(text: () => s)).ToList()));
                    return new ReadOnlyCollection<IWebElement>([row]);
                }
                throw new NotSupportedException(method.Name);
            }),
            _ when id.StartsWith("By.XPath:") && id.Contains("Cohort ID:") => CreateElement(text: () => HeaderId),
            _ when id.Contains("a[href*='type=resultSet']") && AttachedListId is not null => SeleniumDispatchProxy.Create<IWebElement>((method, args) => method.Name switch
            {
                "get_Text" => $"{CohortNameValue} / {AttachedListId}",
                "GetAttribute" => $"https://phis.example/phsdsm/UIContextHeaderServlet?contextId={LinkCohortId}&id={AttachedListId}&type=resultSet",
                _ => throw new NotSupportedException(method.Name)
            }),
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
            bool enabled = true,
            Func<string>? value = null,
            Func<string>? text = null,
            Func<string>? domText = null,
            Action<string>? setValue = null,
            Action? onClick = null) => SeleniumDispatchProxy.Create<IWebElement>((method, arguments) => method.Name switch
        {
            "get_Displayed" => displayed,
            "get_Enabled" => enabled,
            "get_Text" => text?.Invoke() ?? string.Empty,
            "GetDomProperty" => arguments![0] as string == "textContent" ? domText?.Invoke() ?? string.Empty : null,
            "GetAttribute" => arguments![0] as string == "value" ? value?.Invoke() ?? string.Empty : null,
            "Clear" => ClearValue(setValue),
            "SendKeys" => SetValue(setValue, arguments![0] as string ?? string.Empty),
            "Click" => Click(onClick),
            _ => throw new NotSupportedException(method.Name)
        });

        private static object? ClearValue(Action<string>? setValue) { setValue?.Invoke(string.Empty); return null; }
        private static object? SetValue(Action<string>? setValue, string value) { setValue?.Invoke(value); return null; }
        private static object? Click(Action? onClick) { onClick?.Invoke(); return null; }

        private IWebElement CreateRadio(Func<bool> selected) => SeleniumDispatchProxy.Create<IWebElement>((m, a) => m.Name == "get_Selected" ? selected() : throw new NotSupportedException(m.Name));
        private IEnumerable<string> GetDropdownOptions() => ExistingDropdownState switch
        {
            "MISSING" => ["123, OTHER"],
            "DUPLICATE" => ["24189, LIST", "24189, LIST"],
            _ => ["", "123, OTHER", "24189, LIST"]
        };
        private IReadOnlyList<IWebElement> CreatePanelElements(string selector) => selector.Contains("filter", StringComparison.Ordinal)
            ? [CreateElement()]
            : GetDropdownOptions().Select(label => CreateElement(text: () => label, onClick: () =>
            {
                if (label == "24189, LIST") SelectedUploadList = "24189";
            })).ToList();

        private static IWebElement CreateResultRow(int cohortId, string cohortName) => SeleniumDispatchProxy.Create<IWebElement>((method, arguments) =>
        {
            if (method.Name == "get_Displayed") return true;
            if (method.Name == "FindElements")
            {
                string locator = arguments![0]!.ToString() ?? string.Empty;
                if (locator.Contains("td[role='gridcell']", StringComparison.Ordinal))
                {
                    IWebElement[] cells =
                    [
                        CreateElement(text: () => string.Empty),
                        CreateElement(text: () => string.Empty),
                        CreateElement(text: () => cohortId.ToString()),
                        CreateElement(text: () => cohortName)
                    ];
                    return new ReadOnlyCollection<IWebElement>(cells);
                }
            }
            throw new NotSupportedException(method.Name);
        });
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
