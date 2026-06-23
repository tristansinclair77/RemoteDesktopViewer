using System.Drawing;
using System.Drawing.Imaging;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RDV.Host.Dxgi;

// DXGI Desktop Duplication based capture. Unlike GDI CopyFromScreen, this handles
// fullscreen-exclusive D3D games (FFXIV) without hanging or returning black frames.
// Falls back to last cached frame on transient errors so the streaming loop never blocks.
public sealed class DxgiScreenCapture : IDisposable
{
    private readonly ImageCodecInfo _jpegCodec;
    private readonly EncoderParameters _encoderParams;
    private readonly object _lock = new();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;

    private int _selectedIndex;
    private Rectangle _selectedBounds;
    private byte[]? _lastFrameData;
    private OutputInfo[] _outputs = Array.Empty<OutputInfo>();
    private Task<CapturedFrame>? _pendingCapture;

    public int SelectedIndex { get { lock (_lock) return _selectedIndex; } }
    public int ScreenWidth { get { lock (_lock) return _selectedBounds.Width; } }
    public int ScreenHeight { get { lock (_lock) return _selectedBounds.Height; } }
    public int ScreenLeft { get { lock (_lock) return _selectedBounds.Left; } }
    public int ScreenTop { get { lock (_lock) return _selectedBounds.Top; } }

    private sealed record OutputInfo(string Name, Rectangle Bounds, bool Primary, int AdapterIndex, int OutputIndexOnAdapter);

    public DxgiScreenCapture(int jpegQuality = 60)
    {
        _jpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)jpegQuality);

        EnumerateOutputs();
        var primary = Array.FindIndex(_outputs, o => o.Primary);
        SetSelectedScreen(primary >= 0 ? primary : 0);
    }

    public static ScreenDescriptor[] EnumerateScreensStatic()
    {
        var list = new List<ScreenDescriptor>();
        var outs = EnumerateOutputsStatic();
        for (int i = 0; i < outs.Length; i++)
        {
            var o = outs[i];
            list.Add(new ScreenDescriptor(i, o.Name, o.Bounds.Width, o.Bounds.Height, o.Primary));
        }
        return list.ToArray();
    }

    private static OutputInfo[] EnumerateOutputsStatic()
    {
        var list = new List<OutputInfo>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (int ai = 0; ; ai++)
        {
            var ar = factory.EnumAdapters1((uint)ai, out IDXGIAdapter1? adapter);
            if (ar.Failure || adapter is null) break;
            using (adapter)
            {
                for (int oi = 0; ; oi++)
                {
                    var or = adapter.EnumOutputs((uint)oi, out IDXGIOutput? output);
                    if (or.Failure || output is null) break;
                    using (output)
                    {
                        var desc = output.Description;
                        var rect = new Rectangle(
                            desc.DesktopCoordinates.Left,
                            desc.DesktopCoordinates.Top,
                            desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left,
                            desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top);
                        bool primary = rect.X == 0 && rect.Y == 0;
                        list.Add(new OutputInfo(desc.DeviceName, rect, primary, ai, oi));
                    }
                }
            }
        }
        return list.ToArray();
    }

    private void EnumerateOutputs() => _outputs = EnumerateOutputsStatic();

    public ScreenDescriptor[] EnumerateScreens()
    {
        var result = new ScreenDescriptor[_outputs.Length];
        for (int i = 0; i < _outputs.Length; i++)
        {
            var o = _outputs[i];
            result[i] = new ScreenDescriptor(i, o.Name, o.Bounds.Width, o.Bounds.Height, o.Primary);
        }
        return result;
    }

    public bool SetSelectedScreen(int index)
    {
        if (index < 0 || index >= _outputs.Length) return false;
        lock (_lock)
        {
            _selectedIndex = index;
            _selectedBounds = _outputs[index].Bounds;
            ResetDuplication();
        }
        return true;
    }

    private void ResetDuplication()
    {
        _duplication?.Dispose(); _duplication = null;
        _staging?.Dispose(); _staging = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
    }

    private void EnsureDuplication()
    {
        if (_duplication != null) return;
        var sel = _outputs[_selectedIndex];

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var r = factory.EnumAdapters1((uint)sel.AdapterIndex, out IDXGIAdapter1? adapter);
        if (r.Failure || adapter is null)
            throw new InvalidOperationException("Adapter enumeration failed.");

        try
        {
            var dr = D3D11.D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                null,
                out ID3D11Device? device);
            if (dr.Failure || device is null)
                throw new InvalidOperationException("D3D11CreateDevice failed: 0x" + dr.Code.ToString("X8"));
            _device = device;
            _context = device.ImmediateContext;

            var or = adapter.EnumOutputs((uint)sel.OutputIndexOnAdapter, out IDXGIOutput? output);
            if (or.Failure || output is null)
                throw new InvalidOperationException("Output enumeration failed.");
            using (output)
            {
                using var output1 = output.QueryInterface<IDXGIOutput1>();
                _duplication = output1.DuplicateOutput(_device);
            }
        }
        finally
        {
            adapter.Dispose();
        }
    }

    public CapturedFrame CaptureFrame()
    {
        Rectangle bounds; int idx;
        lock (_lock) { bounds = _selectedBounds; idx = _selectedIndex; }

        try
        {
            EnsureDuplication();
            return CaptureViaDxgi(bounds, idx);
        }
        catch
        {
            ResetDuplication();
            return new CapturedFrame(_lastFrameData ?? Array.Empty<byte>(), bounds.Width, bounds.Height, idx);
        }
    }

    private CapturedFrame CaptureViaDxgi(Rectangle bounds, int idx)
    {
        var r = _duplication!.AcquireNextFrame(100, out OutduplFrameInfo info, out IDXGIResource? resource);
        if (r == Vortice.DXGI.ResultCode.WaitTimeout)
            return new CapturedFrame(_lastFrameData ?? Array.Empty<byte>(), bounds.Width, bounds.Height, idx);
        if (r == Vortice.DXGI.ResultCode.AccessLost)
        {
            ResetDuplication();
            return new CapturedFrame(_lastFrameData ?? Array.Empty<byte>(), bounds.Width, bounds.Height, idx);
        }
        if (r.Failure || resource is null)
            throw new InvalidOperationException($"AcquireNextFrame failed: 0x{r.Code:X8}");

        try
        {
            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            var desc = texture.Description;

            if (_staging == null || _staging.Description.Width != desc.Width || _staging.Description.Height != desc.Height)
            {
                _staging?.Dispose();
                _staging = _device!.CreateTexture2D(new Texture2DDescription
                {
                    Width = desc.Width,
                    Height = desc.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = desc.Format,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    CPUAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    MiscFlags = ResourceOptionFlags.None
                });
            }

            _context!.CopyResource(_staging, texture);
            var mapped = _context.Map(_staging, 0, Vortice.Direct3D11.MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var data = EncodeJpeg(mapped.DataPointer, (int)mapped.RowPitch, (int)desc.Width, (int)desc.Height);
                _lastFrameData = data;
                return new CapturedFrame(data, (int)desc.Width, (int)desc.Height, idx);
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }
        }
        finally
        {
            resource.Dispose();
            try { _duplication.ReleaseFrame(); } catch { }
        }
    }

    private byte[] EncodeJpeg(IntPtr src, int srcRowPitch, int width, int height)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            int rowBytes = width * 4;
            unsafe
            {
                byte* srcBase = (byte*)src;
                byte* dstBase = (byte*)bd.Scan0;
                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(srcBase + y * srcRowPitch, dstBase + y * bd.Stride, rowBytes, rowBytes);
                }
            }
        }
        finally { bmp.UnlockBits(bd); }

        using var ms = new MemoryStream();
        bmp.Save(ms, _jpegCodec, _encoderParams);
        return ms.ToArray();
    }

    public async Task<CapturedFrame> CaptureFrameAsync(CancellationToken ct)
    {
        if (_pendingCapture != null && !_pendingCapture.IsCompleted)
            return LastFrameFallback();

        _pendingCapture = Task.Run(CaptureFrame, CancellationToken.None);
        try
        {
            return await _pendingCapture.WaitAsync(TimeSpan.FromSeconds(2), ct);
        }
        catch (TimeoutException)
        {
            return LastFrameFallback();
        }
    }

    private CapturedFrame LastFrameFallback()
    {
        byte[] data; Rectangle bounds; int idx;
        lock (_lock) { data = _lastFrameData ?? Array.Empty<byte>(); bounds = _selectedBounds; idx = _selectedIndex; }
        return new CapturedFrame(data, bounds.Width, bounds.Height, idx);
    }

    public void Dispose()
    {
        ResetDuplication();
        _encoderParams.Dispose();
    }
}

public readonly record struct CapturedFrame(byte[] Data, int Width, int Height, int ScreenIndex);
public readonly record struct ScreenDescriptor(int Index, string Name, int Width, int Height, bool Primary);
