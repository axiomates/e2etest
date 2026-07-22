using E2ETest.Core.Comparing;
using E2ETest.Core.Model;

namespace E2ETest.Core.Tests;

public sealed class ComparisonRoundGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"e2etest-round-guard-{Guid.NewGuid():N}");

    [Fact]
    public void RejectsRoundWhoseStatusIsRunning()
    {
        Directory.CreateDirectory(_dir);
        var round = new ReplayRoundResult { Status = "running" };

        Assert.Throws<InvalidOperationException>(() => ComparisonRoundGuard.EnsureReady(_dir, round));
    }

    [Fact]
    public void RejectsRoundWithRunningLockEvenWhenResultSaysCompleted()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, ".running.lock"), "");
        var round = new ReplayRoundResult { Status = "completed" };

        Assert.Throws<InvalidOperationException>(() => ComparisonRoundGuard.EnsureReady(_dir, round));
    }

    [Fact]
    public void AcceptsFinishedRoundWithoutRunningLock()
    {
        Directory.CreateDirectory(_dir);
        var round = new ReplayRoundResult { Status = "completed" };

        ComparisonRoundGuard.EnsureReady(_dir, round);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
