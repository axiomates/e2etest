namespace E2ETest.Core.Model;

public sealed class ReplayShotResult
{
    public int ShotIndex { get; set; }
    public string BaselinePath { get; set; } = "";
    public string ReplayPath { get; set; } = "";
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class SampleReplayResult
{
    public string SampleId { get; set; } = "";
    public string DisplayName { get; set; } = "";
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
    public int SchemaVersion { get; set; } = 1;
    public string RoundId { get; set; } = "";
    public string Status { get; set; } = "running";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public int TotalSamples { get; set; }
    public int SucceededSamples { get; set; }
    public int FailedSamples { get; set; }
    public int CancelledSamples { get; set; }
    public List<SampleReplayResult> Samples { get; set; } = new();
}
