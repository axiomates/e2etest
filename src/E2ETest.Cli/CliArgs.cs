namespace E2ETest.Cli;

/// <summary>极简 CLI 参数解析。支持 --key value、--flag，以及位置参数。</summary>
public sealed class CliArgs
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _duplicates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = new();

    private CliArgs() { }

    public static CliArgs Parse(string[] argv)
    {
        var a = new CliArgs();
        for (int i = 0; i < argv.Length; i++)
        {
            if (argv[i].StartsWith("--"))
            {
                string key = argv[i][2..];
                string? val = (i + 1 < argv.Length && !argv[i + 1].StartsWith("--")) ? argv[++i] : null;
                if (a._values.ContainsKey(key)) a._duplicates.Add(key);
                a._values[key] = val;
            }
            else
            {
                a._positional.Add(argv[i]);
            }
        }
        return a;
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public bool Has(string key) => _values.ContainsKey(key);

    public void Validate(IEnumerable<string> valueOptions, IEnumerable<string> flagOptions)
    {
        if (_duplicates.Count > 0)
            throw new ArgumentException($"参数重复: --{_duplicates.OrderBy(value => value).First()}");
        var values = valueOptions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var flags = flagOptions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _values)
        {
            if (!values.Contains(pair.Key) && !flags.Contains(pair.Key))
                throw new ArgumentException($"未知参数: --{pair.Key}");
            if (values.Contains(pair.Key) && string.IsNullOrWhiteSpace(pair.Value))
                throw new ArgumentException($"参数 --{pair.Key} 必须提供值。");
            if (flags.Contains(pair.Key) && pair.Value is not null)
                throw new ArgumentException($"开关 --{pair.Key} 不能提供值。");
        }
        if (_positional.Count > 0)
            throw new ArgumentException($"不支持位置参数: {_positional[0]}");
    }

    /// <summary>第 index 个位置参数（如 config 后的 init/show）。</summary>
    public string? Positional(int index) => index < _positional.Count ? _positional[index] : null;
}
