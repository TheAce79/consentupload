using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsvProcessor
{
    public class EncodingConfiguration
    {
        public string Name { get; set; } = string.Empty;
        public string CodePage { get; set; } = string.Empty;
        public bool UseBOM { get; set; } = false;
        public int Priority { get; set; } = 99;
    }
}
