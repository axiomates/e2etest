using E2ETest.Core.Comparing;
using E2ETest.Core.Model;
using E2ETest.Core.Storage;

namespace E2ETest.Cli.Commands;

public static class CompareCommand
{
    public static int Run(CliArgs args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return RunCore(args, cancellation.Token); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static int RunCore(CliArgs args, CancellationToken cancellationToken)
    {
        args.Validate(["round", "name", "root"], ["ai"]);
        string? requestedRound = args.Get("round");
        if (string.IsNullOrWhiteSpace(requestedRound)) throw new ArgumentException("compare 必须指定 --round <roundId>。");
        string roundId = SafeId.Validate(requestedRound, "round");
        string? requestedName = args.Get("name");
        if (requestedName is not null) requestedName = SafeId.ValidateTestCaseName(requestedName);

        var repo = new TestCaseRepository(DataRootResolver.Resolve(args.Get("root")));
        var config = ConfigStore.Load(repo.ConfigPath);
        bool useAi = args.Has("ai");
        if (useAi)
        {
            if (string.IsNullOrWhiteSpace(config.Ai.ApiKey)) config.Ai.ApiKey = Environment.GetEnvironmentVariable("E2ETEST_AI_API_KEY") ?? "";
            if (string.IsNullOrWhiteSpace(config.Ai.BaseUrl)) config.Ai.BaseUrl = Environment.GetEnvironmentVariable("E2ETEST_AI_BASE_URL") ?? "";
            if (string.IsNullOrWhiteSpace(config.Ai.Model)) config.Ai.Model = Environment.GetEnvironmentVariable("E2ETEST_AI_MODEL") ?? "";
        }

        string replaysRoot = Path.GetFullPath(Path.Combine(repo.Root, config.Paths.Replays));
        string roundDir = SafeId.ResolveChild(replaysRoot, roundId, "round");
        string replayResultPath = Path.Combine(roundDir, "result.json");
        if (!File.Exists(replayResultPath)) throw new DirectoryNotFoundException($"回放轮次不存在: {roundId}");
        var replayRound = Json.Deserialize<ReplayRoundResult>(AtomicFile.ReadAllText(replayResultPath));
        ComparisonRoundGuard.EnsureReady(roundDir, replayRound);

        string reportsRoot = Path.GetFullPath(Path.Combine(repo.Root, config.Paths.Reports));
        string reportDir = SafeId.ResolveChild(reportsRoot, roundId, "round");
        using var compareLock = ComparisonRoundGuard.AcquireCompareLock(reportDir);
        var report = new ComparisonRoundResult
        {
            RoundId = roundId,
            ReplayStatus = replayRound.Status,
            ReplayError = replayRound.Error,
            ReplayLifecycleSucceeded = string.Equals(replayRound.Status, "completed", StringComparison.OrdinalIgnoreCase),
            StartedAt = DateTimeOffset.UtcNow,
        };
        var selected = (requestedName is null
            ? replayRound.TestCases
            : replayRound.TestCases.Where(item => item.Name == requestedName)).ToList();
        if (requestedName is not null && selected.Count == 0)
        {
            var skipped = new TestCaseComparisonResult
            {
                Name = requestedName, Status = "skipped", Error = "本轮未找到该测试用例的 replay 结果。",
                FinalVerdict = "skipped", Ai = new AiAssessment { Status = "skipped", Reason = "testcase_not_in_round" },
            };
            report.TestCases.Add(skipped);
            PersistCase(reportDir, skipped);
        }

        var comparer = new PixelComparer();
        for (int index = 0; index < selected.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                AddCancelled(reportDir, report, selected.Skip(index));
                break;
            }

            var replayCase = selected[index];
            Console.WriteLine($"对比 [{index + 1}/{selected.Count}] {replayCase.Name}: 计算像素差异…");
            var caseResult = new TestCaseComparisonResult { Name = replayCase.Name };
            string caseOutputDir = SafeId.ResolveTestCase(Path.Combine(reportDir, "testcases"), replayCase.Name);
            bool cancelled = false;
            try
            {
                using var snapshot = repo.LoadSnapshotForComparison(replayCase.Name);
                TestCaseManifest manifest = snapshot.Manifest;
                caseResult.DurationMs = manifest.DurationMs;
                caseResult.TestFocus = manifest.TestFocus;
                caseResult.AcceptanceCriteria = manifest.AcceptanceCriteria;
                foreach (var replayShot in replayCase.Shots.OrderBy(item => item.ShotIndex))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var manifestShot = manifest.Shots.FirstOrDefault(item => item.Index == replayShot.ShotIndex);
                    string baselinePath = manifestShot is null ? "" : Path.GetFullPath(Path.Combine(snapshot.Directory, manifestShot.File));
                    string replayPath = Path.Combine(roundDir, "testcases", replayCase.Name, $"shot-{replayShot.ShotIndex:D4}.png");
                    string? hardFailure = null;
                    string? error = null;
                    if (manifestShot is null || !replayShot.Ok || !File.Exists(replayPath) || !File.Exists(baselinePath))
                    {
                        hardFailure = "image_missing";
                        error = replayShot.Error ?? "缺少可比较的 manifest、baseline 或 replay 截图。";
                    }
                    else if (!string.IsNullOrWhiteSpace(replayShot.BaselineSha256) && !BaselineIdentity.Matches(baselinePath, replayShot.BaselineSha256))
                    {
                        hardFailure = "baseline_changed";
                        error = "baseline 在 replay 之后发生变化，不能与该 round 对比。";
                    }

                    if (hardFailure is not null)
                    {
                        caseResult.Shots.Add(new ShotComparisonResult
                        {
                            ShotIndex = replayShot.ShotIndex, Status = "failed", BaselinePath = baselinePath, ReplayPath = replayPath,
                            FinalVerdict = "failed", HardFailureCode = hardFailure,
                            Ai = new AiAssessment { Status = "skipped", Reason = "hard_failure" }, Error = error,
                        });
                    }
                    else
                    {
                        caseResult.Shots.Add(comparer.Compare(baselinePath, replayPath, caseOutputDir, replayShot.ShotIndex, config.Pixel));
                    }
                }

                foreach (var shot in caseResult.Shots)
                    shot.AtMs = manifest.Shots.FirstOrDefault(item => item.Index == shot.ShotIndex)?.AtMs;
                IncidentAggregator.Finalize(caseResult, config.Pixel);
                EvidenceSheetBuilder.GenerateAll(caseResult, caseOutputDir, config.Ai.MaxImageDimension);
                if (!replayCase.Ok)
                {
                    caseResult.Status = caseResult.FinalVerdict = "failed";
                    caseResult.Error = replayCase.Error ?? $"回放未成功完成（状态: {replayCase.Status}）。";
                    caseResult.Ai = new AiAssessment { Status = "skipped", Reason = "replay_failure" };
                }
                else if (caseResult.Shots.Any(item => item.HardFailureCode is not null))
                {
                    caseResult.FinalVerdict = "failed";
                    caseResult.Ai = new AiAssessment { Status = "skipped", Reason = "hard_failure" };
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    caseResult.ComparisonCancelled = true;
                    caseResult.Ai = new AiAssessment { Status = "cancelled", Reason = "user_cancelled" };
                }
                else if (useAi)
                {
                    if (caseResult.Shots.All(item => item.Ai.Status == "skipped"))
                    {
                        caseResult.Ai = new AiAssessment { Status = "skipped", Reason = "all_shots_skipped" };
                    }
                    else
                    {
                        Console.WriteLine($"对比 [{index + 1}/{selected.Count}] {replayCase.Name}: AI 语义复核…（Ctrl+C 可取消）");
                        try { new AiCaseReviewer().ReviewAsync(caseResult, caseOutputDir, config.Ai, cancellationToken).GetAwaiter().GetResult(); }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            cancelled = true;
                            caseResult.ComparisonCancelled = true;
                            caseResult.Ai = new AiAssessment { Status = "cancelled", Reason = "user_cancelled" };
                        }
                        catch (Exception ex) { caseResult.Ai = new AiAssessment { Status = "failed", Reason = ex.Message }; }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                caseResult.Status = caseResult.FinalVerdict = "cancelled";
                caseResult.ComparisonCancelled = true;
                caseResult.Ai = new AiAssessment { Status = "cancelled", Reason = "user_cancelled" };
            }
            catch (Exception ex)
            {
                caseResult.Status = caseResult.FinalVerdict = "failed";
                caseResult.Ai = new AiAssessment { Status = "skipped", Reason = "comparison_error" };
                caseResult.Error = ex.ToString();
            }

            report.TestCases.Add(caseResult);
            PersistCase(reportDir, caseResult);
            Console.WriteLine($"对比 [{index + 1}/{selected.Count}] {replayCase.Name}: {caseResult.FinalVerdict}");
            if (cancelled || cancellationToken.IsCancellationRequested)
            {
                report.ComparisonCancelled = true;
                AddCancelled(reportDir, report, selected.Skip(index + 1));
                break;
            }
        }

        Summarize(report);
        report.FinishedAt = DateTimeOffset.UtcNow;
        if (ComparisonReportPolicy.ShouldWriteRoundSummary(requestedName))
            AtomicFile.WriteAllText(Path.Combine(reportDir, "result.json"), Json.Serialize(report));
        string output = requestedName is null ? Path.Combine(reportDir, "result.json") : Path.Combine(reportDir, "testcases", requestedName, "result.json");
        Console.WriteLine($"对比完成: 本地通过 {report.PassedTestCases}, 本地失败 {report.FailedTestCases}, 本地待确认 {report.UncertainTestCases}; " +
                          $"最终通过 {report.FinalPassedTestCases}, 最终失败 {report.FinalFailedTestCases}, 最终待确认 {report.FinalNeedsReviewTestCases}, " +
                          $"取消 {report.CancelledTestCases}, 回放生命周期 {(report.ReplayLifecycleSucceeded ? "成功" : "失败")}, 跳过 {report.SkippedTestCases}, 报告 {output}");
        if (report.ComparisonCancelled) return 130;
        return report.ReplayLifecycleSucceeded && report.FinalFailedTestCases == 0 && report.FinalNeedsReviewTestCases == 0 ? 0 : 1;
    }

    private static void PersistCase(string reportDir, TestCaseComparisonResult result)
    {
        string directory = SafeId.ResolveTestCase(Path.Combine(reportDir, "testcases"), result.Name);
        Directory.CreateDirectory(directory);
        AtomicFile.WriteAllText(Path.Combine(directory, "result.json"), Json.Serialize(result));
        ReportArtifactCleaner.Clean(directory, result);
    }

    private static void AddCancelled(string reportDir, ComparisonRoundResult report, IEnumerable<TestCaseReplayResult> remaining)
    {
        report.ComparisonCancelled = true;
        foreach (var replayCase in remaining)
        {
            var result = new TestCaseComparisonResult
            {
                Name = replayCase.Name, Status = "cancelled", FinalVerdict = "cancelled",
                ComparisonCancelled = true,
                Error = "compare 被用户取消。", Ai = new AiAssessment { Status = "cancelled", Reason = "user_cancelled" },
            };
            report.TestCases.Add(result);
            PersistCase(reportDir, result);
        }
    }

    private static void Summarize(ComparisonRoundResult report)
    {
        report.PassedTestCases = report.TestCases.Count(item => item.Status == "passed");
        report.FailedTestCases = report.TestCases.Count(item => item.Status == "failed");
        report.UncertainTestCases = report.TestCases.Count(item => item.Status == "uncertain");
        report.SkippedTestCases = report.TestCases.Count(item => item.Status == "skipped");
        report.CancelledTestCases = report.TestCases.Count(item => item.ComparisonCancelled || item.Status == "cancelled" || item.FinalVerdict == "cancelled");
        report.FinalPassedTestCases = report.TestCases.Count(item => item.FinalVerdict == "passed");
        report.FinalFailedTestCases = report.TestCases.Count(item => item.FinalVerdict == "failed");
        report.FinalNeedsReviewTestCases = report.TestCases.Count(item => item.FinalVerdict is "uncertain" or "needs_review");
    }
}
