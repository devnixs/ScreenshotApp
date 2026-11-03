using System.Runtime.InteropServices;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
namespace Snapper.Windows.Recording;

/// <summary>Native-resolution copies bypass Direct2D. Scaling clears opaque bars and uses the shared device lock.</summary>
internal sealed class LetterboxCompositor(Direct3D11Helper.DeviceLease device) : IDisposable
{
    private ID2D1Factory1? factory;
    private ID2D1Device? d2dDevice;
    private ID2D1DeviceContext? context;
    public unsafe void Compose(ID3D11Texture2D source, ID3D11Texture2D target, PixelRect crop, LetterboxPlacement placement)
    {
        lock (device.ContextLock)
        {
            if (placement.IsIdentity)
            {
                var box = new D3D11_BOX { left = (uint)crop.Left, top = (uint)crop.Top, right = (uint)crop.Right, bottom = (uint)crop.Bottom, back = 1 };
                device.Context.CopySubresourceRegion(target, 0, 0, 0, 0, source, 0, &box);
                return;
            }
            var dc = GetContext();
            var properties = new D2D1_BITMAP_PROPERTIES1_unmanaged
            {
                pixelFormat = new() { format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_IGNORE },
                dpiX = 96, dpiY = 96,
                bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_CANNOT_DRAW
            };
            dc.CreateBitmapFromDxgiSurface((IDXGISurface)target, &properties, out var targetBitmap);
            ID2D1Bitmap1? sourceBitmap = null;
            try
            {
                properties.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
                dc.CreateBitmapFromDxgiSurface((IDXGISurface)source, &properties, out sourceBitmap);
                dc.SetTarget(targetBitmap);
                dc.BeginDraw();
                var black = new D2D1_COLOR_F { a = 1 };
                dc.Clear(&black);
                var destination = Rect(placement.Destination); var sourceRect = Rect(crop);
                dc.DrawBitmap(sourceBitmap, &destination, 1, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC, &sourceRect, null);
                dc.EndDraw(null, null).ThrowOnFailure();
            }
            finally { dc.SetTarget(null); Release(sourceBitmap); Release(targetBitmap); }
        }
    }
    private ID2D1DeviceContext GetContext()
    {
        if (context is not null) return context;
        try
        {
            PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_MULTI_THREADED, typeof(ID2D1Factory1).GUID, null, out var created).ThrowOnFailure();
            factory = (ID2D1Factory1)created;
            factory.CreateDevice((IDXGIDevice)device.Device, out var nativeDevice); d2dDevice = nativeDevice;
            nativeDevice.CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS.D2D1_DEVICE_CONTEXT_OPTIONS_NONE, out var dc); context = dc;
            dc.SetDpi(96, 96);
            return dc;
        }
        catch { Dispose(); throw; }
    }
    public static unsafe ID3D11Texture2D CreateOutputTexture(Direct3D11Helper.DeviceLease device, int width, int height)
    {
        var description = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, SampleDesc = new() { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE
        };
        ID3D11Texture2D_unmanaged* pointer = null;
        device.Device.CreateTexture2D(&description, null, &pointer);
        try { return (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)pointer); }
        finally { Marshal.Release((nint)pointer); }
    }
    private static D2D_RECT_F Rect(PixelRect value) => new() { left = value.Left, top = value.Top, right = value.Right, bottom = value.Bottom };
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose() { lock (device.ContextLock) { Release(context); context = null; Release(d2dDevice); d2dDevice = null; Release(factory); factory = null; } }
}
