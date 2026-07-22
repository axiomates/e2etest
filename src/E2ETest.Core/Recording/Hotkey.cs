namespace E2ETest.Core.Recording;

/// <summary>只支持单键的录制控制键，例如 F11、F12 或 Pause。</summary>
public sealed class Hotkey
{
    public int Vk { get; init; }

    public static Hotkey Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('+'))
            throw new FormatException($"录制控制键只支持单键，不支持组合键: {text}");

        return new Hotkey { Vk = KeyToVk(text.Trim()) };
    }

    private static int KeyToVk(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }

        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key.AsSpan(1), out int fn) && fn is >= 1 and <= 24)
            return 0x70 + (fn - 1);

        return key.ToUpperInvariant() switch
        {
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" or "RETURN" => 0x0D,
            "PAUSE" => 0x13,
            "INSERT" => 0x2D,
            "DELETE" or "DEL" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            _ => throw new FormatException($"无法识别的单键: {key}"),
        };
    }
}
