using System.Drawing;
using System.Drawing.Imaging;
using E2ETest.Core.Model;

namespace E2ETest.Core.Capture;

/// <summary>按裁剪规则截图并存 PNG。录制/回放共用，保证区域一致。</summary>
public static class Screenshotter
{
    private static readonly SemaphoreSlim PngEncoders = new(2, 2);
    /// <summary>只抓取屏幕像素，不做 PNG 编码。调用方负责 Dispose 返回的 Bitmap。</summary>
    public static Bitmap CaptureBitmap(CaptureRule rule)
    {
        var crop = rule.CropRect;
        var bitmap = new Bitmap(crop.Width, crop.Height, PixelFormat.Format24bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(crop.X, crop.Y, 0, 0,
                new Size(crop.Width, crop.Height), CopyPixelOperation.SourceCopy);
            var captureBounds = new Rectangle(crop.X, crop.Y, crop.Width, crop.Height);
            foreach (var mask in rule.MaskRects)
            {
                var intersection = Rectangle.Intersect(captureBounds,
                    new Rectangle(mask.X, mask.Y, mask.Width, mask.Height));
                if (intersection.IsEmpty) continue;
                graphics.FillRectangle(Brushes.Black,
                    intersection.X - crop.X,
                    intersection.Y - crop.Y,
                    intersection.Width,
                    intersection.Height);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public static void SavePng(Bitmap bitmap, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        bitmap.Save(outputPath, ImageFormat.Png);
    }

    public static async Task EncodePngAndDisposeAsync(
        Bitmap bitmap,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        bool entered = false;
        try
        {
            await PngEncoders.WaitAsync(cancellationToken);
            entered = true;
            await Task.Run(() => SavePng(bitmap, outputPath), cancellationToken);
        }
        finally
        {
            bitmap.Dispose();
            if (entered) PngEncoders.Release();
        }
    }

    public static void CaptureToPng(CaptureRule rule, string outputPath)
    {
        using var bitmap = CaptureBitmap(rule);
        SavePng(bitmap, outputPath);
    }
}
