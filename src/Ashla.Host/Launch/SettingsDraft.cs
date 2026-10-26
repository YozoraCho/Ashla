using Ashla.Host.Overlay;
using Ashla.Host.Settings;

namespace Ashla.Host.Launch;

internal enum SetupStep
{
    Welcome,
    Game,
    CombatLogging,
    Hotkey,
    Toasts,
    Theme,
    Plugins,
    Done
}

internal enum SettingsSection
{
    Game,
    Overlay,
    Appearance,
    Fights,
    Plugins,
    About
}

internal sealed class SettingsDraft
{
    public SettingsSection Section { get; set; } = SettingsSection.Game;

    public string FolderInput { get; set; } = "";

    public bool FolderIsAutomatic { get; set; }

    public GameLaunchMethod Method { get; set; }

    public float? UiScale { get; set; }

    public bool SaveFights { get; set; } = true;

    public int KeepDays { get; set; } = 30;

    public Hotkey? ToggleHotkey { get; set; }

    public ToastCorner ToastCorner { get; set; } = ToastCorner.BottomRight;

    public ThemeChoice Theme { get; set; }

    public bool CapturingHotkey { get; set; }

    public IReadOnlyList<InstalledPlugin> Plugins { get; set; } = [];

    public bool CheckForUpdates { get; set; } = true;

    public Dictionary<string, bool> PluginEnabled { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> PluginPicks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Error { get; set; }

    public string? Notice { get; set; }
}
