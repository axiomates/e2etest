using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using E2ETest.Core.Model;
using StorageJson = E2ETest.Core.Storage.Json;

namespace E2ETest.Core.Comparing;

public sealed class AiCaseReviewer
{
    private const string Instructions = """
你是 Windows 桌面软件端到端测试审查员。baseline 是录制时的预期截图序列，replay 是本次实际截图序列。
必须结合整个时间线判断，不要孤立判断单张图。像素不同本身不是失败：文件名、文件数量、日期时间、运行时数据，以及 3D 动态测距数值可能变化。只有错误提示、异常窗口、关键界面状态缺失、流程明显跑偏或业务语义明显不一致才判 failed。
只返回 JSON：{"verdict":"passed|failed|needs_review","confidence":0到1,"reason":"中文简述","incidents":[{"id":"incident-001","verdict":"passed|failed|needs_review","reason":"中文说明"}]}。不要 Markdown。
""";

    public async Task ReviewAsync(TestCaseComparisonResult testCase, string outputDir, AiConfig config, CancellationToken cancellationToken = default)
    {
        Validate(config);
        string timelinePath = Path.Combine(outputDir, "ai-timeline.png");
        CreateTimeline(testCase, timelinePath, config.MaxImageDimension);
        testCase.AiTimelinePath = timelinePath;
        var incidents = testCase.Incidents
            .Where(incident => incident.LocalVerdict is "failed" or "uncertain")
            .OrderByDescending(incident => incident.AttentionScore)
            .Take(config.MaxEvidenceIncidents)
            .ToList();
        foreach (var incident in incidents)
        {
            PixelRegion? region = RegionsFor(testCase, incident).OrderByDescending(item => item.ChangedPixels).FirstOrDefault();
            if (region is null) continue;
            string evidencePath = Path.Combine(outputDir, $"ai-{incident.Id}.png");
            CreateEvidenceSheet(region, evidencePath);
            await ReviewIncidentAsync(testCase, incident, timelinePath, evidencePath, config, cancellationToken);
        }
        var reviewed = incidents.Where(incident => incident.Ai.Status == "completed").ToList();
        string verdict = reviewed.Any(incident => incident.Ai.Verdict == "failed") ? "failed" :
            reviewed.Any(incident => incident.Ai.Verdict == "needs_review") ? "needs_review" : "passed";
        testCase.Ai = new AiAssessment { Status = "completed", Verdict = verdict, Reason = $"已审查 {reviewed.Count} 个高关注 incident。" };
        testCase.FinalVerdict = verdict;
    }

    private static async Task ReviewIncidentAsync(TestCaseComparisonResult testCase, ComparisonIncident incident, string timelinePath, string evidencePath, AiConfig config, CancellationToken cancellationToken)
    {
        var content = new List<object>
        {
            new { type = "text", text = Instructions + "\n本次只审查一个 incident。第一张图是完整时间线缩略图，第二张图是该 incident 的四宫格局部证据。\n" + StorageJson.Serialize(new
            {
                testCase = testCase.Name, testCase.TotalShots, testCase.DurationMs,
                incident = new { incident.Id, incident.LocalVerdict, incident.AttentionScore, incident.AttentionReasons, incident.ShotIndexes, incident.RegionIds },
            }) },
            ImagePart(timelinePath, config.MaxImageDimension),
            ImagePart(evidencePath, config.MaxImageDimension),
        };
        var body = new { model = config.Model, temperature = 0.1, max_tokens = 800, enable_thinking = false, messages = new[] { new { role = "user", content = (object)content } } };
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        string endpoint = config.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var response = await client.PostAsync(endpoint, new StringContent(StorageJson.Serialize(body), Encoding.UTF8, "application/json"), cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var responseJson = JsonDocument.Parse(responseText);
        string raw = responseJson.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        using var verdict = JsonDocument.Parse(ExtractJson(raw));
        string final = ReadVerdict(verdict.RootElement.GetProperty("verdict").GetString());
        incident.Ai = new AiAssessment { Status = "completed", Verdict = final,
            Confidence = verdict.RootElement.TryGetProperty("confidence", out var confidence) && confidence.TryGetDouble(out var value) ? value : null,
            Reason = verdict.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null };
        incident.FinalVerdict = final;
    }

    private static IEnumerable<PixelRegion> RegionsFor(TestCaseComparisonResult testCase, ComparisonIncident incident) =>
        testCase.Shots.SelectMany(shot => shot.Pixel?.Regions ?? []).Where(region => incident.RegionIds.Contains(region.Id));

    private static void Validate(AiConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.Model) ||
            config.MaxImageDimension < 0 || config.MaxEvidenceIncidents < 1 || config.TimeoutMs <= 0)
            throw new InvalidDataException("ai 配置不完整或无效。");
    }

    private static object ImagePart(string path, int maxDimension) => new { type = "image_url", image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(ReadImage(path, maxDimension)) } };

    private static byte[] ReadImage(string path, int maxDimension)
    {
        using var source = new Bitmap(path);
        double scale = maxDimension == 0 ? 1 : Math.Min(1, maxDimension / (double)Math.Max(source.Width, source.Height));
        using var resized = new Bitmap(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        using (var graphics = Graphics.FromImage(resized)) graphics.DrawImage(source, 0, 0, resized.Width, resized.Height);
        using var stream = new MemoryStream(); resized.Save(stream, ImageFormat.Png); return stream.ToArray();
    }

    private static void CreateTimeline(TestCaseComparisonResult testCase, string path, int maxDimension)
    {
        const int imageWidth = 280, imageHeight = 166, labelHeight = 22, margin = 10;
        using var sheet = new Bitmap(imageWidth * 2 + margin * 3, testCase.Shots.Count * (imageHeight + labelHeight + margin) + margin);
        using var graphics = Graphics.FromImage(sheet);
        graphics.Clear(Color.White); using var font = new Font(SystemFonts.DefaultFont.FontFamily, 9);
        foreach (var shot in testCase.Shots)
        {
            int y = margin + (shot.Ordinal - 1) * (imageHeight + labelHeight + margin);
            graphics.DrawString($"{shot.Ordinal}/{testCase.TotalShots} {shot.Role} {shot.AtMs ?? 0}ms {shot.Status}", font, Brushes.Black, margin, y);
            DrawThumb(graphics, shot.BaselinePath, new Rectangle(margin, y + labelHeight, imageWidth, imageHeight));
            DrawThumb(graphics, shot.ReplayPath, new Rectangle(margin * 2 + imageWidth, y + labelHeight, imageWidth, imageHeight));
        }
        sheet.Save(path, ImageFormat.Png);
    }

    private static void DrawThumb(Graphics graphics, string path, Rectangle target)
    {
        using var image = new Bitmap(path);
        double scale = Math.Min(target.Width / (double)image.Width, target.Height / (double)image.Height);
        int width = (int)(image.Width * scale), height = (int)(image.Height * scale);
        graphics.DrawImage(image, target.X + (target.Width - width) / 2, target.Y + (target.Height - height) / 2, width, height);
        graphics.DrawRectangle(Pens.Gray, target);
    }

    private static void CreateEvidenceSheet(PixelRegion region, string path)
    {
        using var baseline = new Bitmap(region.BaselineCropPath!);
        using var replay = new Bitmap(region.ReplayCropPath!);
        using var diff = new Bitmap(region.DiffCropPath!);
        using var overlay = new Bitmap(region.OverlayCropPath!);
        int cellWidth = 360, cellHeight = 220, label = 20, margin = 8;
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
}
