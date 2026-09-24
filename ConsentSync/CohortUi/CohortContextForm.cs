using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using ConsentSync.Data;
using ConsentSync.Data.Entities;
using ConsentSyncCore.Services.Csv;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.Pdf;
using ConsentSyncCore.Services.Browser;
using ConsentSyncCore.Services.Phis;
using IWebDriver = OpenQA.Selenium.IWebDriver;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using ConsentSync.Ui;

namespace CohortUi;

public partial class CohortContextForm : Form
{
    private static readonly string AppSettingsPath =
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    private static readonly string AppSettingsSourcePath =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "ConsentSyncCore",
            "appsettings.json"));

    private DbManager? _dbManager;
    private CohortContextEntity? _activeContext;
    private bool _isUserCustomOverride;
    private bool _isBindingContext;
    private bool _isSynchronizingClientListName;
    private bool _hasSavedContext;
    private bool _isPhase2Running;
    private bool _isStartingNextCohort;
    private bool _contextFieldsDirty;
    private bool _isLogSubscribed;
    private IWebDriver? _cohortDriver;
    private PhisSessionManager? _cohortSessionManager;

    public CohortContextForm()
    {
        InitializeComponent();
        InitializeWorkflowTabs();
        LoggerService.LogMessage += OnLogMessage;
        _isLogSubscribed = true;
    }

    private readonly FlowLayoutPanel _workspaceToolbar = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        Padding = new Padding(0)
    };
    private readonly LavenderCardPanel _workspaceToolbarCard = new() { Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 8, 12, 8) };
    private readonly Button _nextCohortButton = new() { Text = "Next Cohort", AutoSize = true };

    private void OnLogMessage(object? sender, LogEventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => AppendLogMessage(e));
        }
        catch (InvalidOperationException)
        {
            // The form is closing while a background PHIS operation emits a log message.
        }
    }

    private void AppendLogMessage(LogEventArgs e)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        rtxt_Log.SelectionStart = rtxt_Log.TextLength;
        rtxt_Log.SelectionLength = 0;
        rtxt_Log.SelectionColor = e.Level switch
        {
            LogLevel.Error or LogLevel.Critical => LavenderSlatePalette.Error,
            LogLevel.Warning => LavenderSlatePalette.Warning,
            LogLevel.Debug => LavenderSlatePalette.MutedText,
            _ => LavenderSlatePalette.Slate
        };
        rtxt_Log.AppendText(e.FormattedMessage + Environment.NewLine);
        rtxt_Log.SelectionColor = rtxt_Log.ForeColor;
        rtxt_Log.ScrollToCaret();
    }

    private async void CohortContextForm_Load(object? sender, EventArgs e)
    {
        SetFormEnabled(false);
        try
        {
            LoggerService.LogInformation("\n═══ Phase 0 — Loading cohort context ═══");
            LoggerService.LogInformation($"CohortUi executable: {Environment.ProcessPath}; application directory: {AppContext.BaseDirectory}");
            var coreAssembly = typeof(PhisSearchService).Assembly;
            string coreVersion = coreAssembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "unknown";
            LoggerService.LogInformation($"PHIS core build: {coreVersion}; module ID: {coreAssembly.ManifestModule.ModuleVersionId}; paginator mode: ALL dropdown with total verification.");
            var configuration = ConfigurationService.GetConfiguration();
            _dbManager = new DbManager(configuration);
            await _dbManager.InitializeAsync();

            CohortContextEntity draftContext = CreateContextFromConfiguration();

            await LoadPrefixesAsync(draftContext.Prefix);
            await LoadLocationsAsync(draftContext.Location);
            BindNewContext(draftContext);
            await RefreshClientListSearchAsync();
            LoggerService.LogInformation("✅ Cohort context draft loaded. Select a date and save, or load a saved list.");
        }
        catch (Exception ex)
        {
            LoggerService.LogError("Cohort context could not be loaded.", ex);
            MessageBox.Show(
                this,
                $"Cohort context could not be loaded.\n\n{ex.Message}",
                "Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetFormEnabled(true);
        }
    }

    private async void btn_SaveCohortContext_Click(object? sender, EventArgs e)
    {
        if (_formBusy) return;
        if (_dbManager is null)
        {
            MessageBox.Show(this, "Database manager is not ready yet.", "Not Ready", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!TryBuildContextFromFields(out CohortContextEntity context, out string validationMessage))
        {
            MessageBox.Show(this, validationMessage, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!TryValidateStandardizedOutputPath(context.ClientListName, out validationMessage))
        {
            MessageBox.Show(this, validationMessage, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!ConfirmReviewTransition()) return;
        _hasSavedContext = false;
        SetFormEnabled(false);
        UpdateProcessingAvailability();
        btn_SaveCohortContext.Enabled = false;
        btn_SaveCohortContext.Text = "Saving...";

        try
        {
            CohortContextEntity? nameMatch = await _dbManager.GetCohortContextByListNameAsync(context.ClientListName);
            int activeContextId = _activeContext?.CohortContextId ?? 0;
            if (nameMatch is not null && nameMatch.CohortContextId != activeContextId)
            {
                MessageBox.Show(
                    this,
                    $"A cohort context for '{context.ClientListName}' already exists. Choose a unique Client List Name.",
                    "Duplicate Client List Name",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            CohortContextEntity? businessKeyMatch = await _dbManager.GetCohortContextByBusinessKeyAsync(
                context.Prefix,
                context.Location,
                context.Type,
                context.CohortDate);

            if (businessKeyMatch is not null && businessKeyMatch.CohortContextId != context.CohortContextId)
            {
                if (nameMatch is not null)
                {
                    MessageBox.Show(
                        this,
                        "The selected cohort name belongs to the current record, but the setup fields belong to another saved cohort. Choose a unique Client List Name before saving.",
                        "Duplicate Client List Name",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                context.CohortContextId = businessKeyMatch.CohortContextId;
                context.PhisCohortId = businessKeyMatch.PhisCohortId;
                context.PhisClientListId = businessKeyMatch.PhisClientListId;
                context.CreatedOn = businessKeyMatch.CreatedOn;
            }

            LoggerService.LogInformation($"\n═══ Phase 0 — Saving cohort context: {context.ClientListName} ═══");
            int contextId = await _dbManager.SaveCohortContextAsync(context);
            context.CohortContextId = contextId;
            _activeContext = context;
            _contextFieldsDirty = false;
            SetClientListNameText(context.ClientListName);
            _isUserCustomOverride = !string.Equals(
                context.ClientListName,
                BuildDerivedClientListName(),
                StringComparison.OrdinalIgnoreCase);

            UpdateAppsettings(context);
            ConfigurationService.ReloadConfiguration();
            CohortWorkspaceService.EnsureDirectories(ConfigurationService.GetConfiguration(), context.ClientListName);
            _hasSavedContext = true;
            LoadActiveReview();
            RefreshStandardizedCsvPreview();
            await RefreshClientListSearchAsync(context.ClientListName);
            RestoreSaveButton();
            LoggerService.LogInformation($"✅ Cohort context saved. Id: {context.CohortContextId}; Client list: {context.ClientListName}");

            MessageBox.Show(
                this,
                $"Cohort context saved.\n\nId: {context.CohortContextId}\nClient list: {context.ClientListName}",
                "Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            LoggerService.LogError("Cohort context could not be saved.", ex);
            RestoreSaveButton();
            MessageBox.Show(
                this,
                $"Cohort context could not be saved.\n\n{ex.Message}",
                "Save Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            RestoreSaveButton();
            SetFormEnabled(true);
            UpdateProcessingAvailability();
        }
    }

    private async void btn_ExtractCsv_Click(object? sender, EventArgs e)
    {
        if (!TryGetSavedClientListName(out string clientListName))
        {
            return;
        }

        SetFormEnabled(false);
        btn_ExtractCsv.Text = "Extracting...";
        try
        {
            LoggerService.LogInformation("\n═══ Phase 1 — Extracting CSV from PDF roster ═══");
            var configuration = ConfigurationService.GetConfiguration();
            var (_, inputPdfDir, _) = CohortWorkspaceService.EnsureDirectories(configuration, clientListName);
            string targetCsvPath = CohortWorkspaceService.GetStandardizedInputCsvPath(configuration, clientListName);

            using var dialog = new OpenFileDialog
            {
                InitialDirectory = inputPdfDir, Filter = "PDF files (*.pdf)|*.pdf", Multiselect = true,
                Title = "Select the complete current clinic schedule"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0) return;
            var selectedFiles = dialog.FileNames.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var parser = new PdfRosterParserService();
            var records = await Task.Run(() => parser.ExtractRecordsFromPdfFiles(selectedFiles, LoggerService.LogInformation));
            if (parser.LastPageWarnings.Count > 0)
                throw new InvalidOperationException("The selected schedule has page(s) with no recognized clients. Review the PDF selection or formatting before accepting this schedule:\n" + string.Join("\n", parser.LastPageWarnings));
            if (records.Count == 0)
            {
                LoggerService.LogWarning($"No client records found in PDF folder: {inputPdfDir}");
                MessageBox.Show(this,
                    $"No client records were found in PDFs inside:\n{inputPdfDir}\n\nPlace clinic schedule PDFs in this folder and try again.",
                    "No Records Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var existing = File.Exists(targetCsvPath) ? await Task.Run(() => CsvImporterService.ReadFromCsv(targetCsvPath)) : [];
            var existingKeys = existing.Select(ClinicScheduleSummary.Key).ToHashSet(StringComparer.Ordinal);
            var added = records.Where(x => existingKeys.Add(ClinicScheduleSummary.Key(x))).ToList();
            if (added.Count > 0 || !File.Exists(targetCsvPath)) await Task.Run(() => CsvExporterService.SaveToCsv(existing.Concat(added), targetCsvPath));

            var prior = await _dbManager!.GetLatestScheduleSnapshotAsync(_activeContext!.CohortContextId, clientListName);
            var currentSchedule = ClinicScheduleSummary.AttachClientIds(records, existing.Concat(added));
            var priorSchedule = prior is null ? null : JsonSerializer.Deserialize<List<ClinicScheduleClient>>(prior.ClientSnapshotJson);
            var comparison = ClinicScheduleSummary.Compare(currentSchedule, priorSchedule);
            DateTime? verifiedUpload = null; int? verifiedPhisCount = null;
            if (_activeContext.PhisCohortId is int verifiedCohortId)
            {
                var uploadSnapshot = await _dbManager.GetLatestPhisUploadSnapshotAsync(_activeContext.CohortContextId, verifiedCohortId, _activeContext.PhisClientListId, clientListName);
                if (uploadSnapshot is not null)
                {
                    verifiedUpload = uploadSnapshot.UploadedOn;
                    verifiedPhisCount = JsonSerializer.Deserialize<List<PhisUploadClient>>(uploadSnapshot.ClientSnapshotJson)?.Count;
                }
            }
            var sourceFiles = selectedFiles.Select(path => new { Name = Path.GetFileName(path), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }).ToList();
            await _dbManager.SaveScheduleSnapshotAsync(new ScheduleSnapshotEntity { CohortContextId = _activeContext.CohortContextId, ClientListName = clientListName, BatchId = Guid.NewGuid().ToString("N"), SourceFilesJson = JsonSerializer.Serialize(sourceFiles), ClientSnapshotJson = JsonSerializer.Serialize(currentSchedule), ImportedOn = DateTime.UtcNow });
            string outputDirectory = Path.GetDirectoryName(CohortWorkspaceService.GetStandardizedOutputCsvPath(configuration, clientListName))!;
            string reportPath = Path.Combine(outputDirectory, $"{clientListName}_Clinic_Schedule_Update_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
            await File.WriteAllTextAsync(reportPath, ClinicScheduleSummary.Format(clientListName, _activeContext.PhisCohortId, _activeContext.PhisClientListId, comparison, prior?.ImportedOn, selectedFiles.Select(path => Path.GetFileName(path) ?? path), verifiedUpload, verifiedPhisCount), new System.Text.UTF8Encoding(false));
            RefreshStandardizedCsvPreview();
            LoggerService.LogInformation($"✅ Retained {existing.Count} existing record(s), added {added.Count} new client record(s). Schedule report: {reportPath}");
            MessageBox.Show(this,
                $"Current schedule: {currentSchedule.Count} client(s).\nNew CSV records added: {added.Count}.\nExisting records retained: {existing.Count}.\n\nAdministrative update:\n{reportPath}",
                "Extraction Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            LoggerService.LogError("PDF roster extraction failed.", ex);
            MessageBox.Show(this, $"PDF roster extraction failed.\n\n{ex.Message}", "Extraction Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btn_ExtractCsv.Text = "Extract CSV from PDFs";
            SetFormEnabled(true);
        }
    }

    private async Task RefreshCurrentScheduleReportAsync()
    {
        if (_activeContext is null || string.IsNullOrWhiteSpace(_activeContext.ClientListName)) return;
        var snapshots = await _dbManager!.GetRecentScheduleSnapshotsAsync(_activeContext.CohortContextId, _activeContext.ClientListName);
        if (snapshots.Count == 0) return;
        var current = JsonSerializer.Deserialize<List<ClinicScheduleClient>>(snapshots[0].ClientSnapshotJson) ?? [];
        var prior = snapshots.Count > 1 ? JsonSerializer.Deserialize<List<ClinicScheduleClient>>(snapshots[1].ClientSnapshotJson) : null;
        var csvPath = CohortWorkspaceService.GetStandardizedInputCsvPath(ConfigurationService.GetConfiguration(), _activeContext.ClientListName);
        var csv = File.Exists(csvPath) ? CsvImporterService.ReadFromCsv(csvPath) : [];
        var comparison = ClinicScheduleSummary.Compare(ClinicScheduleSummary.AttachClientIds(current.Select(x => new ConsentSyncCore.Models.ClinicPdfClientRecord { FullName = x.FullName, DateOfBirth = x.DateOfBirth, VaccineType = x.VaccineType }), csv), prior);
        var output = Path.GetDirectoryName(CohortWorkspaceService.GetStandardizedOutputCsvPath(ConfigurationService.GetConfiguration(), _activeContext.ClientListName))!;
        var reportPath = Path.Combine(output, $"{_activeContext.ClientListName}_Clinic_Schedule_Update_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
        var sourceFiles = JsonDocument.Parse(snapshots[0].SourceFilesJson).RootElement.EnumerateArray().Select(x => x.TryGetProperty("Name", out var name) ? name.GetString() ?? "Unknown PDF" : "Unknown PDF").ToList();
        await File.WriteAllTextAsync(reportPath, ClinicScheduleSummary.Format(_activeContext.ClientListName, _activeContext.PhisCohortId, _activeContext.PhisClientListId, comparison, snapshots.Count > 1 ? snapshots[1].ImportedOn : null, sourceFiles, DateTime.UtcNow, null), new System.Text.UTF8Encoding(false));
        LoggerService.LogInformation($"Refreshed current schedule report with resolved PHIS IDs: {reportPath}");
    }

    private async void btn_SearchPhis_Click(object? sender, EventArgs e)
    {
        if (_isPhase2Running) return;
        if (!TryGetSavedClientListName(out string clientListName))
        {
            return;
        }

        if (!ConfirmReviewTransition()) return;

        var config = ConfigurationService.GetConfiguration();
        string inputCsvPath;
        string outputCsvPath;
        try
        {
            inputCsvPath = CohortWorkspaceService.GetStandardizedInputCsvPath(config, clientListName);
            outputCsvPath = CohortWorkspaceService.GetStandardizedOutputCsvPath(config, clientListName);
            if (!File.Exists(inputCsvPath))
            {
                using var dialog = new OpenFileDialog
                {
                    InitialDirectory = Path.GetDirectoryName(inputCsvPath), Filter = "CSV files (*.csv)|*.csv", Multiselect = false,
                    Title = "Select an AbleAssess or cohort CSV"
                };
                if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName)) return;
                inputCsvPath = dialog.FileName;
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogError("Cohort CSV path validation failed.", ex);
            MessageBox.Show(this, $"The cohort CSV paths are invalid.\n\n{ex.Message}", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        List<ConsentSyncCore.Models.ClinicPdfClientRecord> records;
        try { records = CsvImporterService.ReadFromCsv(inputCsvPath); }
        catch (Exception ex)
        {
            LoggerService.LogError($"Phase 2 input CSV could not be read: {inputCsvPath}", ex);
            MessageBox.Show(this, $"Input CSV could not be read.\n\n{ex.Message}", "CSV Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (records.Count == 0)
        {
            LoggerService.LogWarning($"Phase 2 input CSV has no client records: {inputCsvPath}");
            MessageBox.Show(this, "The input CSV contains no client records.", "No Records", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            await _dbManager!.PreloadCacheForDatesAsync(records.Select(record => record.DateOfBirth));
            int cacheHits = await ResolveCachedClientIdsAsync(records);
            LoggerService.LogInformation($"Cohort cache preload completed for {records.Select(record => record.DateOfBirth).Distinct().Count()} roster DOB value(s). Cache hits: {cacheHits}; PHIS required: {records.Count - cacheHits}.");
        }
        catch (Exception ex)
        {
            LoggerService.LogWarning($"Cohort cache preload failed; PHIS search will continue for all records. {ex.Message}");
        }

        _isPhase2Running = true;
        SetFormEnabled(false);
        btn_SearchPhis.Text = "Searching PHIS...";
        pb_Phase2.Minimum = 0; pb_Phase2.Maximum = records.Count; pb_Phase2.Value = 0;
        lbl_Phase2Progress.Text = $"0 / {records.Count}";
        lbl_Phase2Status.Text = "Opening PHIS session...";
        LoggerService.LogInformation($"\n═══ Phase 2 — PHIS client ID resolution started ═══\n   Records: {records.Count}\n   Input CSV: {inputCsvPath}\n   Output CSV: {outputCsvPath}");
        var progress = new Progress<Phase2Progress>(p =>
        {
            pb_Phase2.Maximum = p.Total;
            pb_Phase2.Value = Math.Min(p.Current, p.Total);
            lbl_Phase2Progress.Text = $"{p.Current} / {p.Total}";
            lbl_Phase2Status.Text = $"Searching {p.DateOfBirth} — {p.StudentName}";
        });

        try
        {
            List<ConsentSyncCore.Models.ClinicPdfClientRecord> remaining = records.Where(record => record.ClientIdStatus != ConsentSyncCore.Models.ClientIdStatus.Found || string.IsNullOrWhiteSpace(record.ClientId)).ToList();
            List<ConsentSyncCore.Models.ClinicPdfClientRecord> updated = remaining.Count == 0 ? records : await Task.Run(async () =>
            {
                IWebDriver driver = new ChromeDriverFactory(config).CreateDriver();
                try
                {
                    var session = new PhisSessionManager(driver, config);
                    if (!session.Login()) throw new InvalidOperationException("PHIS login was not completed.");
                    LoggerService.LogInformation("✅ PHIS session opened for Phase 2.");
                    var service = new PhisSearchService(driver, config, new PhisResultExtractor(config), session);
                    await new CohortPhisSearchRunner(service).ExecuteSearchAsync(remaining, progress);
                    return records;
                }
                finally { driver.Dispose(); }
            });
            CsvExporterService.SaveToCsv(updated, outputCsvPath);
            LoadActiveReview();
            _workflowTabs.SelectedTab = _reviewTab;
            int manualReviewCount = updated.Count(record => record.ClientIdStatus == ConsentSyncCore.Models.ClientIdStatus.NeedsManualReview);
            LoggerService.LogInformation($"✅ Phase 2 complete. Enriched CSV saved: {outputCsvPath}");
            MessageBox.Show(this,
                $"PHIS search complete.\n\nTotal records: {updated.Count}\nRecords needing manual review: {manualReviewCount}\n\nEnriched CSV saved to:\n{outputCsvPath}",
                "Search Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            LoggerService.LogError("Phase 2 PHIS search stopped. The existing output CSV was preserved.", ex);
            MessageBox.Show(this, $"PHIS search stopped. The existing output CSV was preserved.\n\n{ex.Message}\n\nCheck the Debug Log for details.", "Search Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isPhase2Running = false;
            btn_SearchPhis.Text = "Search PHIS";
            SetFormEnabled(true);
        }
    }

    private async Task<int> ResolveCachedClientIdsAsync(IEnumerable<ConsentSyncCore.Models.ClinicPdfClientRecord> records)
    {
        if (_dbManager is null) return 0;
        int hits = 0;
        foreach (var record in records)
        {
            string cacheKey = DbManager.BuildCacheKey(record.FullName, record.DateOfBirth);
            if (string.IsNullOrWhiteSpace(cacheKey)) continue;
            string? clientId = await _dbManager.GetClientIdAsync(cacheKey);
            if (string.IsNullOrWhiteSpace(clientId)) continue;
            record.ClientId = clientId;
            record.ClientIdStatus = ConsentSyncCore.Models.ClientIdStatus.Found;
            record.ErrorDetails = null;
            record.BestMatch = null;
            hits++;
        }
        return hits;
    }

    private void CohortContextForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_formBusy || _isPhase2Running)
        {
            e.Cancel = true;
            MessageBox.Show(this, "An operation is still running. Wait for it to finish before closing.", "Operation Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ConfirmReviewTransition())
        {
            e.Cancel = true;
            return;
        }
        DisposeCohortPhisSession();
        UnsubscribeFromLogs();
    }

    private void DisposeCohortPhisSession()
    {
        if (_cohortDriver is null)
        {
            return;
        }

        try { _cohortDriver.Quit(); _cohortDriver.Dispose(); }
        catch (Exception ex) { LoggerService.LogWarning($"PHIS cohort session disposal warning: {ex.Message}"); }
        finally
        {
            _cohortDriver = null;
            _cohortSessionManager = null;
        }
    }

    private void UnsubscribeFromLogs()
    {
        if (!_isLogSubscribed)
        {
            return;
        }

        LoggerService.LogMessage -= OnLogMessage;
        _isLogSubscribed = false;
    }

    private void OnContextParameterChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, dtp_CohortDate))
        {
            dtp_CohortDate.CustomFormat = dtp_CohortDate.Checked ? "yyyy-MM-dd" : " ";
        }

        if (!_isBindingContext)
        {
            _contextFieldsDirty = true;
            _hasSavedContext = false;
            UpdateProcessingAvailability();
        }

        UpdateClientListNameFromContextParameters();
        RefreshStandardizedCsvPreview();
    }

    private async void btn_LoadContext_Click(object? sender, EventArgs e) =>
        await LoadSelectedCohortContextAsync();

    private void btn_NextCohort_Click(object? sender, EventArgs e)
    {
        if (_formBusy || _isPhase2Running || _isStartingNextCohort)
        {
            return;
        }

        if (!ConfirmNextCohortTransition())
        {
            return;
        }

        CohortContextEntity defaults = CreateNextCohortDefaults();
        _isStartingNextCohort = true;
        SetFormEnabled(false);
        try
        {
            DisposeCohortPhisSession();
            ResetReviewForNextCohort();
            BindNewContext(defaults);
            cb_SearchClientListName.SelectedIndex = -1;
            cb_SearchClientListName.Text = string.Empty;
            pb_Phase2.Minimum = 0;
            pb_Phase2.Maximum = 1;
            pb_Phase2.Value = 0;
            lbl_Phase2Progress.Text = "0 / 0";
            lbl_Phase2Status.Text = "Ready";
            _workflowTabs.SelectedIndex = 0;
            LoggerService.LogInformation("✅ Ready for next cohort. Select a cohort date and save, or load a saved list.");
        }
        finally
        {
            _isStartingNextCohort = false;
            SetFormEnabled(true);
            BeginInvoke(() =>
            {
                if (!IsDisposed && !Disposing)
                {
                    dtp_CohortDate.Focus();
                }
            });
        }
    }

    private bool ConfirmNextCohortTransition()
    {
        if (!ConfirmReviewTransition())
        {
            return false;
        }

        if (!_contextFieldsDirty && !_phisFieldsDirty)
        {
            return true;
        }

        DialogResult choice = MessageBox.Show(
            this,
            "Discard unsaved setup or PHIS field changes and start the next cohort?\n\nYes: Discard and Continue\nNo: Cancel",
            "Unsaved Cohort Changes",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        return choice == DialogResult.Yes;
    }

    private CohortContextEntity CreateNextCohortDefaults()
    {
        if (_activeContext is not null)
        {
            return new CohortContextEntity
            {
                Prefix = _activeContext.Prefix,
                Location = _activeContext.Location,
                Type = _activeContext.Type,
                Jurisdiction = _activeContext.Jurisdiction,
                EncounterGroup = _activeContext.EncounterGroup
            };
        }

        return new CohortContextEntity
        {
            Prefix = cb_Prefix.Text,
            Location = cb_Location.Text,
            Type = txt_Type.Text,
            Jurisdiction = txt_Jurisdiction.Text,
            EncounterGroup = txt_EncounterGroup.Text
        };
    }

    private async void cb_SearchClientListName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        await LoadSelectedCohortContextAsync();
    }

    private void txt_ClientListName_TextChanged(object? sender, EventArgs e)
    {
        if (!_isSynchronizingClientListName && !_isBindingContext)
        {
            _isUserCustomOverride = !string.Equals(
                txt_ClientListName.Text.Trim(),
                BuildDerivedClientListName(),
                StringComparison.OrdinalIgnoreCase);
            _hasSavedContext = false;
            _contextFieldsDirty = true;
            UpdateProcessingAvailability();
        }

        RefreshStandardizedCsvPreview();
    }

    private void BindContext(CohortContextEntity context)
    {
        _phisFieldsDirty = false;
        _contextFieldsDirty = false;
        _isBindingContext = true;
        try
        {
            SelectOrAppendComboItem(cb_Prefix, context.Prefix);
            SelectOrAppendComboItem(cb_Location, context.Location);

            txt_Type.Text = context.Type;
            SetCohortDate(context.CohortDate == default ? null : context.CohortDate.Date);
            txt_Jurisdiction.Text = context.Jurisdiction;
            txt_EncounterGroup.Text = context.EncounterGroup;

            string listName = string.IsNullOrWhiteSpace(context.ClientListName)
                ? BuildDerivedClientListName()
                : context.ClientListName.Trim().ToUpperInvariant();

            SetClientListNameText(listName);
        }
        finally
        {
            _isBindingContext = false;
        }

        _isUserCustomOverride = !string.Equals(
            txt_ClientListName.Text.Trim(),
            BuildDerivedClientListName(),
            StringComparison.OrdinalIgnoreCase);
        RefreshStandardizedCsvPreview();
    }

    private void BindNewContext(CohortContextEntity context)
    {
        BindContext(new CohortContextEntity
        {
            Prefix = context.Prefix,
            Location = context.Location,
            Type = context.Type,
            Jurisdiction = context.Jurisdiction,
            EncounterGroup = context.EncounterGroup,
            CohortDate = default,
            ClientListName = string.Empty
        });
        _activeContext = null;
        _isUserCustomOverride = false;
        _hasSavedContext = false;
        _contextFieldsDirty = false;
        SetCohortDate(null);
        SetClientListNameText(string.Empty);
        RefreshStandardizedCsvPreview();
        _contextFieldsDirty = false;
        UpdateProcessingAvailability();
    }

    private bool TryBuildContextFromFields(out CohortContextEntity context, out string validationMessage)
    {
        context = new CohortContextEntity
        {
            PhisCohortId = _activeContext?.PhisCohortId,
            PhisClientListId = _activeContext?.PhisClientListId,
            Prefix = cb_Prefix.Text.Trim(),
            Location = cb_Location.Text.Trim(),
            Type = txt_Type.Text.Trim(),
            Jurisdiction = txt_Jurisdiction.Text.Trim(),
            EncounterGroup = txt_EncounterGroup.Text.Trim(),
            CohortDate = dtp_CohortDate.Checked ? dtp_CohortDate.Value.Date : default,
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };

        context.ClientListName = string.IsNullOrWhiteSpace(txt_ClientListName.Text)
            ? BuildDerivedClientListName()
            : txt_ClientListName.Text.Trim().ToUpperInvariant();

        if (!dtp_CohortDate.Checked)
        {
            validationMessage = "Cohort date is required before saving a client list.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(context.Prefix) ||
            string.IsNullOrWhiteSpace(context.Location) ||
            string.IsNullOrWhiteSpace(context.Type) ||
            string.IsNullOrWhiteSpace(context.ClientListName) ||
            string.IsNullOrWhiteSpace(context.Jurisdiction) ||
            string.IsNullOrWhiteSpace(context.EncounterGroup))
        {
            validationMessage = "Prefix, Location, Type, Client List Name, Jurisdiction, and Encounter Group are required.";
            return false;
        }

        bool updateExistingContext = IsSameCohortIdentity(_activeContext, context);
        if (!updateExistingContext)
        {
            context.PhisCohortId = null;
            context.PhisClientListId = null;
        }
        context.CohortContextId = updateExistingContext
            ? _activeContext?.CohortContextId ?? 0
            : 0;
        context.CreatedOn = updateExistingContext && _activeContext is not null
            ? _activeContext.CreatedOn
            : DateTime.UtcNow;

        validationMessage = string.Empty;
        return true;
    }

    private void UpdateClientListNameFromContextParameters()
    {
        if (_isBindingContext || _isUserCustomOverride)
        {
            return;
        }

        SetClientListNameText(BuildDerivedClientListName());
    }

    private void RefreshStandardizedCsvPreview()
    {
        try
        {
            txt_StandardizedCsvName.Text = CohortWorkspaceService.FormatStandardizedCsvFileName(
                ConfigurationService.GetConfiguration(),
                txt_ClientListName.Text);
        }
        catch
        {
            // Intermediate edits can be incomplete or invalid; saving shows the validation detail.
            txt_StandardizedCsvName.Clear();
        }
    }

    private static bool TryValidateStandardizedOutputPath(string clientListName, out string validationMessage)
    {
        try
        {
            _ = CohortWorkspaceService.ResolveWorkspacePaths(
                ConfigurationService.GetConfiguration(), clientListName);
            _ = CohortWorkspaceService.FormatStandardizedCsvFileName(
                ConfigurationService.GetConfiguration(), clientListName);
            validationMessage = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            validationMessage = $"The standardized CSV destination is invalid.\n\n{ex.Message}";
            return false;
        }
    }

    private string BuildDerivedClientListName() => !dtp_CohortDate.Checked
        ? string.Empty
        : $"{cb_Prefix.Text.Trim()}{cb_Location.Text.Trim()}{txt_Type.Text.Trim()}{dtp_CohortDate.Value:yyyyMMdd}"
            .ToUpperInvariant();

    private void SetCohortDate(DateTime? date)
    {
        dtp_CohortDate.Value = date ?? DateTime.Today;
        dtp_CohortDate.Checked = date.HasValue;
        dtp_CohortDate.CustomFormat = date.HasValue ? "yyyy-MM-dd" : " ";
    }

    private void SetClientListNameText(string value)
    {
        _isSynchronizingClientListName = true;
        try
        {
            txt_ClientListName.Text = value;
        }
        finally
        {
            _isSynchronizingClientListName = false;
        }
    }

    private static CohortContextEntity CreateContextFromConfiguration()
    {
        var config = ConfigurationService.GetConfiguration();
        _ = int.TryParse(config["CohortContext:LastCohortContextId"], out int lastCohortContextId);
        DateTime cohortDate = DateTime.TryParse(config["CohortContext:CohortDate"], out DateTime configuredDate)
            ? configuredDate.Date
            : DateTime.Today;

        return new CohortContextEntity
        {
            CohortContextId = lastCohortContextId,
            Prefix = config["CohortContext:Prefix"] ?? "CIP",
            Location = config["CohortContext:Location"] ?? "MONCTON",
            Type = config["CohortContext:Type"] ?? "SP",
            Jurisdiction = config["CohortContext:Jurisdiction"] ?? "Moncton Public Health, Moncton, New Brunswick",
            EncounterGroup = config["CohortContext:EncounterGroup"] ?? "Immunization",
            ClientListName = config["CohortContext:LastClientListName"] ?? string.Empty,
            CohortDate = cohortDate,
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };
    }

    private static void UpdateAppsettings(CohortContextEntity context)
    {
        JsonNode node = JsonNode.Parse(File.ReadAllText(AppSettingsPath))
            ?? throw new InvalidOperationException("appsettings.json is empty or invalid.");

        JsonObject cohort = node["CohortContext"] as JsonObject ?? new JsonObject();
        node["CohortContext"] = cohort;

        cohort["LastCohortContextId"] = context.CohortContextId;
        cohort["LastClientListName"] = context.ClientListName;
        cohort["Prefix"] = context.Prefix;
        cohort["Location"] = context.Location;
        cohort["Type"] = context.Type;
        cohort["CohortDate"] = context.CohortDate.ToString("yyyy-MM-dd");
        cohort["Jurisdiction"] = context.Jurisdiction;
        cohort["EncounterGroup"] = context.EncounterGroup;

        string json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppSettingsPath, json);
        SyncToSourceAppsettings(json);
    }

    private static void SyncToSourceAppsettings(string json)
    {
        try
        {
            if (File.Exists(AppSettingsSourcePath))
            {
                File.WriteAllText(AppSettingsSourcePath, json);
            }
        }
        catch
        {
            // The runtime appsettings file is authoritative for published builds.
        }
    }

    private void SetFormEnabled(bool enabled)
    {
        _formBusy = !enabled;
        grp_CohortContext.Enabled = enabled;
        grp_PdfRosterExtraction.Enabled = enabled && _hasSavedContext;
        grp_PhisSearch.Enabled = enabled && _hasSavedContext;
        btn_SaveCohortContext.Enabled = enabled;
        _nextCohortButton.Enabled = enabled && !_isPhase2Running && !_isStartingNextCohort;
        UpdateReviewAvailability();
    }

    private void UpdateProcessingAvailability()
    {
        if (!IsDisposed && !Disposing)
        {
            grp_PdfRosterExtraction.Enabled = _hasSavedContext && !_formBusy;
            grp_PhisSearch.Enabled = _hasSavedContext && !_formBusy;
            UpdateReviewAvailability();
        }
    }

    private bool TryGetSavedClientListName(out string clientListName)
    {
        clientListName = _activeContext?.ClientListName ?? string.Empty;
        if (_hasSavedContext && !string.IsNullOrWhiteSpace(clientListName))
        {
            return true;
        }

        MessageBox.Show(this,
            "Save the cohort context or load a saved client list before processing files.",
            "Save Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private async Task LoadSelectedCohortContextAsync()
    {
        if (_formBusy) return;
        if (!ConfirmReviewTransition()) return;
        if (_dbManager is null)
        {
            MessageBox.Show(this, "Database manager is not ready yet.", "Not Ready", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string clientListName = cb_SearchClientListName.Text.Trim();
        if (string.IsNullOrWhiteSpace(clientListName))
        {
            MessageBox.Show(this, "Enter or select a saved client list name.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btn_LoadContext.Enabled = false;
        btn_LoadContext.Text = "Loading...";
        SetFormEnabled(false);
        _hasSavedContext = false;
        UpdateProcessingAvailability();

        try
        {
            CohortContextEntity? context = await _dbManager.GetCohortContextByListNameAsync(clientListName);
            if (context is null)
            {
                RestoreLoadButton();
                MessageBox.Show(
                    this,
                    $"No saved cohort found for '{clientListName}'.",
                    "Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!TryValidateStandardizedOutputPath(context.ClientListName, out string validationMessage))
            {
                MessageBox.Show(this, validationMessage, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool activated = await _dbManager.SetActiveCohortContextAsync(context.CohortContextId);
            if (!activated)
            {
                RestoreLoadButton();
                MessageBox.Show(
                    this,
                    $"No saved cohort found for '{clientListName}'.",
                    "Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            context.IsActive = true;
            _activeContext = context;
            await LoadPrefixesAsync(context.Prefix);
            await LoadLocationsAsync(context.Location);
            BindContext(context);
            UpdateAppsettings(context);
            ConfigurationService.ReloadConfiguration();
            CohortWorkspaceService.EnsureDirectories(ConfigurationService.GetConfiguration(), context.ClientListName);
            _hasSavedContext = true;
            LoadActiveReview();
            RefreshStandardizedCsvPreview();
            await RefreshClientListSearchAsync(context.ClientListName);
            UpdateProcessingAvailability();
        }
        catch (Exception ex)
        {
            RestoreLoadButton();
            MessageBox.Show(
                this,
                $"Cohort context could not be loaded.\n\n{ex.Message}",
                "Load Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            RestoreLoadButton();
            SetFormEnabled(true);
        }
    }

    private async Task RefreshClientListSearchAsync(string? selectedClientListName = null)
    {
        if (_dbManager is null)
        {
            return;
        }

        string currentText = selectedClientListName ?? cb_SearchClientListName.Text;
        IReadOnlyList<string> savedLists = await _dbManager.GetRecentClientListNamesAsync();

        cb_SearchClientListName.Items.Clear();
        var autoComplete = new AutoCompleteStringCollection();

        foreach (string clientListName in savedLists
            .Where(clientListName => !string.IsNullOrWhiteSpace(clientListName))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cb_SearchClientListName.Items.Add(clientListName);
            autoComplete.Add(clientListName);
        }

        cb_SearchClientListName.AutoCompleteCustomSource = autoComplete;
        cb_SearchClientListName.Text = currentText ?? string.Empty;
    }

    private async Task LoadPrefixesAsync(string? currentPrefix)
    {
        if (_dbManager is null)
        {
            return;
        }

        cb_Prefix.Items.Clear();

        foreach (string prefix in await _dbManager.GetPrefixesAsync())
        {
            cb_Prefix.Items.Add(prefix);
        }

        if (!string.IsNullOrWhiteSpace(currentPrefix) &&
            !cb_Prefix.Items.Cast<string>().Contains(currentPrefix, StringComparer.OrdinalIgnoreCase))
        {
            cb_Prefix.Items.Add(currentPrefix);
        }

        if (cb_Prefix.Items.Count > 0 && cb_Prefix.SelectedIndex < 0)
        {
            cb_Prefix.SelectedIndex = 0;
        }
    }

    private async Task LoadLocationsAsync(string? currentLocation)
    {
        if (_dbManager is null)
        {
            return;
        }

        cb_Location.Items.Clear();

        foreach (string location in await _dbManager.GetLocationsAsync())
        {
            cb_Location.Items.Add(location);
        }

        if (!string.IsNullOrWhiteSpace(currentLocation) &&
            !cb_Location.Items.Cast<string>().Contains(currentLocation, StringComparer.OrdinalIgnoreCase))
        {
            cb_Location.Items.Add(currentLocation);
        }

        if (cb_Location.Items.Count > 0 && cb_Location.SelectedIndex < 0)
        {
            cb_Location.SelectedIndex = 0;
        }
    }

    private static void SelectOrAppendComboItem(ComboBox comboBox, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string normalizedValue = value.Trim();
        string? matchingItem = comboBox.Items
            .Cast<string>()
            .FirstOrDefault(item => string.Equals(item, normalizedValue, StringComparison.OrdinalIgnoreCase));

        if (matchingItem is null)
        {
            comboBox.Items.Add(normalizedValue);
            matchingItem = normalizedValue;
        }

        comboBox.SelectedItem = matchingItem;
    }

    private void RestoreSaveButton()
    {
        btn_SaveCohortContext.Enabled = true;
        btn_SaveCohortContext.Text = "Save Cohort Context";
    }

    private void RestoreLoadButton()
    {
        btn_LoadContext.Enabled = true;
        btn_LoadContext.Text = "Load";
    }

    private static bool IsSameCohortIdentity(CohortContextEntity? currentContext, CohortContextEntity candidate)
    {
        if (currentContext is null || currentContext.CohortContextId <= 0)
        {
            return false;
        }

        return SameText(currentContext.Prefix, candidate.Prefix) &&
            SameText(currentContext.Location, candidate.Location) &&
            SameText(currentContext.Type, candidate.Type) &&
            currentContext.CohortDate.Date == candidate.CohortDate.Date &&
            SameText(currentContext.ClientListName, candidate.ClientListName);
    }

    private static bool SameText(string? left, string? right) =>
        string.Equals(
            (left ?? string.Empty).Trim(),
            (right ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);
}
