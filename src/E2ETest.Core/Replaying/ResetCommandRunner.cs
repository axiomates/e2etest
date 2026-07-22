using System.Diagnostics;
using Serilog;

namespace E2ETest.Core.Replaying;

public sealed class ResetProcessTerminationException : Exception
{
    public ResetProcessTerminationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>执行样例级 resetCommand；整个等待和终止流程均有界。</summary>
public static class ResetCommandRunner
{
    public static async Task RunAsync(
        string? command,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));

        string shell = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"/d /s /c \"{command}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        Log.Information("执行 resetCommand TimeoutMs={TimeoutMs} Command={ResetCommand}", timeoutMs, command);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 resetCommand");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Exception? killError = await TryTerminateProcessTreeAsync(process);

            Task exitTask = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(exitTask, Task.Delay(2000, CancellationToken.None)) != exitTask)
                throw new ResetProcessTerminationException(
                    "resetCommand 超时后进程树仍未退出，为避免污染后续样例，必须终止本轮回放。", killError);
            await exitTask;
            throw new TimeoutException(
                killError is null
                    ? $"resetCommand 超过 {timeoutMs}ms，进程树已终止。"
                    : $"resetCommand 超过 {timeoutMs}ms；终止请求报错但进程已退出: {killError.Message}",
                killError);
        }
        catch (OperationCanceledException)
        {
            Exception? killError = await TryTerminateProcessTreeAsync(process);
            Task exitTask = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(exitTask, Task.Delay(2000, CancellationToken.None)) != exitTask)
                throw new ResetProcessTerminationException(
                    "取消回放后 resetCommand 进程树仍未退出。", killError);
            await exitTask;
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"resetCommand 失败，退出码 {process.ExitCode}");
    }

    private static async Task<Exception?> TryTerminateProcessTreeAsync(Process process)
    {
        if (process.HasExited) return null;
        try
        {
            using var taskkill = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/PID {process.Id} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("无法启动 taskkill.exe");
            Task wait = taskkill.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(wait, Task.Delay(2000)) != wait)
            {
                try { taskkill.Kill(); } catch { }
                return new TimeoutException("taskkill.exe 未在 2 秒内结束。");
            }
            await wait;
            if (taskkill.ExitCode != 0 && !process.HasExited)
                return new InvalidOperationException($"taskkill 退出码 {taskkill.ExitCode}");
            return null;
        }
        catch (Exception ex)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception fallbackError) { return new AggregateException(ex, fallbackError); }
            return ex;
        }
    }
}
