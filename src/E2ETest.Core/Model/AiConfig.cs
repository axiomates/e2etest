namespace E2ETest.Core.Model;

/// <summary>AI 对比配置。协议为 openai-chat（POST {BaseUrl}/chat/completions）。ApiKey 明文存盘（内部使用）。</summary>
public sealed class AiConfig
{
    /// <summary>OpenAI 兼容端点基址，如 https://host/v1 。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>API Key。内部使用，明文存盘。</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "";

    /// <summary>发给模型前的图片缩放系数(0,1]，保持宽高比。</summary>
    public double ImageScaleFactor { get; set; } = 0.5;
}

public sealed class PixelConfig
{
    /// <summary>本地像素对比的缩放系数(0,1]。</summary>
    public double ScaleFactor { get; set; } = 0.5;
}

public sealed class HotkeyConfig
{
    public string StartStop { get; set; } = "Ctrl+Alt+R";
    public string Screenshot { get; set; } = "Ctrl+Alt+S";
}

public sealed class ThresholdConfig
{
    public double Same { get; set; } = 0.85;
    public double Uncertain { get; set; } = 0.4;
}
