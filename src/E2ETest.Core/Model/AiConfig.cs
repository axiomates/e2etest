namespace E2ETest.Core.Model;

/// <summary>AI 对比配置。协议为 openai-chat（POST {BaseUrl}/chat/completions）。ApiKey 明文存盘（内部使用）。</summary>
public sealed class AiConfig
{
    /// <summary>OpenAI 兼容端点基址，如 https://host/v1 。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>API Key。内部使用，明文存盘。</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "";

    /// <summary>被测软件、业务语义及允许变化的项目背景；会作为固定审查规则的补充发送给 AI。</summary>
    public string ContextPrompt { get; set; } = "";

    /// <summary>是否发送 enable_thinking；null 表示不发送该非标准 OpenAI 兼容字段。</summary>
    public bool? EnableThinking { get; set; }

    /// <summary>发给模型前图片的最长边；0 表示不缩放。</summary>
    public int MaxImageDimension { get; set; } = 1080;

    /// <summary>每个 case 最多附带的区域四宫格数，按差异像素数从大到小选择。</summary>
    public int MaxEvidenceRegions { get; set; } = 10;

    /// <summary>OpenAI Chat Completions 请求的最大输出 token 数。</summary>
    public int MaxOutputTokens { get; set; } = 12000;

    /// <summary>一次 AI 复核最多请求次数；仅瞬时故障重试。</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>首次重试前等待时间，后续按指数退避，最大 10 秒。</summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>整个 testcase AI 复核（含重试）的总超时。</summary>
    public int TimeoutMs { get; set; } = 300000;
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
