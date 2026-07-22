using System.Runtime.InteropServices;

namespace E2ETest.Core.Native;

/// <summary>隐藏控制台并仅在原本可见时恢复，避免 GUI 后台调用结束后弹出窗口。</summary>
public static class ConsoleWindow
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    public readonly record struct VisibilityState(nint Handle, bool WasVisible);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    public static VisibilityState CaptureAndHide()
    {
        nint handle = GetConsoleWindow();
        bool visible = handle != 0 && IsWindowVisible(handle);
        if (visible) ShowWindow(handle, SW_HIDE);
        return new VisibilityState(handle, visible);
    }

    public static void Restore(VisibilityState state)
    {
        if (state.Handle != 0 && state.WasVisible) ShowWindow(state.Handle, SW_SHOW);
    }
}
