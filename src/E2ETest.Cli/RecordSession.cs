using System.Drawing;
using System.Windows.Forms;
using E2ETest.Core.Capture;
using E2ETest.Core.Model;
using E2ETest.Core.Recording;
using E2ETest.Core.Storage;

namespace E2ETest.Cli;

/// <summary>
/// 录制会话：托盘图标 + 全局钩子跑在同一 WinForms UI 线程（其消息循环驱动钩子回调）。
/// 启动即开始录制；截图热键截图，停止热键结束并落盘。
/// </summary>
public sealed class RecordSession : ApplicationContext
{
    private readonly SampleRepository _repo;
    private readonly string _sampleId;
    private readonly string _displayName;
    private readonly CaptureRule _capture;
    private readonly Recorder _recorder;
    private readonly NotifyIcon _tray;
    private readonly List<ShotEntry> _shots = new();
    private int _shotCounter;
    private bool _finished;

    public RecordSession(SampleRepository repo, string sampleId, string displayName,
        CaptureRule capture, Hotkey screenshotKey, Hotkey stopKey)
    {
        _repo = repo;
        _sampleId = sampleId;
        _displayName = displayName;
        _capture = capture;
        _recorder = new Recorder(screenshotKey, stopKey);
        _recorder.ScreenshotHotkeyPressed += OnScreenshot;
        _recorder.StopHotkeyPressed += OnStop;

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"E2E 录制中: {displayName}",
            Visible = true,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("立即截图", null, (_, _) => OnScreenshot());
        menu.Items.Add("停止并保存", null, (_, _) => OnStop());
        _tray.ContextMenuStrip = menu;

        _recorder.Start();
    }

    private void OnScreenshot()
    {
        int index = ++_shotCounter;
        string file = Path.Combine("baseline", $"shot-{index:D4}.png");
        Screenshotter.CaptureToPng(_capture, Path.Combine(_repo.SampleDir(_sampleId), file));
        _shots.Add(new ShotEntry { Index = index, File = file, AtMs = _recorder.ElapsedMs, Kind = "manual" });
        _recorder.AppendScreenshotEvent(index);
    }

    private void OnStop()
    {
        if (_finished) return;
        _finished = true;
        long duration = _recorder.ElapsedMs;
        _recorder.Stop();
        CaptureFinalShot(duration);
        SaveManifest(duration);
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }

    private void CaptureFinalShot(long atMs)
    {
        int index = ++_shotCounter;
        string file = Path.Combine("baseline", $"shot-{index:D4}.png");
        Screenshotter.CaptureToPng(_capture, Path.Combine(_repo.SampleDir(_sampleId), file));
        _shots.Add(new ShotEntry { Index = index, File = file, AtMs = atMs, Kind = "final" });
    }

    private void SaveManifest(long duration)
    {
        var now = DateTimeOffset.UtcNow;
        var manifest = new SampleManifest
        {
            SampleId = _sampleId,
            DisplayName = _displayName,
            CreatedAt = now,
            UpdatedAt = now,
            DurationMs = duration,
            Capture = _capture,
            Shots = _shots,
            Events = new List<InputEvent>(_recorder.Events),
        };
        _repo.SaveManifest(manifest);
        Console.WriteLine($"已保存样例 '{_sampleId}': {manifest.Events.Count} 事件, {_shots.Count} 截图, {duration}ms");
    }
}
