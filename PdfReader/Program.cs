// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Configuration;
using System.Text;

Console.WriteLine("Hello, World!");


// Register encoding provider for legacy encodings (Windows-1252, etc.)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

Console.WriteLine("Starting Validation Workflow...");

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

// Create and run validator
var validator = new CsvValidator(configuration);
validator.ProcessAndValidate();

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();



//// Program.cs
//var processor = new PdfProcessor();
//string sourceDir = processor.GetConfigSourceDir();

//Console.WriteLine($"Scanning Source: {sourceDir}");
//Console.WriteLine("=====================================");
//Console.WriteLine("DEBUG MODE: Analyzing PDF structure");
//Console.WriteLine("=====================================\n");

//// Create a debug folder for all debug output
//string debugDir = Path.Combine(sourceDir, "_debug");
//if (!Directory.Exists(debugDir))
//{
//    Directory.CreateDirectory(debugDir);
//}

//// Enable debug mode to see OCR output and rotated images
//var pdfData = PdfProcessor.GetDirectoryPdfInfo(sourceDir, debugOcr: true, debugOutputDir: debugDir);

//// Convert to a JSON string so PowerShell can consume it as an object
//string jsonOutput = System.Text.Json.JsonSerializer.Serialize(pdfData);

//// Create the full path to save the JSON file in the source directory
//string jsonFilePath = Path.Combine(sourceDir, "pdf_mapping.json");

//// Delete existing file if it exists
//if (File.Exists(jsonFilePath))
//{
//    File.Delete(jsonFilePath);
//    Console.WriteLine($"Existing mapping file deleted: {jsonFilePath}");
//}

//// Write the JSON to the source directory
//File.WriteAllText(jsonFilePath, jsonOutput);

//Console.WriteLine($"\nMapping file created successfully: {jsonFilePath}");
//Console.WriteLine($"Debug files saved to: {debugDir}");
//Console.WriteLine("\nExtracted Data:");
//Console.WriteLine(jsonOutput);

//Console.ReadLine();