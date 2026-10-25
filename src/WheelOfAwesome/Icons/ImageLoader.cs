using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace WheelOfAwesome.Icons;

/// <summary>Decodes PNG/JPG/BMP/GIF/ICO/SVG into frozen WPF images. Must run on the UI thread (SVG rendering needs it).</summary>
public static class ImageLoader
{
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".svg" };

    public static ImageSource? LoadFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Load(stream, path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    public static ImageSource? Load(Stream stream, bool isSvg)
    {
        if (isSvg) return LoadSvg(stream);

        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        if (LooksLikeSvg(ms)) return LoadSvg(ms);

        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        // .ico files hold several sizes; take the biggest.
        BitmapSource best = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
        best.Freeze();
        return best;
    }

    private static ImageSource? LoadSvg(Stream stream)
    {
        var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true };
        var reader = new FileSvgReader(settings);
        var drawing = reader.Read(stream);
        if (drawing == null) return null;
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private static bool LooksLikeSvg(MemoryStream ms)
    {
        var buf = new byte[Math.Min(256, (int)ms.Length)];
        ms.Read(buf, 0, buf.Length);
        ms.Position = 0;
        var head = System.Text.Encoding.UTF8.GetString(buf).TrimStart('﻿', ' ', '\r', '\n', '\t');
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && head.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Rasterises any image to a square PNG (keeps aspect ratio) so cached icons load fast and look consistent.</summary>
    public static void SavePng(ImageSource source, string path, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double w = source.Width, h = source.Height;
            double scale = Math.Min(size / w, size / h);
            double dw = w * scale, dh = h * scale;
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(source, new Rect((size - dw) / 2, (size - dh) / 2, dw, dh));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) encoder.Save(fs);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Pixel width for bitmaps; vectors count as "large".</summary>
    public static int PixelSize(ImageSource source) => source is BitmapSource b ? Math.Min(b.PixelWidth, b.PixelHeight) : 512;
}
