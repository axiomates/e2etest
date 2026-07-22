using E2ETest.Core.Model;

namespace E2ETest.Core.Comparing;

/// <summary>compare 只能读取已经退出 replay 写入阶段的 round。</summary>
public static class ComparisonRoundGuard
{
    public static void EnsureReady(string roundDirectory, ReplayRoundResult round)
    {
        if (string.Equals(round.Status, "running", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(roundDirectory, ".running.lock")))
            throw new InvalidOperationException($"回放轮次仍在运行，暂不能对比: {round.RoundId}");
    }
}
