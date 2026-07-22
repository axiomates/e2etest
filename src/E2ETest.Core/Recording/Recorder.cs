using System.Diagnostics;
using E2ETest.Core.Model;
using E2ETest.Core.Native;

namespace E2ETest.Core.Recording;

/// <summary>
/// 全局输入录制器。基于 WH_MOUSE_LL / WH_KEYBOARD_LL 低级钩子，
/// 捕获对任意其他软件的鼠标/键盘操作（与焦点无关）。
/// 必须在带消息循环的线程上 Start（record 子命令用 WinForms UI 线程）。
/// </summary>
public sealed partial class Recorder
{
    private readonly Hotkey _screenshotKey;
    private readonly Hotkey _stopKey;
    private readonly int _moveThrottleMs;

    // 保持委托引用存活，防止 GC 回收导致钩子回调野指针。
    private HookInterop.HookProc? _mouseProc;
    private HookInterop.HookProc? _keyboardProc;
    private nint _mouseHook;
    private nint _keyboardHook;

    private readonly Stopwatch _clock = new();
    private readonly List<InputEvent> _events = new();
    private long _lastMoveAt = long.MinValue;
    private int _lastMoveX, _lastMoveY;

    /// <summary>截图热键被按下（已从时间轴吞掉），由宿主负责实际截图。</summary>
    public event Action? ScreenshotHotkeyPressed;

    /// <summary>停止热键被按下。</summary>
    public event Action? StopHotkeyPressed;

    public Recorder(Hotkey screenshotKey, Hotkey stopKey, int moveThrottleMs = 15)
    {
        _screenshotKey = screenshotKey;
        _stopKey = stopKey;
        _moveThrottleMs = moveThrottleMs;
    }

    public long ElapsedMs => _clock.ElapsedMilliseconds;
    public IReadOnlyList<InputEvent> Events => _events;

    public void Start()
    {
        _events.Clear();
        _lastMoveAt = long.MinValue;
        _mouseProc = MouseHookProc;
        _keyboardProc = KeyboardHookProc;
        nint hMod = HookInterop.GetModuleHandleW(null);
        _mouseHook = HookInterop.SetWindowsHookExW(HookInterop.WH_MOUSE_LL, _mouseProc, hMod, 0);
        _keyboardHook = HookInterop.SetWindowsHookExW(HookInterop.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        if (_mouseHook == 0 || _keyboardHook == 0)
            throw new InvalidOperationException("安装低级钩子失败，请以足够权限运行。");
        _clock.Restart();
    }

    public void Stop()
    {
        _clock.Stop();
        if (_mouseHook != 0) { HookInterop.UnhookWindowsHookEx(_mouseHook); _mouseHook = 0; }
        if (_keyboardHook != 0) { HookInterop.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = 0; }
        _mouseProc = null;
        _keyboardProc = null;
    }

    /// <summary>宿主截图后调用，在当前时点追加一条 screenshot 事件。</summary>
    public void AppendScreenshotEvent(int shotIndex) =>
        _events.Add(new ScreenshotEvent { T = ElapsedMs, ShotIndex = shotIndex });
}
