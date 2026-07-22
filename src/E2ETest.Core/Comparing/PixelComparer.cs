using System.Drawing;
using System.Drawing.Imaging;
using E2ETest.Core.Model;

namespace E2ETest.Core.Comparing;

public sealed class PixelComparer
{
    public ShotComparisonResult Compare(string baselinePath, string replayPath, string outputDir, int shotIndex, PixelConfig settings)
    {
        Validate(settings);
        using var originalBaseline = new Bitmap(baselinePath);
        using var originalReplay = new Bitmap(replayPath);
        if (originalBaseline.Width != originalReplay.Width || originalBaseline.Height != originalReplay.Height)
            return new ShotComparisonResult
            {
                ShotIndex = shotIndex, Status = "failed", BaselinePath = baselinePath, ReplayPath = replayPath,
                Error = $"截图尺寸不一致: baseline {originalBaseline.Width}x{originalBaseline.Height}, replay {originalReplay.Width}x{originalReplay.Height}。",
            };

        using var baseline = Normalize(originalBaseline);
        using var replay = Normalize(originalReplay);
        int width = baseline.Width, height = baseline.Height, length = width * height;
        var changed = new bool[length];
        var diffs = new byte[length];
        ComparePixels(baseline, replay, settings.ColorTolerance, changed, diffs);
        List<PixelRegion> regions = FindRegions(changed, width, height, settings.MinRegionPixels, out bool[] retained);
        int changedPixels = retained.Count(value => value);
        int largest = regions.Count == 0 ? 0 : regions.Max(region => region.ChangedPixels);
        string status = changedPixels == 0 ? "passed" :
            changedPixels / (double)length >= settings.FailChangedPixelRatio || largest >= settings.FailLargestRegionPixels
                ? "failed" : "uncertain";

        Directory.CreateDirectory(outputDir);
        string diffPath = Path.Combine(outputDir, $"diff-shot-{shotIndex:D4}.png");
        string overlayPath = Path.Combine(outputDir, $"overlay-shot-{shotIndex:D4}.png");
        using var diff = new Bitmap(replay.Width, replay.Height, PixelFormat.Format32bppArgb);
        using var overlay = (Bitmap)replay.Clone();
        RenderArtifacts(replay, retained, diffs, diff, overlay);
        DrawRegionOverlays(overlay, regions);
        diff.Save(diffPath, ImageFormat.Png);
        overlay.Save(overlayPath, ImageFormat.Png);
        ExportRegionArtifacts(baseline, replay, diff, overlay, regions, outputDir, shotIndex, settings);
        return new ShotComparisonResult
        {
            ShotIndex = shotIndex, Status = status, BaselinePath = baselinePath, ReplayPath = replayPath,
            DiffPath = diffPath, OverlayPath = overlayPath,
            Pixel = new PixelComparisonResult
            {
                Width = width, Height = height, ColorTolerance = settings.ColorTolerance,
                ChangedPixels = changedPixels, ChangedRatio = Math.Round(changedPixels / (double)length, 6),
                LargestRegionPixels = largest, Regions = regions.OrderByDescending(region => region.ChangedPixels).Take(settings.MaxRegions).ToList(),
            },
        };
    }

    private static void Validate(PixelConfig s)
    {
        if (s.ColorTolerance is < 0 or > 255 || s.MinRegionPixels < 1 || s.FailLargestRegionPixels < 1 ||
            s.RegionPaddingPixels < 0 || s.MaxRegions < 1 ||
            !double.IsFinite(s.FailChangedPixelRatio) || s.FailChangedPixelRatio is < 0 or > 1)
            throw new InvalidDataException("pixel 配置无效。");
    }

    private static Bitmap Normalize(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImageUnscaled(source, 0, 0);
        return result;
    }

    private static unsafe void ComparePixels(Bitmap left, Bitmap right, int tolerance, bool[] changed, byte[] diffs)
    {
        var rect = new Rectangle(0, 0, left.Width, left.Height);
        var leftData = left.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var rightData = right.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < left.Height; y++)
            {
                byte* a = (byte*)leftData.Scan0 + y * leftData.Stride;
                byte* b = (byte*)rightData.Scan0 + y * rightData.Stride;
                for (int x = 0; x < left.Width; x++)
                {
                    int offset = x * 4, index = y * left.Width + x;
                    int delta = Math.Max(Math.Abs(a[offset] - b[offset]), Math.Max(Math.Abs(a[offset + 1] - b[offset + 1]), Math.Abs(a[offset + 2] - b[offset + 2])));
                    diffs[index] = (byte)delta;
                    changed[index] = delta > tolerance;
                }
            }
        }
        finally { left.UnlockBits(leftData); right.UnlockBits(rightData); }
    }

    private static List<PixelRegion> FindRegions(bool[] changed, int width, int height, int minimumPixels, out bool[] retained)
    {
        var visited = new bool[changed.Length];
        retained = new bool[changed.Length];
        var result = new List<PixelRegion>();
        var queue = new Queue<int>();
        for (int start = 0; start < changed.Length; start++)
        {
            if (!changed[start] || visited[start]) continue;
            visited[start] = true; queue.Enqueue(start);
            var component = new List<int>();
            int count = 0, minX = width, minY = height, maxX = 0, maxY = 0;
            while (queue.Count > 0)
            {
                int index = queue.Dequeue(), x = index % width, y = index / width;
                component.Add(index); count++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int next = ny * width + nx;
                    if (changed[next] && !visited[next]) { visited[next] = true; queue.Enqueue(next); }
                }
            }
            if (count >= minimumPixels)
            {
                foreach (int index in component) retained[index] = true;
                result.Add(new PixelRegion { X = minX, Y = minY, Width = maxX - minX + 1, Height = maxY - minY + 1, ChangedPixels = count });
            }
        }
        return result;
    }

    private static unsafe void RenderArtifacts(Bitmap replay, bool[] retained, byte[] diffs, Bitmap diff, Bitmap overlay)
    {
        var rect = new Rectangle(0, 0, replay.Width, replay.Height);
        var diffData = diff.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var overlayData = overlay.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < replay.Height; y++)
            {
                byte* d = (byte*)diffData.Scan0 + y * diffData.Stride;
                byte* o = (byte*)overlayData.Scan0 + y * overlayData.Stride;
                for (int x = 0; x < replay.Width; x++)
                {
                    int index = y * replay.Width + x, offset = x * 4;
                    if (!retained[index]) { d[offset + 3] = 0; continue; }
                    byte intensity = (byte)Math.Max(96, (int)diffs[index]);
                    d[offset] = intensity; d[offset + 1] = 0; d[offset + 2] = 255; d[offset + 3] = 255;
                    o[offset] = (byte)(o[offset] * 0.55); o[offset + 1] = (byte)(o[offset + 1] * 0.55); o[offset + 2] = 255;
                }
            }
        }
        finally { diff.UnlockBits(diffData); overlay.UnlockBits(overlayData); }
    }

    private static void DrawRegionOverlays(Bitmap overlay, List<PixelRegion> regions)
    {
        Color[] colors =
        [
            Color.FromArgb(96, 255, 66, 66), Color.FromArgb(96, 0, 210, 255),
            Color.FromArgb(96, 255, 202, 40), Color.FromArgb(96, 190, 80, 255),
            Color.FromArgb(96, 50, 220, 120), Color.FromArgb(96, 255, 120, 25),
        ];
        using var graphics = Graphics.FromImage(overlay);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        foreach (var pair in regions.OrderByDescending(item => item.ChangedPixels).Select((region, index) => (region, index)))
        {
            Color fill = colors[pair.index % colors.Length];
            Color border = Color.FromArgb(255, fill.R, fill.G, fill.B);
            var rect = new Rectangle(pair.region.X, pair.region.Y, pair.region.Width, pair.region.Height);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(border, 2);
            graphics.FillRectangle(brush, rect);
            graphics.DrawRectangle(pen, rect);
        }
    }

    private static void ExportRegionArtifacts(
        Bitmap baseline, Bitmap replay, Bitmap diff, Bitmap overlay, List<PixelRegion> regions,
        string outputDir, int shotIndex, PixelConfig settings)
    {
        int number = 0;
        foreach (var region in regions.OrderByDescending(item => item.ChangedPixels).Take(settings.MaxRegions))
        {
            number++;
            var context = Rectangle.FromLTRB(
                Math.Max(0, region.X - settings.RegionPaddingPixels),
                Math.Max(0, region.Y - settings.RegionPaddingPixels),
                Math.Min(baseline.Width, region.X + region.Width + settings.RegionPaddingPixels),
                Math.Min(baseline.Height, region.Y + region.Height + settings.RegionPaddingPixels));
            region.ContextX = context.X; region.ContextY = context.Y;
            region.ContextWidth = context.Width; region.ContextHeight = context.Height;
            string prefix = Path.Combine(outputDir, $"shot-{shotIndex:D4}-region-{number:D3}");
            region.BaselineCropPath = $"{prefix}-baseline.png";
            region.ReplayCropPath = $"{prefix}-replay.png";
            region.DiffCropPath = $"{prefix}-diff.png";
            region.OverlayCropPath = $"{prefix}-overlay.png";
            SaveCrop(baseline, context, region.BaselineCropPath);
            SaveCrop(replay, context, region.ReplayCropPath);
            SaveCrop(diff, context, region.DiffCropPath);
            SaveCrop(overlay, context, region.OverlayCropPath);
        }
    }

    private static void SaveCrop(Bitmap source, Rectangle rect, string path)
    {
        using var crop = source.Clone(rect, PixelFormat.Format32bppArgb);
        crop.Save(path, ImageFormat.Png);
    }
}
