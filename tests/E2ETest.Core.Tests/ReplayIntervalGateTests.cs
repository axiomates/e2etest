using E2ETest.Core.Model;
using E2ETest.Core.Replaying;
using E2ETest.Core.Storage;

namespace E2ETest.Core.Tests;

public sealed class ReplayIntervalGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "e2etest-replay-interval-" + Guid.NewGuid().ToString("N"));

    public ReplayIntervalGateTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void DefaultConfigLeavesNonZeroSpacingBetweenCasesAndRounds()
    {
        var config = Json.Deserialize<AppConfig>("{}");

        Assert.Equal(10000, config.Replay.BetweenTestCasesMs);
        Assert.Equal(20000, config.Replay.BetweenRoundsMs);
    }

    [Fact]
    public void CalculatesOnlyTheUnelapsedPartOfRoundInterval()
    {
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 10, TimeSpan.Zero);

        TimeSpan remaining = ReplayIntervalGate.CalculateRemaining(now.AddMilliseconds(-1200), 3000, now);

        Assert.Equal(TimeSpan.FromMilliseconds(1800), remaining);
        Assert.Equal(TimeSpan.Zero, ReplayIntervalGate.CalculateRemaining(now.AddSeconds(-4), 3000, now));
        Assert.Equal(TimeSpan.Zero, ReplayIntervalGate.CalculateRemaining(null, 3000, now));
    }

    [Fact]
    public void PersistsLastRoundFinishAcrossProcesses()
    {
        var finishedAt = new DateTimeOffset(2026, 7, 22, 12, 0, 0, 123, TimeSpan.Zero);

        ReplayIntervalGate.MarkRoundFinished(_root, finishedAt);

        Assert.Equal(finishedAt, ReplayIntervalGate.ReadLastFinishedAt(_root));
    }

    [Fact]
    public void RejectsNegativeIntervals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReplayIntervalGate.CalculateRemaining(DateTimeOffset.UtcNow, -1, DateTimeOffset.UtcNow));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
