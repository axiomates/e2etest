namespace E2ETest.Core.Model;

public sealed class ReplayShotResult
{
    public int ShotIndex { get; set; }
    public string BaselinePath { get; set; } = "";
    public string? BaselineSha256 { get; set; }
    public string ReplayPath { get; set; } = "";
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class TestCaseReplayResult
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "pending";
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int ScreenshotCount { get; set; }
    public long ElapsedMs { get; set; }
    public List<ReplayShotResult> Shots { get; set; } = new();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
}

public sealed class ReplayRoundResult
{
    public int SchemaVersion { get; set; } = 2;
    public string RoundId { get; set; } = "";
    public string Status { get; set; } = "running";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public string? Error { get; set; }
    public int TotalTestCases { get; set; }
    public int SucceededTestCases { get; set; }
    public int FailedTestCases { get; set; }
    public int CancelledTestCases { get; set; }
    public List<TestCaseReplayResult> TestCases { get; set; } = new();
}
