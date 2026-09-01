using Docnet.Core;
using Docnet.Core.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Orchestrator.Phase4.Auditing.DocumentReconciliation;

internal sealed class PdfPageEvidence
{
    public int NativeTextCharacterCount { get; init; }
    public int NativeWordCount { get; init; }
    public bool HasReliableRasterGeometry { get; init; }
    public double LargestRasterCoverageRatio { get; init; }
    public double RasterUnionCoverageRatio { get; init; }
    public bool IsVisuallyBlank { get; init; }
    public int? RotationDegrees { get; init; }
}

internal interface IPdfAuditInspector
{
    PdfInspection Inspect(string path, bool includeConsentEvidence);
}

internal sealed record PdfInspection(int PdfPigPageCount, int DocnetPageCount, IReadOnlyList<PdfPageEvidence> Pages);

internal sealed class PdfConsentInspector : IPdfAuditInspector
{
    public PdfInspection Inspect(string path, bool includeConsentEvidence)
    {
        using var document = PdfDocument.Open(path);
        using var docReader = DocLib.Instance.GetDocReader(path, new PageDimensions(612, 792));
        int docnetPageCount = docReader.GetPageCount();
        var pages = new List<PdfPageEvidence>(document.NumberOfPages);

        for (int pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            Page page = document.GetPage(pageNumber);
            using var pageReader = pageNumber <= docnetPageCount ? docReader.GetPageReader(pageNumber - 1) : null;
            bool visuallyBlank = pageReader is not null && IsVisuallyBlank(pageReader.GetImage(), pageReader.GetPageWidth(), pageReader.GetPageHeight());
            pages.Add(includeConsentEvidence ? CreateEvidence(page, visuallyBlank) : new PdfPageEvidence { IsVisuallyBlank = visuallyBlank });
        }

        return new PdfInspection(document.NumberOfPages, docnetPageCount, pages);
    }

    private static PdfPageEvidence CreateEvidence(Page page, bool visuallyBlank)
    {
        IReadOnlyList<PdfRectangle> imageBounds;
        try { imageBounds = page.GetImages().Select(image => image.BoundingBox).ToArray(); }
        catch { return new PdfPageEvidence { NativeTextCharacterCount = page.Text.Length, NativeWordCount = page.GetWords().Count(), IsVisuallyBlank = visuallyBlank, RotationDegrees = page.Rotation.Value }; }

        PdfRectangle crop = page.CropBox.Bounds;
        var clipped = imageBounds.Select(bounds => Clip(bounds, crop)).Where(rectangle => rectangle.HasValue).Select(rectangle => rectangle!.Value).ToArray();
        double cropArea = Area(crop);
        double largest = cropArea <= 0 ? 0 : clipped.Select(Area).DefaultIfEmpty(0).Max() / cropArea;
        double union = cropArea <= 0 ? 0 : UnionArea(clipped) / cropArea;

        return new PdfPageEvidence
        {
            NativeTextCharacterCount = page.Text.Length,
            NativeWordCount = page.GetWords().Count(),
            HasReliableRasterGeometry = true,
            LargestRasterCoverageRatio = Math.Clamp(largest, 0, 1),
            RasterUnionCoverageRatio = Math.Clamp(union, 0, 1),
            IsVisuallyBlank = visuallyBlank,
            RotationDegrees = page.Rotation.Value
        };
    }

    private static bool IsVisuallyBlank(byte[] bytes, int width, int height)
    {
        if (width <= 0 || height <= 0 || bytes.Length < 4) return false;
        int samples = 0;
        int nonWhite = 0;
        int stride = Math.Max(1, (width * height) / 20_000);
        for (int pixel = 0; pixel < width * height && pixel * 4 + 2 < bytes.Length; pixel += stride)
        {
            int index = pixel * 4;
            samples++;
            if (bytes[index] < 245 || bytes[index + 1] < 245 || bytes[index + 2] < 245) nonWhite++;
        }
        return samples > 0 && nonWhite * 100 < samples;
    }

    private static PdfRectangle? Clip(PdfRectangle source, PdfRectangle crop)
    {
        double left = Math.Max(source.Left, crop.Left);
        double right = Math.Min(source.Right, crop.Right);
        double bottom = Math.Max(source.Bottom, crop.Bottom);
        double top = Math.Min(source.Top, crop.Top);
        return right > left && top > bottom ? new PdfRectangle(left, bottom, right, top) : null;
    }

    private static double Area(PdfRectangle rectangle) => Math.Max(0, rectangle.Right - rectangle.Left) * Math.Max(0, rectangle.Top - rectangle.Bottom);

    private static double UnionArea(IReadOnlyList<PdfRectangle> rectangles)
    {
        var xValues = rectangles.SelectMany(rectangle => new[] { rectangle.Left, rectangle.Right }).Distinct().OrderBy(value => value).ToArray();
        double area = 0;
        for (int index = 0; index < xValues.Length - 1; index++)
        {
            double left = xValues[index];
            double right = xValues[index + 1];
            if (right <= left) continue;
            var intervals = rectangles.Where(rectangle => rectangle.Left < right && rectangle.Right > left).Select(rectangle => (rectangle.Bottom, rectangle.Top)).OrderBy(interval => interval.Bottom).ToArray();
            double covered = 0;
            double currentBottom = 0;
            double currentTop = 0;
            foreach ((double bottom, double top) in intervals)
            {
                if (covered == 0) { currentBottom = bottom; currentTop = top; covered = 1; continue; }
                if (bottom > currentTop) { area += (right - left) * (currentTop - currentBottom); currentBottom = bottom; currentTop = top; }
                else currentTop = Math.Max(currentTop, top);
            }
            if (covered > 0) area += (right - left) * (currentTop - currentBottom);
        }
        return area;
    }
}
