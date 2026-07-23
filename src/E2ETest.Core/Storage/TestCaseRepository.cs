using E2ETest.Core.Model;

namespace E2ETest.Core.Storage;

public sealed class FileLockLease : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _path;
    private bool _disposed;

    internal FileLockLease(FileStream stream, string path)
    {
        _stream = stream;
        _path = path;
    }

    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
        try { File.Delete(_path); } catch { }
    }
}

public sealed class TestCaseWriteLease : IDisposable
{
    internal string Name { get; }
    internal FileLockLease Lock { get; }
    internal TestCaseWriteLease(string name, FileLockLease fileLock) { Name = name; Lock = fileLock; }
    public void Dispose() => Lock.Dispose();
}

internal sealed class TestCaseReadLease : IDisposable
{
    private readonly FileLockLease _lock;
    internal TestCaseReadLease(FileLockLease fileLock) => _lock = fileLock;
    public void Dispose() => _lock.Dispose();
}

public sealed record RecordingStaging(string Name, string DirectoryPath);

/// <summary>测试用例仓储：创建、读取、删除；不维护历史版本。</summary>
public sealed class TestCaseRepository
{
    public string Root { get; }
    public string TestCasesDir { get; }
    public string ConfigPath => Path.Combine(Root, "config.json");
    private string LocksDir => Path.Combine(Root, ".locks");
    private string StagingDir => Path.Combine(Root, ".staging");

    public TestCaseRepository(string root)
    {
        Root = Path.GetFullPath(root);
        var config = ConfigStore.Load(ConfigPath);
        TestCasesDir = Path.GetFullPath(Path.Combine(Root, config.Paths.TestCases));
    }

    public string TestCaseDir(string name) => SafeId.ResolveTestCase(TestCasesDir, name);
    public bool Exists(string name) => File.Exists(Path.Combine(TestCaseDir(name), "manifest.json"));

    public IEnumerable<string> ListNames()
    {
        if (!Directory.Exists(TestCasesDir)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(TestCasesDir))
        {
            string name = Path.GetFileName(directory);
            if (!File.Exists(Path.Combine(directory, "manifest.json"))) continue;
            string? valid = null;
            try { valid = SafeId.ValidateTestCaseName(name); } catch { }
            if (valid is not null) yield return valid;
        }
    }

    public TestCaseSnapshot LoadSnapshot(string name) => LoadSnapshotCore(name, forComparison: false);

    public TestCaseSnapshot LoadSnapshotForComparison(string name) => LoadSnapshotCore(name, forComparison: true);

    private TestCaseSnapshot LoadSnapshotCore(string name, bool forComparison)
    {
        name = SafeId.ValidateTestCaseName(name);
        var readLease = AcquireReadLease(name);
        try
        {
            string directory = TestCaseDir(name);
            string manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new DirectoryNotFoundException($"测试用例不存在: {name}");
            var manifest = Json.Deserialize<TestCaseManifest>(AtomicFile.ReadAllText(manifestPath));
            if (manifest.Name != name)
                throw new InvalidDataException("manifest.name 与测试用例目录不一致。");
            if (forComparison)
                ManifestValidator.ValidateForComparison(manifest, directory, requireBaselineFiles: true);
            else
                ManifestValidator.Validate(manifest, directory, requireBaselineFiles: true);
            return new TestCaseSnapshot { Directory = directory, Manifest = manifest, ReadLease = readLease };
        }
        catch
        {
            readLease.Dispose();
            throw;
        }
    }

    public FileLockLease AcquireDesktopLock()
    {
        Directory.CreateDirectory(LocksDir);
        return AcquireExclusiveFileLock(
            Path.Combine(LocksDir, "desktop.lock"),
            $"数据根目录 '{Root}' 已有录制或回放占用桌面输入。");
    }

    public TestCaseWriteLease AcquireWriteLease(string name)
    {
        name = SafeId.ValidateTestCaseName(name);
        Directory.CreateDirectory(LocksDir);
        var fileLock = AcquireExclusiveFileLock(
            Path.Combine(LocksDir, $"{name}.lock"),
            $"测试用例 '{name}' 正在被另一个操作使用。");
        return new TestCaseWriteLease(name, fileLock);
    }

    private TestCaseReadLease AcquireReadLease(string name)
    {
        Directory.CreateDirectory(LocksDir);
        string path = Path.Combine(LocksDir, $"{name}.lock");
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            return new TestCaseReadLease(new FileLockLease(stream, path));
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"测试用例 '{name}' 正在被写操作使用。", ex);
        }
    }

    public RecordingStaging CreateRecordingStaging(string name, TestCaseWriteLease lease)
    {
        ValidateLease(name, lease);
        Directory.CreateDirectory(StagingDir);
        string directory = Path.Combine(StagingDir, $"recording-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "baseline"));
        return new RecordingStaging(name, directory);
    }

    public void SaveStagedManifest(RecordingStaging staging, TestCaseManifest manifest)
    {
        ValidateStaging(staging, manifest.Name);
        AtomicFile.WriteAllText(Path.Combine(staging.DirectoryPath, "manifest.json"), Json.Serialize(manifest));
    }

    public void CommitStagedTestCase(RecordingStaging staging, TestCaseWriteLease lease)
    {
        ValidateLease(staging.Name, lease);
        ValidateStaging(staging, staging.Name);
        var manifest = Json.Deserialize<TestCaseManifest>(
            AtomicFile.ReadAllText(Path.Combine(staging.DirectoryPath, "manifest.json")));
        if (manifest.Name != staging.Name)
            throw new InvalidDataException("staging manifest.name 与目标测试用例不一致。");
        ManifestValidator.Validate(manifest, staging.DirectoryPath, requireBaselineFiles: true);

        string target = TestCaseDir(staging.Name);
        if (Directory.Exists(target))
            throw new InvalidOperationException($"测试用例已存在: {staging.Name}。请先删除后再创建。");
        Directory.CreateDirectory(TestCasesDir);
        Directory.Move(staging.DirectoryPath, target);
    }

    public void DeleteStaging(RecordingStaging staging)
    {
        ValidateStaging(staging, staging.Name);
        if (Directory.Exists(staging.DirectoryPath)) Directory.Delete(staging.DirectoryPath, recursive: true);
        try { Directory.Delete(StagingDir); } catch { }
    }

    public bool Delete(string name)
    {
        using var lease = AcquireWriteLease(name);
        string directory = TestCaseDir(name);
        if (!Directory.Exists(directory)) return false;
        string trashRoot = Path.Combine(Root, ".trash");
        Directory.CreateDirectory(trashRoot);
        string trash = Path.Combine(trashRoot, $"deleted-{Guid.NewGuid():N}");
        Directory.Move(directory, trash);
        try { Directory.Delete(trash, recursive: true); } catch { }
        try { Directory.Delete(trashRoot); } catch { }
        return true;
    }

    public TestCaseManifest UpdateAiGuidance(string name, string? testFocus, string? acceptanceCriteria)
    {
        name = SafeId.ValidateTestCaseName(name);
        using var lease = AcquireWriteLease(name);
        string directory = TestCaseDir(name);
        string manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new DirectoryNotFoundException($"测试用例不存在: {name}");
        var manifest = Json.Deserialize<TestCaseManifest>(AtomicFile.ReadAllText(manifestPath));
        if (manifest.Name != name)
            throw new InvalidDataException("manifest.name 与测试用例目录不一致。");
        ManifestValidator.Validate(manifest, directory, requireBaselineFiles: true);
        if (testFocus is not null) manifest.TestFocus = NormalizeGuidance(testFocus);
        if (acceptanceCriteria is not null) manifest.AcceptanceCriteria = NormalizeGuidance(acceptanceCriteria);
        ManifestValidator.Validate(manifest, directory, requireBaselineFiles: true);
        AtomicFile.WriteAllText(manifestPath, Json.Serialize(manifest));
        return manifest;
    }

    private static string? NormalizeGuidance(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FileLockLease AcquireExclusiveFileLock(
        string path, string errorMessage)
    {
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new FileLockLease(stream, path);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(errorMessage, ex);
        }
    }

    private static void ValidateLease(string name, TestCaseWriteLease lease)
    {
        name = SafeId.ValidateTestCaseName(name);
        if (lease.Lock.IsDisposed || lease.Name != name)
            throw new InvalidOperationException("测试用例写锁已释放或名称不一致。");
    }

    private void ValidateStaging(RecordingStaging staging, string name)
    {
        if (staging.Name != name) throw new InvalidOperationException("staging 与测试用例名称不一致。");
        string root = Path.GetFullPath(StagingDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(staging.DirectoryPath);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).StartsWith("recording-", StringComparison.Ordinal))
            throw new InvalidOperationException("staging 路径无效。");
    }
}
