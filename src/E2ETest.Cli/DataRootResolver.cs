namespace E2ETest.Cli;

/// <summary>确定 CLI 数据根目录；显式参数优先，其次查找当前目录和程序目录中的 config.json。</summary>
public static class DataRootResolver
{
    public static string Resolve(string? explicitRoot) =>
        Resolve(explicitRoot, Environment.CurrentDirectory, AppContext.BaseDirectory);

    public static string Resolve(string? explicitRoot, string currentDirectory, string executableDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return Path.GetFullPath(explicitRoot);

        string current = Path.GetFullPath(currentDirectory);
        if (File.Exists(Path.Combine(current, "config.json"))) return current;

        string executable = Path.GetFullPath(executableDirectory);
        if (File.Exists(Path.Combine(executable, "config.json"))) return executable;

        // 保持无配置时的既有行为：新项目仍在调用命令的当前目录中创建。
        return current;
    }
}
