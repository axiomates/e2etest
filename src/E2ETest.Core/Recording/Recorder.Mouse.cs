using System.Runtime.InteropServices;
using E2ETest.Core.Model;
using E2ETest.Core.Native;

namespace E2ETest.Core.Recording;

partial class Recorder
{
    private nint MouseHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var s = Marshal.PtrToStructure<HookInterop.MSLLHOOKSTRUCT>(lParam);
            long t = ElapsedMs;
            int x = s.pt.x, y = s.pt.y;
            int msg = (int)wParam;

            switch (msg)
            {
                case HookInterop.WM_MOUSEMOVE:
                    if (t - _lastMoveAt >= _moveThrottleMs || Math.Abs(x - _lastMoveX) + Math.Abs(y - _lastMoveY) > 4)
                    {
                        _events.Add(new MouseMoveEvent { T = t, X = x, Y = y });
                        _lastMoveAt = t; _lastMoveX = x; _lastMoveY = y;
                    }
                    break;

                case HookInterop.WM_LBUTTONDOWN: case HookInterop.WM_LBUTTONUP:
                    _events.Add(new MouseButtonEvent { T = t, Button = MouseButton.Left,
                        Down = msg == HookInterop.WM_LBUTTONDOWN, X = x, Y = y }); break;

                case HookInterop.WM_RBUTTONDOWN: case HookInterop.WM_RBUTTONUP:
                    _events.Add(new MouseButtonEvent { T = t, Button = MouseButton.Right,
                        Down = msg == HookInterop.WM_RBUTTONDOWN, X = x, Y = y }); break;

                case HookInterop.WM_MBUTTONDOWN: case HookInterop.WM_MBUTTONUP:
                    _events.Add(new MouseButtonEvent { T = t, Button = MouseButton.Middle,
                        Down = msg == HookInterop.WM_MBUTTONDOWN, X = x, Y = y }); break;

                case HookInterop.WM_XBUTTONDOWN: case HookInterop.WM_XBUTTONUP:
                    var xBtn = (s.mouseData >> 16) == 1 ? MouseButton.XButton1 : MouseButton.XButton2;
                    _events.Add(new MouseButtonEvent { T = t, Button = xBtn,
                        Down = msg == HookInterop.WM_XBUTTONDOWN, X = x, Y = y }); break;

                case HookInterop.WM_MOUSEWHEEL:
                    int delta = unchecked((short)((s.mouseData >> 16) & 0xFFFF));
                    _events.Add(new MouseWheelEvent { T = t, X = x, Y = y, Delta = delta, Horizontal = false }); break;

                case HookInterop.WM_MOUSEHWHEEL:
                    int hdelta = unchecked((short)((s.mouseData >> 16) & 0xFFFF));
                    _events.Add(new MouseWheelEvent { T = t, X = x, Y = y, Delta = hdelta, Horizontal = true }); break;
            }
        }
        return HookInterop.CallNextHookEx(0, nCode, wParam, lParam);
    }
}
