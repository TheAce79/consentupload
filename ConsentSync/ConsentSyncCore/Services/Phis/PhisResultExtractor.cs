using ConsentSyncCore.Models;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Phis
{
    /// <summary>
    /// Extracts search results from PHIS table
    /// Handles dynamic column detection
    /// </summary>
    public class PhisResultExtractor
    {

        private readonly IConfiguration _config;
        private readonly PhisColumnHeaders _columnHeaders;

        private int? _clientIdIdx;
        private int? _firstNameIdx;
        private int? _lastNameIdx;
        private int? _medicareIdx;
        private bool _columnIndicesInitialized = false;

        public PhisResultExtractor(IConfiguration config)
        {
            _config = config;
            _columnHeaders = ConfigurationService.GetPhisColumnHeaders();
        }

        /// <summary>
        /// Extract all results from current search page
        /// </summary>
        public List<PhisSearchResult> ExtractAllResults(IWebDriver driver)
        {
            var results = new List<PhisSearchResult>();

            try
            {
                // Initialize column indices on first use
                if (!_columnIndicesInitialized)
                {
                    InitializeColumnIndices(driver);
                }

                // Find all result rows
                var resultRows = driver.FindElements(By.CssSelector("tbody[id*='dataTable_data'] tr[data-rk]"));

                if (resultRows.Count == 0)
                {
                    return results;
                }

                // Extract data from each row
                foreach (var row in resultRows)
                {
                    try
                    {
                        var result = ExtractRowData(row);
                        results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ⚠️  Could not extract row: {ex.Message}");
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error extracting results: {ex.Message}");
                throw;
            }
        }




        /// <summary>
        /// Extract data from single table row
        /// </summary>
        private PhisSearchResult ExtractRowData(IWebElement row)
        {
            var cells = row.FindElements(By.TagName("td"));

            if (!_columnIndicesInitialized)
            {
                throw new InvalidOperationException("Column indices not initialized");
            }

            var result = new PhisSearchResult
            {
                ClientId = cells[_clientIdIdx!.Value].Text.Trim(),
                FirstName = cells[_firstNameIdx!.Value].Text.Trim(),
                LastName = cells[_lastNameIdx!.Value].Text.Trim()
            };

            // Extract Medicare if column exists
            if (_medicareIdx.HasValue && _medicareIdx.Value < cells.Count)
            {
                var medicareText = cells[_medicareIdx.Value].Text.Trim();

                // Validate it looks like a Medicare number
                if (!string.IsNullOrWhiteSpace(medicareText) &&
                    medicareText.All(c => char.IsDigit(c) || c == '-' || c == ' '))
                {
                    result.MedicareNumber = medicareText;
                }
            }

            return result;
        }



        /// <summary>
        /// Initialize column indices by reading table headers
        /// </summary>
        private void InitializeColumnIndices(IWebDriver driver)
        {
            if (_columnIndicesInitialized) return;

            try
            {
                _clientIdIdx = GetColumnIndexByName(driver, _columnHeaders.ClientId);
                _firstNameIdx = GetColumnIndexByName(driver, _columnHeaders.FirstName);
                _lastNameIdx = GetColumnIndexByName(driver, _columnHeaders.LastName);

                // Medicare is optional
                try
                {
                    _medicareIdx = GetColumnIndexByName(driver, _columnHeaders.Medicare);
                }
                catch
                {
                    Console.WriteLine("   ⚠️  Medicare column not found");
                    _medicareIdx = null;
                }

                _columnIndicesInitialized = true;
                Console.WriteLine($"   ✅ Columns: ClientID={_clientIdIdx}, FirstName={_firstNameIdx}, LastName={_lastNameIdx}, Medicare={_medicareIdx?.ToString() ?? "N/A"}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed to initialize columns: {ex.Message}");
                throw;
            }
        }

        private int GetColumnIndexByName(IWebDriver driver, string columnName)
        {
            var table = driver.FindElement(By.Id("form:dataTable:dataTable"));
            var headerRow = table.FindElement(By.CssSelector("thead tr"));
            var headers = headerRow.FindElements(By.TagName("th"));

            for (int i = 0; i < headers.Count; i++)
            {
                if (headers[i].Text.Trim().Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            throw new Exception($"Column '{columnName}' not found");
        }







    }
}
