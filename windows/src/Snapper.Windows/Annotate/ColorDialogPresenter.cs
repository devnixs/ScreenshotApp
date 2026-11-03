using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.Dialogs;
namespace Snapper.Windows.Annotate;
internal static class ColorDialogPresenter
{
    private static readonly uint[] customColors = new uint[16];
    public static unsafe RgbaColor? Pick(Window owner, RgbaColor initial)
    {
        fixed (uint* colors = customColors)
        {
            var dialog = new CHOOSECOLORW
            {
                lStructSize = (uint)Marshal.SizeOf<CHOOSECOLORW>(), hwndOwner = new HWND(new WindowInteropHelper(owner).Handle),
                rgbResult = new COLORREF(ColorRefConversion.ToColorRef(initial)), lpCustColors = (COLORREF*)colors,
                Flags = CHOOSECOLOR_FLAGS.CC_RGBINIT | CHOOSECOLOR_FLAGS.CC_FULLOPEN | CHOOSECOLOR_FLAGS.CC_ANYCOLOR
            };
            return PInvoke.ChooseColor(ref dialog) ? ColorRefConversion.FromColorRef(dialog.rgbResult.Value) : null;
        }
    }
}
