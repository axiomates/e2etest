namespace E2ETest.Core.Model;

/// <summary>回放配置（样例级）。</summary>
public sealed class ReplaySettings
{
    /// <summary>回放前执行的重置命令（通常 taskkill 结束被测进程）。</summary>
    public string? ResetCommand { get; set; }

    /// <summary>重置后等待再开始回放的毫秒数。</summary>
    public int ResetWaitMs { get; set; } = 3000;

    /// <summary>时序缩放，1.0 = 真实时序。</summary>
    public double SpeedFactor { get; set; } = 1.0;

    /// <summary>可选的空闲间隔上限；0 表示严格按录制时间轴、不压缩。</summary>
    public int MaxIdleGapMs { get; set; } = 0;

    /// <summary>resetCommand 超时；超时后终止进程树并将当前样例标记失败。</summary>
    public int ResetTimeoutMs { get; set; } = 30000;
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
    /// <summary>"manual" 手动触发 / "final" 结尾自动截图。</summary>
    public string Kind { get; set; } = "manual";
}
