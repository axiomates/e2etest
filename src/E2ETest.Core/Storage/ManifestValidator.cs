using System.Drawing;
using E2ETest.Core.Model;

namespace E2ETest.Core.Storage;

/// <summary>集中验证 manifest 的时间轴、截图映射、路径和截图尺寸不变量。</summary>
public static class ManifestValidator
{
    public const int CurrentSchemaVersion = 3;

    public static void Validate(TestCaseManifest manifest, string testCaseDir, bool requireBaselineFiles)
    {
        if (manifest.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"不支持的 manifest schemaVersion: {manifest.SchemaVersion}");
        if (manifest.Capture is null || manifest.Capture.Screen is null || manifest.Capture.CropRect is null ||
            manifest.Replay is null || manifest.Events is null || manifest.Shots is null)
            throw new InvalidDataException("manifest 缺少必需对象或数组。");
        SafeId.ValidateTestCaseName(manifest.Name);
        if (manifest.DurationMs < 0) throw new InvalidDataException("durationMs 不能为负数。");
        if (!double.IsFinite(manifest.Replay.SpeedFactor) || manifest.Replay.SpeedFactor <= 0)
            throw new InvalidDataException("replay.speedFactor 必须大于 0。");
        if (manifest.Replay.MaxIdleGapMs < 0 || manifest.Replay.ResetWaitMs < 0 ||
            manifest.Replay.ResetTimeoutMs <= 0)
            throw new InvalidDataException("replay 等待/超时配置无效。");

        var screen = manifest.Capture.Screen;
        var crop = manifest.Capture.CropRect;
        if (manifest.Capture.CoordinateSpace != "physical-pixels")
            throw new InvalidDataException("不支持的坐标空间，当前只支持 physical-pixels。");
        if (screen.Width <= 0 || screen.Height <= 0 || screen.Dpi <= 0)
            throw new InvalidDataException("录制屏幕尺寸或 DPI 无效。");
        if (crop.Width <= 0 || crop.Height <= 0 ||
            crop.X < screen.X || crop.Y < screen.Y ||
            (long)crop.X + crop.Width > (long)screen.X + screen.Width ||
            (long)crop.Y + crop.Height > (long)screen.Y + screen.Height)
            throw new InvalidDataException("截图裁剪区域超出录制屏幕。");

        long previousT = -1;
        foreach (var inputEvent in manifest.Events)
        {
            if (inputEvent.T < 0 || inputEvent.T > manifest.DurationMs)
                throw new InvalidDataException($"事件时间超出录制范围: {inputEvent.T}ms");
            if (inputEvent.T < previousT)
                throw new InvalidDataException("events 必须按时间升序排列。");
            previousT = inputEvent.T;

            bool invalidCoordinate = inputEvent switch
            {
                MouseMoveEvent move => !InsideScreen(move.X, move.Y, screen),
                MouseButtonEvent button => !InsideScreen(button.X, button.Y, screen),
                MouseWheelEvent wheel => !InsideScreen(wheel.X, wheel.Y, screen),
                _ => false,
            };
            if (invalidCoordinate)
                throw new InvalidDataException($"鼠标事件坐标超出录制屏幕: {inputEvent.T}ms");
            if (inputEvent is MouseButtonEvent buttonEvent && !Enum.IsDefined(buttonEvent.Button))
                throw new InvalidDataException("鼠标按钮值无效。");
            if (inputEvent is KeyEvent keyEvent &&
                (keyEvent.Vk is < 0 or > 0xFF || keyEvent.Scan is < 0 or > ushort.MaxValue))
                throw new InvalidDataException("键盘 VK 或扫描码超出有效范围。");
        }

        if (manifest.Shots.Count == 0) throw new InvalidDataException("样例至少需要一张结尾图。");
        var shotsByIndex = new Dictionary<int, ShotEntry>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string baselineRoot = Path.GetFullPath(Path.Combine(testCaseDir, "baseline"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var shot in manifest.Shots)
        {
            if (shot.Index <= 0 || !shotsByIndex.TryAdd(shot.Index, shot))
                throw new InvalidDataException($"截图 index 无效或重复: {shot.Index}");
            if (shot.Kind is not ("manual" or "final"))
                throw new InvalidDataException($"未知截图类型: {shot.Kind}");
            if (shot.AtMs < 0 || shot.AtMs > manifest.DurationMs)
                throw new InvalidDataException($"截图时间超出录制范围: {shot.Index}");
            if (shot.RequestedAtMs is < 0 || shot.RequestedAtMs > shot.AtMs)
                throw new InvalidDataException($"截图请求时间晚于实际抓图时间: {shot.Index}");

            string fullPath = Path.GetFullPath(Path.Combine(testCaseDir, shot.File));
            if (!fullPath.StartsWith(baselineRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"截图路径必须位于 baseline 目录: {shot.File}");
            if (!files.Add(fullPath)) throw new InvalidDataException($"截图路径重复: {shot.File}");

            if (requireBaselineFiles)
            {
                if (!File.Exists(fullPath)) throw new FileNotFoundException("baseline 截图不存在", fullPath);
                using var image = Image.FromFile(fullPath);
                if (image.Width != crop.Width || image.Height != crop.Height)
                    throw new InvalidDataException($"截图尺寸与 cropRect 不一致: {shot.File}");
            }
        }

        if (!Enumerable.Range(1, manifest.Shots.Count).All(shotsByIndex.ContainsKey))
            throw new InvalidDataException("截图 index 必须从 1 开始连续递增。");

        var finalShots = manifest.Shots.Where(s => s.Kind == "final").ToList();
        if (finalShots.Count != 1 || finalShots[0].AtMs != manifest.DurationMs)
            throw new InvalidDataException("必须有且仅有一张位于 durationMs 的结尾图。");

        var screenshotEvents = manifest.Events.OfType<ScreenshotEvent>().ToList();
        foreach (var manual in manifest.Shots.Where(s => s.Kind == "manual"))
        {
            var matches = screenshotEvents.Where(e => e.ShotIndex == manual.Index).ToList();
            if (matches.Count != 1 || matches[0].T != manual.AtMs)
                throw new InvalidDataException($"manual 截图与时间轴映射不唯一: {manual.Index}");
        }
        foreach (var screenshotEvent in screenshotEvents)
        {
            if (!shotsByIndex.TryGetValue(screenshotEvent.ShotIndex, out var shot) || shot.Kind != "manual")
                throw new InvalidDataException($"截图事件引用了无效 index: {screenshotEvent.ShotIndex}");
        }
    }

    private static bool InsideScreen(int x, int y, ScreenSize screen) =>
        x >= screen.X && y >= screen.Y &&
        x < (long)screen.X + screen.Width && y < (long)screen.Y + screen.Height;
}
