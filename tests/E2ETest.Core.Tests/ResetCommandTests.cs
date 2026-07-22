using System.Diagnostics;
using E2ETest.Core.Replaying;

namespace E2ETest.Core.Tests;

public sealed class ResetCommandTests
{
    [Fact]
    public async Task TimeoutIsBoundedAndDoesNotHang()
    {
        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            ResetCommandRunner.RunAsync(
                "powershell -NoProfile -Command \"Start-Sleep -Seconds 30\"",
                timeoutMs: 100));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }
}
