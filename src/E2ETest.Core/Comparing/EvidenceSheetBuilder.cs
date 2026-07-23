using System.Drawing;
using System.Drawing.Imaging;
using E2ETest.Core.Model;

namespace E2ETest.Core.Comparing;

/// <summary>为报告中的每个已导出差异区域生成 baseline/replay/diff/overlay 四宫格。</summary>
public static class EvidenceSheetBuilder
{
    public static void GenerateAll(TestCaseComparisonResult testCase, string outputDir, int maximumDimension)
    {
        if (maximumDimension < 0) throw new InvalidDataException("ai.maxImageDimension 不能小于 0。");
        Directory.CreateDirectory(outputDir);
        foreach (var region in testCase.Shots.SelectMany(shot => shot.Pixel?.Regions ?? []))
        {
            string path = Path.Combine(outputDir, $"evidence-{region.Id}.png");
            Create(region, path, maximumDimension);
            region.AiEvidencePath = path;
        }
    }

    public static void Create(PixelRegion region, string path, int maximumDimension)
    {
        if (maximumDimension < 0) throw new InvalidDataException("ai.maxImageDimension 不能小于 0。");
        using var baseline = new Bitmap(Required(region.BaselineCropPath, region.Id, "baseline"));
        using var replay = new Bitmap(Required(region.ReplayCropPath, region.Id, "replay"));
        using var diff = new Bitmap(Required(region.DiffCropPath, region.Id, "diff"));
        using var overlay = new Bitmap(Required(region.OverlayCropPath, region.Id, "overlay"));
        int budget = maximumDimension == 0 ? 1080 : Math.Max(320, maximumDimension);
        int margin = Math.Max(6, budget / 90);
        using var font = new Font(SystemFonts.DefaultFont.FontFamily, Math.Max(8, budget / 90f));
        int labelHeight = (int)Math.Ceiling(font.GetHeight()) + Math.Max(4, margin / 2) * 2;
        double aspect = baseline.Width / (double)Math.Max(1, baseline.Height);
        int cellWidth = (budget - margin * 3) / 2;
        int pictureHeight = Math.Max(1, (int)Math.Round(cellWidth / aspect));
        int cellHeight = labelHeight + pictureHeight;
        if (cellHeight * 2 + margin * 3 > budget)
        {
            pictureHeight = Math.Max(1, (budget - margin * 3 - labelHeight * 2) / 2);
            cellWidth = Math.Max(1, (int)Math.Round(pictureHeight * aspect));
            cellHeight = labelHeight + pictureHeight;
        }
        int sheetWidth = cellWidth * 2 + margin * 3;
        int sheetHeight = cellHeight * 2 + margin * 3;
        using var sheet = new Bitmap(sheetWidth, sheetHeight);
        using var graphics = Graphics.FromImage(sheet);
        graphics.Clear(Color.White);
        DrawCell(graphics, baseline, "baseline", new Rectangle(margin, margin, cellWidth, cellHeight), font, labelHeight);
        DrawCell(graphics, replay, "replay", new Rectangle(margin * 2 + cellWidth, margin, cellWidth, cellHeight), font, labelHeight);
        DrawCell(graphics, diff, "diff", new Rectangle(margin, margin * 2 + cellHeight, cellWidth, cellHeight), font, labelHeight);
        DrawCell(graphics, overlay, "overlay", new Rectangle(margin * 2 + cellWidth, margin * 2 + cellHeight, cellWidth, cellHeight), font, labelHeight);
        sheet.Save(path, ImageFormat.Png);
    }

    private static void DrawCell(Graphics graphics, Bitmap image, string label, Rectangle target, Font font, int labelHeight)
    {
        var title = new Rectangle(target.X, target.Y, target.Width, labelHeight);
        graphics.FillRectangle(Brushes.White, title);
        var state = graphics.Save();
        graphics.SetClip(title);
        using var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        graphics.DrawString(label, font, Brushes.Black, title, format);
        graphics.Restore(state);
        var picture = new Rectangle(target.X, target.Y + labelHeight, target.Width, target.Height - labelHeight);
        double scale = Math.Min(picture.Width / (double)image.Width, picture.Height / (double)image.Height);
        int width = (int)(image.Width * scale), height = (int)(image.Height * scale);
        graphics.DrawImage(image, picture.X + (picture.Width - width) / 2, picture.Y + (picture.Height - height) / 2, width, height);
        graphics.DrawRectangle(Pens.Gray, picture);
    }

    private static string Required(string? path, string regionId, string kind) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? path
            : throw new FileNotFoundException($"区域 {regionId} 缺少 {kind} 证据图。", path);
}
