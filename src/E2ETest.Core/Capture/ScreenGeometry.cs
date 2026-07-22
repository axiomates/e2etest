using E2ETest.Core.Model;
using E2ETest.Core.Native;

namespace E2ETest.Core.Capture;

/// <summary>解析屏幕尺寸与任务栏矩形，生成截图裁剪规则。</summary>
public static class ScreenGeometry
{
    public static (int Width, int Height) PrimaryScreenSize()
    {
        int w = ShellInterop.GetSystemMetrics(ShellInterop.SM_CXSCREEN);
        int h = ShellInterop.GetSystemMetrics(ShellInterop.SM_CYSCREEN);
        return (w, h);
    }

    /// <summary>取任务栏矩形；取不到返回 null。</summary>
    public static Rect? TaskbarRect()
    {
        var data = new ShellInterop.APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ShellInterop.APPBARDATA>(),
        };
        var r = ShellInterop.SHAppBarMessage(ShellInterop.ABM_GETTASKBARPOS, ref data);
        if (r == 0) return null;
        return new Rect
        {
            X = data.rc.Left,
            Y = data.rc.Top,
            Width = data.rc.Right - data.rc.Left,
            Height = data.rc.Bottom - data.rc.Top,
        };
    }
}
