namespace E2ETest.Core.Model;

public enum MouseButton { Left, Right, Middle, XButton1, XButton2 }

/// <summary>鼠标移动（节流采样后）。坐标为绝对屏幕像素。</summary>
public sealed class MouseMoveEvent : InputEvent
{
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>鼠标按键。Down=true 为按下，false 为抬起。拖拽/长按靠 down/up 分离还原。</summary>
public sealed class MouseButtonEvent : InputEvent
{
    public MouseButton Button { get; set; }
    public bool Down { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>滚轮。Delta 为 WHEEL_DELTA 倍数（±120 一格）。Horizontal 区分横向滚动。</summary>
public sealed class MouseWheelEvent : InputEvent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Delta { get; set; }
    public bool Horizontal { get; set; }
}
