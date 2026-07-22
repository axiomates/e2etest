using E2ETest.Core.Model;
using E2ETest.Core.Native;
using E2ETest.Core.Replaying;
using E2ETest.Core.Storage;
using Serilog;
using Serilog.Context;

namespace E2ETest.Cli.Commands;

public static class ReplayCommand
{
    public static int Run(CliArgs args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(CliArgs args)
    {
        if (args.Has("sample"))
            throw new ArgumentException("--sample 已改为 --name。");

        string root = args.Get("root") ?? ".";
        string? requestedName = args.Get("name");
        string roundId = SafeId.Validate(
            args.Get("round") ?? $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..35],
            "round");
        using var roundLogContext = LogContext.PushProperty("RoundId", roundId);
        var repo = new TestCaseRepository(root);
        var config = ConfigStore.Load(repo.ConfigPath);
        var hooks = config.ReplayHooks ?? new ReplayHooksConfig();
        if (hooks.TimeoutMs <= 0)
            throw new InvalidDataException("replayHooks.timeoutMs 必须大于 0。");
        using var desktopLock = repo.AcquireDesktopLock();

        var names = requestedName is not null
            ? new List<string> { SafeId.ValidateTestCaseName(requestedName) }
            : repo.ListNames().OrderBy(x => x, StringComparer.CurrentCulture).ToList();
        if (names.Count == 0)
        {
            Console.Error.WriteLine("没有找到可回放的测试用例。");
            return 2;
        }

        string replaysRoot = Path.GetFullPath(Path.Combine(repo.Root, config.Paths.Replays));
        EnsureLocalPath(replaysRoot);
        Directory.CreateDirectory(replaysRoot);
        string roundDir = SafeId.ResolveChild(replaysRoot, roundId, "round");
        if (Directory.Exists(roundDir))
            throw new InvalidOperationException($"回放轮次已存在: {roundId}");
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
            TotalTestCases = names.Count,
        };
        string testCasesOutputRoot = Path.Combine(roundDir, "testcases");
        Directory.CreateDirectory(testCasesOutputRoot);
        string roundResultPath = Path.Combine(roundDir, "result.json");
        AtomicFile.WriteAllText(roundResultPath, Json.Serialize(round));

        Log.Information("开始回放 TestCaseCount={TestCaseCount}", names.Count);
        Console.WriteLine($"开始回放 {names.Count} 条测试用例，轮次: {roundId}");
        Console.WriteLine("回放期间控制台将隐藏，避免进入截图。");
        var consoleState = ConsoleWindow.CaptureAndHide();
        bool roundHookFailed = false;
        try
        {
            try
            {
                await HookCommandRunner.RunAsync("beforeRound", hooks.BeforeRound, hooks.TimeoutMs, cancellation.Token);
            }
            catch (Exception ex)
            {
                roundHookFailed = true;
                round.Error = $"beforeRound: {ex}";
                Log.Error(ex, "beforeRound hook 失败");
            }

            if (!roundHookFailed)
            {
                for (int index = 0; index < names.Count; index++)
                {
                    string name = names[index];
                    using var caseLogContext = LogContext.PushProperty("TestCaseName", name);
                    Log.Information("开始回放测试用例");
                    TestCaseReplayResult result;
                    try
                    {
                        result = await ReplayOneAsync(repo, name, testCasesOutputRoot, hooks, cancellation.Token);
                    }
                    catch (HookProcessTerminationException ex)
                    {
                        cancellation.Cancel();
                        result = FailedResult(name, ex);
                    }
                    catch (Exception ex)
                    {
                        result = FailedResult(name, ex);
                    }

                    try { PersistResult(testCasesOutputRoot, result); }
                    catch (Exception persistError)
                    {
                        result.Ok = false;
                        result.Status = "failed";
                        result.Error = $"{result.Error}{Environment.NewLine}result-write: {persistError}";
                    }
                    AddResult(round, result);
                    AtomicFile.WriteAllText(roundResultPath, Json.Serialize(round));
                    if (!cancellation.IsCancellationRequested) continue;

                    for (int remaining = index + 1; remaining < names.Count; remaining++)
                    {
                        var cancelled = CancelledResult(names[remaining]);
                        PersistResult(testCasesOutputRoot, cancelled);
                        AddResult(round, cancelled);
                    }
                    AtomicFile.WriteAllText(roundResultPath, Json.Serialize(round));
                    break;
                }
            }
            else
            {
                foreach (string name in names)
                {
                    var failed = FailedResult(name, new InvalidOperationException("beforeRound hook 失败，测试用例未执行。"));
                    PersistResult(testCasesOutputRoot, failed);
                    AddResult(round, failed);
                }
                AtomicFile.WriteAllText(roundResultPath, Json.Serialize(round));
            }
        }
        finally
        {
            try
            {
                try
                {
                    await HookCommandRunner.RunAsync("afterRound", hooks.AfterRound, hooks.TimeoutMs, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    roundHookFailed = true;
                    round.Error = AppendError(round.Error, $"afterRound: {ex}");
                    Log.Error(ex, "afterRound hook 失败");
                }
                ConsoleWindow.Restore(consoleState);
                round.FinishedAt = DateTimeOffset.UtcNow;
                round.Status = cancellation.IsCancellationRequested ? "cancelled" : roundHookFailed ? "failed" : "completed";
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

        foreach (var result in round.TestCases)
            Console.WriteLine($"[{(result.Ok ? "OK" : result.Status.ToUpperInvariant())}] {result.Name}: " +
                              (result.Ok ? $"{result.ScreenshotCount} 截图, {result.ElapsedMs}ms" : result.Error));
        Log.Information("回放完成 Succeeded={Succeeded} Failed={Failed} Cancelled={Cancelled} Directory={Directory}",
            round.SucceededTestCases, round.FailedTestCases, round.CancelledTestCases, roundDir);
        Console.WriteLine($"回放完成: 成功 {round.SucceededTestCases}, 失败 {round.FailedTestCases}, " +
                          $"取消 {round.CancelledTestCases}, 目录 {roundDir}");
        if (cancellation.IsCancellationRequested) return 130;
        return round.FailedTestCases == 0 && !roundHookFailed ? 0 : 1;
    }

    private static async Task<TestCaseReplayResult> ReplayOneAsync(
        TestCaseRepository repo,
        string name,
        string testCasesOutputRoot,
        ReplayHooksConfig hooks,
        CancellationToken cancellationToken)
    {
        var result = new TestCaseReplayResult
        {
            Name = name,
            StartedAt = DateTimeOffset.UtcNow,
        };
        string outputDir = SafeId.ResolveTestCase(testCasesOutputRoot, name);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputDir);
            using var snapshot = repo.LoadSnapshot(name);
            var manifest = snapshot.Manifest;

            await HookCommandRunner.RunAsync("beforeTestCase", hooks.BeforeTestCase, hooks.TimeoutMs, cancellationToken);

            var playback = await new ReplayPlayer().PlayAsync(
                manifest, snapshot.Directory, outputDir, cancellationToken);
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
        catch (HookProcessTerminationException)
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
            try
            {
                await HookCommandRunner.RunAsync("afterTestCase", hooks.AfterTestCase, hooks.TimeoutMs, CancellationToken.None);
            }
            catch (HookProcessTerminationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Status = "failed";
                result.Error = AppendError(result.Error, $"afterTestCase: {ex}");
            }
            result.FinishedAt = DateTimeOffset.UtcNow;
        }

        return result;
    }

    private static void EnsureLocalPath(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException("回放输出必须位于本地磁盘。");
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || new DriveInfo(root).DriveType == DriveType.Network)
            throw new InvalidOperationException("回放输出必须位于本地磁盘。");
    }

    private static void AddResult(ReplayRoundResult round, TestCaseReplayResult result)
    {
        round.TestCases.Add(result);
        switch (result.Status)
        {
            case "succeeded":
                round.SucceededTestCases++;
                Log.Information("测试用例回放成功 ScreenshotCount={ScreenshotCount} ElapsedMs={ElapsedMs}",
                    result.ScreenshotCount, result.ElapsedMs);
                break;
            case "cancelled":
                round.CancelledTestCases++;
                Log.Warning("测试用例回放取消");
                break;
            default:
                round.FailedTestCases++;
                Log.Error("测试用例回放失败 Error={Error}", result.Error);
                break;
        }
    }

    private static void PersistResult(string outputRoot, TestCaseReplayResult result)
    {
        string outputDir = SafeId.ResolveTestCase(outputRoot, result.Name);
        Directory.CreateDirectory(outputDir);
        AtomicFile.WriteAllText(Path.Combine(outputDir, "result.json"), Json.Serialize(result));
    }

    private static TestCaseReplayResult CancelledResult(string name) => new()
    {
        Name = name,
        Status = "cancelled",
        Error = "轮次已取消，测试用例未执行。",
        StartedAt = DateTimeOffset.UtcNow,
        FinishedAt = DateTimeOffset.UtcNow,
    };

    private static TestCaseReplayResult FailedResult(string name, Exception error) => new()
    {
        Name = name,
        Status = "failed",
        Error = error.ToString(),
        StartedAt = DateTimeOffset.UtcNow,
        FinishedAt = DateTimeOffset.UtcNow,
    };

    private static string AppendError(string? existing, string next) =>
        string.IsNullOrWhiteSpace(existing) ? next : $"{existing}{Environment.NewLine}{next}";
}
