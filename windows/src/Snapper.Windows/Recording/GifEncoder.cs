using System.Runtime.InteropServices;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using static Snapper.Windows.Interop.MediaFoundationRuntime;
namespace Snapper.Windows.Recording;

/// <summary>Two-pass offline MTA encoder. Only one decoded bitmap and one WIC output frame are retained.</summary>
internal static class GifEncoder
{
    private const uint Video = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
    public static Task EncodeAsync(string input, string output, GifConversionPlan plan, CancellationToken token) =>
        Task.Run(() => Encode(input, output, plan, token), token);
    private sealed record Timing(IReadOnlyList<GifFrame> Frames, PixelSize RecordedSize);

    private static void Encode(string input, string output, GifConversionPlan plan, CancellationToken token)
    {
        WriterStarted();
        try
        {
            var timing = ReadTiming(input, plan.Settings.FramesPerSecond, token);
            if (timing.Frames.Count == 0) throw new InvalidOperationException("The recording has no video frames.");
            EncodeFrames(input, output, plan, timing, token);
        }
        finally { WriterEnded(); }
    }
    private static unsafe Timing ReadTiming(string input, int fps, CancellationToken token)
    {
        IMFSourceReader? reader = null;
        IMFMediaType? type = null;
        try
        {
            PInvoke.MFCreateSourceReaderFromURL(input, null, out reader).ThrowOnFailure();
            SelectVideo(reader);
            reader.GetCurrentMediaType(Video, out type);
            type.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out var size);
            var recorded = new PixelSize(checked((int)(size >> 32)), checked((int)(size & uint.MaxValue)));
            long duration = 0;
            PROPVARIANT value = default;
            try
            {
                var key = PInvoke.MF_PD_DURATION;
                reader.GetPresentationAttribute(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_MEDIASOURCE), &key, out value);
                if (value.vt == VARENUM.VT_UI8 && value.uhVal <= long.MaxValue) duration = (long)value.uhVal;
            }
            catch (COMException) { } // Missing duration: the last frame still gets one sampling interval.
            finally { _ = PInvoke.PropVariantClear(ref value); }
            var times = new List<long>();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                reader.ReadSample(Video, 0, out _, out var flags, out _, out var sample);
                try
                {
                    CheckFlags(flags);
                    if (sample is not null) { sample.GetSampleTime(out var time); times.Add(time); }
                    if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                }
                finally { Release(sample); }
            }
            return new(GifFrameSampler.Plan(times, duration, fps), recorded);
        }
        finally { Release(type); Release(reader); }
    }
    private static void SelectVideo(IMFSourceReader reader)
    {
        reader.SetStreamSelection(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS), false);
        reader.SetStreamSelection(Video, true);
    }
    private static void CheckFlags(uint flags)
    {
        if ((flags & (uint)(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR | MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED)) != 0)
            throw new InvalidOperationException("The recording couldn't be decoded.");
    }
    private static unsafe void EncodeFrames(string input, string output, GifConversionPlan plan, Timing timing, CancellationToken token)
    {
        IMFSourceReader? reader = null;
        IMFAttributes? attributes = null;
        IMFMediaType? requested = null, type = null;
        IWICImagingFactory? factory = null;
        IWICStream? stream = null;
        IWICBitmapEncoder? encoder = null;
        IWICMetadataQueryWriter? metadata = null;
        DecodedBitmap? last = null;
        IMFSample? lastDecodedSample = null;
        try
        {
            PInvoke.MFCreateAttributes(out attributes, 2).ThrowOnFailure();
            attributes.SetUINT32(PInvoke.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            attributes.SetUINT32(PInvoke.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
            PInvoke.MFCreateSourceReaderFromURL(input, attributes, out reader).ThrowOnFailure();
            SelectVideo(reader);
            PInvoke.MFCreateMediaType(out requested).ThrowOnFailure();
            requested.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
            requested.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_RGB32);
            reader.SetCurrentMediaType(Video, requested);
            reader.GetCurrentMediaType(Video, out type);
            type.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out var size);
            var width = checked((int)(size >> 32)); var height = checked((int)(size & uint.MaxValue));
            var crop = GifFrameCrop.CropFor(width, height, Aperture(type), timing.RecordedSize.Width, timing.RecordedSize.Height);
            var stride = -checked(width * 4);
            try { type.GetUINT32(PInvoke.MF_MT_DEFAULT_STRIDE, out var raw); stride = unchecked((int)raw); }
            catch (COMException) { } // RGB32's default uncompressed orientation is bottom-up.
            PInvoke.CoCreateInstance(PInvoke.CLSID_WICImagingFactory, null, CLSCTX.CLSCTX_INPROC_SERVER, out factory).ThrowOnFailure();
            if (factory is null) throw new InvalidOperationException("WIC imaging factory is unavailable.");
            factory.CreateStream(out stream);
            fixed (char* filename = output) stream.InitializeFromFilename(new PCWSTR(filename), 0x40000000); // GENERIC_WRITE, WinNT.h.
            var container = PInvoke.GUID_ContainerFormatGif;
            encoder = factory.CreateEncoder(&container, null);
            encoder.Initialize(stream, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache);
            encoder.GetMetadataQueryWriter(out metadata);
            // Canonical WIC metadata query names: learn.microsoft.com/windows/win32/wic/-wic-native-image-format-metadata-queries
            PropVariantHelper.SetBytes(metadata, "/appext/Application", GifMetadata.NetscapeApplicationId);
            PropVariantHelper.SetBytes(metadata, "/appext/Data", GifMetadata.LoopForeverData());
            PropVariantHelper.SetUInt16(metadata, "/logscrdesc/Width", checked((ushort)plan.Width));
            PropVariantHelper.SetUInt16(metadata, "/logscrdesc/Height", checked((ushort)plan.Height));
            var cursor = new GifFrameCursor(timing.Frames);
            long? lastDecodedTime = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                reader.ReadSample(Video, 0, out _, out var flags, out _, out var sample);
                try
                {
                    CheckFlags(flags);
                    if (sample is not null)
                    {
                        sample.GetSampleTime(out var time);
                        if (lastDecodedTime is { } previous && time < previous) continue;
                        lastDecodedTime = time;
                        var frames = cursor.Take(time);
                        if (frames.Count > 0)
                        {
                            last?.Dispose(); last = null;
                            last = Decode(factory, sample, width, height, stride, crop);
                            foreach (var frame in frames) WriteFrame(factory, encoder, last.Bitmap, plan, frame, token);
                        }
                        // Keep one decoder sample for an end-of-stream fallback without copying every skipped frame.
                        Release(lastDecodedSample); lastDecodedSample = sample; sample = null;
                    }
                    if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                }
                finally { Release(sample); }
            }
            var remaining = cursor.Remaining();
            if (remaining.Count > 0 && lastDecodedSample is not null)
            {
                last?.Dispose(); last = null;
                last = Decode(factory, lastDecodedSample, width, height, stride, crop);
            }
            if (last is null) throw new InvalidOperationException("The recording has no video frames.");
            foreach (var frame in remaining) WriteFrame(factory, encoder, last.Bitmap, plan, frame, token);
            token.ThrowIfCancellationRequested();
            encoder.Commit();
        }
        finally
        {
            Release(lastDecodedSample); last?.Dispose(); Release(metadata); Release(encoder); Release(stream); Release(factory);
            Release(type); Release(requested); Release(reader); Release(attributes);
        }
    }
    private static unsafe PixelRect? Aperture(IMFMediaType type)
    {
        try
        {
            var key = PInvoke.MF_MT_MINIMUM_DISPLAY_APERTURE;
            var data = new byte[sizeof(MFVideoArea)];
            uint copied = 0;
            type.GetBlob(&key, data, (uint)data.Length, &copied);
            if (copied < data.Length) return null;
            fixed (byte* pointer = data)
            {
                var area = *(MFVideoArea*)pointer;
                return new(area.OffsetX.value, area.OffsetY.value, area.Area.cx, area.Area.cy);
            }
        }
        catch (COMException) { return null; }
    }
    private sealed class DecodedBitmap(IWICBitmap bitmap, GCHandle pin) : IDisposable
    {
        public IWICBitmap Bitmap { get; } = bitmap;
        public void Dispose() { Release(Bitmap); if (pin.IsAllocated) pin.Free(); }
    }
    private static unsafe DecodedBitmap Decode(IWICImagingFactory factory, IMFSample sample, int width, int height, int stride, PixelRect crop)
    {
        sample.ConvertToContiguousBuffer(out var buffer);
        try
        {
            byte[] pixels;
            if (buffer is IMF2DBuffer twoD)
            {
                byte* scanline = null;
                twoD.Lock2D(&scanline, out var pitch);
                try
                {
                    var lowest = pitch < 0 ? scanline + (long)pitch * (height - 1) : scanline;
                    var length = checked((int)(Math.Abs((long)pitch) * (height - 1) + width * 4L));
                    var first = pitch < 0 ? checked((int)(Math.Abs((long)pitch) * (height - 1))) : 0;
                    pixels = GifFrameRows.Copy(new ReadOnlySpan<byte>(lowest, length), first, pitch, crop);
                }
                finally { twoD.Unlock2D(); }
            }
            else
            {
                buffer.Lock(out var pointer, out _, out var length);
                try
                {
                    var first = stride < 0 ? checked((int)(-(long)stride * (height - 1))) : 0;
                    pixels = GifFrameRows.Copy(new ReadOnlySpan<byte>(pointer, checked((int)length)), first, stride, crop);
                }
                finally { buffer.Unlock(); }
            }
            var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                var format = PInvoke.GUID_WICPixelFormat32bppBGR;
                factory.CreateBitmapFromMemory((uint)crop.Width, (uint)crop.Height, &format, checked((uint)crop.Width * 4), (uint)pixels.Length, pixels, out var bitmap);
                return new(bitmap, pin);
            }
            catch { pin.Free(); throw; }
        }
        finally { Release(buffer); }
    }
    private static unsafe void WriteFrame(IWICImagingFactory factory, IWICBitmapEncoder encoder, IWICBitmap bitmap,
        GifConversionPlan plan, GifFrame frame, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IWICBitmapScaler? scaler = null;
        IWICPalette? palette = null;
        IWICFormatConverter? converter = null;
        IWICBitmapFrameEncode? output = null;
        IPropertyBag2? options = null;
        IWICMetadataQueryWriter? metadata = null;
        try
        {
            bitmap.GetSize(out var width, out var height);
            IWICBitmapSource source = bitmap;
            if (width != plan.Width || height != plan.Height)
            {
                factory.CreateBitmapScaler(out scaler);
                scaler.Initialize(source, (uint)plan.Width, (uint)plan.Height, WICBitmapInterpolationMode.WICBitmapInterpolationModeFant);
                source = scaler;
            }
            factory.CreatePalette(out palette);
            palette.InitializeFromBitmap(source, 256, false);
            factory.CreateFormatConverter(out converter);
            var indexed = PInvoke.GUID_WICPixelFormat8bppIndexed;
            converter.Initialize(source, &indexed, WICBitmapDitherType.WICBitmapDitherTypeNone, palette, 0, WICBitmapPaletteType.WICBitmapPaletteTypeCustom);
            encoder.CreateNewFrame(out output, ref options);
            output.Initialize(options);
            output.SetSize((uint)plan.Width, (uint)plan.Height);
            output.SetPixelFormat(&indexed);
            if (indexed != PInvoke.GUID_WICPixelFormat8bppIndexed) throw new InvalidOperationException("The GIF encoder rejected indexed color.");
            output.SetPalette(palette);
            output.GetMetadataQueryWriter(out metadata);
            PropVariantHelper.SetUInt16(metadata, "/grctlext/Delay", checked((ushort)frame.DelayCentiseconds));
            PropVariantHelper.SetByte(metadata, "/grctlext/Disposal", checked((byte)GifMetadata.DisposalDoNotDispose));
            output.WriteSource(converter, null);
            output.Commit();
        }
        finally { Release(metadata); Release(output); Release(options); Release(converter); Release(palette); Release(scaler); }
    }
}
