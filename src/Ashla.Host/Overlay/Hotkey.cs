using Hexa.NET.ImGui;

namespace Ashla.Host.Overlay;

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8
}

internal sealed record HotkeyKey(string Name, uint VirtualKey, ImGuiKey ImGuiKey)
{
    public static IReadOnlyList<HotkeyKey> All { get; } = BuildTable();

    private static readonly Dictionary<string, HotkeyKey> ByName =
        All.ToDictionary(k => k.Name, StringComparer.OrdinalIgnoreCase);

    public static HotkeyKey? Find(string name) => ByName.GetValueOrDefault(name.Trim());

    private static List<HotkeyKey> BuildTable()
    {
        var keys = new List<HotkeyKey>();
        for (int i = 0; i < 26; i++) keys.Add(new(((char)('A' + i)).ToString(), (uint)('A' + i), ImGuiKey.A + i));
        for (int i = 0; i < 10; i++) keys.Add(new(i.ToString(), (uint)('0' + i), ImGuiKey.Key0 + i));
        for (int i = 0; i < 24; i++) keys.Add(new($"F{i + 1}", (uint)(0x70 + i), ImGuiKey.F1 + i));
        for (int i = 0; i < 10; i++) keys.Add(new($"NumPad{i}", (uint)(0x60 + i), ImGuiKey.Keypad0 + i));
        keys.AddRange(
        [
            new("NumPadMultiply", 0x6A, ImGuiKey.KeypadMultiply),
            new("NumPadAdd", 0x6B, ImGuiKey.KeypadAdd),
            new("NumPadSubtract", 0x6D, ImGuiKey.KeypadSubtract),
            new("NumPadDecimal", 0x6E, ImGuiKey.KeypadDecimal),
            new("NumPadDivide", 0x6F, ImGuiKey.KeypadDivide),
            new("Space", 0x20, ImGuiKey.Space),
            new("PageUp", 0x21, ImGuiKey.PageUp),
            new("PageDown", 0x22, ImGuiKey.PageDown),
            new("End", 0x23, ImGuiKey.End),
            new("Home", 0x24, ImGuiKey.Home),
            new("Left", 0x25, ImGuiKey.LeftArrow),
            new("Up", 0x26, ImGuiKey.UpArrow),
            new("Right", 0x27, ImGuiKey.RightArrow),
            new("Down", 0x28, ImGuiKey.DownArrow),
            new("Insert", 0x2D, ImGuiKey.Insert),
            new("Delete", 0x2E, ImGuiKey.Delete),
            new("Pause", 0x13, ImGuiKey.Pause),
            new(";", 0xBA, ImGuiKey.Semicolon),
            new("=", 0xBB, ImGuiKey.Equal),
            new(",", 0xBC, ImGuiKey.Comma),
            new("-", 0xBD, ImGuiKey.Minus),
            new(".", 0xBE, ImGuiKey.Period),
            new("/", 0xBF, ImGuiKey.Slash),
            new("`", 0xC0, ImGuiKey.GraveAccent),
            new("[", 0xDB, ImGuiKey.LeftBracket),
            new("\\", 0xDC, ImGuiKey.Backslash),
            new("]", 0xDD, ImGuiKey.RightBracket),
            new("'", 0xDE, ImGuiKey.Apostrophe),
        ]);
        return keys;
    }
}

internal sealed record Hotkey(HotkeyModifiers Modifiers, HotkeyKey Key)
{
    public const string DefaultText = "Ctrl+Shift+H";

    public static Hotkey Default { get; } = Parse(DefaultText)!;

    public string? Problem => (Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) == 0
        ? "A hotkey needs Ctrl or Alt, so it can't take over keys you type in the game."
        : null;

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key.Name);
        return string.Join('+', parts);
    }

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 1 || parts.Any(p => p.Length == 0)) return null;
        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => (HotkeyModifiers?)null
            };
            if (modifier is null || modifiers.HasFlag(modifier.Value)) return null;
            modifiers |= modifier.Value;
        }
        return HotkeyKey.Find(parts[^1]) is { } key ? new Hotkey(modifiers, key) : null;
    }
}
