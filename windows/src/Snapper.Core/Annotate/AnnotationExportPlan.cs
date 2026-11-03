using Snapper.Core.Geometry;
using Snapper.Core.Annotate.Model;

namespace Snapper.Core.Annotate;

public enum AnnotationExportError { SizeMismatch, EmptyImage, RenderFailed }
public sealed class AnnotationExportException(AnnotationExportError error, Exception? inner = null)
    : Exception(error == AnnotationExportError.SizeMismatch
        ? "The annotation dimensions don't match the captured image." : "The annotated image couldn't be rendered.", inner)
{
    public AnnotationExportError Error { get; } = error;
}

/// <summary>Render units are image pixels, independent of any canvas or monitor transform.</summary>
public sealed record AnnotationExportPlan(int PixelWidth, int PixelHeight)
{
    public double Dpi => 96;
    public double PixelsPerDip => 1;
    public RectD ImageRect => new(0, 0, PixelWidth, PixelHeight);
    public bool DrawSelectionChrome => false;
    public static bool CanMarkClean(AnnotationDocument current, AnnotationDocument exported, bool isEditingText) =>
        !current.IsEditing && !isEditingText && current.Shapes.SequenceEqual(exported.Shapes);
    public static AnnotationExportPlan Create(PixelSize documentImageSize, int sourcePixelWidth, int sourcePixelHeight)
    {
        if (sourcePixelWidth <= 0 || sourcePixelHeight <= 0 || documentImageSize.Width <= 0 || documentImageSize.Height <= 0)
            throw new AnnotationExportException(AnnotationExportError.EmptyImage);
        if (documentImageSize != new PixelSize(sourcePixelWidth, sourcePixelHeight))
            throw new AnnotationExportException(AnnotationExportError.SizeMismatch);
        return new(sourcePixelWidth, sourcePixelHeight);
    }
}
