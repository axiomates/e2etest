using E2ETest.Core.Comparing;
using E2ETest.Core.Model;

namespace E2ETest.Core.Tests;

public sealed class IncidentAggregatorTests
{
    [Fact]
    public void GroupsNearbyRegionsAcrossAdjacentShotsAndRanksAttention()
    {
        var testCase = new TestCaseComparisonResult
        {
            Name = "case-1",
            Shots = new List<ShotComparisonResult>
            {
                Shot(1, "uncertain", new PixelRegion { Id = "shot-0001-region-001", X = 100, Y = 100, Width = 20, Height = 20, ChangedPixels = 100 }),
                Shot(2, "failed", new PixelRegion { Id = "shot-0002-region-001", X = 110, Y = 105, Width = 20, Height = 20, ChangedPixels = 3000 }),
            },
        };

        IncidentAggregator.Finalize(testCase);

        var incident = Assert.Single(testCase.Incidents);
        Assert.Equal(new[] { 1, 2 }, incident.ShotIndexes);
        Assert.Equal(3100, incident.ChangedPixels);
        Assert.Equal("P1", incident.AttentionLevel);
        Assert.Equal("failed", testCase.Status);
        Assert.Equal("not_requested", testCase.Ai.Status);
    }

    [Fact]
    public void IncidentVerdictUsesConfiguredLargestRegionThreshold()
    {
        var testCase = new TestCaseComparisonResult
        {
            Shots = new List<ShotComparisonResult>
            {
                Shot(1, "uncertain", new PixelRegion { Id = "r1", Width = 20, Height = 20, ChangedPixels = 150 }),
            },
        };

        IncidentAggregator.Finalize(testCase, new PixelConfig { FailLargestRegionPixels = 100 });

        Assert.Equal("failed", Assert.Single(testCase.Incidents).LocalVerdict);
    }

    private static ShotComparisonResult Shot(int index, string status, PixelRegion region) => new()
    {
        ShotIndex = index,
        Status = status,
        Pixel = new PixelComparisonResult { Regions = new List<PixelRegion> { region } },
    };
}
