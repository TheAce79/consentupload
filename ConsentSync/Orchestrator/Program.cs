// See https://aka.ms/new-console-template for more information
using CsvProcessing;

Console.WriteLine("Hello, World!");


var csvRepo = new StudentCsvRepository();

// Check if already processed
if (!csvRepo.ProcessedCsvExists())
{
    Console.WriteLine("📄 Processing raw CSV...");
    csvRepo.ProcessRawCsv();
}
else
{
    Console.WriteLine("✅ CSV already processed");
}

// Show preview
csvRepo.PreviewProcessedCsv(5);

// Show statistics
csvRepo.DisplayStatistics();