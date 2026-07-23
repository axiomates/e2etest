namespace E2ETest.Core.Model;

public sealed class PixelRegion
{
    public string Id { get; set; } = "";
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
    /// <summary>报告查看器默认显示的四宫格证据图；启用 AI 时可能从中选择并发送。</summary>
    public string? AiEvidencePath { get; set; }
    public AiAssessment Ai { get; set; } = new();
}

public sealed class PixelComparisonResult
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int ColorTolerance { get; set; }
    public int ChangedPixels { get; set; }
    public bool ExactPixelMatch { get; set; }
    public double ChangedRatio { get; set; }
    public int LargestRegionPixels { get; set; }
    /// <summary>合并后实际检测到的区域总数，可能大于 Regions 中导出的数量。</summary>
    public int DetectedRegionCount { get; set; }
    public List<PixelRegion> Regions { get; set; } = new();
}

public sealed class ShotComparisonResult
{
    public int ShotIndex { get; set; }
    public int Ordinal { get; set; }
    public string Role { get; set; } = "intermediate";
    public long? AtMs { get; set; }
    public string Status { get; set; } = "pending";
    public string FinalVerdict { get; set; } = "pending";
    public string? HardFailureCode { get; set; }
    public string? Error { get; set; }
    public string BaselinePath { get; set; } = "";
    public string ReplayPath { get; set; } = "";
    public string? DiffPath { get; set; }
    public string? OverlayPath { get; set; }
    public PixelComparisonResult? Pixel { get; set; }
    public AiAssessment Ai { get; set; } = new();
}

public sealed class TestCaseComparisonResult
{
    public string Name { get; set; } = "";
    public string? TestFocus { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string Status { get; set; } = "pending";
    public string FinalVerdict { get; set; } = "pending";
    /// <summary>对比过程被取消；已完成的本地判定仍保留在 Status/FinalVerdict。</summary>
    public bool ComparisonCancelled { get; set; }
    public int TotalShots { get; set; }
    public long? DurationMs { get; set; }
    public string? Error { get; set; }
    public List<ShotComparisonResult> Shots { get; set; } = new();
    public List<ComparisonIncident> Incidents { get; set; } = new();
    public int AttentionScore { get; set; }
    public string AttentionLevel { get; set; } = "P3";
    public AiAssessment Ai { get; set; } = new();
}

public sealed class AiAssessment
{
    public string Status { get; set; } = "not_requested";
    public string? Verdict { get; set; }
    public double? Confidence { get; set; }
    /// <summary>AI 对可见界面的客观描述，先于结论输出。</summary>
    public string? Observation { get; set; }
    public string? Reason { get; set; }
}

public sealed class ComparisonIncident
{
    public string Id { get; set; } = "";
    public string LocalVerdict { get; set; } = "uncertain";
    public string FinalVerdict { get; set; } = "uncertain";
    public int ChangedPixels { get; set; }
    public int AttentionScore { get; set; }
    public string AttentionLevel { get; set; } = "P3";
    public List<string> AttentionReasons { get; set; } = new();
    public List<int> ShotIndexes { get; set; } = new();
    public List<string> RegionIds { get; set; } = new();
    public AiAssessment Ai { get; set; } = new();
}

public sealed class ComparisonRoundResult
{
    public int SchemaVersion { get; set; } = 2;
    public string RoundId { get; set; } = "";
    public string ReplayStatus { get; set; } = "unknown";
    public string? ReplayError { get; set; }
    public bool ReplayLifecycleSucceeded { get; set; }
    public bool ComparisonCancelled { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public int PassedTestCases { get; set; }
    public int FailedTestCases { get; set; }
    public int UncertainTestCases { get; set; }
    public int SkippedTestCases { get; set; }
    public int CancelledTestCases { get; set; }
    public int FinalPassedTestCases { get; set; }
    public int FinalFailedTestCases { get; set; }
    public int FinalNeedsReviewTestCases { get; set; }
    public List<TestCaseComparisonResult> TestCases { get; set; } = new();
}
