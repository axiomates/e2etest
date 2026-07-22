namespace E2ETest.Core.Model;

/// <summary>
/// 单条测试样例的完整定义：元数据 + 截图索引 + 事件时间轴。
/// 目录名 = SampleId（稳定 slug），重命名只改 DisplayName。
/// </summary>
public sealed class SampleManifest
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>目录名，fs-safe，重命名不改它。</summary>
    public string SampleId { get; set; } = "";

    /// <summary>自由文本显示名，可含中文，GUI 展示，可改。</summary>
    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>录制总时长（毫秒）。</summary>
    public long DurationMs { get; set; }

    public CaptureRule Capture { get; set; } = new();
    public ReplaySettings Replay { get; set; } = new();

    /// <summary>截图索引，回放按 Index 一一配对。</summary>
    public List<ShotEntry> Shots { get; set; } = new();

    /// <summary>事件时间轴，按 T 升序。</summary>
    public List<InputEvent> Events { get; set; } = new();
}
