namespace E2ETest.Core.Model;

/// <summary>单条测试用例：名称、录制时间轴、截图索引和回放设置。</summary>
public sealed class TestCaseManifest
{
    public int SchemaVersion { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public long DurationMs { get; set; }
    public CaptureRule Capture { get; set; } = new();
    public ReplaySettings Replay { get; set; } = new();
    public List<ShotEntry> Shots { get; set; } = new();
    public List<InputEvent> Events { get; set; } = new();
}
