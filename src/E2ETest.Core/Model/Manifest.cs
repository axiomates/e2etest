namespace E2ETest.Core.Model;

/// <summary>回放配置（样例级）。</summary>
public sealed class ReplaySettings
{
    /// <summary>时序缩放，1.0 = 真实时序。</summary>
    public double SpeedFactor { get; set; } = 1.0;

    /// <summary>可选的空闲间隔上限；0 表示严格按录制时间轴、不压缩。</summary>
    public int MaxIdleGapMs { get; set; } = 0;

}

/// <summary>一张截图的索引条目。</summary>
public sealed class ShotEntry
{
    public int Index { get; set; }
    public string File { get; set; } = "";
    /// <summary>实际固化屏幕像素的时间点，回放使用此时间。</summary>
    public long AtMs { get; set; }
    /// <summary>用户按下截图控制键的原始时间；manual 截图保留用于审计。</summary>
    public long? RequestedAtMs { get; set; }
    /// <summary>固定为 "manual"，表示录制时由用户手动触发。</summary>
    public string Kind { get; set; } = "manual";
}
