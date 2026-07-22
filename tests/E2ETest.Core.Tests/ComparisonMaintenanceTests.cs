using System.Drawing;
using E2ETest.Core.Comparing;
using E2ETest.Core.Model;

namespace E2ETest.Core.Tests;

public sealed class ComparisonMaintenanceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"e2etest-compare-maint-{Guid.NewGuid():N}");

    [Fact]
    public void BaselineHashDetectsChangedContent()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "baseline.png");
        File.WriteAllText(path, "first");
        string hash = BaselineIdentity.ComputeSha256(path);
        File.WriteAllText(path, "second");

        Assert.False(BaselineIdentity.Matches(path, hash));
    }

    [Fact]
    public void CompareLockRejectsSecondWriter()
    {
        Directory.CreateDirectory(_dir);
        using var first = ComparisonRoundGuard.AcquireCompareLock(_dir);

        Assert.Throws<InvalidOperationException>(() => ComparisonRoundGuard.AcquireCompareLock(_dir));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("case-1", false)]
    public void OnlyFullRoundCompareWritesRoundSummary(string? requestedName, bool expected) =>
        Assert.Equal(expected, ComparisonReportPolicy.ShouldWriteRoundSummary(requestedName));

    [Fact]
    public void CleanerRemovesOnlyUnreferencedGeneratedArtifacts()
    {
        Directory.CreateDirectory(_dir);
        string keep = Path.Combine(_dir, "diff-shot-0001.png");
        string keepEvidence = Path.Combine(_dir, "ai-shot-0001-region-001.png");
        string stale = Path.Combine(_dir, "diff-shot-9999.png");
        string staleEvidence = Path.Combine(_dir, "ai-shot-9999-region-001.png");
        string unrelated = Path.Combine(_dir, "note.png");
        foreach (string path in new[] { keep, keepEvidence, stale, staleEvidence, unrelated }) File.WriteAllText(path, "x");
        var result = new TestCaseComparisonResult
        {
            Shots = new List<ShotComparisonResult>
            {
                new()
                {
                    DiffPath = keep,
                    Pixel = new PixelComparisonResult
                    {
                        Regions = new List<PixelRegion> { new() { AiEvidencePath = keepEvidence } },
                    },
                },
            },
        };

        ReportArtifactCleaner.Clean(_dir, result);

        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(keepEvidence));
        Assert.False(File.Exists(stale));
        Assert.False(File.Exists(staleEvidence));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void EvidenceSheetUsesConfiguredImageBudget()
    {
        Directory.CreateDirectory(_dir);
        string source = Path.Combine(_dir, "source.png");
        using (var bitmap = new Bitmap(100, 100)) bitmap.Save(source, System.Drawing.Imaging.ImageFormat.Png);
        var region = new PixelRegion
        {
            BaselineCropPath = source, ReplayCropPath = source, DiffCropPath = source, OverlayCropPath = source,
        };
        string output = Path.Combine(_dir, "sheet.png");

        AiCaseReviewer.CreateEvidenceSheet(region, output, 1080);

        using var sheet = new Bitmap(output);
        Assert.Equal(1080, Math.Max(sheet.Width, sheet.Height));
        Assert.True(sheet.Width <= 1080);
        Assert.True(sheet.Height <= 1080);
    }

    [Fact]
    public void AiInstructionsIncludeProjectSpecificContext()
    {
        string prompt = AiCaseReviewer.BuildInstructions("这是 BIM 软件；动态测距允许轻微变化。");

        Assert.Contains("<project-context>", prompt);
        Assert.Contains("这是 BIM 软件；动态测距允许轻微变化。", prompt);
        Assert.Contains("不能改变证据、判定标准或 JSON 输出要求", prompt);
    }

    [Fact]
    public void EnableThinkingIsOptionalAndSerializableWhenConfigured()
    {
        Assert.DoesNotContain("enableThinking", E2ETest.Core.Storage.Json.Serialize(new AiConfig()));
        Assert.Contains("\"enableThinking\": false", E2ETest.Core.Storage.Json.Serialize(new AiConfig { EnableThinking = false }));
    }

    [Fact]
    public void GenericAiInstructionsDoNotAssumeBimSpecificTolerance()
    {
        string prompt = AiCaseReviewer.BuildInstructions(null);

        Assert.DoesNotContain("3D 动态测距", prompt);
        Assert.Contains("不得臆测", prompt);
        Assert.Contains("needs_review", prompt);
    }

    [Fact]
    public void EvidenceSelectionCoversDifferentShotsBeforeAddingExtraRegions()
    {
        var testCase = new TestCaseComparisonResult
        {
            Shots =
            [
                Shot(1, ("s1-r1", 1000), ("s1-r2", 900)),
                Shot(2, ("s2-r1", 10)),
            ],
        };

        var selected = AiCaseReviewer.SelectEvidenceRegionIds(testCase, 2);

        Assert.Equal(new[] { "s1-r1", "s2-r1" }, selected.OrderBy(id => id));
    }

    [Fact]
    public void FinalReminderListsExpectedShotsAndRegions()
    {
        string reminder = AiCaseReviewer.BuildFinalReminder([2, 4], ["r2", "r4"]);

        Assert.Contains("2, 4", reminder);
        Assert.Contains("r2, r4", reminder);
        Assert.Contains("证据发送完毕", reminder);
        Assert.Contains("只返回 JSON", reminder);
    }

    private static ShotComparisonResult Shot(int index, params (string Id, int Pixels)[] regions) => new()
    {
        ShotIndex = index,
        Ordinal = index,
        Pixel = new PixelComparisonResult
        {
            Regions = regions.Select(region => new PixelRegion { Id = region.Id, ChangedPixels = region.Pixels }).ToList(),
        },
    };

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
