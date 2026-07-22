using E2ETest.Core.Model;

namespace E2ETest.Core.Capture;

/// <summary>根据当前屏幕与任务栏构建裁剪规则。录制与回放调用同一逻辑保证一致。</summary>
public static class CaptureRuleBuilder
{
    public static CaptureRule Build(bool fullscreen)
    {
        var (sw, sh) = ScreenGeometry.PrimaryScreenSize();
        var taskbar = ScreenGeometry.TaskbarRect();

        var rule = new CaptureRule
        {
            Screen = new ScreenSize { Width = sw, Height = sh },
            Fullscreen = fullscreen,
            TaskbarRect = taskbar,
        };

        if (fullscreen || taskbar is null)
        {
            rule.CropRect = new Rect { X = 0, Y = 0, Width = sw, Height = sh };
            return rule;
        }

        rule.CropRect = ScreenMinusTaskbar(sw, sh, taskbar);
        return rule;
    }

    /// <summary>整屏减去任务栏所占的那一条边（任务栏在上/下/左/右均可）。</summary>
    private static Rect ScreenMinusTaskbar(int sw, int sh, Rect tb)
    {
        bool horizontal = tb.Width >= tb.Height; // 顶/底
        if (horizontal)
        {
            bool atTop = tb.Y <= 0;
            int y = atTop ? tb.Height : 0;
            return new Rect { X = 0, Y = y, Width = sw, Height = sh - tb.Height };
        }
        else
        {
            bool atLeft = tb.X <= 0;
            int x = atLeft ? tb.Width : 0;
            return new Rect { X = x, Y = 0, Width = sw - tb.Width, Height = sh };
        }
    }
}
