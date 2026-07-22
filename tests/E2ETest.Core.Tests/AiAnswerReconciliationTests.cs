using System.Text.Json;
using E2ETest.Core.Comparing;
using E2ETest.Core.Model;

namespace E2ETest.Core.Tests;

public sealed class AiAnswerReconciliationTests
{
    [Fact]
    public void RegionFailureDominatesContradictoryCasePass()
    {
        var testCase = Case("r1");
        using var answer = Answer("""
        {"verdict":"passed","shots":[{"shotIndex":1,"verdict":"passed"}],"regions":[{"id":"r1","verdict":"failed"}]}
        """);

        AiCaseReviewer.ApplyAnswer(testCase, ["r1"], answer.RootElement);

        Assert.Equal("failed", testCase.FinalVerdict);
        Assert.Equal("failed", testCase.Shots[0].Pixel!.Regions[0].Ai.Verdict);
    }

    [Fact]
    public void MissingSubmittedRegionMakesResultNeedsReview()
    {
        var testCase = Case("r1", "r2");
        using var answer = Answer("""
        {"verdict":"passed","shots":[{"shotIndex":1,"verdict":"passed"}],"regions":[{"id":"r1","verdict":"passed"}]}
        """);

        AiCaseReviewer.ApplyAnswer(testCase, ["r1", "r2"], answer.RootElement);

        Assert.Equal("needs_review", testCase.FinalVerdict);
        Assert.Equal("needs_review", testCase.Shots[0].Pixel!.Regions[1].Ai.Verdict);
    }

    [Fact]
    public void OmittedEvidencePreventsAutomaticPass()
    {
        var testCase = Case("r1", "r2");
        using var answer = Answer("""
        {"verdict":"passed","shots":[{"shotIndex":1,"verdict":"passed"}],"regions":[{"id":"r1","verdict":"passed"}]}
        """);

        AiCaseReviewer.ApplyAnswer(testCase, ["r1"], answer.RootElement);

        Assert.Equal("needs_review", testCase.FinalVerdict);
        Assert.Equal("skipped", testCase.Shots[0].Pixel!.Regions[1].Ai.Status);
        Assert.Equal("ai_evidence_limit", testCase.Shots[0].Pixel!.Regions[1].Ai.Reason);
    }

    [Fact]
    public void UndeliveredRegionsBeyondReportLimitPreventAutomaticPass()
    {
        var testCase = Case("r1");
        testCase.Shots[0].Pixel!.DetectedRegionCount = 2;
        using var answer = Answer("""
        {"verdict":"passed","shots":[{"shotIndex":1,"verdict":"passed"}],"regions":[{"id":"r1","verdict":"passed"}]}
        """);

        AiCaseReviewer.ApplyAnswer(testCase, ["r1"], answer.RootElement);

        Assert.Equal("needs_review", testCase.FinalVerdict);
        Assert.Contains("未附图", testCase.Ai.Reason);
    }

    private static TestCaseComparisonResult Case(params string[] regionIds)
    {
        var regions = regionIds.Select((id, index) => new PixelRegion
        {
            Id = id, X = index * 100, Y = 0, Width = 20, Height = 20, ChangedPixels = 100,
        }).ToList();
        var testCase = new TestCaseComparisonResult
        {
            Name = "case",
            Shots =
            [
                new ShotComparisonResult
                {
                    ShotIndex = 1, Status = "failed", FinalVerdict = "failed",
                    Pixel = new PixelComparisonResult { DetectedRegionCount = regions.Count, Regions = regions },
                },
            ],
        };
        IncidentAggregator.Finalize(testCase);
        return testCase;
    }

    private static JsonDocument Answer(string json) => JsonDocument.Parse(json);
}
