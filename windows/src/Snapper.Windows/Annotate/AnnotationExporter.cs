using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;

namespace Snapper.Windows.Annotate;

internal static class AnnotationExporter
{
    public static BitmapSource Flatten(BitmapSource source, AnnotationDocument document)
    {
        Application.Current.Dispatcher.VerifyAccess();
        AnnotationExportPlan plan;
        try { plan = AnnotationExportPlan.Create(document.ImagePixelSize, source.PixelWidth, source.PixelHeight); }
        catch (AnnotationExportException error) when (error.Error == AnnotationExportError.SizeMismatch)
        { Debug.Fail(error.Message); throw; }
        try
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (var dc = visual.RenderOpen())
            {
                dc.PushClip(new RectangleGeometry(ShapeRenderer.Rect(plan.ImageRect)));
                dc.DrawImage(source, ShapeRenderer.Rect(plan.ImageRect));
                ShapeRenderer.Draw(dc, document.Shapes, document.PixelsPerDip, plan.PixelsPerDip);
                dc.Pop();
            }
            var output = new RenderTargetBitmap(plan.PixelWidth, plan.PixelHeight, plan.Dpi, plan.Dpi, PixelFormats.Pbgra32);
            output.Render(visual); output.Freeze(); return output;
        }
        catch (Exception error) { throw new AnnotationExportException(AnnotationExportError.RenderFailed, error); }
    }
}
