using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;
using E2ETest.Core.Capture;
using E2ETest.Core.Model;
using E2ETest.Core.Recording;
using E2ETest.Core.Storage;

namespace E2ETest.Cli;

/// <summary>录制会话：hook 回调只入队，UI 线程按 FIFO 固化截图像素，PNG 后台编码。</summary>
public sealed class RecordSession : ApplicationContext
{
    private enum ControlKind { Screenshot, Stop }
    private readonly record struct ControlRequest(ControlKind Kind, long AtMs);

    private readonly TestCaseRepository _repo;
    private readonly string _name;
    private readonly RecordingStaging _staging;
    private readonly TestCaseWriteLease _writeLease;
    private readonly CaptureRule _capture;
    private readonly string? _testFocus;
    private readonly string? _acceptanceCriteria;
    private readonly Recorder _recorder;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _dispatcher;
    private readonly ConcurrentQueue<ControlRequest> _controls = new();
    private readonly List<Task> _captureTasks = new();
    private readonly List<ShotEntry> _shots = new();
    private Task? _finishTask;
    private int _shotCounter;
    private DateTime _captureStatusUntilUtc;
    private bool _showingCaptureStatus;
    private bool _reportedCaptureFailure;
    private bool _stopping;
    private bool _disposed;

    public Exception? Failure { get; private set; }
    public bool Committed { get; private set; }
    public int EventCount { get; private set; }
    public int ScreenshotCount => _shots.Count;
    public long DurationMs { get; private set; }

    public RecordSession(
        TestCaseRepository repo,
        string name,
        RecordingStaging staging,
        TestCaseWriteLease writeLease,
        CaptureRule capture,
        Hotkey screenshotKey,
        Hotkey stopKey,
        string? testFocus,
        string? acceptanceCriteria)
    {
        _repo = repo;
        _name = name;
        _staging = staging;
        _writeLease = writeLease;
        _capture = capture;
        _testFocus = testFocus;
        _acceptanceCriteria = acceptanceCriteria;
        _recorder = new Recorder(screenshotKey, stopKey);
        _recorder.ScreenshotHotkeyPressed += atMs =>
            _controls.Enqueue(new ControlRequest(ControlKind.Screenshot, atMs));
        _recorder.StopHotkeyPressed += atMs =>
            _controls.Enqueue(new ControlRequest(ControlKind.Stop, atMs));

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.RecordingEmpty,
            Text = TrayText($"E2E 录制中（尚无截图）: {name}"),
            Visible = true,
        };
        _menu = new ContextMenuStrip();
        _menu.Items.Add("立即截图", null, (_, _) =>
            _controls.Enqueue(new ControlRequest(ControlKind.Screenshot, _recorder.ElapsedMs)));
        _menu.Items.Add("停止并保存", null, (_, _) =>
        {
            long atMs = _recorder.ElapsedMs;
            _recorder.MarkStopRequested(atMs);
            _controls.Enqueue(new ControlRequest(ControlKind.Stop, atMs));
        });
        _tray.ContextMenuStrip = _menu;

        _dispatcher = new System.Windows.Forms.Timer { Interval = 5 };
        _dispatcher.Tick += (_, _) => DispatchControls();
        try
        {
            _recorder.Start();
            _dispatcher.Start();
        }
        catch
        {
            _recorder.Dispose();
            CleanupUi();
            throw;
        }
    }

    private void DispatchControls()
    {
        if (_stopping) return;
        RefreshCaptureStatus();
        if (_stopping) return;
        if (_recorder.Failure is not null)
        {
            BeginStop(_recorder.ElapsedMs, _recorder.Failure);
            return;
        }

        while (!_stopping && _controls.TryDequeue(out var request))
        {
            if (request.Kind == ControlKind.Screenshot)
            {
                try { CaptureManualShot(request.AtMs); }
                catch (Exception ex) { BeginStop(request.AtMs, ex); }
            }
            else
            {
                BeginStop(request.AtMs, null);
            }
        }
    }

    private void CaptureManualShot(long requestedAtMs)
    {
        _showingCaptureStatus = true;
        _captureStatusUntilUtc = DateTime.UtcNow.AddMilliseconds(500);
        TrySetTrayIcon(TrayIcons.Capturing);
        TrySetTrayText($"E2E 正在截图并保存，请勿操作: {_name}");
        var bitmap = Screenshotter.CaptureBitmap(_capture);
        long capturedAtMs = Math.Max(requestedAtMs, _recorder.ElapsedMs);
        int index = ++_shotCounter;
        string file = Path.Combine("baseline", $"shot-{index:D4}.png");
        _shots.Add(new ShotEntry
        {
            Index = index,
            File = file,
            AtMs = capturedAtMs,
            RequestedAtMs = requestedAtMs,
            Kind = "manual",
        });
        _recorder.AppendScreenshotEvent(index, capturedAtMs);
        QueueEncoding(bitmap, file);
    }

    private void RefreshCaptureStatus()
    {
        if (!_showingCaptureStatus || _stopping) return;
        if (_captureTasks.Any(task => !task.IsCompleted) || DateTime.UtcNow < _captureStatusUntilUtc) return;

        var errors = _captureTasks
            .Where(task => task.IsFaulted)
            .SelectMany(task => task.Exception!.Flatten().InnerExceptions)
            .ToList();
        if (errors.Count > 0)
        {
            _reportedCaptureFailure = true;
            BeginStop(_recorder.ElapsedMs, new AggregateException("截图编码失败。", errors));
            return;
        }

        _showingCaptureStatus = false;
        TrySetTrayIcon(TrayIcons.Recording);
        TrySetTrayText($"E2E 录制中（{_shots.Count} 张截图）: {_name}");
    }

    private void BeginStop(long atMs, Exception? failure)
    {
        if (_stopping) return;
        _stopping = true;
        _showingCaptureStatus = false;
        _dispatcher.Stop();
        Failure = failure;
        _recorder.MarkStopRequested(atMs);
        long lastEventMs = _recorder.Events.Count == 0 ? 0 : _recorder.Events.Max(e => e.T);
        long finalAtMs = Math.Max(Math.Max(atMs, _recorder.ElapsedMs), lastEventMs);

        TrySetTrayIcon(Failure is null ? TrayIcons.Saving : TrayIcons.Error);
        TrySetTrayText(Failure is null ? $"E2E 正在保存: {_name}" : $"E2E 录制失败: {_name}");
        TryNotify("录制已停止", Failure is null ? "正在保存截图和测试记录…" : "录制发生错误，正在安全清理…",
            Failure is null ? ToolTipIcon.Info : ToolTipIcon.Error);

        try { _recorder.Stop(); }
        catch (Exception ex) { Failure = Combine(Failure, ex); }
        _finishTask = FinishRecordingAsync(finalAtMs);
    }

    private void QueueEncoding(Bitmap bitmap, string relativeFile)
    {
        string outputPath = Path.Combine(_staging.DirectoryPath, relativeFile);
        try
        {
            _captureTasks.Add(Screenshotter.EncodePngAndDisposeAsync(bitmap, outputPath));
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private async Task FinishRecordingAsync(long duration)
    {
        await Task.Yield();
        try
        {
            try { await Task.WhenAll(_captureTasks); }
            catch
            {
                var encodingErrors = _captureTasks
                    .Where(task => task.IsFaulted)
                    .SelectMany(task => task.Exception!.Flatten().InnerExceptions)
                    .ToList();
                if (!_reportedCaptureFailure)
                    Failure = Combine(Failure, new AggregateException("截图编码失败。", encodingErrors));
            }

            if (Failure is null)
            {
                if (_shots.Count == 0)
                    throw new InvalidOperationException("录制无效：未捕获任何截图。请在停止前按截图键后重新录制。");
                var manifest = BuildManifest(duration);
                ManifestValidator.Validate(manifest, _staging.DirectoryPath, requireBaselineFiles: true);
                _repo.SaveStagedManifest(_staging, manifest);
                _repo.CommitStagedTestCase(_staging, _writeLease);
                Committed = true;
                EventCount = manifest.Events.Count;
                DurationMs = duration;
                TrySetTrayIcon(TrayIcons.Success);
                TrySetTrayText($"E2E 录制完成: {_name}");
                TryNotify("录制完成", $"{_name} 已保存，共 {_shots.Count} 张截图。", ToolTipIcon.Info);
            }
            else
            {
                TrySetTrayIcon(TrayIcons.Error);
                TrySetTrayText($"E2E 保存失败: {_name}");
                TryNotify("录制保存失败", Failure.Message, ToolTipIcon.Error);
            }
        }
        catch (Exception ex)
        {
            Failure = Combine(Failure, ex);
            TrySetTrayIcon(TrayIcons.Error);
            TrySetTrayText($"E2E 保存失败: {_name}");
            TryNotify("录制保存失败", Failure.Message, ToolTipIcon.Error);
        }
        finally
        {
            if (!Committed)
            {
                try { _repo.DeleteStaging(_staging); }
                catch (Exception ex) { Failure = Combine(Failure, ex); }
            }
            await Task.Delay(1200);
            CleanupUi();
            ExitThread();
        }
    }

    private TestCaseManifest BuildManifest(long duration)
    {
        var events = FilterEventsForTargetScreen(_recorder.Events.OrderBy(e => e.T), _capture.Screen);
        return new TestCaseManifest
        {
            SchemaVersion = ManifestValidator.CurrentSchemaVersion,
            Name = _name,
            CreatedAt = DateTimeOffset.UtcNow,
            DurationMs = duration,
            TestFocus = _testFocus,
            AcceptanceCriteria = _acceptanceCriteria,
            Capture = _capture,
            Replay = new ReplaySettings(),
            Shots = _shots,
            Events = events,
        };
    }

    private static List<InputEvent> FilterEventsForTargetScreen(
        IEnumerable<InputEvent> source,
        ScreenSize screen)
    {
        var result = new List<InputEvent>();
        var pressedInside = new HashSet<MouseButton>();
        foreach (var inputEvent in source)
        {
            switch (inputEvent)
            {
                case MouseMoveEvent move when !Inside(move.X, move.Y, screen):
                case MouseWheelEvent wheel when !Inside(wheel.X, wheel.Y, screen):
                    continue;
                case MouseButtonEvent button when button.Down:
                    if (!Inside(button.X, button.Y, screen)) continue;
                    pressedInside.Add(button.Button);
                    result.Add(button);
                    break;
                case MouseButtonEvent button:
                    if (!pressedInside.Remove(button.Button)) continue;
                    if (Inside(button.X, button.Y, screen))
                    {
                        result.Add(button);
                    }
                    else
                    {
                        result.Add(new MouseButtonEvent
                        {
                            T = button.T,
                            Button = button.Button,
                            Down = false,
                            X = Math.Clamp(button.X, screen.X, screen.X + screen.Width - 1),
                            Y = Math.Clamp(button.Y, screen.Y, screen.Y + screen.Height - 1),
                        });
                    }
                    break;
                default:
                    result.Add(inputEvent);
                    break;
            }
        }
        return result;
    }

    private static bool Inside(int x, int y, ScreenSize screen) =>
        x >= screen.X && y >= screen.Y &&
        x < (long)screen.X + screen.Width && y < (long)screen.Y + screen.Height;

    private static Exception Combine(Exception? first, Exception second) =>
        first is null ? second : new AggregateException(first, second);

    private static string TrayText(string value) => value.Length <= 63 ? value : value[..63];

    private void TrySetTrayText(string value)
    {
        try { _tray.Text = TrayText(value); } catch { }
    }

    private void TrySetTrayIcon(Icon icon)
    {
        try { _tray.Icon = icon; } catch { }
    }

    private void TryNotify(string title, string text, ToolTipIcon icon)
    {
        try { _tray.ShowBalloonTip(2500, title, text, icon); } catch { }
    }

    private void CleanupUi()
    {
        try { _dispatcher.Stop(); } catch { }
        try { _tray.Visible = false; } catch { }
        try { _menu.Dispose(); } catch { }
        try { _tray.Dispose(); } catch { }
        try { _dispatcher.Dispose(); } catch { }
    }

    public void EnsureStoppedAndWaitForCaptures()
    {
        if (!_stopping) _recorder.MarkStopRequested(_recorder.ElapsedMs);
        _recorder.Dispose();
        try { Task.WaitAll(_captureTasks.ToArray()); }
        catch (AggregateException ex) { Failure = Combine(Failure, ex.Flatten()); }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (!_stopping) _recorder.MarkStopRequested(_recorder.ElapsedMs);
            _recorder.Dispose();
            if (_recorder.HasActiveHooks)
                Failure = Combine(Failure, new InvalidOperationException("输入钩子未能完全卸载。"));
            CleanupUi();
        }
        base.Dispose(disposing);
    }
}
