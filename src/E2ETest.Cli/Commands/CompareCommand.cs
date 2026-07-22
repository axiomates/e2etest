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
        report.FinishedAt = DateTimeOffset.UtcNow;
        AtomicFile.WriteAllText(Path.Combine(reportDir, "result.json"), Json.Serialize(report));
        Console.WriteLine($"对比完成: 通过 {report.PassedTestCases}, 失败 {report.FailedTestCases}, 待确认 {report.UncertainTestCases}, 跳过 {report.SkippedTestCases}, 目录 {reportDir}");
        return report.FailedTestCases == 0 && report.UncertainTestCases == 0 ? 0 : 1;
    }

    private static TestCaseManifest? TryLoadManifest(string testCaseDir)
    {
        string path = Path.Combine(testCaseDir, "manifest.json");
        if (!File.Exists(path)) return null;
        try { return Json.Deserialize<TestCaseManifest>(AtomicFile.ReadAllText(path)); }
        catch { return null; }
    }
}
