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
    public DeviceLease Acquire(bool videoSupport = false)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (resources is not null && videoSupport && !resources.VideoSupport) { resources.ReleaseReference(); resources = null; }
            resources ??= new DeviceResources(videoSupport);
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
        public object ContextLock => Resources.ContextLock;
        // Returned native textures are owned by the caller (Marshal.ReleaseComObject).
        public ID3D11Texture2D GetTexture(IDirect3DSurface surface) => Resources.GetTexture(surface);
        public ID3D11Texture2D CreateStagingTexture(int width, int height) => Resources.CreateStagingTexture(width, height);
        public CapturedImage CopySurface(IDirect3DSurface surface, PixelRect crop, double scale) => Resources.CopySurface(surface, crop, scale);
        public void Dispose() => Interlocked.Exchange(ref resources, null)?.ReleaseReference();
    }

    internal sealed class DeviceResources
    {
        private int references = 1; // The helper owns one reference until Reset/Dispose.
        public object ContextLock { get; } = new();
        public bool VideoSupport { get; }
        private readonly ID3D11Device device;
        private readonly ID3D11DeviceContext context;
        // WGC invokes frame callbacks in the MTA. Calling an STA-created RCW there asks COM
        // to marshal D3D interfaces, which do not support that projection (E_NOINTERFACE).
        // Keep independently owned native interfaces for these thread-safe D3D calls.
        private readonly nint nativeDevice;
        private readonly nint nativeContext;
        public IDirect3DDevice WinRtDevice { get; }
        public ID3D11Device Device => device;
        public ID3D11DeviceContext Context => context;
        public unsafe DeviceResources(bool videoSupport)
        {
            VideoSupport = videoSupport;
            var flags = D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | (videoSupport ? D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT : 0);
            ID3D11Device? createdDevice = null;
            ID3D11DeviceContext? createdContext = null;
            try
            {
                var result = PInvoke.D3D11CreateDevice(pAdapter: null, DriverType: D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                    Software: default, Flags: flags, pFeatureLevels: default, SDKVersion: PInvoke.D3D11_SDK_VERSION,
                    ppDevice: out createdDevice, ppImmediateContext: out createdContext);
                if (result.Failed)
                {
                    Release(createdContext); createdContext = null;
                    Release(createdDevice); createdDevice = null;
                    PInvoke.D3D11CreateDevice(pAdapter: null, DriverType: D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
                        Software: default, Flags: flags, pFeatureLevels: default, SDKVersion: PInvoke.D3D11_SDK_VERSION,
                        ppDevice: out createdDevice, ppImmediateContext: out createdContext).ThrowOnFailure();
                }
                device = createdDevice; context = createdContext;
                nativeDevice = NativeInterface<ID3D11Device_unmanaged>(device);
                nativeContext = NativeInterface<ID3D11DeviceContext_unmanaged>(context);
                ((ID3D11Multithread)context).SetMultithreadProtected(true);
                if (videoSupport) ((global::Windows.Win32.Graphics.Direct3D10.ID3D10Multithread)device).SetMultithreadProtected(true);
                global::Windows.Win32.System.WinRT.IInspectable? inspectable = null;
                try
                {
                    PInvoke.CreateDirect3D11DeviceFromDXGIDevice((IDXGIDevice)device, out inspectable).ThrowOnFailure();
                    var pointer = Marshal.GetIUnknownForObject(inspectable);
                    try { WinRtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
                    finally { Marshal.Release(pointer); }
                }
                finally { Release(inspectable); }
            }
            catch
            {
                if (nativeContext != 0) Marshal.Release(nativeContext);
                if (nativeDevice != 0) Marshal.Release(nativeDevice);
                Release(createdContext); Release(createdDevice);
                throw;
            }
        }
        public void AddReference() => Interlocked.Increment(ref references);
        public void ReleaseReference()
        {
            if (Interlocked.Decrement(ref references) != 0) return;
            try { WinRtDevice.Dispose(); }
            finally
            {
                Marshal.Release(nativeContext); Marshal.Release(nativeDevice);
                Release(context); Release(device);
            }
        }
        private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        private static nint NativeInterface<T>(object value) where T : unmanaged
        {
            var unknown = Marshal.GetIUnknownForObject(value);
            try
            {
                var iid = typeof(T).GUID;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var pointer));
                return pointer; // The caller owns the QueryInterface reference.
            }
            finally { Marshal.Release(unknown); }
        }

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
            ((ID3D11Device_unmanaged*)nativeDevice)->CreateTexture2D(&description, null, out pointer);
            try { return (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)pointer); }
            finally { Marshal.Release((nint)pointer); }
        }
        public unsafe CapturedImage CopySurface(IDirect3DSurface surface, PixelRect crop, double scale)
        {
            lock (ContextLock)
            {
                ID3D11Texture2D? texture = null;
                ID3D11Texture2D? staging = null;
                nint sourcePointer = 0, stagingPointer = 0;
                try
                {
                    texture = GetTexture(surface);
                    staging = CreateStagingTexture(crop.Width, crop.Height);
                    sourcePointer = NativeInterface<ID3D11Resource_unmanaged>(texture);
                    stagingPointer = NativeInterface<ID3D11Resource_unmanaged>(staging);
                    var box = new D3D11_BOX { left = (uint)crop.Left, top = (uint)crop.Top,
                        right = (uint)crop.Right, bottom = (uint)crop.Bottom, front = 0, back = 1 };
                    var immediateContext = (ID3D11DeviceContext_unmanaged*)nativeContext;
                    var stagingResource = (ID3D11Resource_unmanaged*)stagingPointer;
                    immediateContext->CopySubresourceRegion(stagingResource, 0, 0, 0, 0,
                        (ID3D11Resource_unmanaged*)sourcePointer, 0, &box);
                    D3D11_MAPPED_SUBRESOURCE mapped;
                    immediateContext->Map(stagingResource, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
                    try
                    {
                        var bytes = new byte[checked(crop.Width * crop.Height * 4)];
                        var length = checked((crop.Height - 1) * (int)mapped.RowPitch + crop.Width * 4);
                        CaptureCrop.CopyRows(new ReadOnlySpan<byte>(mapped.pData, length), (int)mapped.RowPitch,
                            crop.Width, crop.Height, bytes);
                        return new CapturedImage(crop.Width, crop.Height, bytes, scale);
                    }
                    finally { immediateContext->Unmap(stagingResource, 0); }
                }
                finally
                {
                    if (stagingPointer != 0) Marshal.Release(stagingPointer);
                    if (sourcePointer != 0) Marshal.Release(sourcePointer);
                    Release(staging); Release(texture);
                }
            }
        }
    }
}
