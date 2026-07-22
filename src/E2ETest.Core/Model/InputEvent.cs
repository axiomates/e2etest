using System.Text.Json.Serialization;

namespace E2ETest.Core.Model;

/// <summary>
/// 时间轴上的一条事件。t = 距录制开始的毫秒。
/// 用 "type" 作为多态判别字段，覆盖鼠标/键盘/滚轮/截图。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MouseMoveEvent), "mouseMove")]
[JsonDerivedType(typeof(MouseButtonEvent), "mouseButton")]
[JsonDerivedType(typeof(MouseWheelEvent), "mouseWheel")]
[JsonDerivedType(typeof(KeyEvent), "key")]
[JsonDerivedType(typeof(ScreenshotEvent), "screenshot")]
public abstract class InputEvent
{
    /// <summary>距录制开始的毫秒数。</summary>
    public long T { get; set; }
}
