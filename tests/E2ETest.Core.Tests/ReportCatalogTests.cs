using E2ETest.Core.Model;
using E2ETest.Core.Reporting;
using E2ETest.Core.Storage;

namespace E2ETest.Core.Tests;

public sealed class ReportCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"e2etest-report-catalog-{Guid.NewGuid():N}");

    [Fact]
    public void LoadsRoundAndUsesLatestPerCaseResult()
    {
        string roundDir = Path.Combine(_root, "round-1");
        Directory.CreateDirectory(roundDir);
        Write(Path.Combine(roundDir, "result.json"), new ComparisonRoundResult
        {
            RoundId = "round-1", FinishedAt = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            TestCases = [Case("case-a", "passed", 10), Case("case-b", "passed", 20)],
        });
        string caseDir = Path.Combine(roundDir, "testcases", "case-a");
        Directory.CreateDirectory(caseDir);
        Write(Path.Combine(caseDir, "result.json"), Case("case-a", "failed", 80));

        var rounds = ReportCatalogLoader.Load(_root);

        var round = Assert.Single(rounds);
        Assert.Equal("failed", round.TestCases.Single(item => item.Name == "case-a").FinalVerdict);
        Assert.Equal(new[] { "case-a", "case-b" }, round.TestCases.Select(item => item.Name));
        Assert.Equal(1, round.FailedCount);
        Assert.Equal(1, round.PassedCount);
    }

    [Fact]
    public void LoadsSingleCaseReportWithoutRoundSummary()
    {
        string caseDir = Path.Combine(_root, "round-only-case", "testcases", "case-one");
        Directory.CreateDirectory(caseDir);
        Write(Path.Combine(caseDir, "result.json"), Case("case-one", "needs_review", 55));

        var round = Assert.Single(ReportCatalogLoader.Load(_root));

        Assert.Equal("round-only-case", round.RoundId);
        Assert.Equal(1, round.NeedsReviewCount);
        Assert.Equal("case-one", Assert.Single(round.TestCases).Name);
    }

    [Theory]
    [InlineData("failed", true)]
    [InlineData("needs_review", true)]
    [InlineData("uncertain", true)]
    [InlineData("passed", false)]
    [InlineData("skipped", false)]
    public void AttentionPolicyMatchesFinalVerdict(string verdict, bool expected) =>
        Assert.Equal(expected, ReportCatalogLoader.NeedsAttention(Case("case", verdict, 0)));

    private static TestCaseComparisonResult Case(string name, string verdict, int attention) => new()
    {
        Name = name, Status = verdict, FinalVerdict = verdict, AttentionScore = attention,
    };

    private static void Write<T>(string path, T value) => AtomicFile.WriteAllText(path, Json.Serialize(value));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
