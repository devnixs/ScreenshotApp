using Snapper.Core.Capture;
namespace Snapper.Core.Tests;
public sealed class ScreenshotCaptureExceptionTests
{
    [Theory]
    [InlineData(ScreenshotCaptureErrorKind.NotSupported,"Screen capture isn't available on this PC.")]
    [InlineData(ScreenshotCaptureErrorKind.DisplayUnavailable,"The display is no longer available.")]
    [InlineData(ScreenshotCaptureErrorKind.TimedOut,"The screen didn't respond in time.")]
    [InlineData(ScreenshotCaptureErrorKind.WindowUnavailable,"The window is no longer available.")]
    public void ExactMessages(ScreenshotCaptureErrorKind kind,string message)
    {var e=new ScreenshotCaptureException(kind);Assert.Equal(kind,e.Kind);Assert.Equal(message,e.Message);}
}
