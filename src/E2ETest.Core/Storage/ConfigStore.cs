using E2ETest.Core.Model;

namespace E2ETest.Core.Storage;

/// <summary>config.json 的读写。不存在时返回默认配置。</summary>
public static class ConfigStore
{
    public static AppConfig Load(string configPath)
    {
        if (!File.Exists(configPath)) return new AppConfig();
        return Json.Deserialize<AppConfig>(File.ReadAllText(configPath));
    }

    public static void Save(string configPath, AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(configPath))!);
        File.WriteAllText(configPath, Json.Serialize(config));
    }
}
