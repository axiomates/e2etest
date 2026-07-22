using System.Drawing;
using E2ETest.Core.Model;
using E2ETest.Core.Storage;

namespace E2ETest.Core.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, $"repo-{Guid.NewGuid():N}");

    [Fact]
    public void CommitSwitchesPointerAndKeepsPreviousVersionImmutable()
    {
        var repo = new SampleRepository(_root);
        string firstVersion;
        using (var lease = repo.AcquireSampleLock("sample-1"))
        {
            var staging = repo.CreateRecordingStaging("sample-1", lease);
            var manifest = CreateManifest("sample-1", "第一版", staging.DirectoryPath, Color.Red);
            repo.SaveStagedManifest(staging, manifest);
            repo.CommitStagedSample(staging, lease);
            firstVersion = staging.VersionId;
        }

        string firstVersionDirectory;
        string firstImage;
        using (var firstSnapshot = repo.LoadSnapshot("sample-1"))
        {
            Assert.Equal(firstVersion, firstSnapshot.VersionId);
            firstVersionDirectory = firstSnapshot.VersionDirectory;
            firstImage = Path.Combine(firstVersionDirectory, "baseline", "shot-0001.png");
            Assert.True(File.Exists(firstImage));
        }

        using (var lease = repo.AcquireSampleLock("sample-1"))
        {
            var staging = repo.CreateRecordingStaging("sample-1", lease);
            var manifest = CreateManifest("sample-1", "第二版", staging.DirectoryPath, Color.Blue);
            repo.SaveStagedManifest(staging, manifest);
            repo.CommitStagedSample(staging, lease);
        }

        using var secondSnapshot = repo.LoadSnapshot("sample-1");
        Assert.NotEqual(firstVersion, secondSnapshot.VersionId);
        Assert.True(File.Exists(firstImage));
        Assert.True(Directory.Exists(firstVersionDirectory));
    }

    [Fact]
    public void MetadataUpdateDoesNotRewriteRecordingVersion()
    {
        var repo = new SampleRepository(_root);
        using (var lease = repo.AcquireSampleLock("sample-1"))
        {
            var staging = repo.CreateRecordingStaging("sample-1", lease);
            var manifest = CreateManifest("sample-1", "原名称", staging.DirectoryPath, Color.Black);
            repo.SaveStagedManifest(staging, manifest);
            repo.CommitStagedSample(staging, lease);
        }
        string version;
        using (var initialSnapshot = repo.LoadSnapshot("sample-1"))
            version = initialSnapshot.VersionId;

        repo.UpdateMetadata("sample-1", metadata => metadata.DisplayName = "新名称");

        using var snapshot = repo.LoadSnapshot("sample-1");
        Assert.Equal(version, snapshot.VersionId);
        Assert.Equal("新名称", snapshot.Manifest.DisplayName);
    }

    [Fact]
    public void ReadSnapshotBlocksDeleteUntilDisposed()
    {
        var repo = CreateCommittedRepository();
        var snapshot = repo.LoadSnapshot("sample-1");
        Assert.Throws<InvalidOperationException>(() => repo.DeleteSample("sample-1"));
        snapshot.Dispose();

        repo.DeleteSample("sample-1");
        Assert.False(repo.Exists("sample-1"));
    }

    [Fact]
    public void DesktopLockIsSharedAcrossDifferentRoots()
    {
        var firstRepo = new SampleRepository(Path.Combine(_root, "first"));
        var secondRepo = new SampleRepository(Path.Combine(_root, "second"));
        using var desktop = firstRepo.AcquireDesktopLock();
        Assert.Throws<InvalidOperationException>(() => secondRepo.AcquireDesktopLock());
    }

    [Fact]
    public void DisposedWriteLeaseCannotCommit()
    {
        var repo = new SampleRepository(_root);
        var lease = repo.AcquireSampleLock("sample-1");
        var staging = repo.CreateRecordingStaging("sample-1", lease);
        var manifest = CreateManifest("sample-1", "测试", staging.DirectoryPath, Color.Black);
        repo.SaveStagedManifest(staging, manifest);
        lease.Dispose();

        Assert.Throws<InvalidOperationException>(() =>
            repo.CommitStagedSample(staging, lease));
    }

    [Fact]
    public void InvalidMetadataUpdateDoesNotCorruptPointer()
    {
        var repo = CreateCommittedRepository();
        Assert.Throws<InvalidDataException>(() =>
            repo.UpdateMetadata("sample-1", metadata => metadata.SampleId = "other"));

        using var snapshot = repo.LoadSnapshot("sample-1");
        Assert.Equal("sample-1", snapshot.Manifest.SampleId);
    }

    private SampleRepository CreateCommittedRepository()
    {
        var repo = new SampleRepository(_root);
        using var lease = repo.AcquireSampleLock("sample-1");
        var staging = repo.CreateRecordingStaging("sample-1", lease);
        var manifest = CreateManifest("sample-1", "测试", staging.DirectoryPath, Color.Black);
        repo.SaveStagedManifest(staging, manifest);
        repo.CommitStagedSample(staging, lease);
        return repo;
    }

    private static SampleManifest CreateManifest(
        string sampleId, string displayName, string stagingDir, Color color)
    {
        string imagePath = Path.Combine(stagingDir, "baseline", "shot-0001.png");
        using (var bitmap = new Bitmap(10, 10))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(color);
            bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        var now = DateTimeOffset.UtcNow;
        return new SampleManifest
        {
            SchemaVersion = ManifestValidator.CurrentSchemaVersion,
            SampleId = sampleId,
            DisplayName = displayName,
            CreatedAt = now,
            UpdatedAt = now,
            DurationMs = 0,
            Capture = new CaptureRule
            {
                CoordinateSpace = "physical-pixels",
                Screen = new ScreenSize { Width = 10, Height = 10, Dpi = 96 },
                CropRect = new Rect { X = 0, Y = 0, Width = 10, Height = 10 },
            },
            Replay = new ReplaySettings(),
            Shots = new List<ShotEntry>
            {
                new() { Index = 1, File = "baseline/shot-0001.png", AtMs = 0, Kind = "final" },
            },
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
