using System.Runtime.InteropServices;
using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.WinRT.Direct3D11;
using WinRT;

namespace Snapper.Windows.Interop;

/// <summary>One lazy device generation. Leases keep in-flight captures alive across device resets.</summary>
internal sealed class Direct3D11Helper : IDisposable
{
    private readonly object gate = new();
    private DeviceResources? resources;
    private bool disposed;
    public DeviceLease Acquire()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            resources ??= new DeviceResources();
            resources.AddReference();
            return new DeviceLease(resources);
        }
    }
    public void Reset()
    {
        lock (gate) { resources?.ReleaseReference(); resources = null; }
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; Reset(); } }

    internal sealed class DeviceLease : IDisposable
    {
        private DeviceResources? resources;
        internal DeviceLease(DeviceResources resources) => this.resources = resources;
        private DeviceResources Resources => resources ?? throw new ObjectDisposedException(nameof(DeviceLease));
        public IDirect3DDevice WinRtDevice => Resources.WinRtDevice;
        public ID3D11Device Device => Resources.Device;
        public ID3D11DeviceContext Context => Resources.Context;
        // Returned native textures are owned by the caller (Marshal.ReleaseComObject).
        public ID3D11Texture2D GetTexture(IDirect3DSurface surface) => Resources.GetTexture(surface);
        public ID3D11Texture2D CreateStagingTexture(int width, int height) => Resources.CreateStagingTexture(width, height);
        public CapturedImage CopySurface(IDirect3DSurface surface, PixelRect crop, double scale) => Resources.CopySurface(surface, crop, scale);
        public void Dispose() => Interlocked.Exchange(ref resources, null)?.ReleaseReference();
    }

    internal sealed class DeviceResources
    {
        private int references = 1; // The helper owns one reference until Reset/Dispose.
        private readonly object contextGate = new();
        private readonly ID3D11Device device;
        private readonly ID3D11DeviceContext context;
        public IDirect3DDevice WinRtDevice { get; }
        public ID3D11Device Device => device;
        public ID3D11DeviceContext Context => context;
        public unsafe DeviceResources()
        {
            var result = PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT, default, PInvoke.D3D11_SDK_VERSION,
                out var createdDevice, out var createdContext);
            if (result.Failed)
            {
                Release(createdContext); Release(createdDevice);
                PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, default,
                    D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT, default, PInvoke.D3D11_SDK_VERSION,
                    out createdDevice, out createdContext).ThrowOnFailure();
            }
            device = createdDevice; context = createdContext;
            try
            {
                ((ID3D11Multithread)context).SetMultithreadProtected(true);
                PInvoke.CreateDirect3D11DeviceFromDXGIDevice((IDXGIDevice)device, out var inspectable).ThrowOnFailure();
                try
                {
                    var pointer = Marshal.GetIUnknownForObject(inspectable);
                    try { WinRtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
                    finally { Marshal.Release(pointer); }
                }
                finally { Release(inspectable); }
            }
            catch { Release(context); Release(device); throw; }
        }
        public void AddReference() => Interlocked.Increment(ref references);
        public void ReleaseReference()
        {
            if (Interlocked.Decrement(ref references) != 0) return;
            WinRtDevice.Dispose(); Release(context); Release(device);
        }
        private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

        public unsafe ID3D11Texture2D GetTexture(IDirect3DSurface surface)
        {
            var access = surface.As<IDirect3DDxgiInterfaceAccess>();
            try
            {
                var iid = typeof(ID3D11Texture2D).GUID;
                access.GetInterface(&iid, out var pointer);
                try { return (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)pointer); }
                finally { Marshal.Release((nint)pointer); }
            }
            finally { Release(access); }
        }
        public unsafe ID3D11Texture2D CreateStagingTexture(int width, int height)
        {
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 }, Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
                CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ
            };
            ID3D11Texture2D_unmanaged* pointer = null;
            device.CreateTexture2D(&description, null, &pointer);
            try { return (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)pointer); }
            finally { Marshal.Release((nint)pointer); }
        }
        public unsafe CapturedImage CopySurface(IDirect3DSurface surface, PixelRect crop, double scale)
        {
            lock (contextGate)
            {
                ID3D11Texture2D? texture = null;
                ID3D11Texture2D? staging = null;
                try
                {
                    texture = GetTexture(surface);
                    staging = CreateStagingTexture(crop.Width, crop.Height);
                    var box = new D3D11_BOX { left = (uint)crop.Left, top = (uint)crop.Top,
                        right = (uint)crop.Right, bottom = (uint)crop.Bottom, front = 0, back = 1 };
                    context.CopySubresourceRegion(staging, 0, 0, 0, 0, texture, 0, &box);
                    D3D11_MAPPED_SUBRESOURCE mapped;
                    context.Map(staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
                    try
                    {
                        var bytes = new byte[checked(crop.Width * crop.Height * 4)];
                        var length = checked((crop.Height - 1) * (int)mapped.RowPitch + crop.Width * 4);
                        CaptureCrop.CopyRows(new ReadOnlySpan<byte>(mapped.pData, length), (int)mapped.RowPitch,
                            crop.Width, crop.Height, bytes);
                        return new CapturedImage(crop.Width, crop.Height, bytes, scale);
                    }
                    finally { context.Unmap(staging, 0); }
                }
                finally { Release(staging); Release(texture); }
            }
        }
    }
}
