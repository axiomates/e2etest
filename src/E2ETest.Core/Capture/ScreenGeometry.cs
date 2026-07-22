using System.Windows.Forms;
using E2ETest.Core.Model;
using E2ETest.Core.Native;

namespace E2ETest.Core.Capture;

/// <summary>解析默认目标显示器（当前为主屏）及其工作区。</summary>
public static class ScreenGeometry
{
    public static Rect PrimaryBounds()
    {
        var bounds = Screen.PrimaryScreen?.Bounds
            ?? throw new InvalidOperationException("无法获取主显示器。");
        return new Rect { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height };
    }

    public static Rect PrimaryWorkingArea()
    {
        var work = Screen.PrimaryScreen?.WorkingArea
            ?? throw new InvalidOperationException("无法获取主显示器工作区。");
        return new Rect { X = work.X, Y = work.Y, Width = work.Width, Height = work.Height };
    }

    public static Rect? TaskbarRect()
    {
        var data = new ShellInterop.APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ShellInterop.APPBARDATA>(),
        };
        var result = ShellInterop.SHAppBarMessage(ShellInterop.ABM_GETTASKBARPOS, ref data);
        if (result == 0) return null;
        return new Rect
        {
            X = data.rc.Left,
            Y = data.rc.Top,
            Width = data.rc.Right - data.rc.Left,
            Height = data.rc.Bottom - data.rc.Top,
        };
    }
}
