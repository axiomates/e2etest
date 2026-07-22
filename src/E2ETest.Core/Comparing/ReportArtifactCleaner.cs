using E2ETest.Core.Model;

namespace E2ETest.Core.Comparing;

public static class ReportArtifactCleaner
{
    private static readonly string[] Patterns =
    [
        "diff-shot-*.png", "overlay-shot-*.png", "shot-*-region-*.png", "ai-shot-*.png", "ai-timeline.png",
    ];

    public static void Clean(string caseDirectory, TestCaseComparisonResult result)
    {
        if (!Directory.Exists(caseDirectory)) return;
        var referenced = ReferencedArtifacts(result).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string pattern in Patterns)
        foreach (string path in Directory.EnumerateFiles(caseDirectory, pattern, SearchOption.TopDirectoryOnly))
        {
            if (referenced.Contains(Path.GetFullPath(path))) continue;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<string> ReferencedArtifacts(TestCaseComparisonResult result)
    {
        foreach (var shot in result.Shots)
        {
            if (shot.DiffPath is not null) yield return shot.DiffPath;
            if (shot.OverlayPath is not null) yield return shot.OverlayPath;
            foreach (var region in shot.Pixel?.Regions ?? [])
            {
                if (region.BaselineCropPath is not null) yield return region.BaselineCropPath;
                if (region.ReplayCropPath is not null) yield return region.ReplayCropPath;
                if (region.DiffCropPath is not null) yield return region.DiffCropPath;
                if (region.OverlayCropPath is not null) yield return region.OverlayCropPath;
                if (region.AiEvidencePath is not null) yield return region.AiEvidencePath;
            }
        }
    }
}
