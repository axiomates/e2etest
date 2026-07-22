namespace E2ETest.Core.Model;

/// <summary>
/// 键盘事件。Down/Up 分离记录，修饰键(Shift/Ctrl/Alt/Win)和任意组合键
/// 均由自然的 down/up 序列还原，无需特殊组合键结构。
/// </summary>
public sealed class KeyEvent : InputEvent
{
    /// <summary>虚拟键码 (VK_*)。</summary>
    public int Vk { get; set; }

    /// <summary>硬件扫描码。部分底层程序只认扫描码，回放用 KEYEVENTF_SCANCODE 打入。</summary>
    public int Scan { get; set; }

    /// <summary>扩展键标志。区分右 Ctrl/右 Alt、方向键、小键盘等，缺失会回放错键。</summary>
    public bool Ext { get; set; }

    /// <summary>true=按下，false=抬起。</summary>
    public bool Down { get; set; }
}

/// <summary>
/// 截图事件。热键触发时写入此事件（而非普通按键），
/// 回放到同一时点触发同样的截图。ShotIndex 对应 manifest.shots 里的条目。
/// </summary>
public sealed class ScreenshotEvent : InputEvent
{
    public int ShotIndex { get; set; }
}
