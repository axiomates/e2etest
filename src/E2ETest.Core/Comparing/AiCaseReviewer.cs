using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using E2ETest.Core.Model;
using StorageJson = E2ETest.Core.Storage.Json;

namespace E2ETest.Core.Comparing;

/// <summary>以完整 testcase 为上下文请求一次多模态语义复核。</summary>
public sealed class AiCaseReviewer
{
    public async Task ReviewAsync(TestCaseComparisonResult testCase, string outputDir, AiConfig config, CancellationToken cancellationToken = default)
    {
        Validate(config);
        var evidence = BuildEvidence(testCase, outputDir, config.MaxEvidenceRegions, config.MaxImageDimension);
        if (evidence.Count == 0)
        {
            testCase.Ai = new AiAssessment { Status = "skipped", Reason = "no_ai_eligible_regions" };
            return;
        }

        var submitted = evidence.Select(item => item.Region.Id).ToHashSet(StringComparer.Ordinal);
        try
        {
            await SendAndApplyAsync(testCase, evidence, submitted, config, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MarkAttemptFailed(testCase, submitted, "cancelled", "user_cancelled");
            throw;
        }
        catch
        {
            MarkAttemptFailed(testCase, submitted, "failed", "case_ai_request_failed");
            throw;
        }
    }

    private static async Task SendAndApplyAsync(TestCaseComparisonResult testCase, List<AiPromptEvidence> evidence, IReadOnlyCollection<string> submitted, AiConfig config, CancellationToken cancellationToken)
    {
        var content = BuildRequest(testCase, evidence, config);
        var body = new
        {
            model = config.Model,
            temperature = 0.1,
            max_tokens = config.MaxOutputTokens,
            enable_thinking = config.EnableThinking,
            messages = new[] { new { role = "user", content = (object)content } },
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(config.TimeoutMs);
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        string endpoint = config.BaseUrl.TrimEnd('/') + "/chat/completions";
        string responseText;
        try
        {
            responseText = await AiRetryPolicy.ExecuteAsync(async (_, token) =>
            {
                try
                {
                    using var request = new StringContent(StorageJson.Serialize(body), Encoding.UTF8, "application/json");
                    using var response = await client.PostAsync(endpoint, request, token);
                    string text = await response.Content.ReadAsStringAsync(token);
                    if (!response.IsSuccessStatusCode)
                        throw new AiRequestFailureException($"AI 请求返回 HTTP {(int)response.StatusCode} ({response.ReasonPhrase})。", IsTransient(response.StatusCode));
                    return text;
                }
                catch (HttpRequestException ex)
                {
                    throw new AiRequestFailureException("AI 网络连接失败。", retryable: true, ex);
                }
            }, config.MaxAttempts, TimeSpan.FromMilliseconds(config.RetryDelayMs), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"AI 复核在 {config.TimeoutMs}ms 总超时内未完成。");
        }
        using var responseJson = JsonDocument.Parse(responseText);
        string raw = responseJson.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        using var answer = JsonDocument.Parse(ExtractJson(raw));
        ApplyAnswer(testCase, submitted, answer.RootElement);
    }

    private static List<AiPromptEvidence> BuildEvidence(TestCaseComparisonResult testCase, string outputDir, int maximum, int maximumImageDimension)
    {
        var allCandidates = testCase.Shots
            .Where(shot => shot.Pixel is { ExactPixelMatch: false } && shot.HardFailureCode is null)
            .SelectMany(shot => (shot.Pixel?.Regions ?? [])
                .Select(region => new { Shot = shot, Region = region }))
            .ToList();
        var selectedIds = SelectEvidenceRegionIds(testCase, maximum).ToHashSet(StringComparer.Ordinal);
        var candidates = allCandidates.Where(item => selectedIds.Contains(item.Region.Id))
            .OrderBy(item => item.Shot.Ordinal)
            .ThenByDescending(item => item.Region.ChangedPixels)
            .ToList();
        foreach (var shot in testCase.Shots)
        foreach (var region in shot.Pixel?.Regions ?? [])
        {
            if (!selectedIds.Contains(region.Id) && shot.Pixel is { ExactPixelMatch: false } && shot.HardFailureCode is null)
                region.Ai = new AiAssessment { Status = "skipped", Reason = "ai_evidence_limit" };
        }
        var result = new List<AiPromptEvidence>(candidates.Count);
        foreach (var item in candidates)
        {
            string path = item.Region.AiEvidencePath ?? Path.Combine(outputDir, $"evidence-{item.Region.Id}.png");
            if (!File.Exists(path))
            {
                EvidenceSheetBuilder.Create(item.Region, path, maximumImageDimension);
                item.Region.AiEvidencePath = path;
            }
            result.Add(new AiPromptEvidence(item.Shot, item.Region, path));
        }
        return result;
    }

    internal static IReadOnlyList<string> SelectEvidenceRegionIds(TestCaseComparisonResult testCase, int maximum)
    {
        var groups = testCase.Shots
            .Where(shot => shot.Pixel is { ExactPixelMatch: false } && shot.HardFailureCode is null)
            .Select(shot => new
            {
                Shot = shot,
                Regions = (shot.Pixel?.Regions ?? []).OrderByDescending(region => region.ChangedPixels).ToList(),
            })
            .Where(group => group.Regions.Count > 0)
            .ToList();
        var primary = groups.Select(group => new { group.Shot, Region = group.Regions[0] }).ToList();
        if (primary.Count >= maximum)
            return primary.OrderByDescending(item => item.Region.ChangedPixels).ThenBy(item => item.Shot.Ordinal)
                .Take(maximum).Select(item => item.Region.Id).ToList();

        var selected = primary.Select(item => item.Region.Id).ToList();
        var selectedSet = selected.ToHashSet(StringComparer.Ordinal);
        selected.AddRange(groups.SelectMany(group => group.Regions.Select(region => new { group.Shot, Region = region }))
            .Where(item => !selectedSet.Contains(item.Region.Id))
            .OrderByDescending(item => item.Region.ChangedPixels).ThenBy(item => item.Shot.Ordinal)
            .Take(maximum - selected.Count).Select(item => item.Region.Id));
        return selected;
    }

    private static List<object> BuildRequest(TestCaseComparisonResult testCase, List<AiPromptEvidence> evidence, AiConfig config) =>
        AiPromptPipeline.Compose(testCase, evidence, config)
            .Select(part => part.Text is not null
                ? TextPart(part.Text)
                : ImagePart(part.ImagePath!, config.MaxImageDimension))
            .ToList();

    internal static string BuildFinalReminder(IEnumerable<int> shotIndexes, IEnumerable<string> regionIds) =>
        AiPromptPipeline.BuildFinalReminder(shotIndexes, regionIds);

    internal static string BuildInstructions(string? contextPrompt)
    {
        return AiPromptPipeline.BuildInstructions(contextPrompt);
    }

    internal static void ApplyAnswer(TestCaseComparisonResult testCase, IReadOnlyCollection<string> submittedRegionIds, JsonElement answer)
    {
        var submitted = submittedRegionIds.ToHashSet(StringComparer.Ordinal);
        var regionItems = testCase.Shots.SelectMany(shot => (shot.Pixel?.Regions ?? []).Select(region => new { Shot = shot, Region = region })).ToList();
        var expectedShotIndexes = regionItems.Where(item => submitted.Contains(item.Region.Id)).Select(item => item.Shot.ShotIndex).ToHashSet();
        bool hasUnreportedRegions = testCase.Shots.Any(shot => DetectedRegionCount(shot) > (shot.Pixel?.Regions.Count ?? 0));
        bool hasOmittedRegions = hasUnreportedRegions || regionItems.Any(item => !submitted.Contains(item.Region.Id));
        var topAssessment = ValidateRequiredNarrative(new AiAssessment
        {
            Status = "completed",
            Verdict = answer.TryGetProperty("verdict", out var top) ? ReadVerdict(top.GetString()) : "needs_review",
            Confidence = answer.TryGetProperty("confidence", out var confidence) && confidence.TryGetDouble(out var confidenceValue) ? confidenceValue : null,
            Observation = answer.TryGetProperty("observation", out var observation) ? observation.GetString() : null,
            Reason = answer.TryGetProperty("reason", out var topReason) ? topReason.GetString() : null,
        }, requireConfidence: true);
        var shots = ReadJudgments(answer, "shots", "shotIndex");
        var regions = ReadJudgments(answer, "regions", "id");
        bool incomplete = false;

        foreach (var item in regionItems.Where(item => !submitted.Contains(item.Region.Id)))
            item.Region.Ai = new AiAssessment { Status = "skipped", Reason = "ai_evidence_limit" };

        foreach (int shotIndex in expectedShotIndexes)
        {
            var shot = testCase.Shots.Single(item => item.ShotIndex == shotIndex);
            if (!shots.TryGetValue(shotIndex.ToString(), out var assessment))
            {
                incomplete = true;
                assessment = MissingAssessment("AI 未返回该步骤的判断。");
            }
            bool shotHasOmitted = DetectedRegionCount(shot) > (shot.Pixel?.Regions.Count ?? 0) ||
                (shot.Pixel?.Regions ?? []).Any(region => !submitted.Contains(region.Id));
            if (shotHasOmitted && assessment.Verdict == "passed")
                assessment = DowngradeToNeedsReview(assessment, "该步骤存在未附图的差异区域。");
            shot.Ai = assessment;
            shot.FinalVerdict = assessment.Verdict ?? "needs_review";
        }

        foreach (var item in regionItems.Where(item => submitted.Contains(item.Region.Id)))
        {
            if (!regions.TryGetValue(item.Region.Id, out var assessment))
            {
                incomplete = true;
                assessment = MissingAssessment("AI 未返回该区域的判断。");
            }
            item.Region.Ai = assessment;
        }

        var authoritativeVerdicts = new List<string> { topAssessment.Verdict ?? "needs_review" };
        authoritativeVerdicts.AddRange(testCase.Shots.Where(shot => expectedShotIndexes.Contains(shot.ShotIndex)).Select(shot => shot.Ai.Verdict ?? "needs_review"));
        authoritativeVerdicts.AddRange(regionItems.Where(item => submitted.Contains(item.Region.Id)).Select(item => item.Region.Ai.Verdict ?? "needs_review"));
        string final = MostSevere(authoritativeVerdicts);
        if ((incomplete || hasOmittedRegions) && final == "passed") final = "needs_review";

        string? reason = topAssessment.Reason;
        if (incomplete) reason = AppendReason(reason, "AI 响应未完整覆盖所有已提交步骤和区域。");
        if (hasOmittedRegions) reason = AppendReason(reason, "仍有差异区域未附图，不能自动判定通过。");
        testCase.Ai = new AiAssessment
        {
            Status = "completed",
            Verdict = final,
            Confidence = topAssessment.Confidence,
            Observation = topAssessment.Observation,
            Reason = reason,
        };
        testCase.FinalVerdict = final;

        foreach (var incident in testCase.Incidents)
        {
            var members = regionItems.Where(item => incident.RegionIds.Contains(item.Region.Id)).Select(item => item.Region).ToList();
            var reviewed = members.Where(region => submitted.Contains(region.Id)).Select(region => region.Ai.Verdict ?? "needs_review").ToList();
            bool incidentOmitted = members.Any(region => !submitted.Contains(region.Id));
            if (reviewed.Count == 0)
            {
                if (incidentOmitted)
                {
                    incident.Ai = new AiAssessment { Status = "skipped", Verdict = "needs_review", Reason = "incident 的差异区域未附图。" };
                    incident.FinalVerdict = "needs_review";
                }
                continue;
            }
            string verdict = MostSevere(reviewed);
            if (incidentOmitted && verdict == "passed") verdict = "needs_review";
            incident.Ai = new AiAssessment { Status = "completed", Verdict = verdict, Observation = "由关联区域 AI 观察汇总。", Reason = incidentOmitted ? "关联区域仅完成部分 AI 审查。" : "由关联区域 AI 结论汇总。" };
            incident.FinalVerdict = verdict;
        }
    }

    internal static void MarkAttemptFailed(TestCaseComparisonResult testCase, IReadOnlyCollection<string> submittedRegionIds, string status, string reason)
    {
        var submitted = submittedRegionIds.ToHashSet(StringComparer.Ordinal);
        foreach (var shot in testCase.Shots)
        {
            var regions = (shot.Pixel?.Regions ?? []).Where(region => submitted.Contains(region.Id)).ToList();
            if (regions.Count == 0) continue;
            shot.Ai = new AiAssessment { Status = status, Reason = reason };
            foreach (var region in regions) region.Ai = new AiAssessment { Status = status, Reason = reason };
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
            var assessment = ValidateRequiredNarrative(new AiAssessment
            {
                Status = "completed", Verdict = item.TryGetProperty("verdict", out var verdict) ? ReadVerdict(verdict.GetString()) : "needs_review",
                Observation = item.TryGetProperty("observation", out var observation) ? observation.GetString() : null,
                Reason = item.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
            }, requireConfidence: false);
            if (result.TryGetValue(key, out var existing) && MostSevere([existing.Verdict ?? "needs_review", assessment.Verdict ?? "needs_review"]) != assessment.Verdict)
                continue;
            result[key] = assessment;
        }
        return result;
    }

    private static AiAssessment ValidateRequiredNarrative(AiAssessment assessment, bool requireConfidence)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(assessment.Observation)) missing.Add("observation");
        if (string.IsNullOrWhiteSpace(assessment.Reason)) missing.Add("reason");
        if (requireConfidence && (assessment.Confidence is null or < 0 or > 1))
        {
            missing.Add("confidence(0..1)");
            assessment.Confidence = null;
        }
        if (missing.Count == 0) return assessment;
        assessment.Reason = AppendReason(assessment.Reason, $"AI 响应缺少或包含无效必需字段: {string.Join(", ", missing)}。");
        if (assessment.Verdict == "passed") assessment.Verdict = "needs_review";
        return assessment;
    }

    private static int DetectedRegionCount(ShotComparisonResult shot) =>
        shot.Pixel is null ? 0 : Math.Max(shot.Pixel.DetectedRegionCount, shot.Pixel.Regions.Count);

    private static AiAssessment MissingAssessment(string reason) => new()
    {
        Status = "completed", Verdict = "needs_review", Reason = reason,
    };

    private static AiAssessment DowngradeToNeedsReview(AiAssessment source, string reason) => new()
    {
        Status = source.Status,
        Verdict = "needs_review",
        Confidence = source.Confidence,
        Observation = source.Observation,
        Reason = AppendReason(source.Reason, reason),
    };

    private static string MostSevere(IEnumerable<string> verdicts)
    {
        var values = verdicts.ToList();
        return values.Any(value => value == "failed") ? "failed" :
            values.Any(value => value == "needs_review") ? "needs_review" : "passed";
    }

    private static string AppendReason(string? current, string addition) =>
        string.IsNullOrWhiteSpace(current) ? addition : current.TrimEnd() + " " + addition;

    private static void Validate(AiConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.Model) ||
            config.MaxImageDimension < 0 || config.MaxEvidenceRegions < 1 || config.MaxOutputTokens < 1 ||
            config.MaxAttempts < 1 || config.RetryDelayMs < 0 || config.TimeoutMs <= 0)
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

    private static string ReadVerdict(string? value) => value is "passed" or "failed" or "needs_review" ? value : "needs_review";
    private static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 || (int)status >= 500;
    private static string ExtractJson(string text)
    {
        int first = text.IndexOf('{'), last = text.LastIndexOf('}');
        if (first < 0 || last < first) throw new InvalidDataException("AI 未返回 JSON。");
        return text[first..(last + 1)];
    }

}
