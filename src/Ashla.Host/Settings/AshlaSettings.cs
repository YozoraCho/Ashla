using Ashla.SDK;

namespace Ashla.Host.Settings;

internal enum GameLaunchMethod
{
    SwtorLauncher,

    Steam,

    Manual
}

internal enum ToastCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

internal sealed class AshlaSettings
{
    public int Version { get; set; } = 1;

    public bool SetupDone { get; set; }

    public GameSettings Game { get; set; } = new();

    public OverlaySettings Overlay { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

    public FightSettings Fights { get; set; } = new();

    public PluginSettings Plugins { get; set; } = new();
}

internal sealed class PluginSettings
{
    public bool CheckForUpdates { get; set; } = true;

    public List<string> Repositories { get; set; } = [];
}

internal sealed class FightSettings
{
    public static readonly int[] Choices = [7, 30, 90, 0];

    public bool Save { get; set; } = true;

    public int KeepDays { get; set; } = 30;
}

internal enum ThemeChoice
{
    Auto,
    Ashla,
    Republic,
    Empire,
    Mandalorian
}

internal sealed class AppearanceSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.Auto;

    public Faction? LastFaction { get; set; }
}

internal sealed class OverlaySettings
{
    public float? UiScale { get; set; }

    public string? ToggleHotkey { get; set; } = Ashla.Host.Overlay.Hotkey.DefaultText;

    public Ashla.Host.Overlay.Hotkey? ParsedToggleHotkey() => ToggleHotkey is null
        ? Ashla.Host.Overlay.Hotkey.Default
        : Ashla.Host.Overlay.Hotkey.Parse(ToggleHotkey) is { Problem: null } hotkey ? hotkey : null;

    public ToastCorner ToastCorner { get; set; } = ToastCorner.BottomRight;

    public static readonly float[] Choices = [1f, 1.25f, 1.5f, 1.75f, 2f];
}

internal sealed class GameSettings
{
    public GameLaunchMethod? LaunchMethod { get; set; }

    public string? InstallFolder { get; set; }
}

internal sealed class AshlaSettingsStore
{
    public const string FileName = "settings.json";

    private readonly ILogger _log;
    private readonly object _gate = new();

    private AshlaSettingsStore(string filePath, ILogger log, AshlaSettings settings)
    {
        FilePath = filePath;
        _log = log;
        Current = settings;
    }

    public string FilePath { get; }

    public AshlaSettings Current { get; }

    public static AshlaSettingsStore Load(string settingsDirectory, ILogger log)
    {
        var path = Path.Combine(settingsDirectory, FileName);
        return new AshlaSettingsStore(path, log, JsonFile.Load<AshlaSettings>(path, log, "Ashla's settings"));
    }

    public bool Save()
    {
        lock (_gate) return JsonFile.Save(FilePath, Current, _log, "Ashla's settings");
    }
}
