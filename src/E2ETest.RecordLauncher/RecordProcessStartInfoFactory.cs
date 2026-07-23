using System.Diagnostics;
using System.IO;
using E2ETest.Core.Storage;

namespace E2ETest.RecordLauncher;

internal static class RecordProcessStartInfoFactory
{
    internal static ProcessStartInfo Create(
        string cliPath,
        string rootValue,
        string nameValue,
        string? focusValue,
        string? criteriaValue)
    {
        string name = SafeId.ValidateTestCaseName(nameValue);
        if (string.IsNullOrWhiteSpace(rootValue))
            throw new ArgumentException("请选择数据根目录。", nameof(rootValue));
        string root = Path.GetFullPath(rootValue.Trim());
        cliPath = Path.GetFullPath(cliPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"数据根目录不存在：{root}");
        if (!File.Exists(cliPath)) throw new FileNotFoundException("启动器同目录中没有找到 e2etest.exe。", cliPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            WorkingDirectory = root,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("record");
        AddValue(startInfo, "--name", name);
        AddValue(startInfo, "--root", root);
        AddOptionalValue(startInfo, "--focus", focusValue);
        AddOptionalValue(startInfo, "--criteria", criteriaValue);
        return startInfo;
    }

    private static void AddOptionalValue(ProcessStartInfo startInfo, string option, string? value)
    {
        string normalized = value?.Trim() ?? "";
        if (normalized.Length > 0) AddValue(startInfo, option, normalized);
    }

    private static void AddValue(ProcessStartInfo startInfo, string option, string value)
    {
        startInfo.ArgumentList.Add(option);
        startInfo.ArgumentList.Add(value);
    }
}
