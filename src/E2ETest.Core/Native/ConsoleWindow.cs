using System.Runtime.InteropServices;

namespace E2ETest.Core.Native;

/// <summary>控制台窗口显隐控制。record 期间隐藏自身控制台，避免进入截图。</summary>
public static class ConsoleWindow
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    public static void Hide()
    {
        var h = GetConsoleWindow();
        if (h != 0) ShowWindow(h, SW_HIDE);
    }

    public static void Show()
    {
        var h = GetConsoleWindow();
        if (h != 0) ShowWindow(h, SW_SHOW);
    }
}
