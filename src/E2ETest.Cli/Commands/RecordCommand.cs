using System.Windows.Forms;
using E2ETest.Core.Capture;
using E2ETest.Core.Recording;
using E2ETest.Core.Storage;

namespace E2ETest.Cli.Commands;

/// <summary>
/// e2etest record --sample &lt;id&gt; [--name &lt;显示名&gt;] [--fullscreen|--no-fullscreen] [--root &lt;dir&gt;]
/// fullscreen 优先级: CLI flag > config.json record.fullscreen (默认 false)
/// </summary>
public static class RecordCommand
{
    public static int Run(CliArgs args)
    {
        string? sampleId = args.Get("sample");
        if (string.IsNullOrWhiteSpace(sampleId))
        {
            Console.Error.WriteLine("缺少 --sample <id>，例: e2etest record --sample login-flow");
            return 2;
        }

        string root = args.Get("root") ?? ".";
        var repo = new SampleRepository(root);
        var config = ConfigStore.Load(repo.ConfigPath);

        string displayName = args.Get("name") ?? sampleId;

        // CLI flag 覆盖 config 默认值
        bool fullscreen = config.Record.Fullscreen;
        if (args.Has("fullscreen")) fullscreen = true;
        if (args.Has("no-fullscreen")) fullscreen = false;

        var capture = CaptureRuleBuilder.Build(fullscreen);
        var screenshotKey = Hotkey.Parse(config.Hotkeys.Screenshot);
        var stopKey = Hotkey.Parse(config.Hotkeys.StartStop);

        Console.WriteLine($"开始录制 '{sampleId}' ({displayName})");
        Console.WriteLine($"  截图热键: {config.Hotkeys.Screenshot}");
        Console.WriteLine($"  停止热键: {config.Hotkeys.StartStop}");
        Console.WriteLine($"  全屏模式: {fullscreen}");
        Console.WriteLine($"  裁剪区域: {capture.CropRect.X},{capture.CropRect.Y} {capture.CropRect.Width}x{capture.CropRect.Height}");
        Console.WriteLine("录制中... 控制台将隐藏，屏幕仅保留托盘图标。按停止热键结束。");

        ApplicationConfiguration.Initialize();
        // 隐藏控制台窗口，避免它出现在截图里；结束后恢复以打印保存结果。
        E2ETest.Core.Native.ConsoleWindow.Hide();
        try
        {
            Application.Run(new RecordSession(repo, sampleId, displayName, capture, screenshotKey, stopKey));
        }
        finally
        {
            E2ETest.Core.Native.ConsoleWindow.Show();
        }
        return 0;
    }
}
