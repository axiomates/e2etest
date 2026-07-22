using E2ETest.Core.Capture;
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
        string name = SafeId.ValidateTestCaseName(args.Get("name") ?? GenerateName());
        using var logContext = LogContext.PushProperty("TestCaseName", name);
        string root = args.Get("root") ?? ".";
        var repo = new TestCaseRepository(root);
        var config = ConfigStore.Load(repo.ConfigPath);
        using var desktopLock = repo.AcquireDesktopLock();
        using var writeLease = repo.AcquireWriteLease(name);

        if (repo.Exists(name))
        {
            Console.Error.WriteLine($"测试用例已存在: {name}。请先删除后再创建。");
            return 2;
        }

        bool fullscreen = config.Record.Fullscreen;
        if (args.Has("fullscreen")) fullscreen = true;
        if (args.Has("no-fullscreen")) fullscreen = false;

        var capture = CaptureRuleBuilder.Build(fullscreen);
        var screenshotKey = Hotkey.Parse(config.Hotkeys.Screenshot);
        var stopKey = Hotkey.Parse(config.Hotkeys.StartStop);
        RecordingStaging staging = repo.CreateRecordingStaging(name, writeLease);
        RecordSession? session = null;
        var consoleState = default(ConsoleWindow.VisibilityState);

        try
        {
            Log.Information("开始录制 Fullscreen={Fullscreen}", fullscreen);
            session = new RecordSession(repo, name, staging, writeLease,
                capture, screenshotKey, stopKey);

            Console.WriteLine($"开始录制测试用例 '{name}'");
            Console.WriteLine($"  截图键: {config.Hotkeys.Screenshot}");
            Console.WriteLine($"  停止键: {config.Hotkeys.StartStop}");
            Console.WriteLine($"  全屏模式: {fullscreen}");
            Console.WriteLine($"  截图区域: {capture.CropRect.X},{capture.CropRect.Y} {capture.CropRect.Width}x{capture.CropRect.Height}");
            Console.WriteLine("录制中... 控制台将隐藏，屏幕仅保留托盘图标。");
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
                catch (Exception ex) { Log.Error(ex, "清理录制 staging 失败 {Staging}", staging.DirectoryPath); }
            }
        }

        if (session.Failure is not null)
        {
            Log.Error(session.Failure, "录制失败");
            Console.Error.WriteLine($"录制截图或落盘失败: {session.Failure}");
            return 1;
        }

        Log.Information("录制完成 EventCount={EventCount} ScreenshotCount={ScreenshotCount} DurationMs={DurationMs}",
            session.EventCount, session.ScreenshotCount, session.DurationMs);
        Console.WriteLine($"录制完成: {session.EventCount} 事件, " +
                          $"{session.ScreenshotCount} 截图, {session.DurationMs}ms");
        return 0;
    }

    private static string GenerateName() =>
        $"testcase-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..38];
}
