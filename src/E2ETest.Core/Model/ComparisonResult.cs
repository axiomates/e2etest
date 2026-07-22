namespace E2ETest.Core.Model;

public sealed class PixelRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int ChangedPixels { get; set; }
    public int ContextX { get; set; }
    public int ContextY { get; set; }
    public int ContextWidth { get; set; }
    public int ContextHeight { get; set; }
    public string? BaselineCropPath { get; set; }
    public string? ReplayCropPath { get; set; }
    public string? DiffCropPath { get; set; }
    public string? OverlayCropPath { get; set; }
}

public sealed class PixelComparisonResult
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int ColorTolerance { get; set; }
    public int ChangedPixels { get; set; }
    public double ChangedRatio { get; set; }
    public int LargestRegionPixels { get; set; }
    public List<PixelRegion> Regions { get; set; } = new();
}

public sealed class ShotComparisonResult
{
    public int ShotIndex { get; set; }
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public string BaselinePath { get; set; } = "";
    public string ReplayPath { get; set; } = "";
    public string? DiffPath { get; set; }
    public string? OverlayPath { get; set; }
    public PixelComparisonResult? Pixel { get; set; }
}

public sealed class TestCaseComparisonResult
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public List<ShotComparisonResult> Shots { get; set; } = new();
}

public sealed class ComparisonRoundResult
{
    public int SchemaVersion { get; set; } = 1;
    public string RoundId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public int PassedTestCases { get; set; }
    public int FailedTestCases { get; set; }
    public int UncertainTestCases { get; set; }
    public int SkippedTestCases { get; set; }
    public List<TestCaseComparisonResult> TestCases { get; set; } = new();
}
