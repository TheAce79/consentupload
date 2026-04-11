using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CsvProcessor
{

    public class ClientIdExtractor
    {
        public static List<string> ExtractClientIds(string xmlResponse)
        {
            var clientIds = new List<string>();

            try
            {
                // Parse the XML response
                XDocument doc = XDocument.Parse(xmlResponse);

                // Get the namespace
                XNamespace ns = "http://www.w3.org/1999/xhtml";

                // Find the update element containing the form data
                var formUpdate = doc.Descendants("update")
                    .FirstOrDefault(e => e.Attribute("id")?.Value == "form");

                if (formUpdate == null)
                    return clientIds;

                string htmlContent = formUpdate.Value;

                // Extract Client IDs from the table rows
                // Pattern matches: <td role="gridcell" class="phsdsm-ui-datatable-cc-numeric">127198</td>
                var pattern = @"<td[^>]*class=""phsdsm-ui-datatable-cc-numeric"">(\d+)</td>";
                var matches = Regex.Matches(htmlContent, pattern);

                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        clientIds.Add(match.Groups[1].Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting client IDs: {ex.Message}");
            }

            return clientIds;
        }

        // Alternative method using more robust parsing
        public static List<ClientSearchResult> ExtractFullClientData(string xmlResponse)
        {
            var results = new List<ClientSearchResult>();

            try
            {
                XDocument doc = XDocument.Parse(xmlResponse);
                var formUpdate = doc.Descendants("update")
                    .FirstOrDefault(e => e.Attribute("id")?.Value == "form");

                if (formUpdate == null)
                    return results;

                string htmlContent = formUpdate.Value;

                // Pattern to match table rows with all client data
                var rowPattern = @"<tr[^>]*data-ri=""(\d+)""[^>]*>.*?</tr>";
                var rowMatches = Regex.Matches(htmlContent, rowPattern, RegexOptions.Singleline);

                foreach (Match rowMatch in rowMatches)
                {
                    string rowHtml = rowMatch.Value;

                    // Extract individual fields
                    var clientId = ExtractField(rowHtml, @"class=""phsdsm-ui-datatable-cc-numeric"">(\d+)</td>");
                    var healthCard = ExtractField(rowHtml, @"style=""width:80px"">(\d+)</td>");
                    var lastName = ExtractFields(rowHtml, @"style=""width:80px"">([^<]+)</td>").ElementAtOrDefault(1);
                    var firstName = ExtractFields(rowHtml, @"style=""width:80px"">([^<]+)</td>").ElementAtOrDefault(2);

                    if (!string.IsNullOrEmpty(clientId))
                    {
                        results.Add(new ClientSearchResult
                        {
                            ClientId = clientId,
                            HealthCardNumber = healthCard,
                            LastName = lastName,
                            FirstName = firstName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting client data: {ex.Message}");
            }

            return results;
        }

        private static string ExtractField(string html, string pattern)
        {
            var match = Regex.Match(html, pattern);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static List<string> ExtractFields(string html, string pattern)
        {
            var results = new List<string>();
            var matches = Regex.Matches(html, pattern);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    results.Add(match.Groups[1].Value.Trim());
                }
            }

            return results;
        }
    }

    public class ClientSearchResult
    {
        public string? ClientId { get; set; }
        public string? HealthCardNumber { get; set; }
        public string? LastName { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? Gender { get; set; }
        public string? DateOfBirth { get; set; }
        public string? HealthRegion { get; set; }
        public string? Status { get; set; }
    }


}
