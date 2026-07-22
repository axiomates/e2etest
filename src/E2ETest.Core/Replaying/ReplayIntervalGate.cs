using System.Globalization;
using E2ETest.Core.Storage;

namespace E2ETest.Core.Replaying;

/// <summary>跨 CLI 进程记录上一轮结束时刻，并在下一轮开始前执行冷却等待。</summary>
public static class ReplayIntervalGate
{
    public const string StateFileName = ".last-round-finished";

    public static TimeSpan CalculateRemaining(DateTimeOffset? lastFinishedAt, int intervalMs, DateTimeOffset now)
    {
        if (intervalMs < 0) throw new ArgumentOutOfRangeException(nameof(intervalMs));
        if (lastFinishedAt is null || intervalMs == 0) return TimeSpan.Zero;
        TimeSpan remaining = lastFinishedAt.Value.AddMilliseconds(intervalMs) - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public static DateTimeOffset? ReadLastFinishedAt(string replaysRoot)
    {
        string path = Path.Combine(replaysRoot, StateFileName);
        if (!File.Exists(path)) return null;
        string value = AtomicFile.ReadAllText(path).Trim();
        if (DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;
        throw new InvalidDataException($"回放轮次间隔状态文件无效: {path}");
    }

    public static void MarkRoundFinished(string replaysRoot, DateTimeOffset finishedAt)
    {
        Directory.CreateDirectory(replaysRoot);
        AtomicFile.WriteAllText(
            Path.Combine(replaysRoot, StateFileName),
            finishedAt.ToString("O", CultureInfo.InvariantCulture));
    }
}
