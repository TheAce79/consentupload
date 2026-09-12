using System.ComponentModel;
using System.Text;
using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.Csv;
using ConsentSyncCore.Services.Phis;
using IWebDriver = OpenQA.Selenium.IWebDriver;

namespace CohortUi;

public partial class CohortContextForm
{
    private readonly TabControl _workflowTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _reviewTab = new("Data Review & Manual Fixes");
    private readonly TabPage _eligibilityTab = new("Final Output & Eligibility");
    private readonly DataGridView _reviewGrid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };
    private readonly ComboBox _reviewFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Label _reviewSummary = new() { AutoSize = true, Padding = new Padding(4) };
    private readonly Label _reviewMessage = new() { AutoSize = true, Padding = new Padding(4), MaximumSize = new Size(950, 0) };
    private readonly TextBox _phisListName = new() { ReadOnly = true, Width = 300 };
    private readonly TextBox _phisCohortId = new() { ReadOnly = true, Width = 140 };
    private readonly TextBox _phisClientListId = new() { ReadOnly = true, Width = 140 };
    private readonly ContextMenuStrip _reviewContextMenu = new();
    private Button _saveReview = null!;
    private Button _acceptMatch = null!;
    private Button _toggleExcluded = null!;
    private Button _retryCacheSync = null!;
    private Button btn_CreatePhisCohort = null!;
    private ToolStripMenuItem _contextAccept = null!;
    private ToolStripMenuItem _contextExclude = null!;
    private ToolStripMenuItem _contextSave = null!;
    private CohortReviewService? _review;
    private bool _reviewDirty;
    private bool _bindingReview;
    private bool _formBusy;
    private bool _contextRowSelectedByClick;
    private bool _cacheSyncRetryAvailable;

    private void InitializeWorkflowTabs()
    {
        var setup = new TabPage("Setup & PHIS Search") { AutoScroll = true };
        setup.Controls.AddRange([grp_CohortContext, grp_PdfRosterExtraction, grp_PhisSearch, grp_DebugLog]);
        grp_DebugLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _workflowTabs.TabPages.AddRange([setup, _reviewTab, _eligibilityTab]);
        Controls.Add(_workflowTabs);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(1120, 820);
        Size = new Size(1180, 880);
        Text = "ConsentSync Cohort Workspace";

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        toolbar.Controls.Add(MakeButton("Reload Review", () => { if (ConfirmReviewTransition()) LoadActiveReview(); }));
        toolbar.Controls.Add(MakeButton("Start Fresh Review", StartFreshReview));
        _acceptMatch = MakeButton("Accept Suggested Match", AcceptSuggestedMatch);
        _toggleExcluded = MakeButton("Exclude / Restore Selected", ToggleExcluded);
        _saveReview = MakeButton("Save Review", () => _ = SaveReviewAsync());
        _retryCacheSync = MakeButton("Retry Cache Sync", () => _ = RetryCacheSyncAsync());
        _retryCacheSync.Enabled = false;
        toolbar.Controls.AddRange([_acceptMatch, _toggleExcluded, _saveReview, _retryCacheSync, _reviewFilter]);
        _reviewFilter.Items.AddRange(["All rows", "Attention required", "Duplicate IDs", "Excluded rows"]);
        _reviewFilter.SelectedIndex = 0;
        _reviewFilter.SelectedIndexChanged += (_, _) => { _reviewGrid.EndEdit(); RefreshReviewGrid(); };

        foreach (var (property, title) in new (string, string)[]
        {
            ("RowNumber", "Row"), ("ClientId", "Client ID"), ("FullName", "Full Name"),
            ("DateOfBirth", "Date of Birth"), ("Medicare", "Medicare"), ("SearchStatus", "Search Status"),
            ("Unresolved", "Unresolved"), ("DuplicateId", "Duplicate ID"), ("Excluded", "Excluded"),
            ("ErrorDetails", "Search Details"), ("BestMatch", "Best Match"),
            ("FirstName", "First Name"), ("LastName", "Last Name"), ("MiddleName", "Middle Name"),
            ("Phone", "Phone"), ("Email", "Email")
        })
        {
            _reviewGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = property, Name = property, HeaderText = title,
                ReadOnly = property != "ClientId", SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
        _reviewGrid.RowsDefaultCellStyle.BackColor = Color.White;
        _reviewGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);
        _reviewGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
        _reviewGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        _reviewGrid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
        _reviewGrid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;
        _contextAccept = new ToolStripMenuItem("Accept Suggested Match", null, (_, _) => AcceptSuggestedMatch());
        _contextExclude = new ToolStripMenuItem("Exclude / Restore Selected", null, (_, _) => ToggleExcluded());
        _contextSave = new ToolStripMenuItem("Save Review", null, async (_, _) => await SaveReviewAsync());
        _reviewContextMenu.Items.AddRange([_contextAccept, _contextExclude, new ToolStripSeparator(), _contextSave]);
        _reviewContextMenu.Opening += ReviewContextMenu_Opening;
        _reviewGrid.ContextMenuStrip = _reviewContextMenu;
        _reviewGrid.MouseDown += ReviewGrid_MouseDown;
        FormClosed += (_, _) => _reviewContextMenu.Dispose();
        _reviewGrid.CellValueChanged += (_, e) =>
        {
            if (_bindingReview || e.RowIndex < 0 || _review is null) return;
            _reviewDirty = true;
            // Defer rebinding until the grid has completed its edit transaction.
            BeginInvoke(() => { if (!IsDisposed && !Disposing) RefreshReviewGrid(); });
        };
        _reviewGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            _reviewMessage.Text = "The value could not be applied. Enter the Client ID as text.";
        };
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_reviewMessage, 0, 1);
        layout.Controls.Add(_reviewGrid, 0, 2);
        layout.Controls.Add(_reviewSummary, 0, 3);

        var phisGroup = new GroupBox { Text = "PHIS Cohort", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(10) };
        var phisFields = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        phisFields.Controls.Add(Field("Client List Name", _phisListName));
        phisFields.Controls.Add(Field("PHIS Cohort ID", _phisCohortId));
        phisFields.Controls.Add(Field("PHIS Client List ID", _phisClientListId));
        btn_CreatePhisCohort = new Button { Text = "Create PHIS Cohort", AutoSize = true, Enabled = false };
        btn_CreatePhisCohort.Click += btn_CreatePhisCohort_Click;
        phisFields.Controls.Add(btn_CreatePhisCohort);
        phisFields.Controls.Add(new Label { Text = "Exports the resolved Client ID list. PHIS cohort creation integration is pending.", AutoSize = true });
        phisGroup.Controls.Add(phisFields);
        layout.Controls.Add(phisGroup, 0, 4);
        _reviewTab.Controls.Add(layout);

        var eligibility = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), FlowDirection = FlowDirection.TopDown };
        eligibility.Controls.Add(new Label { AutoSize = true, Text = "Dose-history sourcing and eligibility rules are pending. Eligibility evaluation and final export are not yet available." });
        eligibility.Controls.Add(new Button { Text = "Evaluate Eligibility", AutoSize = true, Enabled = false });
        eligibility.Controls.Add(new Button { Text = "Export Final Cohort CSV", AutoSize = true, Enabled = false });
        _eligibilityTab.Controls.Add(eligibility);
        UpdateReviewAvailability();
    }

    private void ReviewGrid_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        _contextRowSelectedByClick = false;
        var hit = _reviewGrid.HitTest(e.X, e.Y);
        if (hit.Type != DataGridViewHitTestType.Cell || hit.RowIndex < 0 || hit.ColumnIndex < 0) return;
        _reviewGrid.ClearSelection();
        _reviewGrid.Rows[hit.RowIndex].Selected = true;
        _reviewGrid.CurrentCell = _reviewGrid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
        _contextRowSelectedByClick = true;
    }

    private void ReviewContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        bool available = _hasSavedContext && !_formBusy && _review is not null;
        bool selected = available && _contextRowSelectedByClick && SelectedReviewRows().Count > 0;
        _contextAccept.Enabled = selected;
        _contextExclude.Enabled = selected;
        _contextSave.Enabled = available;
        e.Cancel = !available;
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }

    private static Control Field(string caption, Control input)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        panel.Controls.Add(new Label { Text = caption, Width = 155, Padding = new Padding(0, 5, 0, 0) });
        panel.Controls.Add(input);
        return panel;
    }

    private void UpdateReviewAvailability()
    {
        bool available = _hasSavedContext && !_formBusy && _activeContext is not null;
        _reviewTab.Enabled = available;
        _eligibilityTab.Enabled = available;
        _phisListName.Text = _activeContext?.ClientListName ?? string.Empty;
        _phisCohortId.Text = _activeContext?.PhisCohortId?.ToString() ?? string.Empty;
        _phisClientListId.Text = _activeContext?.PhisClientListId?.ToString() ?? string.Empty;
        if (_saveReview is null) return;
        _saveReview.Enabled = _acceptMatch.Enabled = _toggleExcluded.Enabled = available && _review is not null;
        _retryCacheSync.Enabled = available && _review is not null && _cacheSyncRetryAvailable;
        btn_CreatePhisCohort.Enabled = available && _review is not null;
        if (_contextSave is not null)
        {
            _contextSave.Enabled = available && _review is not null;
            _contextAccept.Enabled = _contextExclude.Enabled = available && _review is not null && SelectedReviewRows().Count > 0;
        }
    }

    private void LoadActiveReview(bool startFresh = false)
    {
        _review = null;
        _reviewDirty = false;
        try
        {
            if (!_hasSavedContext || _activeContext is null) return;
            var config = ConfigurationService.GetConfiguration();
            string source = CohortWorkspaceService.GetStandardizedOutputCsvPath(config, _activeContext.ClientListName);
            string saved = Path.Combine(Path.GetDirectoryName(source)!, _activeContext.ClientListName + "_Cohort.review.json");
            if (!File.Exists(source))
            {
                _reviewMessage.Text = "No enriched CSV is available. Complete PHIS search on Tab 1 first.";
                return;
            }
            _review = CohortReviewService.Load(source, saved, startFresh);
            _ = PreloadCacheForReviewAsync(_review.Rows);
            _reviewDirty = startFresh;
            _reviewMessage.Text = startFresh ? "Fresh review started. Save Review will replace any previous saved review." : "Edit Client IDs or explicitly accept a suggested match. Original roster details are read-only.";
        }
        catch (Exception ex)
        {
            _reviewMessage.Text = $"Review could not be loaded: {ex.Message} Use Start Fresh Review to discard incompatible saved review data.";
            LoggerService.LogError("Cohort review could not be loaded.", ex);
        }
        finally { RefreshReviewGrid(); UpdateReviewAvailability(); }
    }

    private void StartFreshReview()
    {
        if (!ConfirmReviewTransition()) return;
        if (MessageBox.Show(this, "Start again from the current PHIS CSV? Saving the fresh review will replace prior saved corrections and exclusions.",
            "Start Fresh Review", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            LoadActiveReview(startFresh: true);
    }

    private void RefreshReviewGrid()
    {
        if (_reviewGrid.IsCurrentCellInEditMode) return;
        int? selectedRow = (_reviewGrid.CurrentRow?.DataBoundItem as CohortReviewRow)?.RowNumber;
        _bindingReview = true;
        try
        {
            _review?.RefreshDuplicates();
            IEnumerable<CohortReviewRow> rows = _review?.Rows ?? [];
            rows = _reviewFilter.SelectedIndex switch
            {
                1 => rows.Where(r => r.RequiresAttention),
                2 => rows.Where(r => r.DuplicateId),
                3 => rows.Where(r => r.Excluded),
                _ => rows
            };
            _reviewGrid.DataSource = new BindingList<CohortReviewRow>(rows.ToList());
            foreach (DataGridViewRow gridRow in _reviewGrid.Rows)
            {
                if (gridRow.DataBoundItem is not CohortReviewRow row) continue;
                gridRow.DefaultCellStyle.ForeColor = row.Excluded ? Color.Gray : row.RequiresAttention ? Color.DarkRed : Color.Black;
                gridRow.DefaultCellStyle.BackColor = Color.Empty;
                if (row.RowNumber == selectedRow) _reviewGrid.CurrentCell = gridRow.Cells["ClientId"];
            }
            var all = _review?.Rows ?? [];
            _reviewSummary.Text = $"Included: {all.Count(r => !r.Excluded)}   Unresolved: {all.Count(r => !r.Excluded && r.Unresolved)}   Duplicate rows: {all.Count(r => r.DuplicateId)}   Excluded: {all.Count(r => r.Excluded)}" + (_reviewDirty ? "   • Unsaved changes" : "");
        }
        finally { _bindingReview = false; }
    }

    private List<CohortReviewRow> SelectedReviewRows()
    {
        _reviewGrid.EndEdit();
        return _reviewGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<CohortReviewRow>().ToList();
    }

    private async void btn_CreatePhisCohort_Click(object? sender, EventArgs e)
    {
        if (_formBusy)
        {
            return;
        }

        if (!_reviewGrid.EndEdit())
        {
            MessageBox.Show(this,
                "Finish or correct the active Client ID edit before creating the PHIS Cohort payload.",
                "Review Edit Incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_review is null || _review.Rows.Count == 0)
        {
            MessageBox.Show(this,
                "No active cohort review is loaded. Run PHIS search or load a saved review first.",
                "No Review Loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_hasSavedContext || _activeContext is null || string.IsNullOrWhiteSpace(_activeContext.ClientListName))
        {
            MessageBox.Show(this,
                "Client List Name is missing from the saved cohort context.",
                "Missing Context", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string clientListName = _activeContext.ClientListName.Trim();
        string cohortId = _phisCohortId.Text.Trim();
        string phisClientListName = _phisListName.Text.Trim();
        CohortSearchCriterion criterion;
        string criterionValue;
        if (cohortId.Length > 0)
        {
            criterion = CohortSearchCriterion.CohortId;
            criterionValue = cohortId;
        }
        else if (phisClientListName.Length > 0)
        {
            criterion = CohortSearchCriterion.ClientListName;
            criterionValue = phisClientListName;
        }
        else
        {
            MessageBox.Show(this,
                "Enter a PHIS Cohort ID or provide a Client List Name before searching PHIS.",
                "Missing Search Criteria", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var includedRows = _review.Rows.Where(row => !row.Excluded).ToList();
        var resolvedRows = includedRows
            .Where(row => row.SearchStatus == ClientIdStatus.Found && !string.IsNullOrWhiteSpace(row.ClientId))
            .ToList();
        int unresolvedCount = includedRows.Count - resolvedRows.Count;

        if (unresolvedCount > 0)
        {
            var choice = MessageBox.Show(this,
                $"There are {unresolvedCount} record(s) in the roster without a resolved Client ID or requiring manual review.\n\n" +
                "These records will not be included in the PHIS Cohort payload.\n\n" +
                $"Do you want to proceed using the remaining {resolvedRows.Count} resolved record(s)?",
                "Unresolved Records Detected", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes) return;
        }

        if (resolvedRows.Count == 0)
        {
            MessageBox.Show(this,
                "There are no resolved Client IDs available to create a PHIS Cohort.",
                "No Valid Client IDs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? targetPath = null;
        bool payloadExported = false;
        try
        {
            SetFormEnabled(false);
            btn_CreatePhisCohort.Text = "Exporting Payload...";

            var clientIds = resolvedRows
                .Select(row => row.ClientId.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var config = ConfigurationService.GetConfiguration();
            string standardizedCsvPath = CohortWorkspaceService.GetStandardizedOutputCsvPath(config, clientListName);
            string outputDirectory = Path.GetDirectoryName(standardizedCsvPath)
                ?? throw new InvalidOperationException("The cohort output directory could not be resolved.");
            targetPath = Path.Combine(outputDirectory, $"{clientListName}_ClientId_list.txt");

            await File.WriteAllLinesAsync(targetPath, clientIds, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            payloadExported = true;

            LoggerService.LogInformation($"Exported {clientIds.Count} Client ID(s) to plain text file: {targetPath}");
            btn_CreatePhisCohort.Text = "Opening PHIS...";
            await EnsureCohortPhisSessionAsync(config);

            var cohortService = new PhisCohortService(_cohortDriver!, config, _cohortSessionManager!);
            while (!cohortService.IsOnSearchCohortPage())
            {
                DialogResult answer = MessageBox.Show(this,
                    "Please navigate to the 'Search Cohort' page in PHIS, then click OK to continue.",
                    "Navigate to Search Cohort", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (answer != DialogResult.OK)
                {
                    LoggerService.LogInformation("PHIS cohort search cancelled before Search Cohort page navigation completed.");
                    return;
                }
                await Task.Delay(250);
            }

            btn_CreatePhisCohort.Text = "Searching Cohort...";
            await Task.Run(() => cohortService.SearchAsync(criterion, criterionValue));
            btn_CreatePhisCohort.Text = "Checking Search Results...";
            CohortCreationResult creationResult = await Task.Run(() => cohortService.CreateIfSearchReturnedNoResultsAsync(clientListName));

            string fileDetails = $"\n\nClient ID file:\n{targetPath}\n\nTotal Client IDs exported: {clientIds.Count}";
            switch (creationResult.Status)
            {
                case CohortCreationStatus.Created:
                    LoggerService.LogInformation(creationResult.Message);
                    MessageBox.Show(this, creationResult.Message + fileDetails + "\n\nClient association has not been performed.",
                        "PHIS Cohort Created", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case CohortCreationStatus.SaveUnverified:
                    LoggerService.LogWarning(creationResult.Message);
                    MessageBox.Show(this, creationResult.Message + fileDetails + "\n\nReview the open PHIS page before continuing. Client association has not been performed.",
                        "PHIS Save Needs Review", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case CohortCreationStatus.ExistingResults:
                    LoggerService.LogInformation(creationResult.Message);
                    MessageBox.Show(this, creationResult.Message + fileDetails,
                        "PHIS Cohort Already Exists", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                default:
                    LoggerService.LogWarning(creationResult.Message);
                    MessageBox.Show(this, creationResult.Message + fileDetails,
                        "PHIS Search Result Not Confirmed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogError("PHIS cohort export or search failed.", ex);
            string payloadMessage = payloadExported && targetPath is not null
                ? $"\n\nThe Client ID file was created successfully at:\n{targetPath}"
                : string.Empty;
            MessageBox.Show(this, $"PHIS cohort export or search failed:\n\n{ex.Message}{payloadMessage}",
                "PHIS Cohort Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btn_CreatePhisCohort.Text = "Create PHIS Cohort";
            SetFormEnabled(true);
        }
    }

    private async Task EnsureCohortPhisSessionAsync(Microsoft.Extensions.Configuration.IConfiguration config)
    {
        if (_cohortDriver is not null && _cohortSessionManager is not null)
        {
            var existingService = new PhisCohortService(_cohortDriver, config, _cohortSessionManager);
            if (existingService.IsOnSearchCohortPage())
            {
                LoggerService.LogInformation("Reusing active PHIS cohort session.");
                return;
            }

            DisposeCohortPhisSession();
        }

        var session = await Task.Run(() =>
        {
            IWebDriver driver = new ChromeDriverFactory(config).CreateDriver();
            try
            {
                var sessionManager = new PhisSessionManager(driver, config);
                if (!sessionManager.Login())
                {
                    throw new InvalidOperationException("PHIS login was not completed.");
                }
                return (driver, sessionManager);
            }
            catch
            {
                try { driver.Quit(); driver.Dispose(); }
                catch { }
                throw;
            }
        });

        _cohortDriver = session.driver;
        _cohortSessionManager = session.sessionManager;
        LoggerService.LogInformation("PHIS cohort session established.");
    }

    private void AcceptSuggestedMatch()
    {
        int accepted = 0, skipped = 0;
        foreach (var row in SelectedReviewRows())
        {
            if (!row.Excluded && CohortReviewService.TryGetSuggestedClientId(row.BestMatch, out string id))
            {
                row.ClientId = id;
                accepted++;
            }
            else skipped++;
        }
        _reviewDirty |= accepted > 0;
        _reviewMessage.Text = $"Accepted {accepted} suggested match(es). Skipped {skipped} excluded row(s) or hints without a usable Client ID.";
        RefreshReviewGrid();
    }

    private void ToggleExcluded()
    {
        var selected = SelectedReviewRows();
        bool exclude = selected.Any(r => !r.Excluded);
        foreach (var row in selected) row.Excluded = exclude;
        _reviewDirty |= selected.Count > 0;
        RefreshReviewGrid();
    }

    private async Task<bool> SaveReviewAsync()
    {
        _reviewGrid.EndEdit();
        if (_review is null) return false;
        try
        {
            _formBusy = true;
            UpdateReviewAvailability();
            _saveReview.Text = "Saving...";
            await Task.Run(_review.Save);
            _reviewDirty = false;
            try
            {
                var sync = _dbManager is null ? null : await _review.SyncResolvedClientsAsync(_dbManager);
                _cacheSyncRetryAvailable = false;
                _reviewMessage.Text = sync is null ? "Review saved. The enriched CSV and review state were updated." :
                    $"Review saved. The enriched CSV, review state, and {sync.SavedClients} local cache record(s) were updated.";
            }
            catch (Exception ex)
            {
                _cacheSyncRetryAvailable = true;
                _reviewMessage.Text = $"Review files saved, but local DB cache sync failed. Retry Cache Sync is available. {ex.Message}";
                LoggerService.LogError("Review files saved but local cache sync failed.", ex);
            }
            RefreshReviewGrid();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Review could not be saved.\n\n{ex.Message}", "Save Review", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            _formBusy = false;
            _saveReview.Text = "Save Review";
            UpdateReviewAvailability();
        }
    }

    private async Task RetryCacheSyncAsync()
    {
        if (_review is null || _dbManager is null) return;
        try
        {
            _formBusy = true;
            UpdateReviewAvailability();
            _retryCacheSync.Text = "Retrying...";
            int savedClients = await _review.RetryLastCacheSyncAsync(_dbManager);
            _cacheSyncRetryAvailable = false;
            _reviewMessage.Text = $"Local cache sync completed for {savedClients} record(s).";
        }
        catch (Exception ex)
        {
            _cacheSyncRetryAvailable = true;
            _reviewMessage.Text = $"Local DB cache sync failed. You can retry. {ex.Message}";
            LoggerService.LogError("Review cache sync retry failed.", ex);
        }
        finally
        {
            _formBusy = false;
            _retryCacheSync.Text = "Retry Cache Sync";
            UpdateReviewAvailability();
        }
    }

    private async Task PreloadCacheForReviewAsync(IEnumerable<CohortReviewRow> rows)
    {
        if (_dbManager is null) return;
        try { await _dbManager.PreloadCacheForDatesAsync(rows.Select(row => row.DateOfBirth)); }
        catch (Exception ex) { LoggerService.LogWarning($"Review cache preload failed; continuing without cached records. {ex.Message}"); }
    }

    private bool SaveReviewSynchronouslyForTransition()
    {
        _reviewGrid.EndEdit();
        if (_review is null) return false;
        try
        {
            _formBusy = true;
            UpdateReviewAvailability();
            _saveReview.Text = "Saving...";
            _review.Save();
            _reviewDirty = false;
            try
            {
                var sync = _dbManager is null ? null : _review.SyncResolvedClientsAsync(_dbManager).GetAwaiter().GetResult();
                _cacheSyncRetryAvailable = false;
                _reviewMessage.Text = sync is null ? "Review saved. The enriched CSV and review state were updated." :
                    $"Review saved. The enriched CSV, review state, and {sync.SavedClients} local cache record(s) were updated.";
            }
            catch (Exception ex)
            {
                _cacheSyncRetryAvailable = true;
                _reviewMessage.Text = $"Review files saved, but local DB cache sync failed. Retry Cache Sync is available. {ex.Message}";
                LoggerService.LogError("Review files saved but local cache sync failed.", ex);
            }
            RefreshReviewGrid();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Review could not be saved.\n\n{ex.Message}", "Save Review", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            _formBusy = false;
            _saveReview.Text = "Save Review";
            UpdateReviewAvailability();
        }
    }

    private bool ConfirmReviewTransition()
    {
        _reviewGrid.EndEdit();
        if (!_reviewDirty) return true;
        var choice = MessageBox.Show(this, "Save review changes before continuing?\n\nYes: Save   No: Discard   Cancel: Stay here",
            "Unsaved Review Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Yes) return SaveReviewSynchronouslyForTransition();
        // Keep the current session intact until the requested transition actually succeeds.
        return choice == DialogResult.No;
    }
}
