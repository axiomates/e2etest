using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using E2ETest.Core.Model;
using E2ETest.Core.Native;

namespace E2ETest.Core.Recording;

/// <summary>
/// 全局输入录制器。基于 WH_MOUSE_LL / WH_KEYBOARD_LL 低级钩子，
/// 捕获对任意其他软件的鼠标/键盘操作（与焦点无关）。
/// 必须在带消息循环的线程上 Start（record 子命令用 WinForms UI 线程）。
/// </summary>
public sealed partial class Recorder : IDisposable
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
    private uint _startTick;
    private Exception? _failure;
    private long _stopRequestedAt = -1;
    private long _lastMoveAt = long.MinValue;
    private int _lastMoveX, _lastMoveY;

    /// <summary>截图热键被按下（已从时间轴吞掉），由宿主负责实际截图。</summary>
    public event Action<long>? ScreenshotHotkeyPressed;

    /// <summary>停止键被按下。参数为录制时间点（毫秒）。</summary>
    public event Action<long>? StopHotkeyPressed;

    public Recorder(Hotkey screenshotKey, Hotkey stopKey, int moveThrottleMs = 0)
    {
        if (screenshotKey.Vk == stopKey.Vk)
            throw new ArgumentException("截图键和停止键不能设置为同一个按键。");
        _screenshotKey = screenshotKey;
        _stopKey = stopKey;
        _moveThrottleMs = moveThrottleMs;
    }

    public long ElapsedMs => _clock.ElapsedMilliseconds;
    public IReadOnlyList<InputEvent> Events => _events;
    public Exception? Failure => Volatile.Read(ref _failure);
    public bool HasActiveHooks => _mouseHook != 0 || _keyboardHook != 0;

    public void Start()
    {
        WaitForNeutralModifiersAndButtons();
        _events.Clear();
        _downVks.Clear();
        _activeHotkeyVks.Clear();
        _preHeldHotkeyVks.Clear();
        if ((HookInterop.GetAsyncKeyState(_screenshotKey.Vk) & 0x8000) != 0)
            _preHeldHotkeyVks.Add(_screenshotKey.Vk);
        if ((HookInterop.GetAsyncKeyState(_stopKey.Vk) & 0x8000) != 0)
            _preHeldHotkeyVks.Add(_stopKey.Vk);
        _failure = null;
        _stopRequestedAt = -1;
        _lastMoveAt = long.MinValue;
        _startTick = unchecked((uint)Environment.TickCount);
        _clock.Restart();
        _mouseProc = MouseHookProc;
        _keyboardProc = KeyboardHookProc;
        nint hMod = HookInterop.GetModuleHandleW(null);
        _mouseHook = HookInterop.SetWindowsHookExW(HookInterop.WH_MOUSE_LL, _mouseProc, hMod, 0);
        _keyboardHook = HookInterop.SetWindowsHookExW(HookInterop.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        if (_mouseHook == 0 || _keyboardHook == 0)
        {
            var installError = new Win32Exception(Marshal.GetLastWin32Error(), "安装低级输入钩子失败");
            var cleanupErrors = new List<Exception>();
            TryUnhook(ref _mouseHook, "鼠标", cleanupErrors);
            TryUnhook(ref _keyboardHook, "键盘", cleanupErrors);
            if (_mouseHook == 0 && _keyboardHook == 0)
            {
                _mouseProc = null;
                _keyboardProc = null;
            }
            if (cleanupErrors.Count > 0)
                throw new AggregateException(new[] { installError }.Concat(cleanupErrors));
            throw installError;
        }
    }

    private void WaitForNeutralModifiersAndButtons()
    {
        int[] relevantKeys =
        {
            0x01, 0x02, 0x04, 0x05, 0x06,       // 鼠标按钮
            0x10, 0x11, 0x12,                   // 通用 Shift/Ctrl/Alt
            0x5B, 0x5C,                         // Win
            0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5,// 左右修饰键
            _screenshotKey.Vk, _stopKey.Vk,
        };
        var deadline = Stopwatch.StartNew();
        while (relevantKeys.Distinct().Any(vk =>
                   (HookInterop.GetAsyncKeyState(vk) & 0x8000) != 0))
        {
            if (deadline.ElapsedMilliseconds >= 3000)
                throw new InvalidOperationException("开始录制前请松开鼠标按钮、修饰键和录制控制键。");
            Thread.Sleep(10);
        }
    }

    public void Stop()
    {
        _clock.Stop();
        var errors = new List<Exception>();
        TryUnhook(ref _mouseHook, "鼠标", errors);
        TryUnhook(ref _keyboardHook, "键盘", errors);
        if (_mouseHook == 0 && _keyboardHook == 0)
        {
            _mouseProc = null;
            _keyboardProc = null;
        }
        if (errors.Count > 0) throw new AggregateException("卸载输入钩子失败。", errors);
    }

    private static void TryUnhook(ref nint hook, string name, List<Exception> errors)
    {
        if (hook == 0) return;
        if (HookInterop.UnhookWindowsHookEx(hook))
        {
            hook = 0;
            return;
        }
        errors.Add(new Win32Exception(Marshal.GetLastWin32Error(), $"卸载{name}钩子失败"));
    }

    internal long TimeFromHook(uint eventTick) => unchecked(eventTick - _startTick);

    internal bool IsRecordingActive => Volatile.Read(ref _stopRequestedAt) < 0;

    public void MarkStopRequested(long atMs) =>
        Interlocked.CompareExchange(ref _stopRequestedAt, atMs, -1);

    internal void ReportHookFailure(Exception error) =>
        Interlocked.CompareExchange(ref _failure, error, null);

    /// <summary>宿主截图后调用，在当前时点追加一条 screenshot 事件。</summary>
    public void AppendScreenshotEvent(int shotIndex, long atMs) =>
        _events.Add(new ScreenshotEvent { T = atMs, ShotIndex = shotIndex });

    public void Dispose()
    {
        for (int attempt = 0; attempt < 3 && HasActiveHooks; attempt++)
        {
            try { Stop(); }
            catch (Exception ex) { ReportHookFailure(ex); Thread.Sleep(10); }
        }
    }
}
