using E2ETest.Core.Model;
using E2ETest.Core.Native;
using E2ETest.Core.Replaying;
using E2ETest.Core.Storage;
using Serilog;
using Serilog.Context;

namespace E2ETest.Cli.Commands;

/// <summary>批量回放；每条样例和整轮结果均独立、原子落盘。</summary>
public static class ReplayCommand
{
    public static int Run(CliArgs args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(CliArgs args)
    {
        string root = args.Get("root") ?? ".";
        string? requestedSample = args.Get("sample");
        string roundId = SafeId.Validate(
            args.Get("round") ?? $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..35],
            "round");
        using var roundLogContext = LogContext.PushProperty("RoundId", roundId);
        var repo = new SampleRepository(root);
        var config = ConfigStore.Load(repo.ConfigPath);
        using var desktopLock = repo.AcquireDesktopLock();

        var sampleIds = requestedSample is not null
            ? new List<string> { SafeId.Validate(requestedSample, "sample") }
            : repo.ListSampleIds().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (sampleIds.Count == 0)
        {
            Console.Error.WriteLine("没有找到可回放的测试样例。");
            return 2;
        }

        string replaysRoot = Path.GetFullPath(Path.Combine(repo.Root, config.Paths.Replays));
        EnsureLocalPath(replaysRoot);
        Directory.CreateDirectory(replaysRoot);
        string roundDir = SafeId.ResolveChild(replaysRoot, roundId, "round");
        if (Directory.Exists(roundDir))
            throw new InvalidOperationException($"回放轮次已存在，拒绝混入旧结果: {roundId}");
        Directory.CreateDirectory(roundDir);
        string reservationPath = Path.Combine(roundDir, ".running.lock");
        using var roundReservation = new FileStream(
            reservationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        var round = new ReplayRoundResult
        {
            RoundId = roundId,
            StartedAt = DateTimeOffset.UtcNow,
            TotalSamples = sampleIds.Count,
        };
        string samplesOutputRoot = Path.Combine(roundDir, "samples");
        Directory.CreateDirectory(samplesOutputRoot);
        string roundResultPath = Path.Combine(roundDir, "result.json");
        AtomicFile.WriteAllText(roundResultPath, Json.Serialize(round));

        Log.Information("开始回放 SampleCount={SampleCount}", sampleIds.Count);
        Console.WriteLine($"开始回放 {sampleIds.Count} 条样例，轮次: {roundId}");
        Console.WriteLine("回放期间控制台将隐藏，避免进入截图。");
        var consoleState = ConsoleWindow.CaptureAndHide();
        try
        {
            for (int index = 0; index < sampleIds.Count; index++)
            {
                string sampleId = sampleIds[index];
                using var sampleLogContext = LogContext.PushProperty("SampleId", sampleId);
                Log.Information("开始回放样例");
                SampleReplayResult result;
                try
                {
                    result = await ReplayOneAsync(repo, sampleId, samplesOutputRoot, cancellation.Token);
                }
                catch (ResetProcessTerminationException ex)
                {
                    cancellation.Cancel();
                    result = FailedResult(sampleId, ex);
                }
                catch (Exception ex)
                {
                    result = FailedResult(sampleId, ex);
                }

                try { PersistSyntheticResult(samplesOutputRoot, result); }
                catch (Exception persistError)
                {
                    result.Ok = false;
                    result.Status = "failed";
                    result.Error = $"{result.Error}{Environment.NewLine}result-write: {persistError}";
                }
                AddResult(round, result);
                AtomicFile.WriteAllText(roundResultPath, Json.Serialize(round));
                if (!cancellation.IsCancellationRequested) continue;

                for (int remaining = index + 1; remaining < sampleIds.Count; remaining++)
                {
                    var cancelled = CancelledResult(sampleIds[remaining]);
                    PersistSyntheticResult(samplesOutputRoot, cancelled);
                    AddResult(round, cancelled);
                }
                AtomicFile.WriteAllText(roundResultPath, Json.Serialize(round));
                break;
            }
        }
        finally
        {
            try
            {
                ConsoleWindow.Restore(consoleState);
                round.FinishedAt = DateTimeOffset.UtcNow;
                round.Status = cancellation.IsCancellationRequested ? "cancelled" : "completed";
                AtomicFile.WriteAllText(roundResultPath, Json.Serialize(round));
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                roundReservation.Dispose();
                try { File.Delete(reservationPath); }
                catch (Exception ex) { Log.Warning(ex, "删除轮次运行锁失败 {LockPath}", reservationPath); }
            }
        }

        foreach (var result in round.Samples)
            Console.WriteLine($"[{(result.Ok ? "OK" : "FAIL")}] {result.SampleId}: " +
                              (result.Ok ? $"{result.ScreenshotCount} 截图, {result.ElapsedMs}ms" : result.Error));
        Log.Information("回放轮次完成 Succeeded={Succeeded} Failed={Failed} Cancelled={Cancelled} Directory={Directory}",
            round.SucceededSamples, round.FailedSamples, round.CancelledSamples, roundDir);
        Console.WriteLine($"回放完成: 成功 {round.SucceededSamples}, 失败 {round.FailedSamples}, " +
                          $"取消 {round.CancelledSamples}, 目录 {roundDir}");
        if (cancellation.IsCancellationRequested) return 130;
        return round.FailedSamples == 0 ? 0 : 1;
    }

    private static async Task<SampleReplayResult> ReplayOneAsync(
        SampleRepository repo, string sampleId, string samplesOutputRoot, CancellationToken cancellationToken)
    {
        var result = new SampleReplayResult
        {
            SampleId = sampleId,
            StartedAt = DateTimeOffset.UtcNow,
        };
        string sampleOutputDir = SafeId.ResolveChild(samplesOutputRoot, sampleId, "sample");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(sampleOutputDir);
            using var snapshot = repo.LoadSnapshot(sampleId);
            var manifest = snapshot.Manifest;
            result.DisplayName = manifest.DisplayName;
            ManifestValidator.Validate(manifest, snapshot.VersionDirectory, requireBaselineFiles: true);

            if (!string.IsNullOrWhiteSpace(manifest.Replay.ResetCommand))
            {
                await ResetCommandRunner.RunAsync(
                    manifest.Replay.ResetCommand,
                    manifest.Replay.ResetTimeoutMs,
                    cancellationToken);
                if (manifest.Replay.ResetWaitMs > 0)
                    await Task.Delay(manifest.Replay.ResetWaitMs, cancellationToken);
            }

            var playback = await new ReplayPlayer().PlayAsync(
                manifest, snapshot.VersionDirectory, sampleOutputDir, cancellationToken);
            result.Shots = playback.Shots;
            result.ScreenshotCount = playback.Shots.Count(s => s.Ok);
            result.ElapsedMs = playback.ElapsedMs;
            if (playback.Errors.Count == 0 &&
                playback.Shots.Count == manifest.Shots.Count &&
                playback.Shots.All(s => s.Ok))
            {
                result.Ok = true;
                result.Status = "succeeded";
            }
            else
            {
                result.Status = cancellationToken.IsCancellationRequested ? "cancelled" : "failed";
                result.Error = string.Join(Environment.NewLine, playback.Errors);
                if (result.Error.Length == 0) result.Error = "回放截图数量不完整。";
            }
        }
        catch (ResetProcessTerminationException)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            result.Status = "cancelled";
            result.Error = ex.ToString();
        }
        catch (Exception ex)
        {
            result.Status = "failed";
            result.Error = ex.ToString();
        }
        finally
        {
            result.FinishedAt = DateTimeOffset.UtcNow;
            try
            {
                Directory.CreateDirectory(sampleOutputDir);
                AtomicFile.WriteAllText(Path.Combine(sampleOutputDir, "result.json"), Json.Serialize(result));
            }
            catch (Exception writeError)
            {
                result.Ok = false;
                result.Status = "failed";
                result.Error = string.Join(Environment.NewLine,
                    new[] { result.Error, $"result-write: {writeError}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
        }

        return result;
    }

    private static void EnsureLocalPath(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException("回放输出必须位于本地磁盘，不能使用网络路径。");
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || new DriveInfo(root).DriveType == DriveType.Network)
            throw new InvalidOperationException("回放输出必须位于本地磁盘。");
    }

    private static void AddResult(ReplayRoundResult round, SampleReplayResult result)
    {
        round.Samples.Add(result);
        switch (result.Status)
        {
            case "succeeded":
                round.SucceededSamples++;
                Log.Information("样例回放成功 ScreenshotCount={ScreenshotCount} ElapsedMs={ElapsedMs}",
                    result.ScreenshotCount, result.ElapsedMs);
                break;
            case "cancelled":
                round.CancelledSamples++;
                Log.Warning("样例回放取消");
                break;
            default:
                round.FailedSamples++;
                Log.Error("样例回放失败 Error={Error}", result.Error);
                break;
        }
    }

    private static void PersistSyntheticResult(string samplesOutputRoot, SampleReplayResult result)
    {
        string outputDir = SafeId.ResolveChild(samplesOutputRoot, result.SampleId, "sample");
        Directory.CreateDirectory(outputDir);
        AtomicFile.WriteAllText(Path.Combine(outputDir, "result.json"), Json.Serialize(result));
    }

    private static SampleReplayResult CancelledResult(string sampleId) => new()
    {
        SampleId = sampleId,
        Status = "cancelled",
        Error = "轮次已取消，样例未执行。",
        StartedAt = DateTimeOffset.UtcNow,
        FinishedAt = DateTimeOffset.UtcNow,
    };

    private static SampleReplayResult FailedResult(string sampleId, Exception error) => new()
    {
        SampleId = sampleId,
        Status = "failed",
        Ok = false,
        Error = error.ToString(),
        StartedAt = DateTimeOffset.UtcNow,
        FinishedAt = DateTimeOffset.UtcNow,
    };
}
