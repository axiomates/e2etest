using System.Text.Json;
using E2ETest.Core.Capture;
using E2ETest.Core.Model;
using E2ETest.Core.Native;
using E2ETest.Core.Recording;
using E2ETest.Core.Storage;
using Serilog;
using Serilog.Context;

namespace E2ETest.Cli.Commands;

public static class RecordCommand
{
    public static int Run(CliArgs args)
    {
        string? requestedId = args.Get("sample");
        if (string.IsNullOrWhiteSpace(requestedId))
        {
            Console.Error.WriteLine("缺少 --sample <id>，例: e2etest record --sample login-flow");
            return 2;
        }

        string sampleId = SafeId.Validate(requestedId, "sample");
        using var sampleLogContext = LogContext.PushProperty("SampleId", sampleId);
        string root = args.Get("root") ?? ".";
        bool jsonOutput = args.Has("json");
        var repo = new SampleRepository(root);
        var config = ConfigStore.Load(repo.ConfigPath);
        using var desktopLock = repo.AcquireDesktopLock();
        using var sampleLock = repo.AcquireSampleLock(sampleId);

        SampleManifest? existing = null;
        if (repo.Exists(sampleId))
        {
            using var snapshot = repo.LoadSnapshot(sampleId, sampleLock);
            existing = snapshot.Manifest;
        }
        string displayName = args.Get("name") ?? existing?.DisplayName ?? sampleId;

        bool fullscreen = config.Record.Fullscreen;
        if (args.Has("fullscreen")) fullscreen = true;
        if (args.Has("no-fullscreen")) fullscreen = false;

        var capture = CaptureRuleBuilder.Build(fullscreen);
        var screenshotKey = Hotkey.Parse(config.Hotkeys.Screenshot);
        var stopKey = Hotkey.Parse(config.Hotkeys.StartStop);
        RecordingStaging staging = repo.CreateRecordingStaging(sampleId, sampleLock);
        RecordSession? session = null;
        var consoleState = default(ConsoleWindow.VisibilityState);

        try
        {
            Log.Information("开始录制 DisplayName={DisplayName} Fullscreen={Fullscreen}", displayName, fullscreen);
            session = new RecordSession(repo, sampleId, displayName, staging, sampleLock, existing,
                capture, screenshotKey, stopKey);

            if (jsonOutput)
            {
                WriteJson(new
                {
                    type = "recording_started",
                    sampleId,
                    displayName,
                    screenshotKey = config.Hotkeys.Screenshot,
                    stopKey = config.Hotkeys.StartStop,
                    fullscreen,
                });
            }
            else
            {
                Console.WriteLine($"开始录制 '{sampleId}' ({displayName})");
                Console.WriteLine($"  截图键: {config.Hotkeys.Screenshot}");
                Console.WriteLine($"  停止键: {config.Hotkeys.StartStop}");
                Console.WriteLine($"  全屏模式: {fullscreen}");
                Console.WriteLine($"  裁剪区域: {capture.CropRect.X},{capture.CropRect.Y} {capture.CropRect.Width}x{capture.CropRect.Height}");
                Console.WriteLine("录制中... 控制台将隐藏，屏幕仅保留托盘图标。");
            }
            Console.Out.Flush();

            consoleState = ConsoleWindow.CaptureAndHide();
            System.Windows.Forms.Application.Run(session);
        }
        finally
        {
            ConsoleWindow.Restore(consoleState);
            session?.EnsureStoppedAndWaitForCaptures();
            session?.Dispose();
            if (session?.Committed != true)
            {
                try { repo.DeleteStaging(staging); }
                catch (Exception cleanupError) { Log.Error(cleanupError, "清理录制 staging 失败 {Staging}", staging.DirectoryPath); }
            }
        }

        if (session.Failure is not null)
        {
            Log.Error(session.Failure, "录制失败");
            if (jsonOutput)
                WriteJson(new { type = "recording_failed", sampleId, error = session.Failure.ToString() });
            else
                Console.Error.WriteLine($"录制截图或落盘失败: {session.Failure}");
            return 1;
        }

        Log.Information("录制完成 EventCount={EventCount} ScreenshotCount={ScreenshotCount} DurationMs={DurationMs}",
            session.EventCount, session.ScreenshotCount, session.DurationMs);
        if (jsonOutput)
        {
            WriteJson(new
            {
                type = "recording_completed",
                sampleId,
                eventCount = session.EventCount,
                screenshotCount = session.ScreenshotCount,
                durationMs = session.DurationMs,
                sampleDirectory = repo.SampleDir(sampleId),
            });
        }
        else
        {
            Console.WriteLine($"录制完成: {session.EventCount} 事件, " +
                              $"{session.ScreenshotCount} 截图, {session.DurationMs}ms");
        }
        return 0;
    }

    private static void WriteJson(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value));
        Console.Out.Flush();
    }
}
