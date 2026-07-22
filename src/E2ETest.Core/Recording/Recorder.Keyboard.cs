using System.Runtime.InteropServices;
using E2ETest.Core.Model;
using E2ETest.Core.Native;

namespace E2ETest.Core.Recording;

partial class Recorder
{
    // 当前按住的修饰键 -> 其已记录的 down 事件（便于命中热键时回撤）。
    private readonly Dictionary<int, KeyEvent> _heldMods = new();
    // 命中热键后需要吞掉其 key-up 的 VK 集合。
    private readonly HashSet<int> _swallowUp = new();

    private static HotkeyMods ModOf(int vk) => vk switch
    {
        0x11 or 0xA2 or 0xA3 => HotkeyMods.Ctrl,
        0x12 or 0xA4 or 0xA5 => HotkeyMods.Alt,
        0x10 or 0xA0 or 0xA1 => HotkeyMods.Shift,
        0x5B or 0x5C => HotkeyMods.Win,
        _ => HotkeyMods.None,
    };

    private HotkeyMods CurrentMods()
    {
        var m = HotkeyMods.None;
        foreach (var vk in _heldMods.Keys) m |= ModOf(vk);
        return m;
    }

    private nint KeyboardHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var s = Marshal.PtrToStructure<HookInterop.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg == HookInterop.WM_KEYDOWN || msg == HookInterop.WM_SYSKEYDOWN;
            int vk = (int)s.vkCode;
            bool ext = (s.flags & HookInterop.LLKHF_EXTENDED) != 0;

            if (!down && _swallowUp.Remove(vk))
                return 1; // 吞掉热键组成键的抬起

            if (down && ModOf(vk) == HotkeyMods.None && TryMatchHotkey(vk))
                return 1; // 命中热键：吞掉主键按下

            var ev = new KeyEvent { T = ElapsedMs, Vk = vk, Scan = (int)s.scanCode, Ext = ext, Down = down };
            _events.Add(ev);
            TrackModifier(vk, down, ev);
        }
        return HookInterop.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private void TrackModifier(int vk, bool down, KeyEvent ev)
    {
        if (ModOf(vk) == HotkeyMods.None) return;
        if (down) _heldMods[vk] = ev;
        else _heldMods.Remove(vk);
    }

    private bool TryMatchHotkey(int vk)
    {
        var mods = CurrentMods();
        Hotkey? hit = null;
        if (vk == _screenshotKey.Vk && mods == _screenshotKey.Mods) hit = _screenshotKey;
        else if (vk == _stopKey.Vk && mods == _stopKey.Mods) hit = _stopKey;
        if (hit is null) return false;

        // 回撤已记录的、属于该热键的按住修饰键 down 事件，并吞掉其抬起。
        foreach (var (mvk, downEv) in _heldMods.ToArray())
        {
            if ((hit.Mods & ModOf(mvk)) != 0)
            {
                _events.Remove(downEv);
                _swallowUp.Add(mvk);
            }
        }
        if (hit == _screenshotKey) ScreenshotHotkeyPressed?.Invoke();
        else StopHotkeyPressed?.Invoke();
        return true;
    }
}
