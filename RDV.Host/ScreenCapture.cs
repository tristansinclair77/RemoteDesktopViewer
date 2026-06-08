using System.Drawing;
using System.Drawing.Imaging;

namespace RDV.Host;

public sealed class ScreenCapture : IDisposable
{
    private readonly ImageCodecInfo _jpegCodec;
    private readonly EncoderParameters _encoderParams;

    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }

    public ScreenCapture(int jpegQuality = 60)
    {
        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)jpegQuality);
        RefreshDimensions();
    }

    public void RefreshDimensions()
    {
        var b = Screen.PrimaryScreen!.Bounds;
        ScreenWidth = b.Width;
        ScreenHeight = b.Height;
    }

    public byte[] CaptureFrame()
    {
        var bounds = Screen.PrimaryScreen!.Bounds;
        ScreenWidth = bounds.Width;
        ScreenHeight = bounds.Height;

        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);

        using var ms = new MemoryStream();
        bmp.Save(ms, _jpegCodec, _encoderParams);
        return ms.ToArray();
    }

    public void Dispose() => _encoderParams.Dispose();
}
