namespace Snapper.Core.Capture;
public enum ScreenshotCaptureErrorKind { NotSupported, DisplayUnavailable, TimedOut, WindowUnavailable }
public sealed class ScreenshotCaptureException(ScreenshotCaptureErrorKind kind) : Exception(kind switch
{
    ScreenshotCaptureErrorKind.NotSupported => "Screen capture isn't available on this PC.",
    ScreenshotCaptureErrorKind.DisplayUnavailable => "The display is no longer available.",
    ScreenshotCaptureErrorKind.TimedOut => "The screen didn't respond in time.",
    ScreenshotCaptureErrorKind.WindowUnavailable => "The window is no longer available.",
    _ => throw new ArgumentOutOfRangeException(nameof(kind))
}) { public ScreenshotCaptureErrorKind Kind { get; } = kind; }
