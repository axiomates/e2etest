using E2ETest.Core.Model;

namespace E2ETest.Core.Capture;

/// <summary>默认以主显示器物理像素构建截图规则；副屏输入在提交 manifest 时过滤。</summary>
public static class CaptureRuleBuilder
{
    public static CaptureRule Build(bool fullscreen)
    {
        var bounds = ScreenGeometry.PrimaryBounds();
        var capture = fullscreen ? bounds : ScreenGeometry.PrimaryWorkingArea();
        return new CaptureRule
        {
            CoordinateSpace = "physical-pixels",
            Screen = new ScreenSize
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                Dpi = (int)E2ETest.Core.Native.ShellInterop.GetDpiForSystem(),
            },
            Fullscreen = fullscreen,
            CropRect = capture,
            TaskbarRect = ScreenGeometry.TaskbarRect(),
            MaskRects = new List<Rect>(),
        };
    }
}
