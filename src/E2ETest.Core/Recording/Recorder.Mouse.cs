using System.Runtime.InteropServices;
using E2ETest.Core.Model;
using E2ETest.Core.Native;

namespace E2ETest.Core.Recording;

partial class Recorder
{
    private nint MouseHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
            return HookInterop.CallNextHookEx(0, nCode, wParam, lParam);

        if (!IsRecordingActive)
            return HookInterop.CallNextHookEx(0, nCode, wParam, lParam);

        try
        {
            var data = Marshal.PtrToStructure<HookInterop.MSLLHOOKSTRUCT>(lParam);
            long t = TimeFromHook(data.time);
            int x = data.pt.x, y = data.pt.y;
            int message = (int)wParam;

            switch (message)
            {
                case HookInterop.WM_MOUSEMOVE:
                    if (_moveThrottleMs <= 0 || t - _lastMoveAt >= _moveThrottleMs ||
                        Math.Abs(x - _lastMoveX) + Math.Abs(y - _lastMoveY) > 4)
                    {
                        _events.Add(new MouseMoveEvent { T = t, X = x, Y = y });
                        _lastMoveAt = t;
                        _lastMoveX = x;
                        _lastMoveY = y;
                    }
                    break;
                case HookInterop.WM_LBUTTONDOWN:
                case HookInterop.WM_LBUTTONUP:
                    _events.Add(new MouseButtonEvent { T = t, Button = MouseButton.Left,
                        Down = message == HookInterop.WM_LBUTTONDOWN, X = x, Y = y });
                    break;
                case HookInterop.WM_RBUTTONDOWN:
                case HookInterop.WM_RBUTTONUP:
                    _events.Add(new MouseButtonEvent { T = t, Button = MouseButton.Right,
                        Down = message == HookInterop.WM_RBUTTONDOWN, X = x, Y = y });
                    break;
                case HookInterop.WM_MBUTTONDOWN:
                case HookInterop.WM_MBUTTONUP:
                    _events.Add(new MouseButtonEvent { T = t, Button = MouseButton.Middle,
                        Down = message == HookInterop.WM_MBUTTONDOWN, X = x, Y = y });
                    break;
                case HookInterop.WM_XBUTTONDOWN:
                case HookInterop.WM_XBUTTONUP:
                    var xButton = (data.mouseData >> 16) == 1 ? MouseButton.XButton1 : MouseButton.XButton2;
                    _events.Add(new MouseButtonEvent { T = t, Button = xButton,
                        Down = message == HookInterop.WM_XBUTTONDOWN, X = x, Y = y });
                    break;
                case HookInterop.WM_MOUSEWHEEL:
                    _events.Add(new MouseWheelEvent { T = t, X = x, Y = y,
                        Delta = unchecked((short)(data.mouseData >> 16)), Horizontal = false });
                    break;
                case HookInterop.WM_MOUSEHWHEEL:
                    _events.Add(new MouseWheelEvent { T = t, X = x, Y = y,
                        Delta = unchecked((short)(data.mouseData >> 16)), Horizontal = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            ReportHookFailure(ex);
        }

        return HookInterop.CallNextHookEx(0, nCode, wParam, lParam);
    }
}
