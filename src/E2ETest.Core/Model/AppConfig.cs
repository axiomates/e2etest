namespace E2ETest.Core.Model;

public sealed class PathsConfig
{
    public string TestCases { get; set; } = "./testcases";
    public string Replays { get; set; } = "./replays";
    public string Reports { get; set; } = "./reports";
}

/// <summary>录制默认设置（可被 CLI 参数覆盖）。</summary>
public sealed class RecordConfig
{
    public bool Fullscreen { get; set; } = false;
}

public sealed class LoggingConfig
{
    public string Directory { get; set; } = "./logs";
    public string MinimumLevel { get; set; } = "Information";
    public int RetainedFileCount { get; set; } = 14;
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
    public LoggingConfig Logging { get; set; } = new();
}
