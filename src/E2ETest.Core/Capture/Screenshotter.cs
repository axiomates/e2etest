using System.Drawing;
using System.Drawing.Imaging;
using E2ETest.Core.Model;

namespace E2ETest.Core.Capture;

/// <summary>按裁剪规则截图并存 PNG。录制/回放共用，保证区域一致。</summary>
public static class Screenshotter
{
    /// <summary>
    /// 按 rule.CropRect 截取屏幕区域，保存为 PNG。
    /// 非全屏时 CropRect 已排除任务栏，故任务栏状态变化不影响结果。
    /// </summary>
    public static void CaptureToPng(CaptureRule rule, string outputPath)
    {
        var crop = rule.CropRect;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var bmp = new Bitmap(crop.Width, crop.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(crop.X, crop.Y, 0, 0,
                new Size(crop.Width, crop.Height), CopyPixelOperation.SourceCopy);
        }
        bmp.Save(outputPath, ImageFormat.Png);
    }
}
