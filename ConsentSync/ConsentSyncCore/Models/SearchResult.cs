using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Models
{
    /// <summary>
    /// Search result wrapper
    /// </summary>
    public class SearchResult
    {
        public bool Success { get; private set; }
        public List<PhisSearchResult> Results { get; private set; } = new();
        public string? ErrorMessage { get; private set; }

        // Helper properties
        public bool HasResults => Results.Count > 0;
        public bool IsSingleResult => Results.Count == 1;
        public PhisSearchResult? FirstResult => Results.FirstOrDefault();

        private SearchResult() { }

        public static SearchResult IsSuccess(List<PhisSearchResult> results)
        {
            return new SearchResult
            {
                Success = true,
                Results = results
            };
        }

        public static SearchResult NoResults()
        {
            return new SearchResult
            {
                Success = true,
                Results = new List<PhisSearchResult>()
            };
        }

        public static SearchResult Failed(string errorMessage)
        {
            return new SearchResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
