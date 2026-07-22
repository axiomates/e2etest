using E2ETest.Cli;
using E2ETest.Cli.Commands;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string command = args[0].ToLowerInvariant();
var rest = CliArgs.Parse(args[1..]);

try
{
    return command switch
    {
        "record"   => RecordCommand.Run(rest),
        "config"   => ConfigCommand.Run(rest),
        "help" or "-h" or "--help" => Help(),
        _ => Unknown(command),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误: {ex.Message}");
    return 1;
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
      e2etest record  --sample <id> [--name <显示名>] [--fullscreen] [--root <dir>]
          录制一条测试样例（托盘图标，热键截图/停止，配置见 config.json）

      e2etest config  init [--root <dir>]
          生成默认 config.json（可在此设置热键、截图模式等）

      e2etest config  show [--root <dir>]
          打印当前配置

    (replay / compare / run 子命令后续实现)

    配置文件: <root>/config.json
      hotkeys.screenshot  截图热键，默认 F10（防止和软件冲突可修改）
      hotkeys.startStop   停止录制热键，默认 F12
      record.fullscreen   默认截图模式，false=裁掉任务栏，true=全屏
    """);
}
