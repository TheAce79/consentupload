using Microsoft.Extensions.Configuration;
using System.Text;

namespace CsvProcessor
{
    class Program
    {
        static void Main(string[] args)
        {
            // Register encoding provider for legacy encodings (Windows-1252, etc.)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Console.WriteLine("==============================================");
            Console.WriteLine("  CSV Processor - PHIS Immunization Records");
            Console.WriteLine("==============================================\n");

            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            try
            {
                // Create and run CSV processor
                var csvReader = new CsvReader(configuration);
                csvReader.ProcessCsv();

                // Show preview
                csvReader.PreviewCsv(5);

                Console.WriteLine("\n✅ Processing complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}