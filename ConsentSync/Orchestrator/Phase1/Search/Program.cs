using CsvProcessing;
using Microsoft.Extensions.Configuration;
using System.Text;


namespace Orchestrator.Phase1.Search
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Register encoding provider for legacy encodings
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Console.WriteLine("╔════════════════════════════════════════════════════════╗");
            Console.WriteLine("║              ConsentSync - Phase 1                     ║");
            Console.WriteLine("║         Search PHIS for Client IDs by DOB              ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

            try
            {
                // Build configuration
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                // ═══════════════════════════════════════════════════════
                // PRE-PHASE: CSV Processing (if needed)
                // ═══════════════════════════════════════════════════════
                Console.WriteLine("📋 PRE-PHASE: Checking CSV Processing Status\n");

                var csvRepo = new StudentCsvRepository(configuration);

                if (!csvRepo.ProcessedCsvExists())
                {
                    Console.WriteLine("📄 Processing raw CSV...");
                    csvRepo.ProcessRawCsv();
                    Console.WriteLine("✅ CSV processing complete\n");
                }
                else
                {
                    Console.WriteLine("✅ CSV already processed\n");
                }

                // Show preview and statistics
                csvRepo.PreviewProcessedCsv(3);
                csvRepo.DisplayStatistics();

                // Confirm before proceeding
                Console.WriteLine("\n" + new string('─', 60));
                Console.WriteLine("Ready to start Phase 1: Client ID Search");
                Console.WriteLine("Press [Enter] to continue or Ctrl+C to exit...");
                Console.WriteLine(new string('─', 60));
                Console.ReadLine();

                // ═══════════════════════════════════════════════════════
                // PHASE 1: Search Client IDs
                // ═══════════════════════════════════════════════════════
                using var orchestrator = new Phase1Orchestrator(configuration);
                var result = await orchestrator.RunAsync();

                // Display final result
                if (result.HasErrors)
                {
                    Console.WriteLine("\n⚠️  Phase 1 completed with errors");
                    Environment.ExitCode = 1;
                }
                else if (result.ManualReviewCount > 0)
                {
                    Console.WriteLine("\n⚠️  Phase 1 completed - manual review required");
                    Environment.ExitCode = 0;
                }
                else
                {
                    Console.WriteLine("\n✅ Phase 1 completed successfully!");
                    Environment.ExitCode = 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ FATAL ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Environment.ExitCode = 1;
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Get command line argument value
        /// </summary>
        static string? GetArg(string[] args, string key)
        {
            var index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

    }

}