namespace E2ETest.Core.Recording;

[Flags]
public enum HotkeyMods { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// <summary>解析 "Ctrl+Alt+S" 形式的热键为修饰键集合 + 主键 VK。</summary>
public sealed class Hotkey
{
    public HotkeyMods Mods { get; init; }
    public int Vk { get; init; }

    public static Hotkey Parse(string text)
    {
        var mods = HotkeyMods.None;
        int vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyMods.Ctrl; break;
                case "alt": mods |= HotkeyMods.Alt; break;
                case "shift": mods |= HotkeyMods.Shift; break;
                case "win": mods |= HotkeyMods.Win; break;
                default: vk = KeyToVk(raw); break;
            }
        }
        if (vk == 0) throw new FormatException($"热键缺少主键: {text}");
        return new Hotkey { Mods = mods, Vk = vk };
    }

    private static int KeyToVk(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int fn) && fn is >= 1 and <= 24)
            return 0x70 + (fn - 1); // VK_F1 = 0x70
        throw new FormatException($"无法识别的按键: {key}");
    }
}
