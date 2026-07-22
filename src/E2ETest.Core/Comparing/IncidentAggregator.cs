using System.Drawing;
using E2ETest.Core.Model;

namespace E2ETest.Core.Comparing;

public static class IncidentAggregator
{
    public static void Finalize(TestCaseComparisonResult testCase, PixelConfig? settings = null)
    {
        testCase.TotalShots = testCase.Shots.Count;
        for (int index = 0; index < testCase.Shots.Count; index++)
        {
            var shot = testCase.Shots[index];
            shot.Ordinal = index + 1;
            shot.Role = index == 0 ? "first" : index == testCase.Shots.Count - 1 ? "last" : "intermediate";
            shot.FinalVerdict = shot.Status;
        }
        var incidents = new List<(ComparisonIncident Incident, List<(int Shot, PixelRegion Region)> Members)>();
        foreach (var shot in testCase.Shots)
        foreach (var region in shot.Pixel?.Regions ?? [])
        {
            var candidate = incidents.LastOrDefault(item =>
                shot.ShotIndex - item.Members.Max(member => member.Shot) <= 1 &&
                item.Members.Any(member => Close(member.Region, region)));
            if (candidate.Incident is null)
            {
                candidate = (new ComparisonIncident { Id = $"incident-{incidents.Count + 1:D3}" }, new());
                incidents.Add(candidate);
            }
            candidate.Members.Add((shot.ShotIndex, region));
        }
        foreach (var item in incidents)
        {
            item.Incident.ShotIndexes = item.Members.Select(member => member.Shot).Distinct().Order().ToList();
            item.Incident.RegionIds = item.Members.Select(member => member.Region.Id).ToList();
            item.Incident.ChangedPixels = item.Members.Sum(member => member.Region.ChangedPixels);
            int failLargestRegionPixels = settings?.FailLargestRegionPixels ?? 2500;
            item.Incident.LocalVerdict = item.Members.Any(member => member.Region.ChangedPixels >= failLargestRegionPixels) ? "failed" : "uncertain";
            item.Incident.FinalVerdict = item.Incident.LocalVerdict;
            int score = item.Incident.LocalVerdict == "failed" ? 55 : 30;
            if (item.Incident.ShotIndexes.Count > 1) { score += Math.Min(20, 10 * (item.Incident.ShotIndexes.Count - 1)); item.Incident.AttentionReasons.Add("跨多张截图持续出现"); }
            if (item.Incident.ShotIndexes.Any(index => index == testCase.Shots.First().ShotIndex || index == testCase.Shots.Last().ShotIndex)) { score += 10; item.Incident.AttentionReasons.Add("出现在流程首尾截图"); }
            score += Math.Min(15, (int)Math.Log10(Math.Max(1, item.Incident.ChangedPixels)) * 3);
            item.Incident.AttentionReasons.Insert(0, item.Incident.LocalVerdict == "failed" ? "本地检测到明显差异" : "本地检测到待确认差异");
            item.Incident.AttentionScore = Math.Min(99, score);
            item.Incident.AttentionLevel = Level(item.Incident.AttentionScore);
        }
        testCase.Incidents = incidents.Select(item => item.Incident).OrderByDescending(item => item.AttentionScore).ToList();
        testCase.Status = testCase.Shots.Any(shot => shot.Status == "failed") ? "failed" : testCase.Shots.Any(shot => shot.Status == "uncertain") ? "uncertain" : "passed";
        testCase.FinalVerdict = testCase.Status;
        testCase.AttentionScore = testCase.Incidents.Count == 0 ? 0 : testCase.Incidents.Max(item => item.AttentionScore);
        testCase.AttentionLevel = Level(testCase.AttentionScore);
    }

    private static bool Close(PixelRegion left, PixelRegion right)
    {
        const int gap = 64;
        var a = Rectangle.FromLTRB(left.X - gap, left.Y - gap, left.X + left.Width + gap, left.Y + left.Height + gap);
        var b = new Rectangle(right.X, right.Y, right.Width, right.Height);
        return a.IntersectsWith(b);
    }

    private static string Level(int score) => score >= 70 ? "P1" : score >= 35 ? "P2" : "P3";
}
