using System.Runtime.InteropServices;

namespace E2ETest.Core.Native;

/// <summary>任务栏/屏幕相关的 Win32 互操作。</summary>
internal static class ShellInterop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    public const uint ABM_GETTASKBARPOS = 0x00000005;

    [DllImport("shell32.dll")]
    public static extern nint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
}
