using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsvProcessor
{
    public class CsvRecord
    {

        // Dynamic properties to hold all CSV columns
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();

        // Indexer for easy access
        public string this[string columnName]
        {
            get => Properties.ContainsKey(columnName) ? Properties[columnName] : string.Empty;
            set => Properties[columnName] = value;
        }

        // Helper method to get all column names
        public IEnumerable<string> GetColumnNames() => Properties.Keys;

        // Helper method to get all values in order
        public IEnumerable<string> GetValues(IEnumerable<string> columnOrder)
        {
            foreach (var column in columnOrder)
            {
                yield return Properties.ContainsKey(column) ? Properties[column] : string.Empty;
            }
        }


    }
}
