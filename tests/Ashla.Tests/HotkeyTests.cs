using Ashla.Host.Overlay;
using Ashla.Host.Settings;
using Hexa.NET.ImGui;

namespace Ashla.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+H", "Ctrl+Shift+H")]
    [InlineData("shift+ctrl+h", "Ctrl+Shift+H")]
    [InlineData(" Alt + F10 ", "Alt+F10")]
    [InlineData("Control+Win+NumPad5", "Ctrl+Win+NumPad5")]
    [InlineData("Ctrl+Alt+Shift+Win+PageUp", "Ctrl+Alt+Shift+Win+PageUp")]
    [InlineData("Ctrl+/", "Ctrl+/")]
    [InlineData("Shift+K", "Shift+K")]
    public void Parse_ReadsAnyOrderAndCase_AndWritesOneForm(string text, string expected)
    {
        var hotkey = Hotkey.Parse(text);
        Assert.NotNull(hotkey);
        Assert.Equal(expected, hotkey.ToString());
        Assert.Equal(hotkey, Hotkey.Parse(hotkey.ToString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+H")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+Tab")]
    [InlineData("Hyper+H")]
    [InlineData("Ctrl+Shift")]
    public void Parse_RejectsWhatIsntAHotkey(string? text) => Assert.Null(Hotkey.Parse(text));

    [Theory]
    [InlineData("Ctrl+H", true)]
    [InlineData("Alt+H", true)]
    [InlineData("Ctrl+Alt+H", true)]
    [InlineData("H", false)]
    [InlineData("Shift+H", false)]
    [InlineData("Win+H", false)]
    public void Problem_RequiresCtrlOrAlt(string text, bool usable) =>
        Assert.Equal(usable, Hotkey.Parse(text)!.Problem is null);

    [Fact]
    public void Default_IsCtrlShiftH()
    {
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, Hotkey.Default.Modifiers);
        Assert.Equal("H", Hotkey.Default.Key.Name);
        Assert.Null(Hotkey.Default.Problem);
    }

    [Fact]
    public void Modifiers_MatchWin32Flags()
    {
        Assert.Equal(0x1, (int)HotkeyModifiers.Alt);
        Assert.Equal(0x2, (int)HotkeyModifiers.Ctrl);
        Assert.Equal(0x4, (int)HotkeyModifiers.Shift);
        Assert.Equal(0x8, (int)HotkeyModifiers.Win);
    }

    [Theory]
    [InlineData("A", 0x41u, ImGuiKey.A)]
    [InlineData("Z", 0x5Au, ImGuiKey.Z)]
    [InlineData("0", 0x30u, ImGuiKey.Key0)]
    [InlineData("9", 0x39u, ImGuiKey.Key9)]
    [InlineData("F1", 0x70u, ImGuiKey.F1)]
    [InlineData("F12", 0x7Bu, ImGuiKey.F12)]
    [InlineData("F24", 0x87u, ImGuiKey.F24)]
    [InlineData("NumPad0", 0x60u, ImGuiKey.Keypad0)]
    [InlineData("NumPad9", 0x69u, ImGuiKey.Keypad9)]
    [InlineData("Space", 0x20u, ImGuiKey.Space)]
    [InlineData("Home", 0x24u, ImGuiKey.Home)]
    [InlineData("Down", 0x28u, ImGuiKey.DownArrow)]
    [InlineData("`", 0xC0u, ImGuiKey.GraveAccent)]
    public void KeyTable_MapsNamesToVirtualKeysAndImGuiKeys(string name, uint virtualKey, ImGuiKey imGuiKey)
    {
        var key = HotkeyKey.Find(name);
        Assert.NotNull(key);
        Assert.Equal(virtualKey, key.VirtualKey);
        Assert.Equal(imGuiKey, key.ImGuiKey);
    }

    [Fact]
    public void KeyTable_HasNoDuplicates()
    {
        Assert.Equal(HotkeyKey.All.Count, HotkeyKey.All.Select(k => k.Name.ToUpperInvariant()).Distinct().Count());
        Assert.Equal(HotkeyKey.All.Count, HotkeyKey.All.Select(k => k.VirtualKey).Distinct().Count());
        Assert.Equal(HotkeyKey.All.Count, HotkeyKey.All.Select(k => k.ImGuiKey).Distinct().Count());
    }

    [Fact]
    public void Settings_MissingValue_MeansDefault_EmptyMeansNone_BadMeansNone()
    {
        Assert.Equal(Hotkey.Default, new OverlaySettings().ParsedToggleHotkey());
        Assert.Equal(Hotkey.Default, new OverlaySettings { ToggleHotkey = null }.ParsedToggleHotkey());
        Assert.Null(new OverlaySettings { ToggleHotkey = "" }.ParsedToggleHotkey());
        Assert.Null(new OverlaySettings { ToggleHotkey = "Ctrl+Nope" }.ParsedToggleHotkey());
        Assert.Null(new OverlaySettings { ToggleHotkey = "Shift+K" }.ParsedToggleHotkey());
        Assert.Equal("Alt+F2", new OverlaySettings { ToggleHotkey = "alt+f2" }.ParsedToggleHotkey()?.ToString());
    }

    [Fact]
    public void Settings_OlderFileWithoutHotkey_GetsTheDefault()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), "{ \"Version\": 1, \"Overlay\": { \"UiScale\": 1.5 } }");
        var store = AshlaSettingsStore.Load(temp.Path, new TestLogger());
        Assert.Equal(Hotkey.Default, store.Current.Overlay.ParsedToggleHotkey());
    }
}
