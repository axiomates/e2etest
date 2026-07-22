namespace E2ETest.Core.Model;

public sealed class PathsConfig
{
    public string Samples { get; set; } = "./samples";
    public string Replays { get; set; } = "./replays";
    public string Reports { get; set; } = "./reports";
}

/// <summary>录制默认设置（可被 CLI 参数覆盖）。</summary>
public sealed class RecordConfig
{
    /// <summary>
    /// 默认非全屏：截图裁掉任务栏。
    /// 全屏模式下截整屏（用于全屏应用）。
    /// 可被 --fullscreen / --no-fullscreen CLI 参数覆盖。
    /// </summary>
    public bool Fullscreen { get; set; } = false;
}

/// <summary>全局配置，对应 config.json。所有设置明文存盘，内部使用。</summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public AiConfig Ai { get; set; } = new();
    public PixelConfig Pixel { get; set; } = new();
    public HotkeyConfig Hotkeys { get; set; } = new();
    public ThresholdConfig Thresholds { get; set; } = new();
    public PathsConfig Paths { get; set; } = new();
    public RecordConfig Record { get; set; } = new();
}
