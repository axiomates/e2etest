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

    [Fact]
    public void FailedAiAttemptPropagatesToSubmittedChildren()
    {
        var testCase = Case("r1", "r2");

        AiCaseReviewer.MarkAttemptFailed(testCase, ["r1"], "failed", "case_ai_request_failed");

        Assert.Equal("failed", testCase.Shots[0].Ai.Status);
        Assert.Equal("failed", testCase.Shots[0].Pixel!.Regions[0].Ai.Status);
        Assert.Equal("not_requested", testCase.Shots[0].Pixel!.Regions[1].Ai.Status);
    }

    [Fact]
    public void PassWithoutRequiredObservationAndReasonBecomesNeedsReview()
    {
        var testCase = Case("r1");
        using var answer = Answer("""
        {"verdict":"passed","confidence":0.9,"observation":"","reason":"","shots":[{"shotIndex":1,"verdict":"passed","observation":"","reason":""}],"regions":[{"id":"r1","verdict":"passed","observation":"","reason":""}]}
        """);

        AiCaseReviewer.ApplyAnswer(testCase, ["r1"], answer.RootElement);

        Assert.Equal("needs_review", testCase.FinalVerdict);
        Assert.Contains("observation", testCase.Ai.Reason);
    }

    [Fact]
    public void CompletePassWithValidConfidenceRemainsPassed()
    {
        var testCase = Case("r1");
        using var answer = Answer("""
        {"verdict":"passed","confidence":0.9,"observation":"界面文字发生变化。","reason":"项目背景明确允许该动态文字。","shots":[{"shotIndex":1,"verdict":"passed","observation":"文字发生变化。","reason":"该变化被项目背景允许。"}],"regions":[{"id":"r1","verdict":"passed","observation":"局部文字不同。","reason":"属于允许的动态内容。"}]}
        """);

        AiCaseReviewer.ApplyAnswer(testCase, ["r1"], answer.RootElement);

        Assert.Equal("passed", testCase.FinalVerdict);
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
