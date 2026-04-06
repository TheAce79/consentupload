using CsvHelper;
using CsvHelper.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PhisUploadRecord
{
    public class PhisUploadRecord
    {
        public string? ClientID { get; set; }
        public string? LastName { get; set; }
        public string? FirstName { get; set; }
        public string? DocumentTitle { get; set; }
        public string? Description { get; set; }
        public int? NbPage { get; set; }
        public string? Status { get; set; }
    }

    // Mapping class to handle CSV headers with spaces
    public sealed class PhisUploadMap : ClassMap<PhisUploadRecord>
    {
        public PhisUploadMap()
        {
            Map(m => m.ClientID).Name("ClientID");
            Map(m => m.LastName).Name("Last Name");
            Map(m => m.FirstName).Name("First Name");
            Map(m => m.DocumentTitle).Name("Document Title");
            Map(m => m.Description).Name("Description");
            Map(m => m.NbPage).Name("NbPage");
            Map(m => m.Status).Name("Status");
        }
    }

    public static class PhisUploadHelper
    {
        public static void DoUpload(string csvPath, string pdfFolder, int sleepMs)
        {
            var records = LoadUploadData(csvPath);
            Console.WriteLine($"Loaded {records.Count} records for upload.");

            IWebDriver driver = new ChromeDriver();
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

            try
            {
                driver.Navigate().GoToUrl("https://your-phis-portal-url.com");
                Console.WriteLine("Please log in to the portal, then press [Enter] in this console to start automation...");
                Console.ReadLine();

                foreach (var record in records)
                {
                    // Check for "Ok" or "Skip" to allow re-running the script safely
                    if (record.Status == "Ok" || record.Status?.StartsWith("Skip") == true) continue;

                    try
                    {
                        // FIXED: Null-safe comparison for NbPage
                        if (record.NbPage.HasValue && record.NbPage.Value > 1)
                        {
                            Console.WriteLine($"[SKIPPING] Client {record.ClientID}: {record.NbPage} pages (Feuille Rose).");
                            UpdateStatus(record, "Skip: Manual Review (Multi-page)");
                            continue;
                        }

                        Console.WriteLine($"Processing: {record.ClientID} ({record.FirstName} {record.LastName})");

                        // --- SELENIUM ACTIONS ---
                        // 1. Search Logic
                        // var search = wait.Until(d => d.FindElement(By.Id("search-box")));
                        // search.Clear();
                        // search.SendKeys(record.ClientID + Keys.Enter);

                        // 2. Upload Logic
                        // FIXED: Use ClientID for filename, not DocumentTitle
                        string fullPath = Path.Combine(pdfFolder, $"{record.ClientID}.pdf");

                        if (!File.Exists(fullPath))
                        {
                            throw new FileNotFoundException($"PDF not found: {fullPath}");
                        }

                        Console.WriteLine($"  → Found PDF: {fullPath}");

                        // var fileInput = driver.FindElement(By.XPath("//input[@type='file']"));
                        // fileInput.SendKeys(fullPath);

                        // Optional: Fill in DocumentTitle and Description from CSV
                        // var titleField = driver.FindElement(By.Id("document-title"));
                        // titleField.SendKeys(record.DocumentTitle ?? "Grade 7 Immunization");

                        // var descField = driver.FindElement(By.Id("description"));
                        // descField.SendKeys(record.Description ?? "Vaccination consent form");
                        // ------------------------

                        UpdateStatus(record, "Ok");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  [X] Error for {record.ClientID}: {ex.Message}");
                        UpdateStatus(record, "Error: " + ex.Message);
                    }
                }
            }
            finally
            {
                SaveResults(csvPath, records);
                Console.WriteLine($"Process finished. Waiting {sleepMs}ms before closing.");
                Thread.Sleep(sleepMs);
                driver.Quit();
            }
        }

        static List<PhisUploadRecord> LoadUploadData(string path)
        {
            // Use UTF8 encoding to preserve accents during read
            using (var reader = new StreamReader(path, Encoding.UTF8))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Context.RegisterClassMap<PhisUploadMap>();
                return csv.GetRecords<PhisUploadRecord>().ToList();
            }
        }

        static void UpdateStatus(PhisUploadRecord record, string status)
        {
            record.Status = status;
        }

        static void SaveResults(string path, List<PhisUploadRecord> records)
        {
            // Use UTF8 encoding to preserve accents during write
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.Context.RegisterClassMap<PhisUploadMap>();
                csv.WriteRecords(records);
            }
        }
    }
}