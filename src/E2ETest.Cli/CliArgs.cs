namespace E2ETest.Cli;

/// <summary>极简 CLI 参数解析。支持 --key value、--flag，以及位置参数。</summary>
public sealed class CliArgs
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>第 index 个位置参数（如 config 后的 init/show）。</summary>
    public string? Positional(int index) => index < _positional.Count ? _positional[index] : null;
}
