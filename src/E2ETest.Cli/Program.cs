using E2ETest.Cli;
using E2ETest.Cli.Commands;
using Serilog;

// 必须在任何屏幕坐标、任务栏、截图或 SendInput API 之前统一 DPI 坐标空间。
ApplicationConfiguration.Initialize();

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string command = args[0].ToLowerInvariant();
var rest = CliArgs.Parse(args[1..]);
LoggingBootstrap.Configure(rest.Get("root") ?? ".");

try
{
    Log.Information("命令开始 {Command}", command);
    int exitCode = command switch
    {
        "record" => RecordCommand.Run(rest),
        "replay" => ReplayCommand.Run(rest),
        "compare" => CompareCommand.Run(rest),
        "testcase" => TestCaseCommand.Run(rest),
        "config" => ConfigCommand.Run(rest),
        "help" or "-h" or "--help" => Help(),
        _ => Unknown(command),
    };
    Log.Information("命令结束 {Command} ExitCode={ExitCode}", command, exitCode);
    return exitCode;
}
catch (Exception ex)
{
    Log.Error(ex, "命令失败 {Command}", command);
    Console.Error.WriteLine($"错误: {ex.Message}");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static int Help() { PrintUsage(); return 0; }

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"未知命令: {cmd}");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("""
    e2etest — Windows 桌面端到端录制与回放工具

    用法:
      e2etest record [--name <名称>] [--fullscreen | --no-fullscreen] [--root <dir>]
          创建测试用例；省略 --name 时自动生成名称

      e2etest replay [--name <名称>] [--round <id>] [--root <dir>]
          回放全部或指定测试用例；单条失败不影响后续用例

      e2etest compare --round <id> [--name <名称>] [--root <dir>]
          对已有回放轮次执行本地像素对比

      e2etest testcase list [--root <dir>]
          列出测试用例

      e2etest testcase delete --name <名称> [--root <dir>]
          删除测试用例

      e2etest config init | show [--root <dir>]
          生成或显示 config.json

    配置文件: <root>/config.json
      hotkeys.screenshot  截图键，默认 F11（仅支持单键）
      hotkeys.startStop   停止录制键，默认 F12（仅支持单键）
      record.fullscreen   默认截图模式，false=裁掉任务栏，true=全屏
    """);
}
