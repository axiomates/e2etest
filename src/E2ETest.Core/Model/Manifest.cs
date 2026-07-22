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

    /// <summary>回放时把超长空闲间隔钳到此上限（毫秒）。</summary>
    public int MaxIdleGapMs { get; set; } = 2000;
}

/// <summary>一张截图的索引条目。</summary>
public sealed class ShotEntry
{
    public int Index { get; set; }
    public string File { get; set; } = "";
    public long AtMs { get; set; }
    /// <summary>"manual" 手动触发 / "final" 结尾自动截图。</summary>
    public string Kind { get; set; } = "manual";
}
