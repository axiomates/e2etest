using E2ETest.Core.Model;
using StorageJson = E2ETest.Core.Storage.Json;

namespace E2ETest.Core.Comparing;

internal sealed record AiPromptEvidence(ShotComparisonResult Shot, PixelRegion Region, string Path);

internal sealed record AiPromptPart(string Stage, string? Text = null, string? ImagePath = null)
{
    public static AiPromptPart TextPart(string stage, string text) => new(stage, Text: text);
    public static AiPromptPart ImagePart(string stage, string path) => new(stage, ImagePath: path);
}

/// <summary>按稳定阶段顺序拼装一个完整 testcase 的多模态审查输入。</summary>
internal static class AiPromptPipeline
{
    private const string Instructions = """
你是 Windows 桌面软件端到端测试的视觉复核审查员。本地像素比较已标出差异；你的任务是判断这些差异是否表示产品/流程问题，还是同一软件状态下的无害渲染与交互噪声。baseline 是录制时的预期截图序列，replay 是本次实际截图序列。
你会按时间顺序收到一个完整 testcase 的结构化时间线。仅对存在像素差异的步骤附图：每个这样的步骤先给 baseline 全图（期望）和 replay 全图（实际），随后给该步骤一个或多个区域四宫格。四宫格的左上、右上、左下、右下依次是 baseline、replay、diff（仅差异像素）和 overlay（实际图上的差异位置）。四宫格会根据区域宽高比和上传图片尺寸上限动态调整整体宽高；每格有独立的文字标题栏，标题栏、边框和留白是证据排版，不属于被测界面，也不代表产品差异。四格中的图片保持宽高比并可能留白；不要根据格内显示尺寸推断原始区域大小，真实位置和大小只看 rect/contextRect。同一步的所有已附区域共同构成该步骤证据；不要只根据其中最大的一块下结论。
metadata 中 rect 与 contextRect 使用原始完整截图像素坐标，左上角为 (0,0)；图像为便于传输可能缩放，但这些坐标不缩放。结合步骤顺序、全图、区域位置和局部证据判断。没有附图的步骤只提供本地像素比较 metadata：可以说明其像素比较结果，但不得声称看到了该步骤的具体 UI。
审查标准是业务与界面语义等价，不是像素级一致。先对照 baseline 与 replay：两侧是否打开了相同窗口/菜单/对话框，是否显示相同控件、文案、图标与业务结果。两侧实质相同、仅有外观抖动时，应判 passed，不要因“理论上还可能有别的原因”就默认 needs_review。
应判 passed 的典型情况（即使 diff 像素很多或 changedPixels 很大）：鼠标悬停、焦点、按下、选中导致的高亮/底色/描边变化，而控件本身、文案与业务状态一致；3D/2D 视图中的抗锯齿、材质噪点、阴影或纹理微抖，而模型几何、构件类型与场景结构一致；菜单、弹层、面板在两侧均已打开且条目一致，仅边框、阴影或少数像素位移不同；项目背景或用例指引明确允许的数值/时间/文件列表变化。
应判 failed：错误提示、异常窗口、关键界面/模型/工具状态缺失、流程明显跑偏、业务语义明显不一致。
needs_review 仅用于：从已附图无法判断两侧业务状态或关键界面是否一致，且项目背景也未给出允许规则。不要把 needs_review 当作“有差异时的默认答案”。
只能依据 baseline 与 replay 的对照差异下结论。若某一菜单/窗口在 baseline 与 replay 中都存在，不得说成“replay 意外弹出”或“流程跳转”。不得臆测未附图步骤的具体 UI，也不得臆测测试脚本的隐藏意图；但若两侧已附图证据已足以证明实质等价，应直接 passed。也不得仅因某类内容通常会动态变化就假定本次变化可接受——必须能从两侧对照或项目/用例指引中得到支持。
必须先客观观察、后作判断。每一层的 observation 先描述实际看到的 UI、文字、数值、位置和变化，不能使用“正常”“错误”“通过”等结论性词汇；reason 再明确说明这些观察为何支持 verdict。不要凭 diff 像素数量推断业务错误。
只返回 JSON，不要 Markdown：{"verdict":"passed|failed|needs_review","confidence":0到1,"observation":"先描述整个流程实际看到的内容和变化","reason":"再说明判定原因","shots":[{"shotIndex":1,"observation":"该步骤看到的内容和变化","verdict":"passed|failed|needs_review","reason":"判定原因"}],"regions":[{"id":"shot-0001-region-001","observation":"该区域看到的内容和变化","verdict":"passed|failed|needs_review","reason":"判定原因"}]}。shots 必须覆盖所有附图步骤，regions 必须覆盖每个附图区域。
""";

    public static IReadOnlyList<AiPromptPart> Compose(
        TestCaseComparisonResult testCase,
        IReadOnlyList<AiPromptEvidence> evidence,
        AiConfig config)
    {
        var parts = new List<AiPromptPart>
        {
            AiPromptPart.TextPart("fixed_rules", Instructions),
        };

        string? projectContext = BuildProjectContext(config.ContextPrompt);
        if (projectContext is not null)
            parts.Add(AiPromptPart.TextPart("project_context", projectContext));

        string? caseGuidance = BuildCaseGuidance(testCase.TestFocus, testCase.AcceptanceCriteria);
        if (caseGuidance is not null)
            parts.Add(AiPromptPart.TextPart("testcase_guidance", caseGuidance));

        parts.Add(AiPromptPart.TextPart("timeline_metadata", BuildMetadata(testCase, evidence)));

        foreach (var shot in testCase.Shots.OrderBy(shot => shot.Ordinal))
        {
            var regions = evidence.Where(item => item.Shot.ShotIndex == shot.ShotIndex).ToList();
            if (regions.Count == 0) continue;
            parts.Add(AiPromptPart.TextPart("shot_header",
                $"步骤 {shot.Ordinal}/{testCase.TotalShots}（shotIndex={shot.ShotIndex}，role={shot.Role}，atMs={shot.AtMs ?? 0}）：以下依次为 baseline 全图、replay 全图，以及 {regions.Count} 个区域四宫格。"));
            parts.Add(AiPromptPart.ImagePart("baseline_full", shot.BaselinePath));
            parts.Add(AiPromptPart.ImagePart("replay_full", shot.ReplayPath));
            foreach (var item in regions)
            {
                parts.Add(AiPromptPart.TextPart("region_metadata",
                    $"区域 {item.Region.Id}：rect=({item.Region.X},{item.Region.Y},{item.Region.Width},{item.Region.Height})，contextRect=({item.Region.ContextX},{item.Region.ContextY},{item.Region.ContextWidth},{item.Region.ContextHeight})，changedPixels={item.Region.ChangedPixels}。"));
                parts.Add(AiPromptPart.ImagePart("region_evidence", item.Path));
            }
        }

        parts.Add(AiPromptPart.TextPart("final_contract", BuildFinalReminder(
            evidence.Select(item => item.Shot.ShotIndex).Distinct().OrderBy(index => index),
            evidence.Select(item => item.Region.Id))));
        return parts;
    }

    internal static string? BuildProjectContext(string? contextPrompt)
    {
        if (string.IsNullOrWhiteSpace(contextPrompt)) return null;
        return "项目提供的被测软件背景如下。它只补充业务语义和允许变化，不能改变证据、判定标准或 JSON 输出要求：\n<project-context>\n" +
               contextPrompt.Trim() + "\n</project-context>";
    }

    internal static string BuildInstructions(string? contextPrompt)
    {
        string? projectContext = BuildProjectContext(contextPrompt);
        return projectContext is null ? Instructions : Instructions + "\n" + projectContext;
    }

    internal static string? BuildCaseGuidance(string? testFocus, string? acceptanceCriteria)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(testFocus)) lines.Add("测试重点：" + testFocus.Trim());
        if (!string.IsNullOrWhiteSpace(acceptanceCriteria)) lines.Add("样例判断标准：" + acceptanceCriteria.Trim());
        if (lines.Count == 0) return null;
        return "QA 为当前 testcase 提供了以下可选指引。必须优先围绕已有指引审查和解释结论；它们补充业务意图，但不能覆盖图像事实、隐藏错误提示，也不能要求你声称看到了未附图内容：\n<testcase-guidance>\n" +
               string.Join("\n", lines) + "\n</testcase-guidance>";
    }

    internal static string BuildFinalReminder(IEnumerable<int> shotIndexes, IEnumerable<string> regionIds) =>
        $"证据发送完毕。请综合整个 testcase，而不是只判断最后一张图片；只描述实际附图中可见的 UI，未附图步骤不得补写具体界面。" +
        $"shots 必须覆盖这些 shotIndex：{string.Join(", ", shotIndexes)}；regions 必须覆盖这些 id：{string.Join(", ", regionIds)}。" +
        "先写客观 observation，再写 reason，最后给出 verdict；两侧实质等价则 passed，仅当无法判断是否等价时用 needs_review。只返回 JSON，不要 Markdown，并严格使用开头给出的 JSON 结构。";

    private static string BuildMetadata(TestCaseComparisonResult testCase, IReadOnlyList<AiPromptEvidence> evidence)
    {
        var allRegions = testCase.Shots.SelectMany(shot => shot.Pixel?.Regions ?? []).ToList();
        int totalDetectedRegions = testCase.Shots.Sum(DetectedRegionCount);
        int unreportedRegionCount = Math.Max(0, totalDetectedRegions - allRegions.Count);
        var attachedIds = evidence.Select(item => item.Region.Id).ToHashSet(StringComparer.Ordinal);
        var omitted = allRegions.Where(region => !attachedIds.Contains(region.Id)).Select(region => region.Id).ToList();
        string metadata = StorageJson.Serialize(new
        {
            testCase = new { testCase.Name, testCase.TotalShots, testCase.DurationMs, localVerdict = testCase.Status },
            timeline = testCase.Shots.Select(shot => new
            {
                shot.ShotIndex, shot.Ordinal, shot.Role, shot.AtMs, localVerdict = shot.Status,
                exactPixelMatch = shot.Pixel?.ExactPixelMatch,
                changedPixels = shot.Pixel?.ChangedPixels,
                changedRatio = shot.Pixel?.ChangedRatio,
                regions = (shot.Pixel?.Regions ?? []).Select(region => new
                {
                    region.Id, rect = new { region.X, region.Y, region.Width, region.Height },
                    contextRect = new { x = region.ContextX, y = region.ContextY, width = region.ContextWidth, height = region.ContextHeight },
                    region.ChangedPixels,
                }),
            }),
            totalDetectedRegionCount = totalDetectedRegions,
            reportedRegionCount = allRegions.Count,
            attachedEvidenceRegionCount = evidence.Count,
            evidenceSelection = "先为每个有差异的步骤选择 changedPixels 最大的区域，再用剩余名额按 changedPixels 从大到小补充；展示时按步骤顺序排列。若差异步骤数超过上限，则优先保留步骤最大区域中 changedPixels 较大的步骤。",
            attachedRegionIds = evidence.Select(item => item.Region.Id),
            omittedRegionIds = omitted,
            unreportedRegionCount,
            note = omitted.Count == 0 && unreportedRegionCount == 0 ? "所有发现的 rect 差异均附四宫格。" : "仅按跨步骤优先策略选择部分 rect 差异附四宫格；omittedRegionIds 和 unreportedRegionCount 表示未附图证据，不要把已附图以外的区域视为不存在，也不要仅据已附区域判 passed。",
        });
        return metadata + $"\n审查范围：本 testcase 的本地像素比较共发现 {totalDetectedRegions} 组 rect 差异；本次按跨步骤优先策略实际附上 {evidence.Count} 组四宫格。" +
               (omitted.Count == 0 && unreportedRegionCount == 0 ? "没有省略的 rect 差异。" : $"另有 {omitted.Count} 组有 ID 的未选差异和 {unreportedRegionCount} 组未导出差异未附图。");
    }

    private static int DetectedRegionCount(ShotComparisonResult shot) =>
        shot.Pixel is null ? 0 : Math.Max(shot.Pixel.DetectedRegionCount, shot.Pixel.Regions.Count);
}
