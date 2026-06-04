using ConsentSyncCore.Services.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace ConsentSyncCore.Services.Pdf
{
    public class PdfSplitService
    {
        public class SplitResultPdf
        {
            /// <summary>True when at least one split PDF was created successfully.</summary>
            public bool Success { get; set; }

            /// <summary>Source PDF that was processed.</summary>
            public string InputPath { get; set; } = string.Empty;

            /// <summary>Directory where split PDFs were written.</summary>
            public string OutputDirectory { get; set; } = string.Empty;

            /// <summary>Total pages found in the source PDF.</summary>
            public int TotalPagesRead { get; set; }

            /// <summary>Number of split PDF files successfully created.</summary>
            public int FilesCreated { get; set; }

            /// <summary>Full output paths for created files.</summary>
            public List<string> CreatedFiles { get; set; } = new();

            /// <summary>Output file names skipped because they already existed.</summary>
            public List<string> SkippedFiles { get; set; } = new();

            /// <summary>Human-readable error message when <see cref="Success"/> is false.</summary>
            public string ErrorMessage { get; set; } = string.Empty;
        }

        /// <summary>
        /// Splits one PDF into smaller PDF files using a fixed number of pages per output file.
        /// </summary>
        public SplitResultPdf SplitPdfByPages(
            string inputPdfPath,
            string outputDirectory,
            int pagesPerFile = 1,
            int startPage = 1,
            string? filePrefix = null,
            bool overwriteExisting = false)
        {
            var result = new SplitResultPdf
            {
                InputPath = inputPdfPath,
                OutputDirectory = outputDirectory
            };

            try
            {
                if (string.IsNullOrWhiteSpace(inputPdfPath) || !File.Exists(inputPdfPath))
                {
                    result.ErrorMessage = $"Input PDF not found: '{inputPdfPath}'";
                    LoggerService.LogWarning($"   ❌ SplitPdfByPages — {result.ErrorMessage}");
                    return result;
                }

                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    result.ErrorMessage = "Output directory cannot be empty.";
                    LoggerService.LogWarning($"   ❌ SplitPdfByPages — {result.ErrorMessage}");
                    return result;
                }

                if (pagesPerFile < 1)
                {
                    result.ErrorMessage = "Pages per file must be at least 1.";
                    LoggerService.LogWarning($"   ❌ SplitPdfByPages — {result.ErrorMessage}");
                    return result;
                }

                if (startPage < 1)
                {
                    result.ErrorMessage = "Start page must be at least 1.";
                    LoggerService.LogWarning($"   ❌ SplitPdfByPages — {result.ErrorMessage}");
                    return result;
                }

                Directory.CreateDirectory(outputDirectory);

                using var pdfDocument = PdfDocument.Open(inputPdfPath);
                int totalPages = pdfDocument.NumberOfPages;
                result.TotalPagesRead = totalPages;

                if (totalPages == 0)
                {
                    result.ErrorMessage = "The selected PDF has no pages.";
                    LoggerService.LogWarning($"   ❌ SplitPdfByPages — {result.ErrorMessage}");
                    return result;
                }

                if (startPage > totalPages)
                {
                    result.ErrorMessage =
                        $"Start page {startPage} is beyond the end of the PDF ({totalPages} pages).";
                    LoggerService.LogWarning($"   ❌ SplitPdfByPages — {result.ErrorMessage}");
                    return result;
                }

                string prefix = MakeSafeFileName(
                    string.IsNullOrWhiteSpace(filePrefix)
                        ? Path.GetFileNameWithoutExtension(inputPdfPath)
                        : filePrefix);

                LoggerService.LogInformation(
                    $"   ✂ Splitting '{Path.GetFileName(inputPdfPath)}' into chunks of {pagesPerFile} page(s)");

                for (int currentPage = startPage; currentPage <= totalPages; currentPage += pagesPerFile)
                {
                    int endPage = Math.Min(currentPage + pagesPerFile - 1, totalPages);
                    string pageRange = currentPage == endPage
                        ? $"p{currentPage:D3}"
                        : $"p{currentPage:D3}-{endPage:D3}";
                    string outputFileName = $"{prefix}_{pageRange}.pdf";
                    string outputPath = Path.Combine(outputDirectory, outputFileName);

                    if (File.Exists(outputPath) && !overwriteExisting)
                    {
                        LoggerService.LogWarning(
                            $"   ⚠️  Split output already exists, skipping: {outputFileName}");
                        result.SkippedFiles.Add(outputFileName);
                        continue;
                    }

                    string tmpPath = outputPath + ".tmp";

                    try
                    {
                        var builder = new PdfDocumentBuilder();
                        for (int pageNum = currentPage; pageNum <= endPage; pageNum++)
                        {
                            builder.AddPage(pdfDocument, pageNum);
                        }

                        byte[] pdfBytes = builder.Build();
                        File.WriteAllBytes(tmpPath, pdfBytes);
                        File.Move(tmpPath, outputPath, overwriteExisting);

                        result.FilesCreated++;
                        result.CreatedFiles.Add(outputPath);

                        LoggerService.LogInformation(
                            $"      ✅ [{result.FilesCreated}] {outputFileName}  (pages {currentPage}-{endPage})");
                    }
                    catch (Exception ex)
                    {
                        TryDeleteTempFile(tmpPath);
                        LoggerService.LogWarning(
                            $"   ⚠️  Could not create '{outputFileName}': {ex.Message}");
                    }
                }

                if (result.FilesCreated == 0)
                {
                    result.ErrorMessage = result.SkippedFiles.Count > 0
                        ? "No files were created because all output files already exist."
                        : "No split PDF files could be created.";
                    LoggerService.LogWarning($"   ❌ SplitPdfByPages — {result.ErrorMessage}");
                    return result;
                }

                result.Success = true;

                LoggerService.LogInformation(
                    $"   ✅ Split complete — {result.FilesCreated} file(s) created from {totalPages} page(s)");

                if (result.SkippedFiles.Count > 0)
                {
                    LoggerService.LogWarning(
                        $"   ⚠️  {result.SkippedFiles.Count} existing output file(s) skipped: " +
                        string.Join(", ", result.SkippedFiles));
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Unexpected error during split: {ex.Message}";
                LoggerService.LogWarning($"   ❌ SplitPdfByPages — {result.ErrorMessage}");
                LoggerService.LogWarning($"      Stack: {ex.StackTrace}");
            }

            return result;
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? "SplitPdf" : name.Trim();
        }

        private static void TryDeleteTempFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
