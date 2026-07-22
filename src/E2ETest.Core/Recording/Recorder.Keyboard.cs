using System.Runtime.InteropServices;
using E2ETest.Core.Model;
using E2ETest.Core.Native;

namespace E2ETest.Core.Recording;

partial class Recorder
{
    private readonly HashSet<int> _downVks = new();
    private readonly HashSet<int> _activeHotkeyVks = new();
    private readonly HashSet<int> _preHeldHotkeyVks = new();

    private nint KeyboardHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
            return HookInterop.CallNextHookEx(0, nCode, wParam, lParam);

        try
        {
            return ProcessKeyboardHook(nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            ReportHookFailure(ex);
            return HookInterop.CallNextHookEx(0, nCode, wParam, lParam);
        }
    }

    private nint ProcessKeyboardHook(int nCode, nint wParam, nint lParam)
    {
        var data = Marshal.PtrToStructure<HookInterop.KBDLLHOOKSTRUCT>(lParam);
        int message = (int)wParam;
        bool down = message == HookInterop.WM_KEYDOWN || message == HookInterop.WM_SYSKEYDOWN;
        int vk = (int)data.vkCode;

        if (!IsRecordingActive)
        {
            if (_activeHotkeyVks.Contains(vk))
            {
                if (!down)
                {
                    _activeHotkeyVks.Remove(vk);
                    _downVks.Remove(vk);
                }
                return 1;
            }
            return HookInterop.CallNextHookEx(0, nCode, wParam, lParam);
        }

        if (down)
        {
            if (_preHeldHotkeyVks.Contains(vk)) return 1;

            bool repeat = !_downVks.Add(vk);
            if (repeat && _activeHotkeyVks.Contains(vk)) return 1;

            if (!repeat && IsHotkey(vk))
            {
                _activeHotkeyVks.Add(vk);
                long atMs = TimeFromHook(data.time);
                if (vk == _screenshotKey.Vk)
                {
                    ScreenshotHotkeyPressed?.Invoke(atMs);
                }
                else
                {
                    MarkStopRequested(atMs);
                    StopHotkeyPressed?.Invoke(atMs);
                }
                return 1;
            }

            _events.Add(CreateKeyEvent(data, vk, down: true));
        }
        else
        {
            _downVks.Remove(vk);
            _activeHotkeyVks.Remove(vk);
            if (_preHeldHotkeyVks.Remove(vk) || IsHotkey(vk)) return 1;
            _events.Add(CreateKeyEvent(data, vk, down: false));
        }

        return HookInterop.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private bool IsHotkey(int vk) => vk == _screenshotKey.Vk || vk == _stopKey.Vk;

    private KeyEvent CreateKeyEvent(HookInterop.KBDLLHOOKSTRUCT data, int vk, bool down) => new()
    {
        T = TimeFromHook(data.time),
        Vk = vk,
        Scan = (int)data.scanCode,
        Ext = (data.flags & HookInterop.LLKHF_EXTENDED) != 0,
        Down = down,
    };
}
