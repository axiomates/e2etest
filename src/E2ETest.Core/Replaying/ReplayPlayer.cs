using System.Diagnostics;
using E2ETest.Core.Capture;
using E2ETest.Core.Model;
using E2ETest.Core.Storage;

namespace E2ETest.Core.Replaying;

public sealed class ReplayPlaybackResult
{
    public long ElapsedMs { get; init; }
    public List<ReplayShotResult> Shots { get; init; } = new();
    public List<string> Errors { get; init; } = new();
}

/// <summary>按绝对目标时钟回放一条经过验证的样例。</summary>
public sealed class ReplayPlayer
{
    private readonly InputInjector _injector = new();

    public async Task<ReplayPlaybackResult> PlayAsync(
        TestCaseManifest manifest,
        string testCaseDir,
        string replayTestCaseDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ManifestValidator.Validate(manifest, testCaseDir, requireBaselineFiles: true);
        var currentScreen = ScreenGeometry.PrimaryBounds();
        int currentDpi = (int)E2ETest.Core.Native.ShellInterop.GetDpiForSystem();
        if (currentScreen.X != manifest.Capture.Screen.X ||
            currentScreen.Y != manifest.Capture.Screen.Y ||
            currentScreen.Width != manifest.Capture.Screen.Width ||
            currentScreen.Height != manifest.Capture.Screen.Height ||
            currentDpi != manifest.Capture.Screen.Dpi)
        {
            throw new InvalidOperationException(
                $"当前屏幕 {currentScreen.Width}x{currentScreen.Height}@{currentDpi}DPI 与录制环境 " +
                $"{manifest.Capture.Screen.Width}x{manifest.Capture.Screen.Height}@{manifest.Capture.Screen.Dpi}DPI 不一致。");
        }

        Directory.CreateDirectory(replayTestCaseDir);
        var clock = Stopwatch.StartNew();
        long previousT = 0;
        double scheduledMs = 0;
        double speed = manifest.Replay.SpeedFactor > 0 ? manifest.Replay.SpeedFactor : 1.0;
        int maxIdleGap = manifest.Replay.MaxIdleGapMs > 0 ? manifest.Replay.MaxIdleGapMs : int.MaxValue;
        var heldKeys = new Dictionary<(int Scan, bool Ext, int VkFallback), KeyEvent>();
        var heldButtons = new HashSet<MouseButton>();
        var captureTasks = new List<Task<ReplayShotResult>>();
        var errors = new List<string>();
        int lastX = 0, lastY = 0;

        try
        {
            foreach (var inputEvent in manifest.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long sourceGap = inputEvent.T - previousT;
                scheduledMs += Math.Min(maxIdleGap, sourceGap / speed);
                await WaitUntilAsync(clock, scheduledMs, cancellationToken);

                switch (inputEvent)
                {
                    case MouseMoveEvent e:
                        lastX = e.X; lastY = e.Y;
                        _injector.MoveMouse(e.X, e.Y);
                        break;
                    case MouseButtonEvent e:
                        lastX = e.X; lastY = e.Y;
                        _injector.MouseButton(e.Button, e.Down, e.X, e.Y);
                        if (e.Down) heldButtons.Add(e.Button); else heldButtons.Remove(e.Button);
                        break;
                    case MouseWheelEvent e:
                        lastX = e.X; lastY = e.Y;
                        _injector.Wheel(e.X, e.Y, e.Delta, e.Horizontal);
                        break;
                    case KeyEvent e:
                        _injector.Key(e.Vk, e.Scan, e.Ext, e.Down);
                        var key = (e.Scan, e.Ext, e.Scan == 0 ? e.Vk : 0);
                        if (e.Down) heldKeys[key] = e; else heldKeys.Remove(key);
                        break;
                    case ScreenshotEvent e:
                        captureTasks.Add(CaptureShotAsync(manifest, testCaseDir, e.ShotIndex, replayTestCaseDir, cancellationToken));
                        break;
                }
                previousT = inputEvent.T;
            }

        }
        catch (Exception ex)
        {
            errors.Add($"playback: {ex}");
        }

        foreach (var key in heldKeys.Values)
        {
            try { _injector.Key(key.Vk, key.Scan, key.Ext, down: false); }
            catch (Exception ex) { errors.Add($"cleanup-key-{key.Vk}: {ex}"); }
        }
        foreach (var button in heldButtons)
        {
            try { _injector.ReleaseMouseButton(button); }
            catch (Exception ex) { errors.Add($"cleanup-mouse-{button}: {ex}"); }
        }

        ReplayShotResult[] shotResults = await Task.WhenAll(captureTasks);
        foreach (var shot in shotResults.Where(s => !s.Ok))
            errors.Add($"capture-{shot.ShotIndex}: {shot.Error}");

        return new ReplayPlaybackResult
        {
            ElapsedMs = clock.ElapsedMilliseconds,
            Shots = shotResults.OrderBy(s => s.ShotIndex).ToList(),
            Errors = errors,
        };
    }

    private static async Task WaitUntilAsync(
        Stopwatch clock, double targetMs, CancellationToken cancellationToken)
    {
        int remainingMs = (int)Math.Ceiling(targetMs - clock.Elapsed.TotalMilliseconds);
        if (remainingMs > 0) await Task.Delay(remainingMs, cancellationToken);
    }

    private static Task<ReplayShotResult> CaptureShotAsync(
        TestCaseManifest manifest,
        string testCaseDir,
        int shotIndex,
        string replayTestCaseDir,
        CancellationToken cancellationToken)
    {
        var shot = manifest.Shots.Single(s => s.Index == shotIndex);
        string baselinePath = Path.GetFullPath(Path.Combine(testCaseDir, shot.File));
        string replayPath = Path.Combine(replayTestCaseDir, $"shot-{shotIndex:D4}.png");
        System.Drawing.Bitmap bitmap;
        try
        {
            bitmap = Screenshotter.CaptureBitmap(manifest.Capture);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ReplayShotResult
            {
                ShotIndex = shotIndex,
                BaselinePath = baselinePath,
                ReplayPath = replayPath,
                Error = ex.ToString(),
            });
        }

        return EncodeShotAsync(bitmap, shotIndex, baselinePath, replayPath, cancellationToken);
    }

    private static async Task<ReplayShotResult> EncodeShotAsync(
        System.Drawing.Bitmap bitmap,
        int shotIndex,
        string baselinePath,
        string replayPath,
        CancellationToken cancellationToken)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        try
        {
            await Screenshotter.EncodePngAndDisposeAsync(bitmap, replayPath, cancellationToken);
            return new ReplayShotResult
            {
                ShotIndex = shotIndex,
                BaselinePath = baselinePath,
                ReplayPath = replayPath,
                Ok = true,
                Width = width,
                Height = height,
            };
        }
        catch (Exception ex)
        {
            return new ReplayShotResult
            {
                ShotIndex = shotIndex,
                BaselinePath = baselinePath,
                ReplayPath = replayPath,
                Error = ex.ToString(),
            };
        }
    }
}
