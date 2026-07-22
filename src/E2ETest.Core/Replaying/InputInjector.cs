using System.ComponentModel;
using System.Runtime.InteropServices;
using E2ETest.Core.Model;
using E2ETest.Core.Native;
using static E2ETest.Core.Native.SendInputInterop;

namespace E2ETest.Core.Replaying;

/// <summary>把录制的事件通过 SendInput 注入回系统。坐标为绝对屏幕像素。</summary>
public sealed class InputInjector
{
    private readonly int _screenW;
    private readonly int _screenH;

    public InputInjector()
    {
        _screenW = GetSystemMetrics(0); // SM_CXSCREEN
        _screenH = GetSystemMetrics(1); // SM_CYSCREEN
    }

    private (int nx, int ny) Normalize(int x, int y)
    {
        int nx = (int)((x * 65535.0) / Math.Max(1, _screenW - 1));
        int ny = (int)((y * 65535.0) / Math.Max(1, _screenH - 1));
        return (nx, ny);
    }

    private static void Send(INPUT input)
    {
        if (SendInput(1, ref input, Marshal.SizeOf<INPUT>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput 注入失败");
    }

    public void MoveMouse(int x, int y)
    {
        var (nx, ny) = Normalize(x, y);
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION { mi = new MOUSEINPUT { dx = nx, dy = ny, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE } },
        });
    }

    public void MouseButton(MouseButton button, bool down, int x, int y)
    {
        MoveMouse(x, y);
        SendMouseButton(button, down);
    }

    /// <summary>异常清理专用：不依赖额外鼠标移动，直接发送按钮抬起。</summary>
    public void ReleaseMouseButton(MouseButton button) => SendMouseButton(button, down: false);

    private static void SendMouseButton(MouseButton button, bool down)
    {
        var (flags, data) = ButtonFlags(button, down);
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION { mi = new MOUSEINPUT { mouseData = data, dwFlags = flags } },
        });
    }

    public void Wheel(int x, int y, int delta, bool horizontal)
    {
        MoveMouse(x, y);
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION { mi = new MOUSEINPUT { mouseData = unchecked((uint)delta), dwFlags = horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL } },
        });
    }

    public void Key(int vk, int scan, bool ext, bool down)
    {
        uint flags = scan != 0 ? KEYEVENTF_SCANCODE : 0;
        if (ext) flags |= KEYEVENTF_EXTENDEDKEY;
        if (!down) flags |= KEYEVENTF_KEYUP;
        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = scan == 0 ? (ushort)vk : (ushort)0,
                    wScan = (ushort)scan,
                    dwFlags = flags,
                },
            },
        });
    }

    private static (uint flags, uint data) ButtonFlags(MouseButton b, bool down) => b switch
    {
        E2ETest.Core.Model.MouseButton.Left => (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0u),
        E2ETest.Core.Model.MouseButton.Right => (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0u),
        E2ETest.Core.Model.MouseButton.Middle => (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0u),
        E2ETest.Core.Model.MouseButton.XButton1 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON1),
        E2ETest.Core.Model.MouseButton.XButton2 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON2),
        _ => (0u, 0u),
    };
}
