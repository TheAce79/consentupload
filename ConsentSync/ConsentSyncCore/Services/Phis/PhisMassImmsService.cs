using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System.Globalization;

namespace ConsentSyncCore.Services.Phis
{
    public class PhisMassImmsService
    {
        private const string ClientListTableRootId = "createMassImmsForm:clientListContent_DataTable";
        private const string ClientListRowsPerPageName = "createMassImmsForm:clientListContent_DataTable:dataTable_rppDD";
        private const string ClientListTableBodyId = "createMassImmsForm:clientListContent_DataTable:dataTable_data";
        private const string ClientListWidgetVar = "widget_createMassImmsForm_clientListContent_DataTable_dataTable";

        private readonly IWebDriver _driver;
        private readonly WebDriverWait _wait;
        private readonly PhisSessionManager _sessionManager;
        private readonly PhisConfig _phisConfig;

        public PhisMassImmsService(
            IWebDriver driver,
            IConfiguration config,
            PhisSessionManager sessionManager)
        {
            _driver = driver;
            _sessionManager = sessionManager;
            _phisConfig = ConfigurationService.GetPhisConfig();
            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(_phisConfig.WebDriverWaitSeconds));
        }

        public bool IsOnMassImmsPage()
        {
            try
            {
                string url = _driver.Url ?? string.Empty;
                bool urlMatches =
                    url.Contains("createMassImms.xhtml?DTO=", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("MODE=true", StringComparison.OrdinalIgnoreCase);

                if (!urlMatches)
                {
                    return false;
                }

                return _driver.FindElements(By.Id(ClientListTableBodyId)).Count > 0 &&
                       _driver.FindElements(By.Id(ClientListTableRootId)).Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<MassImmsExportResult> ExportRosterToCsvAsync(string outputPath)
        {
            try
            {
                if (!_sessionManager.EnsureSessionValid())
                {
                    return MassImmsExportResult.Failed("Session validation failed.");
                }

                if (!IsOnMassImmsPage())
                {
                    return MassImmsExportResult.Failed("PHIS is not on the View Mass Imms Event page.");
                }

                await EnsureAllRowsDisplayedAsync();

                var rosterRows = ExtractRosterRows();
                if (rosterRows.Count == 0)
                {
                    return MassImmsExportResult.Failed("No roster rows were found on the current PHIS page.");
                }

                WriteRosterCsv(outputPath, rosterRows);
                _sessionManager.UpdateActivity();

                return MassImmsExportResult.IsSuccess(rosterRows.Count);
            }
            catch (Exception ex)
            {
                return MassImmsExportResult.Failed(ex.Message);
            }
        }

        private async Task EnsureAllRowsDisplayedAsync()
        {
            var rowsPerPageElement = _wait.Until(d =>
                d.FindElements(By.Name(ClientListRowsPerPageName)).FirstOrDefault());

            if (rowsPerPageElement == null)
            {
                return;
            }

            string currentValue = GetSelectedRowsPerPageValue(rowsPerPageElement);
            string maxValue = GetMaxRowsPerPageValue(rowsPerPageElement);
            if (string.Equals(currentValue, maxValue, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool updated = false;

            try
            {
                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;
                var jsResult = js.ExecuteScript(@"
                    var targetValue = arguments[0];
                    try {
                        var rowsSelect = document.querySelector('select[name=""createMassImmsForm:clientListContent_DataTable:dataTable_rppDD""]');
                        var tableWidget = typeof PF === 'function'
                            ? PF('widget_createMassImmsForm_clientListContent_DataTable_dataTable')
                            : null;

                        if (tableWidget && tableWidget.getPaginator && tableWidget.getPaginator()) {
                            tableWidget.getPaginator().setRows(parseInt(targetValue, 10));
                            return true;
                        }

                        if (!rowsSelect) return false;

                        rowsSelect.value = targetValue;
                        rowsSelect.dispatchEvent(new Event('change', { bubbles: true }));
                        return true;
                    } catch (error) {
                        console.error('Could not expand mass imms rows-per-page', error);
                        return false;
                    }", maxValue);

                updated = jsResult is bool boolResult && boolResult;
            }
            catch (Exception ex)
            {
                LoggerService.LogInformation($"   ⚠️  Mass Imms rows-per-page JS update failed: {ex.Message}");
            }

            if (!updated)
            {
                var select = new SelectElement(rowsPerPageElement);
                try
                {
                    select.SelectByValue(maxValue);
                }
                catch (NoSuchElementException)
                {
                    select.SelectByText(maxValue);
                }
            }

            await WaitForRowsPerPageAsync(maxValue);
            LoggerService.LogInformation($"   ✅ Mass Imms rows-per-page expanded to {maxValue}");
        }

        private List<MassImmunisationRosterRecord> ExtractRosterRows()
        {
            var tbody = _wait.Until(d =>
                d.FindElements(By.Id(ClientListTableBodyId)).FirstOrDefault());

            if (tbody == null)
            {
                return new List<MassImmunisationRosterRecord>();
            }

            var rows = tbody.FindElements(By.XPath(".//tr[@role='row']"));
            var records = new List<MassImmunisationRosterRecord>();

            foreach (var row in rows)
            {
                var cells = row.FindElements(By.TagName("td"))
                    .Select(cell => cell.Text?.Trim() ?? string.Empty)
                    .ToList();

                if (cells.Count >= 5 && string.IsNullOrWhiteSpace(cells[0]))
                {
                    cells.RemoveAt(0);
                }

                if (cells.Count < 4)
                {
                    continue;
                }

                string clientId = cells[0];
                string clientName = cells[1];
                string dateOfBirth = cells[2];
                string gender = cells[3];

                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientName))
                {
                    continue;
                }

                records.Add(new MassImmunisationRosterRecord
                {
                    ClientId = clientId,
                    ClientName = clientName,
                    DateOfBirth = dateOfBirth,
                    Gender = gender
                });
            }

            return records;
        }

        private void WriteRosterCsv(string outputPath, List<MassImmunisationRosterRecord> rosterRows)
        {
            string tempFile = outputPath + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            try
            {
                var targetEncoding = EncodingConfigurationService.GetPriorityEncoding();

                using (var writer = new StreamWriter(tempFile, false, targetEncoding))
                {
                    writer.WriteLine("ClientId,ClientName,DateOfBirth,Gender");

                    foreach (var row in rosterRows)
                    {
                        writer.WriteLine(string.Join(",",
                            EscapeCsvValue(row.ClientId),
                            EscapeCsvValue(row.ClientName),
                            EscapeCsvValue(row.DateOfBirth),
                            EscapeCsvValue(row.Gender)));
                    }
                }

                File.Move(tempFile, outputPath, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }

                throw;
            }
        }

        private async Task WaitForRowsPerPageAsync(string expectedValue)
        {
            _wait.Until(d =>
            {
                var rowsPerPageElement = d.FindElements(By.CssSelector("select[name*='dataTable_rppDD']")).FirstOrDefault();
                rowsPerPageElement = d.FindElements(By.Name(ClientListRowsPerPageName)).FirstOrDefault();
                if (rowsPerPageElement == null)
                {
                    return false;
                }

                string selectedValue = GetSelectedRowsPerPageValue(rowsPerPageElement);
                if (!string.Equals(selectedValue, expectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var rows = d.FindElements(By.CssSelector($"#{EscapeCssId(ClientListTableBodyId)} tr[role='row']"));
                if (rows.Count == 0)
                {
                    return false;
                }

                var widgetRowsValue = ((IJavaScriptExecutor)d).ExecuteScript(@"
                    try {
                        var tableWidget = typeof PF === 'function'
                            ? PF('widget_createMassImmsForm_clientListContent_DataTable_dataTable')
                            : null;
                        if (!tableWidget || !tableWidget.jq) return '';
                        var selected = tableWidget.jq.find('.ui-paginator-rpp-options :selected').val();
                        return selected || '';
                    } catch (error) {
                        return '';
                    }");

                string widgetSelectedValue = widgetRowsValue?.ToString()?.Trim() ?? string.Empty;
                return string.Equals(widgetSelectedValue, expectedValue, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(selectedValue, expectedValue, StringComparison.OrdinalIgnoreCase);
            });

            await Task.Delay(_phisConfig.AjaxWaitMs);
            await Task.Delay(250);
        }

        private static string GetSelectedRowsPerPageValue(IWebElement rowsPerPageElement)
        {
            try
            {
                var select = new SelectElement(rowsPerPageElement);
                var selectedOption = select.SelectedOption;
                var selectedValue = selectedOption?.GetAttribute("value");

                if (!string.IsNullOrWhiteSpace(selectedValue))
                {
                    return selectedValue.Trim();
                }

                if (!string.IsNullOrWhiteSpace(selectedOption?.Text))
                {
                    return selectedOption.Text.Trim();
                }
            }
            catch
            {
                // Fall through to element value handling.
            }

            return rowsPerPageElement.GetAttribute("value")?.Trim() ?? "ALL";
        }

        private static string GetMaxRowsPerPageValue(IWebElement rowsPerPageElement)
        {
            try
            {
                var select = new SelectElement(rowsPerPageElement);
                var lastOption = select.Options.LastOrDefault();
                if (lastOption == null)
                {
                    return GetSelectedRowsPerPageValue(rowsPerPageElement);
                }

                return lastOption.GetAttribute("value")?.Trim()
                    ?? lastOption.Text.Trim();
            }
            catch
            {
                return "ALL";
            }
        }

        private static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private static string EscapeCssId(string id)
        {
            return id.Replace(":", "\\:");
        }
    }
}
