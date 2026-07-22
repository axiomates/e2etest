using E2ETest.Core.Comparing;
using E2ETest.Core.Model;
using E2ETest.Core.Storage;

namespace E2ETest.Cli.Commands;

public static class CompareCommand
{
    public static int Run(CliArgs args)
    {
        string? requestedRound = args.Get("round");
        if (string.IsNullOrWhiteSpace(requestedRound)) throw new ArgumentException("compare 必须指定 --round <roundId>。");
        string roundId = SafeId.Validate(requestedRound, "round");
        string? requestedName = args.Get("name");
        if (requestedName is not null) requestedName = SafeId.ValidateTestCaseName(requestedName);

        var repo = new TestCaseRepository(args.Get("root") ?? ".");
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

        string reportsRoot = Path.GetFullPath(Path.Combine(repo.Root, config.Paths.Reports));
        string reportDir = SafeId.ResolveChild(reportsRoot, roundId, "round");
        Directory.CreateDirectory(reportDir);
        var report = new ComparisonRoundResult { RoundId = roundId, StartedAt = DateTimeOffset.UtcNow };
        IEnumerable<TestCaseReplayResult> candidates = requestedName is null
            ? replayRound.TestCases
            : replayRound.TestCases.Where(item => item.Name == requestedName);
        var selected = candidates.ToList();
        if (requestedName is not null && selected.Count == 0)
        {
            report.TestCases.Add(new TestCaseComparisonResult
            {
                Name = requestedName, Status = "skipped", Error = "本轮未找到该测试用例的 replay 结果。",
                FinalVerdict = "skipped", Ai = new AiAssessment { Status = "skipped", Reason = "testcase_not_in_round" },
            });
        }

        var comparer = new PixelComparer();
        foreach (var replayCase in selected)
        {
            var caseResult = new TestCaseComparisonResult { Name = replayCase.Name };
            string caseOutputDir = SafeId.ResolveTestCase(Path.Combine(reportDir, "testcases"), replayCase.Name);
            try
            {
                TestCaseManifest? manifest = TryLoadManifest(repo.TestCaseDir(replayCase.Name));
                caseResult.DurationMs = manifest?.DurationMs;
                foreach (var shot in replayCase.Shots.OrderBy(item => item.ShotIndex))
                {
                    string baselinePath = Path.Combine(repo.TestCaseDir(replayCase.Name), "baseline", $"shot-{shot.ShotIndex:D4}.png");
                    string replayPath = Path.Combine(roundDir, "testcases", replayCase.Name, $"shot-{shot.ShotIndex:D4}.png");
                    if (!shot.Ok || !File.Exists(replayPath) || !File.Exists(baselinePath))
                    {
                        caseResult.Shots.Add(new ShotComparisonResult
                        {
                            ShotIndex = shot.ShotIndex, Status = "failed", BaselinePath = baselinePath, ReplayPath = replayPath,
                            FinalVerdict = "failed", HardFailureCode = "image_missing",
                            Ai = new AiAssessment { Status = "skipped", Reason = "hard_failure" },
                            Error = shot.Error ?? "缺少可比较的 baseline 或 replay 截图。",
                        });
                        continue;
                    }
                    caseResult.Shots.Add(comparer.Compare(baselinePath, replayPath, caseOutputDir, shot.ShotIndex, config.Pixel));
                }
                foreach (var shot in caseResult.Shots)
                    shot.AtMs = manifest?.Shots.FirstOrDefault(item => item.Index == shot.ShotIndex)?.AtMs;
                IncidentAggregator.Finalize(caseResult);
                // 截图即使完整，回放生命周期/hook/播放器本身失败也不能由像素或 AI 判为通过。
                if (!replayCase.Ok)
                {
                    caseResult.Status = caseResult.FinalVerdict = "failed";
                    caseResult.Error = replayCase.Error ?? $"回放未成功完成（状态: {replayCase.Status}）。";
                    caseResult.Ai = new AiAssessment { Status = "skipped", Reason = "replay_failure" };
                }
                else if (useAi)
                {
                    if (caseResult.Shots.All(item => item.Ai.Status == "skipped"))
                    {
                        caseResult.Ai = new AiAssessment { Status = "skipped", Reason = "all_shots_skipped" };
                    }
                    else
                    {
                        try { new AiCaseReviewer().ReviewAsync(caseResult, caseOutputDir, config.Ai).GetAwaiter().GetResult(); }
                        catch (Exception ex) { caseResult.Ai = new AiAssessment { Status = "failed", Reason = ex.Message }; }
                    }
                }
            }
            catch (Exception ex)
            {
                caseResult.Status = caseResult.FinalVerdict = "failed";
                caseResult.Ai = new AiAssessment { Status = "skipped", Reason = "comparison_error" };
                caseResult.Error = ex.ToString();
            }
            report.TestCases.Add(caseResult);
            Directory.CreateDirectory(caseOutputDir);
            AtomicFile.WriteAllText(Path.Combine(caseOutputDir, "result.json"), Json.Serialize(caseResult));
        }

        report.PassedTestCases = report.TestCases.Count(item => item.Status == "passed");
        report.FailedTestCases = report.TestCases.Count(item => item.Status == "failed");
        report.UncertainTestCases = report.TestCases.Count(item => item.Status == "uncertain");
        report.SkippedTestCases = report.TestCases.Count(item => item.Status == "skipped");
        report.FinalPassedTestCases = report.TestCases.Count(item => item.FinalVerdict == "passed");
        report.FinalFailedTestCases = report.TestCases.Count(item => item.FinalVerdict == "failed");
        report.FinalNeedsReviewTestCases = report.TestCases.Count(item => item.FinalVerdict is "uncertain" or "needs_review");
        report.FinishedAt = DateTimeOffset.UtcNow;
        AtomicFile.WriteAllText(Path.Combine(reportDir, "result.json"), Json.Serialize(report));
        Console.WriteLine($"对比完成: 本地通过 {report.PassedTestCases}, 本地失败 {report.FailedTestCases}, 本地待确认 {report.UncertainTestCases}; " +
                          $"最终通过 {report.FinalPassedTestCases}, 最终失败 {report.FinalFailedTestCases}, 最终待确认 {report.FinalNeedsReviewTestCases}, 跳过 {report.SkippedTestCases}, 目录 {reportDir}");
        return report.FinalFailedTestCases == 0 && report.FinalNeedsReviewTestCases == 0 ? 0 : 1;
    }

    private static TestCaseManifest? TryLoadManifest(string testCaseDir)
    {
        string path = Path.Combine(testCaseDir, "manifest.json");
        if (!File.Exists(path)) return null;
        try { return Json.Deserialize<TestCaseManifest>(AtomicFile.ReadAllText(path)); }
        catch { return null; }
    }
}
