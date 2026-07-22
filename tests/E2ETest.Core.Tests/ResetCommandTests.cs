using System.Diagnostics;
using E2ETest.Core.Replaying;

namespace E2ETest.Core.Tests;

public sealed class HookCommandTests
{
    [Fact]
    public async Task TimeoutIsBoundedAndDoesNotHang()
    {
        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            HookCommandRunner.RunAsync(
                "test",
                "powershell -NoProfile -Command \"Start-Sleep -Seconds 30\"",
                timeoutMs: 100));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }
}
