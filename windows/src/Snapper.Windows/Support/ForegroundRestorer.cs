using Snapper.Core.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;
namespace Snapper.Windows.Support;

internal sealed class ForegroundRestorer
{
    private HWND previous;
    public void Remember()
    {
        var hwnd = PInvoke.GetForegroundWindow();
        Span<char> className = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, className);
        previous = SelectionFocusPolicy.ShouldRemember(PInvoke.IsWindow(hwnd), PInvoke.IsWindowVisible(hwnd),
            className[..length].ToString()) ? hwnd : default;
    }
    public void Restore()
    {
        var hwnd = previous;
        Forget();
        if (hwnd != default && PInvoke.IsWindow(hwnd)) PInvoke.SetForegroundWindow(hwnd);
    }
    public void Forget() => previous = default;
}
