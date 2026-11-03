namespace Snapper.Windows.Capture;
internal sealed class WindowUnavailableException : Exception
{
    public WindowUnavailableException() : base("The window is no longer available.") { }
}
