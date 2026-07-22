using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using E2ETest.Core.Model;
using StorageJson = E2ETest.Core.Storage.Json;

namespace E2ETest.Core.Comparing;

/// <summary>以完整 testcase 为上下文请求一次多模态语义复核。</summary>
public sealed class AiCaseReviewer
{
    private const string Instructions = """
你是 Windows 桌面软件端到端测试审查员。baseline 是录制时的预期截图序列，replay 是本次实际截图序列。
你会按时间顺序收到一个完整 testcase 的结构化时间线。仅对存在像素差异的步骤附图：每个这样的步骤先给 baseline 全图（期望）和 replay 全图（实际），随后给该步骤一个或多个区域四宫格。四宫格的左上、右上、左下、右下依次是 baseline、replay、diff（仅差异像素）和 overlay（实际图上的差异位置）。同一步的所有区域共同构成该步骤证据；不要只根据其中最大的一块下结论。
metadata 中 rect 与 contextRect 使用原始完整截图像素坐标，左上角为 (0,0)；图像为便于传输可能缩放，但这些坐标不缩放。结合步骤顺序、全图、区域位置和局部证据判断。像素不同本身不是失败：文件名、文件数量、日期时间、运行时数据，以及 3D 动态测距数值可能变化。只有错误提示、异常窗口、关键界面状态缺失、流程明显跑偏或业务语义明显不一致才判 failed。
必须先客观观察、后作判断。每一层的 observation 先描述实际看到的 UI、文字、数值、位置和变化，不能使用“正常”“错误”“通过”等结论性词汇；reason 再明确说明这些观察为何支持 verdict。不要凭 diff 像素数量推断业务错误。
只返回 JSON，不要 Markdown：{"verdict":"passed|failed|needs_review","confidence":0到1,"observation":"先描述整个流程实际看到的内容和变化","reason":"再说明判定原因","shots":[{"shotIndex":1,"observation":"该步骤看到的内容和变化","verdict":"passed|failed|needs_review","reason":"判定原因"}],"regions":[{"id":"shot-0001-region-001","observation":"该区域看到的内容和变化","verdict":"passed|failed|needs_review","reason":"判定原因"}]}。shots 必须覆盖所有附图步骤，regions 必须覆盖每个附图区域。
""";

    public async Task ReviewAsync(TestCaseComparisonResult testCase, string outputDir, AiConfig config, CancellationToken cancellationToken = default)
    {
        Validate(config);
        var evidence = BuildEvidence(testCase, outputDir, config.MaxEvidenceRegions);
        if (evidence.Count == 0)
        {
            testCase.Ai = new AiAssessment { Status = "skipped", Reason = "no_ai_eligible_regions" };
            return;
        }

        var content = BuildRequest(testCase, evidence, config.MaxImageDimension);
        var body = new
        {
            model = config.Model,
            temperature = 0.1,
            max_tokens = 12000,
            enable_thinking = false,
            messages = new[] { new { role = "user", content = (object)content } },
        };
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        string endpoint = config.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var response = await client.PostAsync(endpoint, new StringContent(StorageJson.Serialize(body), Encoding.UTF8, "application/json"), cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var responseJson = JsonDocument.Parse(responseText);
        string raw = responseJson.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        using var answer = JsonDocument.Parse(ExtractJson(raw));
        ApplyAnswer(testCase, evidence, answer.RootElement);
    }

    private static List<EvidenceItem> BuildEvidence(TestCaseComparisonResult testCase, string outputDir, int maximum)
    {
        var candidates = testCase.Shots
            .Where(shot => shot.Pixel is { ExactPixelMatch: false } && shot.HardFailureCode is null)
            .SelectMany(shot => (shot.Pixel?.Regions ?? [])
                .Select(region => new { Shot = shot, Region = region }))
            .OrderByDescending(item => item.Region.ChangedPixels)
            .ThenBy(item => item.Shot.Ordinal)
            .Take(maximum)
            .ToList();
        var selectedIds = candidates.Select(item => item.Region.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var shot in testCase.Shots)
        foreach (var region in shot.Pixel?.Regions ?? [])
        {
            if (!selectedIds.Contains(region.Id) && shot.Pixel is { ExactPixelMatch: false } && shot.HardFailureCode is null)
                region.Ai = new AiAssessment { Status = "skipped", Reason = "ai_evidence_limit" };
        }
        var result = new List<EvidenceItem>(candidates.Count);
        foreach (var item in candidates)
        {
            string path = Path.Combine(outputDir, $"ai-{item.Region.Id}.png");
            CreateEvidenceSheet(item.Region, path);
            result.Add(new EvidenceItem(item.Shot, item.Region, path));
        }
        return result;
    }

    private static List<object> BuildRequest(TestCaseComparisonResult testCase, List<EvidenceItem> evidence, int maxImageDimension)
    {
        var selectedByShot = evidence.GroupBy(item => item.Shot.ShotIndex).ToDictionary(group => group.Key, group => group.ToList());
        var allRegions = testCase.Shots.SelectMany(shot => shot.Pixel?.Regions ?? []).ToList();
        var omitted = allRegions
            .Where(region => evidence.All(item => item.Region.Id != region.Id))
            .Select(region => region.Id).ToList();
        var content = new List<object>
        {
            TextPart(Instructions + "\n" + StorageJson.Serialize(new
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
                totalDetectedRegionCount = allRegions.Count,
                attachedEvidenceRegionCount = evidence.Count,
                evidenceSelection = "按 changedPixels 从大到小选取前 N 组 rect 差异；随后仍按截图步骤顺序展示。",
                attachedRegionIds = evidence.Select(item => item.Region.Id),
                omittedRegionIds = omitted,
                note = omitted.Count == 0 ? "所有发现的 rect 差异均附四宫格。" : "仅前 N 组最大 rect 差异附四宫格；omittedRegionIds 未附图，不要把已附图以外的区域视为不存在，也不要仅据已附区域判 passed。",
            }) + $"\n审查范围：本 testcase 的本地像素比较共发现 {allRegions.Count} 组 rect 差异；本次实际附上其中按 changedPixels 排序最大的 {evidence.Count} 组四宫格。" +
            (omitted.Count == 0 ? "没有省略的 rect 差异。" : $"另有 {omitted.Count} 组较小 rect 差异未附图，ID 已列在 omittedRegionIds 中。"))
        };

        foreach (var shot in testCase.Shots.OrderBy(shot => shot.Ordinal))
        {
            if (!selectedByShot.TryGetValue(shot.ShotIndex, out var regions)) continue;
            content.Add(TextPart($"步骤 {shot.Ordinal}/{testCase.TotalShots}（shotIndex={shot.ShotIndex}，role={shot.Role}，atMs={shot.AtMs ?? 0}）：以下依次为 baseline 全图、replay 全图，以及 {regions.Count} 个区域四宫格。"));
            content.Add(ImagePart(shot.BaselinePath, maxImageDimension));
            content.Add(ImagePart(shot.ReplayPath, maxImageDimension));
            foreach (var item in regions)
            {
                content.Add(TextPart($"区域 {item.Region.Id}：rect=({item.Region.X},{item.Region.Y},{item.Region.Width},{item.Region.Height})，contextRect=({item.Region.ContextX},{item.Region.ContextY},{item.Region.ContextWidth},{item.Region.ContextHeight})，changedPixels={item.Region.ChangedPixels}。"));
                content.Add(ImagePart(item.Path, maxImageDimension));
            }
        }
        return content;
    }

    private static void ApplyAnswer(TestCaseComparisonResult testCase, List<EvidenceItem> evidence, JsonElement answer)
    {
        string final = ReadVerdict(answer.GetProperty("verdict").GetString());
        testCase.Ai = new AiAssessment
        {
            Status = "completed",
            Verdict = final,
            Confidence = answer.TryGetProperty("confidence", out var confidence) && confidence.TryGetDouble(out var value) ? value : null,
            Observation = answer.TryGetProperty("observation", out var observation) ? observation.GetString() : null,
            Reason = answer.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
        };
        testCase.FinalVerdict = final;

        var shots = ReadJudgments(answer, "shots", "shotIndex");
        var regions = ReadJudgments(answer, "regions", "id");
        foreach (var group in evidence.GroupBy(item => item.Shot.ShotIndex))
        {
            var shot = group.First().Shot;
            shots.TryGetValue(shot.ShotIndex.ToString(), out var assessment);
            shot.Ai = assessment ?? new AiAssessment { Status = "completed", Verdict = final, Reason = "AI 未单独返回该步骤，采用 testcase 结论。" };
            shot.FinalVerdict = shot.Ai.Verdict ?? final;
        }
        foreach (var item in evidence)
        {
            regions.TryGetValue(item.Region.Id, out var assessment);
            item.Region.Ai = assessment ?? new AiAssessment { Status = "completed", Verdict = final, Reason = "AI 未单独返回该区域，采用 testcase 结论。" };
        }
        foreach (var incident in testCase.Incidents)
        {
            var assessments = evidence.Where(item => incident.RegionIds.Contains(item.Region.Id)).Select(item => item.Region.Ai).ToList();
            if (assessments.Count == 0) continue;
            string verdict = assessments.Any(item => item.Verdict == "failed") ? "failed" : assessments.Any(item => item.Verdict == "needs_review") ? "needs_review" : "passed";
            incident.Ai = new AiAssessment { Status = "completed", Verdict = verdict, Observation = "由关联区域 AI 观察汇总。", Reason = "由关联区域 AI 结论汇总。" };
            incident.FinalVerdict = verdict;
        }
    }

    private static Dictionary<string, AiAssessment> ReadJudgments(JsonElement answer, string property, string idProperty)
    {
        var result = new Dictionary<string, AiAssessment>(StringComparer.Ordinal);
        if (!answer.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty(idProperty, out var id)) continue;
            string? key = id.ValueKind == JsonValueKind.Number ? id.GetInt32().ToString() : id.GetString();
            if (string.IsNullOrWhiteSpace(key)) continue;
            result[key] = new AiAssessment
            {
                Status = "completed", Verdict = item.TryGetProperty("verdict", out var verdict) ? ReadVerdict(verdict.GetString()) : "needs_review",
                Observation = item.TryGetProperty("observation", out var observation) ? observation.GetString() : null,
                Reason = item.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
            };
        }
        return result;
    }

    private static void Validate(AiConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.Model) ||
            config.MaxImageDimension < 0 || config.MaxEvidenceRegions < 1 || config.TimeoutMs <= 0)
            throw new InvalidDataException("ai 配置不完整或无效。");
    }

    private static object TextPart(string text) => new { type = "text", text };
    private static object ImagePart(string path, int maxDimension) => new { type = "image_url", image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(ReadImage(path, maxDimension)) } };

    private static byte[] ReadImage(string path, int maxDimension)
    {
        using var source = new Bitmap(path);
        double scale = maxDimension == 0 ? 1 : Math.Min(1, maxDimension / (double)Math.Max(source.Width, source.Height));
        using var resized = new Bitmap(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        using (var graphics = Graphics.FromImage(resized)) graphics.DrawImage(source, 0, 0, resized.Width, resized.Height);
        using var stream = new MemoryStream(); resized.Save(stream, ImageFormat.Png); return stream.ToArray();
    }

    private static void CreateEvidenceSheet(PixelRegion region, string path)
    {
        using var baseline = new Bitmap(region.BaselineCropPath!);
        using var replay = new Bitmap(region.ReplayCropPath!);
        using var diff = new Bitmap(region.DiffCropPath!);
        using var overlay = new Bitmap(region.OverlayCropPath!);
        const int cellWidth = 360, cellHeight = 220, label = 20, margin = 8;
        using var sheet = new Bitmap(cellWidth * 2 + margin * 3, (cellHeight + label) * 2 + margin * 3);
        using var graphics = Graphics.FromImage(sheet); graphics.Clear(Color.White); using var font = new Font(SystemFonts.DefaultFont.FontFamily, 9);
        DrawEvidenceCell(graphics, baseline, "baseline", new Rectangle(margin, margin, cellWidth, cellHeight), font);
        DrawEvidenceCell(graphics, replay, "replay", new Rectangle(margin * 2 + cellWidth, margin, cellWidth, cellHeight), font);
        DrawEvidenceCell(graphics, diff, "diff", new Rectangle(margin, margin * 2 + cellHeight + label, cellWidth, cellHeight), font);
        DrawEvidenceCell(graphics, overlay, "overlay", new Rectangle(margin * 2 + cellWidth, margin * 2 + cellHeight + label, cellWidth, cellHeight), font);
        sheet.Save(path, ImageFormat.Png);
    }

    private static void DrawEvidenceCell(Graphics graphics, Bitmap image, string label, Rectangle target, Font font)
    {
        graphics.DrawString(label, font, Brushes.Black, target.X, target.Y);
        var picture = new Rectangle(target.X, target.Y + 20, target.Width, target.Height - 20);
        double scale = Math.Min(picture.Width / (double)image.Width, picture.Height / (double)image.Height);
        int width = (int)(image.Width * scale), height = (int)(image.Height * scale);
        graphics.DrawImage(image, picture.X + (picture.Width - width) / 2, picture.Y + (picture.Height - height) / 2, width, height);
        graphics.DrawRectangle(Pens.Gray, picture);
    }

    private static string ReadVerdict(string? value) => value is "passed" or "failed" or "needs_review" ? value : "needs_review";
    private static string ExtractJson(string text)
    {
        int first = text.IndexOf('{'), last = text.LastIndexOf('}');
        if (first < 0 || last < first) throw new InvalidDataException("AI 未返回 JSON。");
        return text[first..(last + 1)];
    }

    private sealed record EvidenceItem(ShotComparisonResult Shot, PixelRegion Region, string Path);
}
