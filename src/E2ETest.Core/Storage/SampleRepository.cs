using E2ETest.Core.Model;

namespace E2ETest.Core.Storage;

/// <summary>
/// 样例/配置的文件读写。GUI 的管理类操作直接进程内调用这里。
/// 目录布局：&lt;root&gt;/samples/&lt;sampleId&gt;/manifest.json + baseline/*.png
/// </summary>
public sealed class SampleRepository
{
    public string Root { get; }
    public string SamplesDir => Path.Combine(Root, "samples");
    public string ConfigPath => Path.Combine(Root, "config.json");

    public SampleRepository(string root) => Root = Path.GetFullPath(root);

    public string SampleDir(string sampleId) => Path.Combine(SamplesDir, sampleId);
    public string ManifestPath(string sampleId) => Path.Combine(SampleDir(sampleId), "manifest.json");
    public string BaselineDir(string sampleId) => Path.Combine(SampleDir(sampleId), "baseline");

    public IEnumerable<string> ListSampleIds()
    {
        if (!Directory.Exists(SamplesDir)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(SamplesDir))
            if (File.Exists(Path.Combine(dir, "manifest.json")))
                yield return Path.GetFileName(dir);
    }

    public SampleManifest LoadManifest(string sampleId) =>
        Json.Deserialize<SampleManifest>(File.ReadAllText(ManifestPath(sampleId)));

    public void SaveManifest(SampleManifest m)
    {
        Directory.CreateDirectory(SampleDir(m.SampleId));
        Directory.CreateDirectory(BaselineDir(m.SampleId));
        File.WriteAllText(ManifestPath(m.SampleId), Json.Serialize(m));
    }

    public void DeleteSample(string sampleId)
    {
        var dir = SampleDir(sampleId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
