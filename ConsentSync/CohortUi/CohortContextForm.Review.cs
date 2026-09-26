using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ConsentSync.Data.Entities;
using ConsentSyncCore.Collections;
using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.Csv;
using ConsentSyncCore.Services.Phis;
using IWebDriver = OpenQA.Selenium.IWebDriver;
using ConsentSync.Ui;

namespace CohortUi;

public partial class CohortContextForm
{
    private readonly LavenderTabControl _workflowTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _reviewTab = new("Data Review & Manual Fixes");
    private readonly TabPage _eligibilityTab = new("Final Output & Eligibility");
    private readonly LavenderDataGridView _reviewGrid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };
    private readonly BindingSource _reviewBindingSource = new();
    private SortableBindingList<CohortReviewRow>? _reviewRows;
    private string _reviewSortProperty = nameof(CohortReviewRow.FullName);
    private ListSortDirection _reviewSortDirection = ListSortDirection.Ascending;
    private List<CohortReviewRow> _selectedReviewRowsBeforeSort = [];
    private CohortReviewRow? _currentReviewRowBeforeSort;
    private string? _currentReviewColumnBeforeSort;
    private readonly ComboBox _reviewFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Label _reviewSummary = new() { AutoSize = true, Padding = new Padding(4) };
    private readonly Label _reviewMessage = new() { AutoSize = true, Padding = new Padding(4), MaximumSize = new Size(950, 0) };
    private readonly TextBox _phisListName = new() { Width = 300 };
    private readonly TextBox _phisCohortId = new() { Width = 140 };
    private readonly TextBox _phisClientListId = new() { Width = 140 };
    private readonly Button _savePhisDb = new() { Text = "Save Db", AutoSize = true };
    private readonly Button _openCriteriaExplorer = new() { Text = "Go To Criteria Explorer", AutoSize = true, Enabled = false };
    private bool _phisFieldsDirty;
    private bool _bindingPhisFields;
    private int? _phisFieldsContextId;
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
        var setupStack = new LavenderTableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Dock = DockStyle.Top,
            Padding = new Padding(12),
            BackColor = LavenderSlatePalette.Window
        };
        setupStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 4; row++)
            setupStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        foreach (var card in new[] { grp_CohortContext, grp_PdfRosterExtraction, grp_PhisSearch, grp_DebugLog })
        {
            card.HeaderTop = 16;
            card.ApplyExpandedHeaderLayout();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, 8);
        }
        grp_DebugLog.Margin = Padding.Empty;
        grp_DebugLog.MinimumSize = new Size(0, 294);
        grp_DebugLog.Height = 294;
        setupStack.Controls.Add(grp_CohortContext, 0, 0);
        setupStack.Controls.Add(grp_PdfRosterExtraction, 0, 1);
        setupStack.Controls.Add(grp_PhisSearch, 0, 2);
        setupStack.Controls.Add(grp_DebugLog, 0, 3);
        setup.Controls.Add(setupStack);
        setup.Resize += (_, _) => ResizeSetupDebugLog(setup, setupStack);
        ResizeSetupDebugLog(setup, setupStack);
        _workflowTabs.TabPages.AddRange([setup, _reviewTab, _eligibilityTab]);
        Controls.Add(_workflowTabs);
        _nextCohortButton.Click += btn_NextCohort_Click;
        _workspaceToolbar.Controls.Add(_nextCohortButton);
        _workspaceToolbarCard.Controls.Add(_workspaceToolbar);
        Controls.Add(_workspaceToolbarCard);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(1120, 820);
        Size = new Size(1180, 880);
        Text = "ConsentSync Cohort Workspace";

        var layout = new LavenderTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12), BackColor = LavenderSlatePalette.Window };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new LavenderFlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0), Margin = new Padding(0) };
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
                ReadOnly = property is not ("ClientId" or "FullName" or "DateOfBirth" or "Medicare"), SortMode = DataGridViewColumnSortMode.Automatic
            });
        }
        LavenderSlateTheme.ApplyGrid(_reviewGrid);
        _contextAccept = new ToolStripMenuItem("Accept Suggested Match", null, (_, _) => AcceptSuggestedMatch());
        _contextExclude = new ToolStripMenuItem("Exclude / Restore Selected", null, (_, _) => ToggleExcluded());
        _contextSave = new ToolStripMenuItem("Save Review", null, async (_, _) => await SaveReviewAsync());
        _reviewContextMenu.Items.AddRange([_contextAccept, _contextExclude, new ToolStripSeparator(), _contextSave]);
        _reviewContextMenu.Opening += ReviewContextMenu_Opening;
        _reviewGrid.ContextMenuStrip = _reviewContextMenu;
        _reviewGrid.MouseDown += ReviewGrid_MouseDown;
        _reviewGrid.Sorted += ReviewGrid_Sorted;
        _reviewGrid.CellFormatting += ReviewGrid_CellFormatting;
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
            _reviewMessage.Text = "The value could not be applied. Enter Client ID, Full Name, Date of Birth, and Medicare as text.";
        };
        var actionCard = new LavenderCardPanel { AutoSize = true, Dock = DockStyle.Fill };
        actionCard.Controls.Add(toolbar);
        var messageCard = new LavenderCardPanel { AutoSize = true, Dock = DockStyle.Fill };
        messageCard.Controls.Add(_reviewMessage);
        var gridCard = new LavenderCardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        gridCard.Controls.Add(_reviewGrid);
        var summaryCard = new LavenderCardPanel { AutoSize = true, Dock = DockStyle.Fill };
        summaryCard.Controls.Add(_reviewSummary);
        layout.Controls.Add(actionCard, 0, 0);
        layout.Controls.Add(messageCard, 0, 1);
        layout.Controls.Add(gridCard, 0, 2);
        layout.Controls.Add(summaryCard, 0, 3);

        var phisGroup = new LavenderGroupBox { Text = "PHIS Cohort", AutoSize = true, Dock = DockStyle.Fill, HeaderTop = 16 };
        var phisFields = new LavenderFlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        phisFields.Controls.Add(Field("Client List Name", _phisListName));
        phisFields.Controls.Add(Field("PHIS Cohort ID", _phisCohortId));
        phisFields.Controls.Add(Field("PHIS Client List ID", _phisClientListId));
        btn_CreatePhisCohort = new Button { Text = "Create PHIS Cohort", AutoSize = true, Enabled = false };
        btn_CreatePhisCohort.Click += btn_CreatePhisCohort_Click;
        phisFields.Controls.Add(btn_CreatePhisCohort);
        phisFields.Controls.Add(_savePhisDb);
        _savePhisDb.Click += async (_, _) => await SavePhisFieldsAsync();
        foreach (var field in new[] { _phisListName, _phisCohortId, _phisClientListId })
            field.TextChanged += (_, _) => { if (!_bindingPhisFields) _phisFieldsDirty = true; };
        phisFields.Controls.Add(new Label { Text = "Creates or opens the cohort, uploads the full exported client list, and writes an admin summary. Save Db stores manual field edits.", AutoSize = true });
        phisGroup.Controls.Add(phisFields);
        layout.Controls.Add(phisGroup, 0, 4);
        _reviewTab.Controls.Add(layout);

        var eligibility = new LavenderFlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), FlowDirection = FlowDirection.TopDown };
        eligibility.Controls.Add(new Label { AutoSize = true, Text = "Dose-history sourcing and eligibility rules are pending. Eligibility evaluation and final export are not yet available." });
        _openCriteriaExplorer.Click += btn_OpenCriteriaExplorer_Click;
        eligibility.Controls.Add(_openCriteriaExplorer);
        eligibility.Controls.Add(new Button { Text = "Evaluate Eligibility", AutoSize = true, Enabled = false });
        eligibility.Controls.Add(new Button { Text = "Export Final Cohort CSV", AutoSize = true, Enabled = false });
        _eligibilityTab.Controls.Add(eligibility);
        LavenderSlateTheme.Apply(this);
        LavenderSlateTheme.ApplyButton(btn_CreatePhisCohort, LavenderButtonKind.Primary);
        LavenderSlateTheme.ApplyButton(_savePhisDb, LavenderButtonKind.Primary);
        UpdateReviewAvailability();
    }

    private void ResizeSetupDebugLog(TabPage setup, TableLayoutPanel setupStack)
    {
        int cardsHeight = setupStack.Padding.Vertical +
                          grp_CohortContext.Height +
                          grp_PdfRosterExtraction.Height +
                          grp_PhisSearch.Height +
                          24;
        grp_DebugLog.Height = Math.Max(294, setup.ClientSize.Height - cardsHeight);
        setupStack.PerformLayout();
    }

    private void ResetReviewForNextCohort()
    {
        _reviewGrid.EndEdit();
        _review = null;
        _reviewDirty = false;
        _cacheSyncRetryAvailable = false;
        _contextRowSelectedByClick = false;
        _bindingPhisFields = true;
        try
        {
            _phisListName.Clear();
            _phisCohortId.Clear();
            _phisClientListId.Clear();
        }
        finally
        {
            _bindingPhisFields = false;
        }

        _phisFieldsDirty = false;
        _phisFieldsContextId = null;
        _reviewMessage.Text = string.Empty;
        _reviewSummary.Text = string.Empty;
        _reviewFilter.SelectedIndex = 0;
        _reviewGrid.DataSource = null;
        _reviewBindingSource.DataSource = null;
        _reviewRows = null;
        _reviewGrid.ClearSelection();
        _saveReview.Text = "Save Review";
        _retryCacheSync.Text = "Retry Cache Sync";
        btn_CreatePhisCohort.Text = "Create PHIS Cohort";
        UpdateReviewAvailability();
    }

    private void ReviewGrid_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_reviewGrid.HitTest(e.X, e.Y).Type == DataGridViewHitTestType.ColumnHeader) CaptureReviewGridSelection();
            return;
        }
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
        var panel = new LavenderFlowLayoutPanel { AutoSize = true, WrapContents = false };
        panel.Controls.Add(new Label { Text = caption, Width = 155, Padding = new Padding(0, 5, 0, 0) });
        panel.Controls.Add(input);
        return panel;
    }

    private void UpdateReviewAvailability()
    {
        bool available = _hasSavedContext && !_formBusy && _activeContext is not null;
        _reviewTab.Enabled = available;
        _eligibilityTab.Enabled = available;
        if (!_phisFieldsDirty || _phisFieldsContextId != _activeContext?.CohortContextId)
        {
            _bindingPhisFields = true;
            _phisListName.Text = _activeContext?.ClientListName ?? string.Empty;
            _phisCohortId.Text = _activeContext?.PhisCohortId?.ToString() ?? string.Empty;
            _phisClientListId.Text = _activeContext?.PhisClientListId?.ToString() ?? string.Empty;
            _phisFieldsContextId = _activeContext?.CohortContextId;
            _phisFieldsDirty = false;
            _bindingPhisFields = false;
        }
        _savePhisDb.Enabled = available;
        _openCriteriaExplorer.Enabled = available;
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

    private void btn_OpenCriteriaExplorer_Click(object? sender, EventArgs e)
    {
        if (!TryGetSavedClientListName(out string clientListName)) return;

        try
        {
            string criteriaDirectory = CohortWorkspaceService.GetCriteriaDirectory(
                ConfigurationService.GetConfiguration(), clientListName);
            var explorer = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            explorer.ArgumentList.Add(criteriaDirectory);
            Process.Start(explorer);
            LoggerService.LogInformation($"Opened cohort criteria folder in File Explorer: {criteriaDirectory}");
        }
        catch (Exception ex)
        {
            LoggerService.LogError("Could not open the cohort criteria folder in File Explorer.", ex);
            MessageBox.Show(this, $"Could not open the cohort criteria folder.\n\n{ex.Message}", "Explorer Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _reviewMessage.Text = startFresh ? "Fresh review started. Save Review will replace any previous saved review." : "Edit Client ID, Full Name, Date of Birth, or Medicare, or explicitly accept a suggested match.";
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
        CaptureReviewGridSelection();
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
            _reviewRows = new SortableBindingList<CohortReviewRow>(rows);
            _reviewBindingSource.DataSource = _reviewRows;
            _reviewGrid.DataSource = _reviewBindingSource;
            _reviewBindingSource.Sort = $"{_reviewSortProperty} {(_reviewSortDirection == ListSortDirection.Ascending ? "ASC" : "DESC")}";
            foreach (DataGridViewRow gridRow in _reviewGrid.Rows)
            {
                if (gridRow.DataBoundItem is not CohortReviewRow row) continue;
                gridRow.DefaultCellStyle.ForeColor = row.Excluded ? LavenderSlatePalette.MutedText : row.RequiresAttention ? LavenderSlatePalette.Error : LavenderSlatePalette.Slate;
                gridRow.DefaultCellStyle.BackColor = Color.Empty;
                gridRow.DefaultCellStyle.SelectionBackColor = LavenderSlatePalette.Selection;
                gridRow.DefaultCellStyle.SelectionForeColor = LavenderSlatePalette.Card;
            }
            RestoreReviewGridSelection();
            var all = _review?.Rows ?? [];
            _reviewSummary.Text = $"Included: {all.Count(r => !r.Excluded)}   Unresolved: {all.Count(r => !r.Excluded && r.Unresolved)}   Duplicate rows: {all.Count(r => r.DuplicateId)}   Excluded: {all.Count(r => r.Excluded)}" + (_reviewDirty ? "   • Unsaved changes" : "");
        }
        finally { _bindingReview = false; }
    }

    private void ReviewGrid_Sorted(object? sender, EventArgs e)
    {
        if (_reviewGrid.SortedColumn is not null && _reviewGrid.SortOrder is not SortOrder.None)
        {
            _reviewSortProperty = _reviewGrid.SortedColumn.DataPropertyName;
            _reviewSortDirection = _reviewGrid.SortOrder == SortOrder.Ascending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
        }
        RestoreReviewGridSelection();
    }

    private void ReviewGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex >= 0 && _reviewGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(CohortReviewRow.RowNumber))
        {
            e.Value = e.RowIndex + 1;
            e.FormattingApplied = true;
        }
    }

    private void CaptureReviewGridSelection()
    {
        _selectedReviewRowsBeforeSort = _reviewGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem).OfType<CohortReviewRow>().ToList();
        _currentReviewRowBeforeSort = _reviewGrid.CurrentRow?.DataBoundItem as CohortReviewRow;
        _currentReviewColumnBeforeSort = _reviewGrid.CurrentCell?.OwningColumn?.Name;
    }

    private void RestoreReviewGridSelection()
    {
        if (_selectedReviewRowsBeforeSort.Count == 0 && _currentReviewRowBeforeSort is null) return;

        _reviewGrid.ClearSelection();
        foreach (DataGridViewRow gridRow in _reviewGrid.Rows)
        {
            if (gridRow.DataBoundItem is CohortReviewRow row && _selectedReviewRowsBeforeSort.Contains(row)) gridRow.Selected = true;
        }

        DataGridViewRow? currentRow = _reviewGrid.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(row => ReferenceEquals(row.DataBoundItem, _currentReviewRowBeforeSort));
        if (currentRow is not null)
        {
            string columnName = _currentReviewColumnBeforeSort ?? nameof(CohortReviewRow.ClientId);
            _reviewGrid.CurrentCell = _reviewGrid.Columns.Contains(columnName)
                ? currentRow.Cells[columnName]
                : currentRow.Cells[nameof(CohortReviewRow.ClientId)];
        }
    }

    private List<CohortReviewRow> SelectedReviewRows()
    {
        _reviewGrid.EndEdit();
        return _reviewGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<CohortReviewRow>().ToList();
    }

    private async void btn_CreatePhisCohort_Click(object? sender, EventArgs e)
    {
        if (_phisFieldsDirty)
        {
            MessageBox.Show(this, "Use Save Db to save the edited PHIS fields before continuing.", "Unsaved PHIS Fields");
            return;
        }
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

        var currentClients = resolvedRows
            .GroupBy(row => row.ClientId.Trim(), StringComparer.Ordinal)
            .Select(group => new PhisUploadClient(group.Key,
                group.Select(row => row.FullName?.Trim()).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty))
            .ToList();
        PhisUploadSnapshotEntity? previousSnapshot = null;
        if (_activeContext.PhisCohortId is int savedCohortId)
        {
            previousSnapshot = await _dbManager!.GetLatestPhisUploadSnapshotAsync(
                _activeContext.CohortContextId, savedCohortId, _activeContext.PhisClientListId, clientListName);
        }
        IReadOnlyList<PhisUploadClient>? previousClients = previousSnapshot is null
            ? null
            : JsonSerializer.Deserialize<List<PhisUploadClient>>(previousSnapshot.ClientSnapshotJson);
        PhisUploadComparison comparison = PhisUploadComparer.Compare(currentClients, previousClients);
        if (!comparison.IsInitialUpload && !comparison.HasMembershipChanges)
        {
            DialogResult continueUnchanged = MessageBox.Show(this,
                "No Client IDs have changed since the last successful PHIS upload. Do you still want to continue?",
                "No Client ID Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (continueUnchanged != DialogResult.Yes) return;
        }

        string? targetPath = null;
        bool payloadExported = false;
        try
        {
            SetFormEnabled(false);
            btn_CreatePhisCohort.Text = "Exporting Payload...";

            var clientIds = currentClients.Select(client => client.ClientId).ToList();
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
            btn_CreatePhisCohort.Text = "Creating Static Cohort...";
            CohortCreationResult creationResult = await Task.Run(() => cohortService.CreateIfSearchReturnedNoResultsAsync(clientListName));

            if (creationResult.PhisCohortId is int verifiedId)
            {
                if (_activeContext.PhisCohortId is int storedId && storedId != verifiedId)
                    throw new InvalidOperationException("The found cohort ID differs from the saved cohort ID.");
                if (creationResult.Status == CohortCreationStatus.ExistingResults)
                    await Task.Run(() => cohortService.OpenExistingCohortAsync(verifiedId, clientListName));
                await PersistPhisCohortIdAsync(verifiedId);
                btn_CreatePhisCohort.Text = "Uploading Client List...";
                var upload = await Task.Run(() => cohortService.UploadClientListAsync(verifiedId, clientListName, targetPath));
                var updated = System.Text.Json.JsonSerializer.Deserialize<ConsentSync.Data.Entities.CohortContextEntity>(
                    System.Text.Json.JsonSerializer.Serialize(_activeContext))!;
                updated.PhisClientListId = upload.ClientListId;
                string reportPath = Path.Combine(Path.GetDirectoryName(targetPath)!, $"{clientListName}_PHIS_Admin_Summary_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
                string report = PhisAdminSummary.Format(verifiedId, clientListName, upload, clientIds.Count, comparison);
                string? localSaveFailure = null;
                try
                {
                    await _dbManager!.SaveSuccessfulPhisUploadAsync(updated, JsonSerializer.Serialize(currentClients));
                    _activeContext = updated;
                    _phisFieldsDirty = false;
                    UpdateReviewAvailability();
                    await RefreshCurrentScheduleReportAsync();
                }
                catch (Exception ex)
                {
                    LoggerService.LogError("PHIS upload succeeded but its local snapshot could not be saved.", ex);
                    localSaveFailure = $"The PHIS upload succeeded, but the local upload snapshot was not saved: {ex.Message}";
                }
                try
                {
                    await File.WriteAllTextAsync(reportPath, report, new UTF8Encoding(false));
                    LoggerService.LogInformation($"PHIS client list {upload.ClientListId} persisted; clients={upload.ClientCount}; summary={reportPath}");
                }
                catch (Exception ex)
                {
                    LoggerService.LogError("PHIS upload succeeded but the admin summary file could not be written.", ex);
                    localSaveFailure = string.IsNullOrEmpty(localSaveFailure)
                        ? $"The PHIS upload succeeded, but the summary file could not be written: {ex.Message}"
                        : localSaveFailure + $"\n\nThe summary file could not be written: {ex.Message}";
                }
                string summaryLocation = localSaveFailure is null ? $"\nSummary file:\n{reportPath}" : $"\n\n{localSaveFailure}";
                MessageBox.Show(this, report + summaryLocation, localSaveFailure is null ? "PHIS Client List Complete" : "PHIS Client List Uploaded", MessageBoxButtons.OK, localSaveFailure is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                return;
            }

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
                    if (creationResult.PhisCohortId is int existingCohortId)
                    {
                        await PersistPhisCohortIdAsync(existingCohortId);
                    }
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
            LoggerService.LogError("PHIS cohort export, search, or static cohort creation failed.", ex);
            string payloadMessage = payloadExported && targetPath is not null
                ? $"\n\nThe Client ID file was created successfully at:\n{targetPath}"
                : string.Empty;
            MessageBox.Show(this, $"PHIS cohort export, search, or static cohort creation failed:\n\n{ex.Message}{payloadMessage}",
                "PHIS Cohort Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btn_CreatePhisCohort.Text = "Create PHIS Cohort";
            SetFormEnabled(true);
        }
    }

    private async Task SavePhisFieldsAsync()
    {
        if (_formBusy || !_hasSavedContext || _activeContext is null || _dbManager is null) return;
        try
        {
            string name = _phisListName.Text.Trim().ToUpperInvariant();
            if (name.Length == 0 || name.Length > 240 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name is "." or "..")
                throw new InvalidOperationException("Enter a nonempty filename-safe Client List Name of at most 240 characters.");
            static int? ParseId(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                if (int.TryParse(text.Trim(), out int id) && id > 0) return id;
                throw new InvalidOperationException("PHIS IDs must be blank or positive whole numbers.");
            }
            var updated = System.Text.Json.JsonSerializer.Deserialize<ConsentSync.Data.Entities.CohortContextEntity>(
                System.Text.Json.JsonSerializer.Serialize(_activeContext))!;
            updated.ClientListName = name;
            updated.PhisCohortId = ParseId(_phisCohortId.Text);
            updated.PhisClientListId = ParseId(_phisClientListId.Text);
            bool renamed = name != _activeContext.ClientListName;
            if (renamed && _reviewDirty)
                throw new InvalidOperationException("Save the current review before changing Client List Name.");
            SetFormEnabled(false);
            await _dbManager.SaveCohortContextAsync(updated);
            _activeContext = updated;
            _phisFieldsDirty = false;
            BindContext(updated);
            if (renamed)
            {
                _review = null;
                _reviewDirty = false;
                RefreshReviewGrid();
            }
            await RefreshClientListSearchAsync(name);
            LoggerService.LogInformation($"Saved manual PHIS fields for cohort context {updated.CohortContextId}.");
            MessageBox.Show(this, renamed ? "Database updated. Existing files were not renamed. Load the review for the new list name before uploading." : "PHIS fields saved to the database.", "Save Db");
        }
        catch (Exception ex)
        {
            LoggerService.LogError("Saving manual PHIS fields failed.", ex);
            MessageBox.Show(this, ex.Message, "Save Db Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetFormEnabled(true); }
    }

    private async Task PersistPhisCohortIdAsync(int phisCohortId)
    {
        if (_activeContext is null || _dbManager is null)
        {
            throw new InvalidOperationException("The active cohort context is unavailable, so the PHIS Cohort ID cannot be saved.");
        }

        var updated = System.Text.Json.JsonSerializer.Deserialize<ConsentSync.Data.Entities.CohortContextEntity>(
            System.Text.Json.JsonSerializer.Serialize(_activeContext))!;
        updated.PhisCohortId = phisCohortId;
        await _dbManager.SaveCohortContextAsync(updated);
        _activeContext = updated;
        _phisFieldsDirty = false;
        UpdateReviewAvailability();
        LoggerService.LogInformation($"Saved PHIS Cohort ID {phisCohortId} to cohort context {_activeContext.CohortContextId} ({_activeContext.ClientListName}).");
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
        if (!_reviewGrid.EndEdit())
        {
            _reviewMessage.Text = "Finish or correct the active cell before saving the review.";
            return false;
        }
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
        if (!_reviewGrid.EndEdit())
        {
            _reviewMessage.Text = "Finish or correct the active cell before saving the review.";
            return false;
        }
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
