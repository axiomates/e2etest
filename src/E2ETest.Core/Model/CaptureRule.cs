namespace E2ETest.Core.Model;

public sealed class ScreenSize
{
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class Rect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// 截图规则，自描述。录制与回放共用同一份规则裁剪，
/// 任务栏状态如何变化都不影响裁剪后结果。
/// </summary>
public sealed class CaptureRule
{
    public ScreenSize Screen { get; set; } = new();

    /// <summary>全屏应用截整屏且不裁剪；非全屏截整屏后裁掉任务栏。</summary>
    public bool Fullscreen { get; set; }

    /// <summary>实际保存的图像区域（非全屏时 = 全屏减任务栏）。</summary>
    public Rect CropRect { get; set; } = new();

    /// <summary>任务栏矩形（SHAppBarMessage 取得），仅记录供参考。</summary>
    public Rect? TaskbarRect { get; set; }
}
