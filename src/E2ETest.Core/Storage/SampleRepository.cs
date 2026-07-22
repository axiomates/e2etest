using E2ETest.Core.Model;

namespace E2ETest.Core.Storage;

public sealed class SampleWriteLease : IDisposable
{
    internal string SampleId { get; }
    internal bool IsDisposed { get; private set; }
    private readonly FileStream _stream;
    internal SampleWriteLease(string sampleId, FileStream stream) { SampleId = sampleId; _stream = stream; }
    public void Dispose() { if (IsDisposed) return; IsDisposed = true; _stream.Dispose(); }
}

internal sealed class SampleReadLease : IDisposable
{
    private readonly FileStream _stream;
    internal SampleReadLease(FileStream stream) => _stream = stream;
    public void Dispose() => _stream.Dispose();
}

public sealed record RecordingStaging(string SampleId, string VersionId, string DirectoryPath);

/// <summary>
/// 版本化样例仓储。录制版本不可变，提交只原子切换 current.json；GUI/compare 可稳定读取旧快照。
/// </summary>
public sealed class SampleRepository
{
    public string Root { get; }
    public string SamplesDir => Path.Combine(Root, "samples");
    public string ConfigPath => Path.Combine(Root, "config.json");
    private string LocksDir => Path.Combine(Root, ".locks");

    public SampleRepository(string root) => Root = Path.GetFullPath(root);

    public string SampleDir(string sampleId) => SafeId.ResolveChild(SamplesDir, sampleId, nameof(sampleId));
    private string CurrentPath(string sampleId) => Path.Combine(SampleDir(sampleId), "current.json");
    private string VersionsDir(string sampleId) => Path.Combine(SampleDir(sampleId), "versions");

    public bool Exists(string sampleId) => File.Exists(CurrentPath(sampleId));

    public IEnumerable<string> ListSampleIds()
    {
        if (!Directory.Exists(SamplesDir)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(SamplesDir))
        {
            string id = Path.GetFileName(dir);
            if (!File.Exists(Path.Combine(dir, "current.json"))) continue;
            string? valid = null;
            try { valid = SafeId.Validate(id, "sampleId"); } catch { }
            if (valid is not null) yield return valid;
        }
    }

    public SampleSnapshot LoadSnapshot(string sampleId)
    {
        sampleId = SafeId.Validate(sampleId, nameof(sampleId));
        var readLease = AcquireSampleReadLease(sampleId);
        try { return LoadSnapshotCore(sampleId, readLease); }
        catch { readLease.Dispose(); throw; }
    }

    public SampleSnapshot LoadSnapshot(string sampleId, SampleWriteLease writeLease)
    {
        ValidateLease(sampleId, writeLease);
        return LoadSnapshotCore(sampleId, null);
    }

    private SampleSnapshot LoadSnapshotCore(string sampleId, IDisposable? readLease)
    {
        string pointerPath = CurrentPath(sampleId);
        if (!File.Exists(pointerPath))
        {
            if (File.Exists(Path.Combine(SampleDir(sampleId), "manifest.json")))
                throw new InvalidDataException($"样例 '{sampleId}' 是旧 schema/DPI 格式，请重新录制。");
            throw new DirectoryNotFoundException($"测试样例不存在: {sampleId}");
        }

        var pointer = Json.Deserialize<SamplePointer>(AtomicFile.ReadAllText(pointerPath));
        if (pointer.SchemaVersion != 1) throw new InvalidDataException("不支持的 current.json 版本。");
        string versionId = SafeId.Validate(pointer.VersionId, "versionId");
        string versionDir = SafeId.ResolveChild(VersionsDir(sampleId), versionId, "versionId");
        string manifestPath = Path.Combine(versionDir, "manifest.json");
        var manifest = Json.Deserialize<SampleManifest>(AtomicFile.ReadAllText(manifestPath));
        if (!string.Equals(manifest.SampleId, sampleId, StringComparison.Ordinal))
            throw new InvalidDataException("版本 manifest.sampleId 与目录不一致。");

        var metadata = pointer.Metadata;
        if (metadata is null || metadata.SchemaVersion != 1 || metadata.SampleId != sampleId)
            throw new InvalidDataException("current.json metadata 与样例目录不一致。");
        manifest.DisplayName = metadata.DisplayName;
        manifest.Description = metadata.Description;
        manifest.CreatedAt = metadata.CreatedAt;
        manifest.UpdatedAt = metadata.UpdatedAt;
        manifest.Replay = metadata.Replay;
        ManifestValidator.Validate(manifest, versionDir, requireBaselineFiles: true);

        return new SampleSnapshot
        {
            VersionId = versionId,
            VersionDirectory = versionDir,
            Manifest = manifest,
            ReadLease = readLease,
        };
    }

    public SampleMetadata LoadMetadata(string sampleId)
    {
        sampleId = SafeId.Validate(sampleId, nameof(sampleId));
        var pointer = Json.Deserialize<SamplePointer>(AtomicFile.ReadAllText(CurrentPath(sampleId)));
        if (pointer.SchemaVersion != 1 || pointer.Metadata.SchemaVersion != 1 ||
            pointer.Metadata.SampleId != sampleId)
            throw new InvalidDataException("current.json metadata 与样例目录不一致。");
        return pointer.Metadata;
    }

    public void UpdateMetadata(string sampleId, Action<SampleMetadata> update)
    {
        using var lease = AcquireSampleLock(sampleId);
        string currentPath = CurrentPath(sampleId);
        var pointer = Json.Deserialize<SamplePointer>(AtomicFile.ReadAllText(currentPath));
        update(pointer.Metadata);
        pointer.Metadata.UpdatedAt = DateTimeOffset.UtcNow;
        ValidateMetadata(pointer.Metadata, sampleId);
        SafeId.Validate(pointer.VersionId, "versionId");
        AtomicFile.WriteAllText(currentPath, Json.Serialize(pointer));
    }

    public FileStream AcquireDesktopLock()
    {
        string globalLocksDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "E2ETest",
            "locks");
        Directory.CreateDirectory(globalLocksDir);
        string lockPath = Path.Combine(globalLocksDir, "desktop.lock");
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("当前 Windows 用户会话已有录制或回放占用桌面输入。", ex);
        }
    }

    private SampleReadLease AcquireSampleReadLease(string sampleId)
    {
        sampleId = SafeId.Validate(sampleId, nameof(sampleId));
        Directory.CreateDirectory(LocksDir);
        string lockPath = Path.Combine(LocksDir, $"{sampleId}.lock");
        try
        {
            return new SampleReadLease(
                new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite));
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"测试样例 '{sampleId}' 正在被写操作使用。", ex);
        }
    }

    public SampleWriteLease AcquireSampleLock(string sampleId)
    {
        sampleId = SafeId.Validate(sampleId, nameof(sampleId));
        Directory.CreateDirectory(LocksDir);
        string lockPath = Path.Combine(LocksDir, $"{sampleId}.lock");
        try
        {
            return new SampleWriteLease(sampleId,
                new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"测试样例 '{sampleId}' 正在被另一个操作使用。", ex);
        }
    }

    public RecordingStaging CreateRecordingStaging(string sampleId, SampleWriteLease lease)
    {
        ValidateLease(sampleId, lease);
        string versionId = $"v-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        string versions = VersionsDir(sampleId);
        Directory.CreateDirectory(versions);
        string stagingDir = SafeId.ResolveChild(versions, $"staging-{versionId}", "stagingId");
        Directory.CreateDirectory(Path.Combine(stagingDir, "baseline"));
        return new RecordingStaging(sampleId, versionId, stagingDir);
    }

    public void SaveStagedManifest(RecordingStaging staging, SampleManifest manifest)
    {
        ValidateStaging(staging, manifest.SampleId);
        AtomicFile.WriteAllText(Path.Combine(staging.DirectoryPath, "manifest.json"), Json.Serialize(manifest));
    }

    public void CommitStagedSample(RecordingStaging staging, SampleWriteLease lease)
    {
        ValidateLease(staging.SampleId, lease);
        ValidateStaging(staging, staging.SampleId);
        string stagedManifestPath = Path.Combine(staging.DirectoryPath, "manifest.json");
        var manifest = Json.Deserialize<SampleManifest>(AtomicFile.ReadAllText(stagedManifestPath));
        if (manifest.SampleId != staging.SampleId)
            throw new InvalidDataException("staging manifest.sampleId 与目标样例不一致。");
        ManifestValidator.Validate(manifest, staging.DirectoryPath, requireBaselineFiles: true);

        string finalVersionDir = SafeId.ResolveChild(VersionsDir(staging.SampleId), staging.VersionId, "versionId");
        if (Directory.Exists(finalVersionDir)) throw new IOException("版本目录已存在。");
        Directory.Move(staging.DirectoryPath, finalVersionDir);

        var now = DateTimeOffset.UtcNow;
        SampleMetadata metadata;
        if (File.Exists(CurrentPath(staging.SampleId)))
        {
            metadata = LoadMetadata(staging.SampleId);
            metadata.DisplayName = manifest.DisplayName;
            metadata.Description = manifest.Description;
            metadata.Replay = manifest.Replay;
            metadata.UpdatedAt = now;
        }
        else
        {
            metadata = new SampleMetadata
            {
                SampleId = staging.SampleId,
                DisplayName = manifest.DisplayName,
                Description = manifest.Description,
                Replay = manifest.Replay,
                CreatedAt = manifest.CreatedAt,
                UpdatedAt = now,
            };
        }

        ValidateMetadata(metadata, staging.SampleId);
        try
        {
            AtomicFile.WriteAllText(CurrentPath(staging.SampleId), Json.Serialize(new SamplePointer
            {
                VersionId = staging.VersionId,
                Metadata = metadata,
            }));
        }
        catch
        {
            try { Directory.Delete(finalVersionDir, recursive: true); } catch { }
            throw;
        }
    }

    public void DeleteStaging(RecordingStaging staging)
    {
        ValidateStaging(staging, staging.SampleId);
        if (Directory.Exists(staging.DirectoryPath)) Directory.Delete(staging.DirectoryPath, recursive: true);
    }

    public void DeleteSample(string sampleId)
    {
        using var lease = AcquireSampleLock(sampleId);
        string dir = SampleDir(sampleId);
        if (!Directory.Exists(dir)) return;
        string trashRoot = Path.Combine(Root, ".trash");
        Directory.CreateDirectory(trashRoot);
        string trashPath = Path.Combine(trashRoot, $"{sampleId}-{Guid.NewGuid():N}");
        Directory.Move(dir, trashPath); // 先原子移出可见样例空间，再做可恢复清理。
        try { Directory.Delete(trashPath, recursive: true); }
        catch { /* 删除已生效；残留 trash 由维护任务清理。 */ }
    }

    private static void ValidateLease(string sampleId, SampleWriteLease lease)
    {
        sampleId = SafeId.Validate(sampleId, nameof(sampleId));
        if (lease.IsDisposed || !string.Equals(lease.SampleId, sampleId, StringComparison.Ordinal))
            throw new InvalidOperationException("样例写锁已释放或与目标 sampleId 不一致。");
    }

    private static void ValidateMetadata(SampleMetadata metadata, string sampleId)
    {
        if (metadata.SchemaVersion != 1 || metadata.SampleId != sampleId)
            throw new InvalidDataException("metadata 与 sampleId 不一致。");
        if (string.IsNullOrWhiteSpace(metadata.DisplayName))
            throw new InvalidDataException("displayName 不能为空。");
        if (metadata.Replay is null || !double.IsFinite(metadata.Replay.SpeedFactor) ||
            metadata.Replay.SpeedFactor <= 0 || metadata.Replay.ResetWaitMs < 0 ||
            metadata.Replay.ResetTimeoutMs <= 0 || metadata.Replay.MaxIdleGapMs < 0)
            throw new InvalidDataException("metadata.replay 配置无效。");
    }

    private void ValidateStaging(RecordingStaging staging, string sampleId)
    {
        if (staging.SampleId != sampleId) throw new InvalidOperationException("staging 与 manifest.sampleId 不一致。");
        SafeId.Validate(staging.VersionId, "versionId");
        string versionsRoot = Path.GetFullPath(VersionsDir(sampleId)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(staging.DirectoryPath);
        if (!full.StartsWith(versionsRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).StartsWith("staging-", StringComparison.Ordinal))
            throw new InvalidOperationException("staging 路径无效。");
    }
}
