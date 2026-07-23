using E2ETest.Core.Storage;

namespace E2ETest.Cli.Commands;

public static class TestCaseCommand
{
    public static int Run(CliArgs args)
    {
        var repo = new TestCaseRepository(DataRootResolver.Resolve(args.Get("root")));
        return args.Positional(0)?.ToLowerInvariant() switch
        {
            "list" => List(repo, args),
            "delete" => Delete(repo, args),
            "annotate" => Annotate(repo, args),
            _ => InvalidSubcommand(),
        };
    }

    private static int List(TestCaseRepository repo, CliArgs args)
    {
        args.Validate(["root"], [], maximumPositionals: 1);
        foreach (string name in repo.ListNames().OrderBy(x => x, StringComparer.CurrentCulture))
            Console.WriteLine(name);
        return 0;
    }

    private static int Delete(TestCaseRepository repo, CliArgs args)
    {
        args.Validate(["name", "root"], [], maximumPositionals: 1);
        string? requestedName = args.Get("name");
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            Console.Error.WriteLine("缺少 --name <测试用例名称>。");
            return 2;
        }

        string name = SafeId.ValidateTestCaseName(requestedName);
        if (!repo.Delete(name))
        {
            Console.Error.WriteLine($"测试用例不存在: {name}");
            return 2;
        }

        Console.WriteLine($"已删除测试用例: {name}");
        return 0;
    }

    private static int Annotate(TestCaseRepository repo, CliArgs args)
    {
        args.Validate(["name", "focus", "criteria", "root"], [], maximumPositionals: 1);
        string? requestedName = args.Get("name");
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            Console.Error.WriteLine("缺少 --name <测试用例名称>。");
            return 2;
        }
        if (!args.Has("focus") && !args.Has("criteria"))
        {
            Console.Error.WriteLine("至少提供 --focus <测试重点> 或 --criteria <判断标准>。");
            return 2;
        }

        string name = SafeId.ValidateTestCaseName(requestedName);
        var manifest = repo.UpdateAiGuidance(name, args.Get("focus"), args.Get("criteria"));
        Console.WriteLine($"已更新测试用例 AI 指引: {name}");
        Console.WriteLine($"  测试重点: {manifest.TestFocus ?? "（未设置）"}");
        Console.WriteLine($"  判断标准: {manifest.AcceptanceCriteria ?? "（未设置）"}");
        return 0;
    }

    private static int InvalidSubcommand()
    {
        Console.Error.WriteLine("用法: e2etest testcase list | testcase delete --name <名称> | testcase annotate --name <名称> [--focus <重点>] [--criteria <标准>]");
        return 2;
    }
}
