using System.Drawing;
using E2ETest.Core.Model;
using E2ETest.Core.Storage;

namespace E2ETest.Core.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, $"repo-{Guid.NewGuid():N}");

    [Fact]
    public void CommitCreatesTestCaseAndRejectsDuplicate()
    {
        var repo = new TestCaseRepository(_root);
        Commit(repo, "登录流程", Color.Red);

        using (var snapshot = repo.LoadSnapshot("登录流程"))
        {
            Assert.Equal("登录流程", snapshot.Manifest.Name);
            Assert.True(File.Exists(Path.Combine(snapshot.Directory, "baseline", "shot-0001.png")));
        }

        using var lease = repo.AcquireWriteLease("登录流程");
        var staging = repo.CreateRecordingStaging("登录流程", lease);
        repo.SaveStagedManifest(staging, CreateManifest("登录流程", staging.DirectoryPath, Color.Blue));
        Assert.Throws<InvalidOperationException>(() => repo.CommitStagedTestCase(staging, lease));
        repo.DeleteStaging(staging);
    }

    [Fact]
    public void ListAndDeleteUseSingleTestCaseDirectory()
    {
        var repo = new TestCaseRepository(_root);
        Commit(repo, "用例 B", Color.Blue);
        Commit(repo, "用例 A", Color.Red);

        Assert.Equal(new[] { "用例 A", "用例 B" },
            repo.ListNames().OrderBy(x => x, StringComparer.CurrentCulture));
        Assert.True(repo.Delete("用例 A"));
        Assert.False(repo.Exists("用例 A"));
        Assert.False(repo.Delete("不存在"));
    }

    [Fact]
    public void ReadSnapshotBlocksDeleteUntilDisposed()
    {
        var repo = new TestCaseRepository(_root);
        Commit(repo, "case-1", Color.Black);
        var snapshot = repo.LoadSnapshot("case-1");

        Assert.Throws<InvalidOperationException>(() => repo.Delete("case-1"));
        snapshot.Dispose();

        Assert.True(repo.Delete("case-1"));
    }

    [Fact]
    public void DesktopLockIsSharedAcrossDifferentRoots()
    {
        var firstRepo = new TestCaseRepository(Path.Combine(_root, "first"));
        var secondRepo = new TestCaseRepository(Path.Combine(_root, "second"));
        using var desktop = firstRepo.AcquireDesktopLock();
        Assert.Throws<InvalidOperationException>(() => secondRepo.AcquireDesktopLock());
    }

    [Fact]
    public void DisposedWriteLeaseCannotCommit()
    {
        var repo = new TestCaseRepository(_root);
        var lease = repo.AcquireWriteLease("case-1");
        var staging = repo.CreateRecordingStaging("case-1", lease);
        repo.SaveStagedManifest(staging, CreateManifest("case-1", staging.DirectoryPath, Color.Black));
        lease.Dispose();

        Assert.Throws<InvalidOperationException>(() => repo.CommitStagedTestCase(staging, lease));
        repo.DeleteStaging(staging);
    }

    [Fact]
    public void LockFilesAreDeletedAfterOperations()
    {
        var repo = new TestCaseRepository(_root);
        Commit(repo, "case-1", Color.Black);
        Assert.False(File.Exists(Path.Combine(_root, ".locks", "case-1.lock")));

        using (repo.LoadSnapshot("case-1")) { }
        Assert.False(File.Exists(Path.Combine(_root, ".locks", "case-1.lock")));

        repo.Delete("case-1");
        Assert.False(File.Exists(Path.Combine(_root, ".locks", "case-1.lock")));
    }

    [Fact]
    public void UsesConfiguredTestCasesDirectory()
    {
        ConfigStore.Save(Path.Combine(_root, "config.json"), new AppConfig
        {
            Paths = new PathsConfig { TestCases = "./custom-cases" },
        });

        var repo = new TestCaseRepository(_root);
        Commit(repo, "case-1", Color.Black);

        Assert.True(File.Exists(Path.Combine(_root, "custom-cases", "case-1", "manifest.json")));
    }

    [Fact]
    public void AiGuidanceCanBeAddedAfterRecordingAndUpdatedIndependently()
    {
        var repo = new TestCaseRepository(_root);
        Commit(repo, "case-1", Color.Black);

        repo.UpdateAiGuidance("case-1", "关注立柱是否创建。", null);
        repo.UpdateAiGuidance("case-1", null, "立柱可见且无错误提示才通过。");

        using var snapshot = repo.LoadSnapshot("case-1");
        Assert.Equal("关注立柱是否创建。", snapshot.Manifest.TestFocus);
        Assert.Equal("立柱可见且无错误提示才通过。", snapshot.Manifest.AcceptanceCriteria);
    }

    private static void Commit(TestCaseRepository repo, string name, Color color)
    {
        using var lease = repo.AcquireWriteLease(name);
        var staging = repo.CreateRecordingStaging(name, lease);
        repo.SaveStagedManifest(staging, CreateManifest(name, staging.DirectoryPath, color));
        repo.CommitStagedTestCase(staging, lease);
    }

    private static TestCaseManifest CreateManifest(string name, string stagingDir, Color color)
    {
        string imagePath = Path.Combine(stagingDir, "baseline", "shot-0001.png");
        using (var bitmap = new Bitmap(10, 10))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(color);
            bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        return new TestCaseManifest
        {
            SchemaVersion = ManifestValidator.CurrentSchemaVersion,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            DurationMs = 0,
            Capture = new CaptureRule
            {
                CoordinateSpace = "physical-pixels",
                Screen = new ScreenSize { Width = 10, Height = 10, Dpi = 96 },
                CropRect = new Rect { X = 0, Y = 0, Width = 10, Height = 10 },
            },
            Replay = new ReplaySettings(),
            Events = new List<InputEvent>
            {
                new ScreenshotEvent { T = 0, ShotIndex = 1 },
            },
            Shots = new List<ShotEntry>
            {
                new() { Index = 1, File = "baseline/shot-0001.png", AtMs = 0, Kind = "manual" },
            },
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
