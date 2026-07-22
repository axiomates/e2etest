namespace E2ETest.Core.Model;

/// <summary>AI 对比配置。协议为 openai-chat（POST {BaseUrl}/chat/completions）。ApiKey 明文存盘（内部使用）。</summary>
public sealed class AiConfig
{
    /// <summary>OpenAI 兼容端点基址，如 https://host/v1 。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>API Key。内部使用，明文存盘。</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "";

    /// <summary>发给模型前图片的最长边；0 表示不缩放。</summary>
    public int MaxImageDimension { get; set; } = 1080;

    /// <summary>每个 case 最多附带的区域四宫格数，按差异像素数从大到小选择。</summary>
    public int MaxEvidenceRegions { get; set; } = 10;

    public int TimeoutMs { get; set; } = 120000;
}

public sealed class PixelConfig
{
    /// <summary>RGB 任一通道不超过此差值时视为渲染噪声。</summary>
    public int ColorTolerance { get; set; } = 12;

    /// <summary>小于该像素数的差异连通区域视为孤立噪声。</summary>
    public int MinRegionPixels { get; set; } = 9;

    /// <summary>超过该差异比例时本地直接判定失败。</summary>
    public double FailChangedPixelRatio { get; set; } = 0.01;

    /// <summary>单个差异区域超过该像素数时本地直接判定失败。</summary>
    public int FailLargestRegionPixels { get; set; } = 2500;

    /// <summary>导出差异区域证据图时在四周保留的上下文像素。</summary>
    public int RegionPaddingPixels { get; set; } = 32;

    /// <summary>每张截图最多导出的差异区域数量，按差异像素数降序。</summary>
    public int MaxRegions { get; set; } = 20;
}

public sealed class HotkeyConfig
{
    public string StartStop { get; set; } = "F12";
    public string Screenshot { get; set; } = "F11";
}

public sealed class ThresholdConfig
{
    public double Same { get; set; } = 0.85;
    public double Uncertain { get; set; } = 0.4;
}
