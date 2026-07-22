using E2ETest.Core.Model;
using E2ETest.Core.Storage;

namespace E2ETest.Cli.Commands;

/// <summary>e2etest config init | show — 管理 config.json。</summary>
public static class ConfigCommand
{
    public static int Run(CliArgs args)
    {
        string root = args.Get("root") ?? ".";
        var repo = new SampleRepository(root);

        string sub = args.Positional(0) ?? "show";
        return sub switch
        {
            "init" => Init(repo),
            "show" => Show(repo),
            _ => Show(repo),
        };
    }

    public static int Init(SampleRepository repo)
    {
        if (File.Exists(repo.ConfigPath))
        {
            Console.WriteLine($"config.json 已存在: {repo.ConfigPath}");
            return 0;
        }
        ConfigStore.Save(repo.ConfigPath, new AppConfig());
        Console.WriteLine($"已生成默认配置: {repo.ConfigPath}");
        Console.WriteLine("可编辑以下字段来避免按键冲突（录制控制键仅支持单键）:");
        Console.WriteLine("  hotkeys.screenshot  (默认 F11)");
        Console.WriteLine("  hotkeys.startStop   (默认 F12)");
        Console.WriteLine("  record.fullscreen   (默认 false)");
        return 0;
    }

    private static int Show(SampleRepository repo)
    {
        var cfg = ConfigStore.Load(repo.ConfigPath);
        Console.WriteLine(E2ETest.Core.Storage.Json.Serialize(cfg));
        return 0;
    }
}
