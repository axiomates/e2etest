using System.Drawing;
using E2ETest.Core.Comparing;
using E2ETest.Core.Model;

namespace E2ETest.Core.Tests;

public sealed class PixelComparerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"e2etest-pixel-{Guid.NewGuid():N}");

    [Fact]
    public void IdenticalImagesPassAndWriteArtifacts()
    {
        string baseline = CreateImage("baseline.png", Color.White);
        string replay = CreateImage("replay.png", Color.White);
        var result = new PixelComparer().Compare(baseline, replay, _dir, 1, new PixelConfig());

        Assert.Equal("passed", result.Status);
        Assert.Equal(0, result.Pixel!.ChangedPixels);
        Assert.True(File.Exists(result.DiffPath));
        Assert.True(File.Exists(result.OverlayPath));
    }

    [Fact]
    public void LargeChangedRegionFails()
    {
        string baseline = CreateImage("baseline.png", Color.White);
        string replay = CreateImage("replay.png", Color.White, graphics => graphics.FillRectangle(Brushes.Black, 0, 0, 8, 8));
        var result = new PixelComparer().Compare(baseline, replay, _dir, 1, new PixelConfig
        {
            MinRegionPixels = 1, FailLargestRegionPixels = 10, FailChangedPixelRatio = 1,
        });

        Assert.Equal("failed", result.Status);
        Assert.Equal(64, result.Pixel!.ChangedPixels);
        Assert.Single(result.Pixel.Regions);
    }

    private string CreateImage(string name, Color color, Action<Graphics>? draw = null)
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, name);
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        draw?.Invoke(graphics);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
