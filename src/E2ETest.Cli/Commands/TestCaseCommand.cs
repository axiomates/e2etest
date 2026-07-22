using E2ETest.Core.Storage;

namespace E2ETest.Cli.Commands;

public static class TestCaseCommand
{
    public static int Run(CliArgs args)
    {
        var repo = new TestCaseRepository(DataRootResolver.Resolve(args.Get("root")));
        return args.Positional(0)?.ToLowerInvariant() switch
        {
            "list" => List(repo),
            "delete" => Delete(repo, args.Get("name")),
            _ => InvalidSubcommand(),
        };
    }

    private static int List(TestCaseRepository repo)
    {
        foreach (string name in repo.ListNames().OrderBy(x => x, StringComparer.CurrentCulture))
            Console.WriteLine(name);
        return 0;
    }

    private static int Delete(TestCaseRepository repo, string? requestedName)
    {
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

    private static int InvalidSubcommand()
    {
        Console.Error.WriteLine("用法: e2etest testcase list | testcase delete --name <名称>");
        return 2;
    }
}
