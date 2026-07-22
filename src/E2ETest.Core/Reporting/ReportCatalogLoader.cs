using E2ETest.Core.Model;
using E2ETest.Core.Storage;

namespace E2ETest.Core.Reporting;

public sealed class ReportRoundView
{
    public string RoundId { get; init; } = "";
    public string DirectoryPath { get; init; } = "";
    public DateTimeOffset FinishedAt { get; init; }
    public string ReplayStatus { get; init; } = "unknown";
    public bool ReplayLifecycleSucceeded { get; init; }
    public bool ComparisonCancelled { get; init; }
    public IReadOnlyList<TestCaseComparisonResult> TestCases { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public int TotalCount => TestCases.Count;
    public int FailedCount => TestCases.Count(item => item.FinalVerdict == "failed");
    public int NeedsReviewCount => TestCases.Count(item => item.FinalVerdict is "needs_review" or "uncertain");
    public int PassedCount => TestCases.Count(item => item.FinalVerdict == "passed");
    public int OtherCount => TotalCount - FailedCount - NeedsReviewCount - PassedCount;
    public int AttentionCount => TestCases.Count(ReportCatalogLoader.NeedsAttention);
    public string DisplayName => $"{RoundId}  ·  {TotalCount} 个用例";
}

public static class ReportCatalogLoader
{
    public static IReadOnlyList<ReportRoundView> Load(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("报告路径不能为空。", nameof(inputPath));
        string full = Path.GetFullPath(inputPath);
        if (File.Exists(full)) full = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"报告路径不存在: {full}");
        if (string.Equals(Path.GetFileName(Path.GetDirectoryName(full)), "testcases", StringComparison.OrdinalIgnoreCase))
            full = Directory.GetParent(full)!.Parent!.FullName;

        IEnumerable<string> roundDirectories = LooksLikeRoundDirectory(full)
            ? [full]
            : Directory.EnumerateDirectories(full).Where(LooksLikeRoundDirectory);
        return roundDirectories.Select(LoadRound)
            .OrderByDescending(round => round.FinishedAt)
            .ThenByDescending(round => round.RoundId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool NeedsAttention(TestCaseComparisonResult testCase) =>
        testCase.ComparisonCancelled || testCase.FinalVerdict is "failed" or "needs_review" or "uncertain";

    public static IReadOnlyList<TestCaseComparisonResult> SortCases(IEnumerable<TestCaseComparisonResult> cases) =>
        cases.OrderBy(Priority)
            .ThenByDescending(item => item.AttentionScore)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static ReportRoundView LoadRound(string roundDirectory)
    {
        var warnings = new List<string>();
        ComparisonRoundResult? summary = null;
        string roundResultPath = Path.Combine(roundDirectory, "result.json");
        if (File.Exists(roundResultPath))
        {
            try { summary = Json.Deserialize<ComparisonRoundResult>(AtomicFile.ReadAllText(roundResultPath)); }
            catch (Exception ex) { warnings.Add($"整轮报告无法读取: {ex.Message}"); }
        }

        var cases = new Dictionary<string, TestCaseComparisonResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var testCase in summary?.TestCases ?? [])
            if (!string.IsNullOrWhiteSpace(testCase.Name)) cases[testCase.Name] = testCase;

        string testCasesDirectory = Path.Combine(roundDirectory, "testcases");
        if (Directory.Exists(testCasesDirectory))
        foreach (string caseDirectory in Directory.EnumerateDirectories(testCasesDirectory))
        {
            string resultPath = Path.Combine(caseDirectory, "result.json");
            if (!File.Exists(resultPath)) continue;
            try
            {
                var testCase = Json.Deserialize<TestCaseComparisonResult>(AtomicFile.ReadAllText(resultPath));
                if (string.IsNullOrWhiteSpace(testCase.Name))
                    testCase.Name = Path.GetFileName(caseDirectory);
                cases[testCase.Name] = testCase;
            }
            catch (Exception ex) { warnings.Add($"{Path.GetFileName(caseDirectory)} 无法读取: {ex.Message}"); }
        }

        DateTimeOffset finishedAt = summary?.FinishedAt ?? Directory.GetLastWriteTimeUtc(roundDirectory);
        return new ReportRoundView
        {
            RoundId = string.IsNullOrWhiteSpace(summary?.RoundId) ? Path.GetFileName(roundDirectory) : summary.RoundId,
            DirectoryPath = Path.GetFullPath(roundDirectory),
            FinishedAt = finishedAt,
            ReplayStatus = summary?.ReplayStatus ?? "unknown",
            ReplayLifecycleSucceeded = summary?.ReplayLifecycleSucceeded ?? false,
            ComparisonCancelled = summary?.ComparisonCancelled ?? false,
            TestCases = SortCases(cases.Values),
            Warnings = warnings,
        };
    }

    private static bool LooksLikeRoundDirectory(string directory) =>
        File.Exists(Path.Combine(directory, "result.json")) || Directory.Exists(Path.Combine(directory, "testcases"));

    private static int Priority(TestCaseComparisonResult testCase) => testCase.FinalVerdict switch
    {
        "failed" => 0,
        "needs_review" or "uncertain" => 1,
        "cancelled" => 2,
        "passed" => 3,
        "skipped" => 4,
        _ => 5,
    };
}
