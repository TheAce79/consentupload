using ConsentSyncCore.Services.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace ConsentSyncCore.Services.Pdf
{
    public class PdfMergeService
    {
        public class MergeResultPdf
        {
            /// <summary>True when the output file was written successfully.</summary>
            public bool Success { get; set; }

            /// <summary>Full path of the merged output PDF.</summary>
            public string OutputPath { get; set; } = string.Empty;

            /// <summary>Number of pages merged across all input files.</summary>
            public int TotalPagesMerged { get; set; }

            /// <summary>Number of input PDFs that were merged.</summary>
            public int FilesmergedCount { get; set; }

            /// <summary>Input files that could not be read (skipped).</summary>
            public List<string> SkippedFiles { get; set; } = new();

            /// <summary>Human-readable error message when <see cref="Success"/> is false.</summary>
            public string ErrorMessage { get; set; } = string.Empty;
        }

        /// <summary>
        /// Merges every *.pdf found in <paramref name="inputFileDirectory"/> into a
        /// single output file named <paramref name="outputFileName"/> placed in the
        /// same directory.
        /// </summary>
        /// <param name="inputFileDirectory">
        ///   Directory that contains the PDFs to merge.
        ///   Must exist and contain at least one PDF.
        /// </param>
        /// <param name="outputFileName">
        ///   File name (not a full path) for the merged PDF, e.g. "merged.pdf".
        ///   If the file already exists it is deleted and replaced atomically.
        /// </param>
        /// <returns>A <see cref="MergeResultPdf"/> describing the outcome.</returns>
        public MergeResultPdf MergePdf(string inputFileDirectory, string outputFileName)
        {
            var result = new MergeResultPdf();

            try
            {
                // ── Guard: directory must exist ───────────────────────────────
                if (string.IsNullOrWhiteSpace(inputFileDirectory) ||
                    !Directory.Exists(inputFileDirectory))
                {
                    result.ErrorMessage =
                        $"Input directory not found: '{inputFileDirectory}'";
                    LoggerService.LogWarning($"   ❌ MergePdf — {result.ErrorMessage}");
                    return result;
                }

                // ── Guard: output file name must be valid ─────────────────────
                if (string.IsNullOrWhiteSpace(outputFileName))
                {
                    result.ErrorMessage = "OutputFileName cannot be empty.";
                    LoggerService.LogWarning($"   ❌ MergePdf — {result.ErrorMessage}");
                    return result;
                }

                // ── Collect input PDFs (sorted for deterministic page order) ──
                var pdfFiles = Directory
                    .GetFiles(inputFileDirectory, "*.pdf", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (pdfFiles.Count == 0)
                {
                    result.ErrorMessage =
                        $"No PDF files found in '{inputFileDirectory}'.";
                    LoggerService.LogWarning($"   ⚠️  MergePdf — {result.ErrorMessage}");
                    return result;
                }

                string outputPath = Path.Combine(inputFileDirectory, outputFileName);

                // ── Exclude the output file from the inputs (re-run safety) ──
                pdfFiles = pdfFiles
                    .Where(f => !f.Equals(outputPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                LoggerService.LogInformation(
                    $"   📎 Merging {pdfFiles.Count} PDF(s) → {outputFileName}");

                // ── Build merged PDF ──────────────────────────────────────────
                var builder = new PdfDocumentBuilder();
                int totalPages = 0;

                foreach (var pdfPath in pdfFiles)
                {
                    string fileName = Path.GetFileName(pdfPath);

                    // Guard: skip locked or unreadable files
                    try
                    {
                        using var probe = File.Open(
                            pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    }
                    catch (IOException)
                    {
                        LoggerService.LogWarning(
                            $"   ⚠️  Skipping locked file: {fileName}");
                        result.SkippedFiles.Add(fileName);
                        continue;
                    }

                    try
                    {
                        using var srcDoc = PdfDocument.Open(pdfPath);

                        for (int p = 1; p <= srcDoc.NumberOfPages; p++)
                        {
                            builder.AddPage(srcDoc, p);
                            totalPages++;
                        }

                        result.FilesmergedCount++;
                        LoggerService.LogInformation(
                            $"      ✅ [{result.FilesmergedCount}/{pdfFiles.Count}] " +
                            $"{fileName}  ({srcDoc.NumberOfPages} page(s))");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogWarning(
                            $"   ⚠️  Could not read '{fileName}': {ex.Message} — skipping.");
                        result.SkippedFiles.Add(fileName);
                    }
                }

                if (result.FilesmergedCount == 0)
                {
                    result.ErrorMessage =
                        "No PDFs could be read — all files were skipped.";
                    LoggerService.LogWarning($"   ❌ MergePdf — {result.ErrorMessage}");
                    return result;
                }

                // ── Write to a .tmp file first, then atomic-swap ──────────────
                // Prevents a corrupt output if the process is interrupted mid-write.
                string tmpPath = outputPath + ".tmp";

                byte[] pdfBytes = builder.Build();
                File.WriteAllBytes(tmpPath, pdfBytes);

                // Replace (overwrite) the final output atomically
                File.Move(tmpPath, outputPath, overwrite: true);

                result.Success = true;
                result.OutputPath = outputPath;
                result.TotalPagesMerged = totalPages;

                LoggerService.LogInformation(
                    $"   ✅ Merge complete — {result.FilesmergedCount} file(s), " +
                    $"{totalPages} page(s) → {outputFileName}");

                if (result.SkippedFiles.Count > 0)
                {
                    LoggerService.LogWarning(
                        $"   ⚠️  {result.SkippedFiles.Count} file(s) skipped: " +
                        string.Join(", ", result.SkippedFiles));
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Unexpected error during merge: {ex.Message}";
                LoggerService.LogWarning($"   ❌ MergePdf — {result.ErrorMessage}");
                LoggerService.LogWarning($"      Stack: {ex.StackTrace}");
            }

            return result;
        }


    }


}

