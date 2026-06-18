using System.Drawing;
using System.Drawing.Imaging;

namespace RDV.Host;

public sealed class ScreenCapture : IDisposable
{
    private readonly ImageCodecInfo _jpegCodec;
    private readonly EncoderParameters _encoderParams;
    private readonly object _lock = new();
    private int _selectedIndex;
    private Rectangle _selectedBounds;
    private byte[]? _lastFrameData;
    private Task<CapturedFrame>? _pendingCapture;

    public int SelectedIndex { get { lock (_lock) return _selectedIndex; } }
    public int ScreenWidth { get { lock (_lock) return _selectedBounds.Width; } }
    public int ScreenHeight { get { lock (_lock) return _selectedBounds.Height; } }
    public int ScreenLeft { get { lock (_lock) return _selectedBounds.Left; } }
    public int ScreenTop { get { lock (_lock) return _selectedBounds.Top; } }

    public ScreenCapture(int jpegQuality = 60)
    {
        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)jpegQuality);

        var screens = Screen.AllScreens;
        var primary = Array.FindIndex(screens, s => s.Primary);
        SetSelectedScreen(primary >= 0 ? primary : 0);
    }

    public static ScreenDescriptor[] EnumerateScreens()
    {
        var screens = Screen.AllScreens;
        var result = new ScreenDescriptor[screens.Length];
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            result[i] = new ScreenDescriptor(i, s.DeviceName ?? $"Display {i + 1}",
                s.Bounds.Width, s.Bounds.Height, s.Primary);
        }
        return result;
    }

    public bool SetSelectedScreen(int index)
    {
        var screens = Screen.AllScreens;
        if (index < 0 || index >= screens.Length) return false;
        lock (_lock)
        {
            _selectedIndex = index;
            _selectedBounds = screens[index].Bounds;
        }
        return true;
    }

    public CapturedFrame CaptureFrame()
    {
        Rectangle bounds;
        int idx;
        lock (_lock) { bounds = _selectedBounds; idx = _selectedIndex; }

        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);

        using var ms = new MemoryStream();
        bmp.Save(ms, _jpegCodec, _encoderParams);
        return new CapturedFrame(ms.ToArray(), bounds.Width, bounds.Height, idx);
    }

    // Runs CaptureFrame() with a 2-second timeout. If GDI hangs (frozen desktop), returns the
    // last successfully captured frame so the streaming loop never blocks indefinitely.
    public async Task<CapturedFrame> CaptureFrameAsync(CancellationToken ct)
    {
        // If a prior capture is still stuck, don't queue another thread — just return last frame.
        if (_pendingCapture != null && !_pendingCapture.IsCompleted)
            return LastFrameFallback();

        _pendingCapture = Task.Run(CaptureFrame, CancellationToken.None);
        try
        {
            var frame = await _pendingCapture.WaitAsync(TimeSpan.FromSeconds(2), ct);
            lock (_lock) _lastFrameData = frame.Data;
            return frame;
        }
        catch (TimeoutException)
        {
            return LastFrameFallback();
        }
    }

    private CapturedFrame LastFrameFallback()
    {
        byte[] data; Rectangle bounds; int idx;
        lock (_lock) { data = _lastFrameData ?? []; bounds = _selectedBounds; idx = _selectedIndex; }
        return new CapturedFrame(data, bounds.Width, bounds.Height, idx);
    }

    public void Dispose() => _encoderParams.Dispose();
}

public readonly record struct CapturedFrame(byte[] Data, int Width, int Height, int ScreenIndex);
public readonly record struct ScreenDescriptor(int Index, string Name, int Width, int Height, bool Primary);
