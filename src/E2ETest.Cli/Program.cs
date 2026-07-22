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
    e2etest — 端到端录制/回放/对比测试工具

    用法:
      e2etest record  --sample <id> [--name <显示名>] [--fullscreen] [--json] [--root <dir>]
          录制一条测试样例；--json 输出供 GUI 监听的 NDJSON 状态事件

      e2etest replay  [--sample <id>] [--round <id>] [--root <dir>]
          回放全部或指定样例，按时间轴注入输入并截图；单条失败不影响后续样例

      e2etest config  init [--root <dir>]
          生成默认 config.json（可在此设置热键、截图模式等）

      e2etest config  show [--root <dir>]
          打印当前配置

    (compare / run 子命令后续实现)

    配置文件: <root>/config.json
      hotkeys.screenshot  截图键，默认 F11（仅支持单键）
      hotkeys.startStop   停止录制键，默认 F12（仅支持单键）
      record.fullscreen   默认截图模式，false=裁掉任务栏，true=全屏
    """);
}
