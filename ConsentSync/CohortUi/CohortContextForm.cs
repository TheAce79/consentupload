using System.Text.Json;
using System.Text.Json.Nodes;
using ConsentSync.Data;
using ConsentSync.Data.Entities;
using ConsentSyncCore.Services.Configuration;

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

    public CohortContextForm()
    {
        InitializeComponent();
    }

    private async void CohortContextForm_Load(object? sender, EventArgs e)
    {
        SetFormEnabled(false);
        try
        {
            _dbManager = new DbManager(ConfigurationService.GetConfiguration());
            await _dbManager.InitializeAsync();

            _activeContext = await _dbManager.GetActiveCohortContextAsync()
                ?? CreateContextFromConfiguration();

            await LoadPrefixesAsync(_activeContext.Prefix);
            await LoadLocationsAsync(_activeContext.Location);
            BindContext(_activeContext);
            await RefreshClientListSearchAsync(_activeContext.ClientListName);
        }
        catch (Exception ex)
        {
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

        btn_SaveCohortContext.Enabled = false;
        btn_SaveCohortContext.Text = "Saving...";

        try
        {
            int contextId = await _dbManager.SaveCohortContextAsync(context);
            context.CohortContextId = contextId;
            _activeContext = context;
            SetClientListNameText(context.ClientListName);
            _isUserCustomOverride = !string.Equals(
                context.ClientListName,
                BuildDerivedClientListName(),
                StringComparison.OrdinalIgnoreCase);

            UpdateAppsettings(context);
            ConfigurationService.ReloadConfiguration();
            await RefreshClientListSearchAsync(context.ClientListName);
            RestoreSaveButton();

            MessageBox.Show(
                this,
                $"Cohort context saved.\n\nId: {context.CohortContextId}\nClient list: {context.ClientListName}",
                "Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
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
            UpdateClientListNameFromContextParameters();
        }
    }

    private void OnContextParameterChanged(object? sender, EventArgs e) =>
        UpdateClientListNameFromContextParameters();

    private async void btn_LoadContext_Click(object? sender, EventArgs e) =>
        await LoadSelectedCohortContextAsync();

    private async void cb_SearchClientListName_SelectionChangeCommitted(object? sender, EventArgs e) =>
        await LoadSelectedCohortContextAsync();

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
        if (_isSynchronizingClientListName || _isBindingContext)
        {
            return;
        }

        _isUserCustomOverride = !string.Equals(
            txt_ClientListName.Text.Trim(),
            BuildDerivedClientListName(),
            StringComparison.OrdinalIgnoreCase);
    }

    private void BindContext(CohortContextEntity context)
    {
        _isBindingContext = true;
        try
        {
            SelectOrAppendComboItem(cb_Prefix, context.Prefix);
            SelectOrAppendComboItem(cb_Location, context.Location);

            txt_Type.Text = context.Type;
            dtp_CohortDate.Value = context.CohortDate == default
                ? DateTime.Today
                : context.CohortDate.Date;
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
            CohortDate = dtp_CohortDate.Value.Date,
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };

        context.ClientListName = string.IsNullOrWhiteSpace(txt_ClientListName.Text)
            ? BuildDerivedClientListName()
            : txt_ClientListName.Text.Trim().ToUpperInvariant();

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

    private string BuildDerivedClientListName() =>
        $"{cb_Prefix.Text.Trim()}{cb_Location.Text.Trim()}{txt_Type.Text.Trim()}{dtp_CohortDate.Value:yyyyMMdd}"
            .ToUpperInvariant();

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
        grp_CohortContext.Enabled = enabled;
        btn_SaveCohortContext.Enabled = enabled;
    }

    private async Task LoadSelectedCohortContextAsync()
    {
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
            await RefreshClientListSearchAsync(context.ClientListName);
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
        }
    }

    private async Task RefreshClientListSearchAsync(string? selectedClientListName = null)
    {
        if (_dbManager is null)
        {
            return;
        }

        string currentText = selectedClientListName ?? cb_SearchClientListName.Text;
        IReadOnlyList<CohortContextEntity> savedLists = await _dbManager.GetRecentSavedListsAsync();

        cb_SearchClientListName.Items.Clear();
        var autoComplete = new AutoCompleteStringCollection();

        foreach (string clientListName in savedLists
            .Select(context => context.ClientListName)
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
