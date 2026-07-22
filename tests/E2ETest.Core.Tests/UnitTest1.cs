using System.Drawing;
using E2ETest.Core.Model;
using E2ETest.Core.Recording;
using E2ETest.Core.Storage;

namespace E2ETest.Core.Tests;

public sealed class SafeIdTests
{
    [Theory]
    [InlineData("login-flow")]
    [InlineData("case_01.v2")]
    [InlineData("A")]
    public void AcceptsSafeLeafIds(string id) => Assert.Equal(id, SafeId.Validate(id, "id"));

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/b")]
    [InlineData("C:\\outside")]
    [InlineData("CON")]
    [InlineData("..")]
    [InlineData("demo.")]
    [InlineData("中文")]
    public void RejectsUnsafeIds(string id) =>
        Assert.Throws<ArgumentException>(() => SafeId.Validate(id, "id"));

    [Theory]
    [InlineData("登录流程")]
    [InlineData("case 01")]
    [InlineData("用例.v2")]
    public void AcceptsTestCaseNames(string name) =>
        Assert.Equal(name, SafeId.ValidateTestCaseName(name));

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/b")]
    [InlineData("CON")]
    [InlineData(" name")]
    [InlineData("name ")]
    public void RejectsUnsafeTestCaseNames(string name) =>
        Assert.Throws<ArgumentException>(() => SafeId.ValidateTestCaseName(name));
}

public sealed class HotkeyTests
{
    [Fact]
    public void ParsesConfiguredSingleKeys()
    {
        Assert.Equal(0x7A, Hotkey.Parse("F11").Vk);
        Assert.Equal(0x13, Hotkey.Parse("Pause").Vk);
        Assert.Equal('A', Hotkey.Parse("a").Vk);
    }

    [Fact]
    public void RejectsCombinations() =>
        Assert.Throws<FormatException>(() => Hotkey.Parse("Ctrl+F11"));
}

public sealed class ManifestTests : IDisposable
{
    private readonly string _testCaseDir = Path.Combine(AppContext.BaseDirectory, $"manifest-{Guid.NewGuid():N}");

    [Fact]
    public void ValidManifestRoundTripsAndValidates()
    {
        var manifest = CreateValidManifest();
        string json = Json.Serialize(manifest);
        var loaded = Json.Deserialize<TestCaseManifest>(json);

        Assert.IsType<ScreenshotEvent>(loaded.Events[0]);
        ManifestValidator.Validate(loaded, _testCaseDir, requireBaselineFiles: true);
    }

    [Fact]
    public void RejectsDuplicateShotIndex()
    {
        var manifest = CreateValidManifest();
        manifest.Shots.Add(new ShotEntry
        {
            Index = manifest.Shots[0].Index,
            File = "baseline/shot-0002.png",
            AtMs = 10,
            Kind = "manual",
        });
        Assert.Throws<InvalidDataException>(() =>
            ManifestValidator.Validate(manifest, _testCaseDir, requireBaselineFiles: true));
    }

    [Fact]
    public void RejectsEventAfterDuration()
    {
        var manifest = CreateValidManifest();
        manifest.Events.Add(new KeyEvent { T = 21, Vk = 65, Scan = 30, Down = true });
        Assert.Throws<InvalidDataException>(() =>
            ManifestValidator.Validate(manifest, _testCaseDir, requireBaselineFiles: true));
    }

    [Fact]
    public void RejectsMissingSchemaVersion()
    {
        var manifest = CreateValidManifest();
        manifest.SchemaVersion = 0;
        Assert.Throws<InvalidDataException>(() =>
            ManifestValidator.Validate(manifest, _testCaseDir, requireBaselineFiles: true));
    }

    [Fact]
    public void RejectsManifestWithoutManualScreenshots()
    {
        var manifest = CreateValidManifest();
        manifest.Shots.Clear();
        manifest.Events.Clear();
        Assert.Throws<InvalidDataException>(() =>
            ManifestValidator.Validate(manifest, _testCaseDir, requireBaselineFiles: true));
    }

    [Fact]
    public void RejectsAutomaticFinalScreenshot()
    {
        var manifest = CreateValidManifest();
        manifest.Shots[0].Kind = "final";
        Assert.Throws<InvalidDataException>(() =>
            ManifestValidator.Validate(manifest, _testCaseDir, requireBaselineFiles: true));
    }

    [Fact]
    public void StrictTimelineIsDefault()
    {
        Assert.Equal(0, new ReplaySettings().MaxIdleGapMs);
    }

    private TestCaseManifest CreateValidManifest()
    {
        string baseline = Path.Combine(_testCaseDir, "baseline");
        Directory.CreateDirectory(baseline);
        SavePng(Path.Combine(baseline, "shot-0001.png"));

        return new TestCaseManifest
        {
            SchemaVersion = ManifestValidator.CurrentSchemaVersion,
            Name = "test-case",
            CreatedAt = DateTimeOffset.UtcNow,
            DurationMs = 20,
            Capture = new CaptureRule
            {
                Screen = new ScreenSize { Width = 100, Height = 100, Dpi = 96 },
                CropRect = new Rect { X = 0, Y = 0, Width = 100, Height = 90 },
            },
            Replay = new ReplaySettings(),
            Events = new List<InputEvent>
            {
                new ScreenshotEvent { T = 10, ShotIndex = 1 },
            },
            Shots = new List<ShotEntry>
            {
                new() { Index = 1, File = "baseline/shot-0001.png", AtMs = 10, Kind = "manual" },
            },
        };
    }

    private static void SavePng(string path)
    {
        using var bitmap = new Bitmap(100, 90);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testCaseDir)) Directory.Delete(_testCaseDir, recursive: true);
    }
}
