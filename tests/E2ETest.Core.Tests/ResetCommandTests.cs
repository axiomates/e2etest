using System.Diagnostics;
using E2ETest.Core.Replaying;

namespace E2ETest.Core.Tests;

public sealed class HookCommandTests
{
    [Fact]
    public async Task PowerShellFileCommandSupportsQuotedPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "e2etest hook scripts " + Guid.NewGuid().ToString("N"));
        string script = Path.Combine(root, "write marker.ps1");
        string output = Path.Combine(root, "hook output.txt");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(script, "param([string]$OutputPath)\n[IO.File]::WriteAllText($OutputPath, 'ok')\n");

            await HookCommandRunner.RunAsync(
                "test",
                $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{script}\" -OutputPath \"{output}\"",
                timeoutMs: 5000);

            Assert.Equal("ok", File.ReadAllText(output));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

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
