using System.Text.RegularExpressions;

namespace E2ETest.Core.Storage;

/// <summary>验证 sampleId/roundId 为单个安全目录名。</summary>
public static partial class SafeId
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static string Validate(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !Pattern().IsMatch(value) || value.EndsWith('.') ||
            value is "." or ".." || ReservedNames.Contains(value.Split('.')[0]))
        {
            throw new ArgumentException(
                $"{parameterName} 只能包含字母、数字、点、下划线和连字符，长度 1-80，且不能是 Windows 保留名称。",
                parameterName);
        }
        return value;
    }

    public static string ResolveChild(string parent, string id, string parameterName)
    {
        Validate(id, parameterName);
        string parentFull = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string childFull = Path.GetFullPath(Path.Combine(parentFull, id));
        if (!childFull.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{parameterName} 超出允许目录。", parameterName);
        return childFull;
    }
}
