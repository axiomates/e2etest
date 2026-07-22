using E2ETest.Core.Storage;

namespace E2ETest.Cli.Commands;

/// <summary>一个 round 内串联回放与比较；不重复实现两个命令的业务逻辑。</summary>
public static class RunCommand
{
    public static int Run(CliArgs args)
    {
        args.Validate(["round", "name", "root"], ["ai"]);
        string roundId = SafeId.Validate(args.Get("round") ?? ReplayCommand.CreateRoundId(), "round");
        var replayArgs = Forward(args, roundId, includeAi: false);
        Console.WriteLine($"执行测试: 轮次 {roundId}，阶段 1/2 回放。");
        int replayExitCode = ReplayCommand.Run(replayArgs);
        if (replayExitCode is 2 or 130) return replayExitCode;

        Console.WriteLine($"执行测试: 轮次 {roundId}，阶段 2/2 对比{(args.Has("ai") ? "（含 AI 复核）" : "")}。");
        int compareExitCode = CompareCommand.Run(Forward(args, roundId, includeAi: true));

        // 即使截图碰巧可比较，hook 或回放本身的失败也必须让整次 run 失败。
        return compareExitCode != 0 ? compareExitCode : replayExitCode;
    }

    private static CliArgs Forward(CliArgs source, string roundId, bool includeAi)
    {
        var values = new List<string> { "--round", roundId };
        AddValue("name");
        AddValue("root");
        if (includeAi && source.Has("ai")) values.Add("--ai");
        return CliArgs.Parse(values.ToArray());

        void AddValue(string name)
        {
            string? value = source.Get(name);
            if (value is not null) values.AddRange(["--" + name, value]);
        }
    }
}
