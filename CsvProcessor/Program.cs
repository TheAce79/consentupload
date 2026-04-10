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

            FindClientId? finder = null; // ✅ DECLARE OUTSIDE TRY BLOCK

            try
            {
                // Create CSV processor
                var csvProcessor = new StudentCsvProcessor(configuration);

                // Check if output CSV already exists
                string outputPath = Path.Combine(
                    configuration["CsvProcessing:OutputCsvPath"] ?? "",
                    configuration["CsvProcessing:OutputCsvFileName"] ?? ""
                );

                if (File.Exists(outputPath))
                {
                    Console.WriteLine($"ℹ️  Output CSV already exists: {outputPath}");
                    Console.WriteLine($"   File will NOT be reprocessed.\n");
                }
                else
                {
                    Console.WriteLine($"📄 Processing source CSV...\n");
                    csvProcessor.ProcessCsv();
                    Console.WriteLine($"\n✅ CSV processing complete! Output: {outputPath}");
                }

                // Show preview
                csvProcessor.PreviewCsv(5);

                // ✅ START CLIENT ID SEARCH
                Console.WriteLine("\n🚀 Starting Client ID search automation...\n");

                finder = new FindClientId(configuration); // ✅ ASSIGN TO OUTER VARIABLE

                // Login to PHIS
                if (!finder.InitiateLogin())
                {
                    Console.WriteLine("❌ Login failed. Cannot proceed with search.");
                    return;
                }

                // Search for all unprocessed students
                finder.SearchAllClientsInCsv();

                Console.WriteLine("\n✅ All done! Check the CSV for results.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                // ✅ ENSURE CLEANUP HAPPENS EVEN IF EXCEPTION OCCURS
                if (finder != null)
                {
                    Console.WriteLine("\n🧹 Cleaning up resources...");
                    try
                    {
                        finder.Dispose();
                        Console.WriteLine("✅ ChromeDriver disposed successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Warning: Cleanup error: {ex.Message}");
                    }
                }
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}