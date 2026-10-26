using System.Globalization;
using Ashla.Host;
using Ashla.Host.Gui;
using Ashla.Host.Launch;
using Ashla.Host.Overlay;
using Ashla.Host.Overlays;
using Ashla.Host.Settings;
using Ashla.Rendering;
using Ashla.SDK;

var logger = new HostLogger("Host");
string localDir = SettingsLocations.LocalDirectory();
string configDir = SettingsLocations.DefaultSettingsDirectory();
bool configGiven = false;
static bool IsHotkeyFree(Hotkey hotkey) =>
    !OperatingSystem.IsWindows() || TrayIcon.IsHotkeyAvailable((uint)hotkey.Modifiers, hotkey.Key.VirtualKey);

string DefaultCombatLogFolder() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify),
    "Star Wars - The Old Republic", "CombatLogs");
LogHub.StartFileLog(Path.Combine(localDir, "logs"));
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    logger.Error("Ashla stopped because of an unexpected error", e.ExceptionObject as Exception);
    LogHub.Stop();
    if (AshlaBuild.IsPlayerBuild && OperatingSystem.IsWindows())
        NativeDialogs.ShowError("Ashla", $"Ashla stopped because of an unexpected error:\n\n{(e.ExceptionObject as Exception)?.Message}" +
                                         $"\n\nDetails are in the log file:\n{LogHub.FilePath}");
};

var events = new EventBus(logger);
IGameState game = new ExternalProvider();

string pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
string? replay = null;
string? watch = null;
bool demo = false;
bool gui = false;
bool overlay = false;
bool launcher = false;
string targetProcess = "swtor";
string? targetTitle = null;
WindowBounds? overlayRect = null;
float? uiScale = null;

int argumentIndex = 0;
for (; argumentIndex < args.Length; argumentIndex++)
{
    switch (args[argumentIndex])
    {
        case "--demo": demo = true; break;
        case "--gui": gui = true; break;
        case "--overlay": overlay = gui = true; break;
        case "--launcher": launcher = overlay = gui = true; break;
        case "--target-process": targetProcess = Next(); break;
        case "--target-title": targetTitle = Next(); break;
        case "--overlay-rect":
            var rectText = Next();
            if (!WindowBounds.TryParse(rectText, out var rect))
                throw new ArgumentException($"--overlay-rect expects x,y,width,height (e.g. 0,0,1920,1080), got '{rectText}'.");
            overlayRect = rect;
            overlay = gui = true;
            break;
        case "--plugins":
            pluginsDir = Next(); break;
        case "--holocron-url":
            HolocronClient.Official = Next(); break;
        case "--config":
            configDir = Next(); configGiven = true; break;
        case "--ui-scale":
            var scaleText = Next();
            if (!float.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale is < 0.5f or > 4f)
                throw new ArgumentException($"--ui-scale expects a number between 0.5 and 4, got '{scaleText}'.");
            uiScale = scale;
            break;
        case "--replay": replay = Next(); demo = false; break;
        case "--watch":
            var nextIsPath = argumentIndex + 1 < args.Length && !args[argumentIndex + 1].StartsWith("--", StringComparison.Ordinal);
            watch = nextIsPath ? Next() : DefaultCombatLogFolder();
            demo = false; break;
        case "--help": PrintUsage(); return;
        default: throw new ArgumentException($"Unknown argument '{args[argumentIndex]}'. Try --help.");
    }
}

if (args.Length == 0)
{
    if (AshlaBuild.IsPlayerBuild)
    {
        launcher = overlay = gui = true;
    }
    else demo = true;
}

if (launcher && watch is null && replay is null && !demo) watch = DefaultCombatLogFolder();

if (configGiven) LogHub.StartFileLog(Path.Combine(configDir, "logs"));
else SettingsLocations.CopyOlderSettings(localDir, configDir, logger);
logger.Info($"Ashla {AshlaBuild.Version}{(AshlaBuild.IsPlayerBuild ? "" : " (developer build)")}. Log file: {LogHub.FilePath ?? "none"}");

string Next()
{
    if (argumentIndex + 1 >= args.Length) throw new ArgumentException($"Argument {args[argumentIndex]} expects a value.");
    return args[++argumentIndex];
}

void PrintUsage()
{
    Console.WriteLine("Ashla.Host [--launcher | --gui | --overlay] [--demo | --replay <file> | --watch [directory]] [options]");
    Console.WriteLine("--launcher Shows the launcher (Play / Settings), then the overlay. The player build always starts here");
    Console.WriteLine("--gui      Opens the graphical host window and renders plugin panels (SWTOR not required)");
    Console.WriteLine("--overlay  Shows plugin panels in a transparent, click-through window over the SWTOR window");
    Console.WriteLine("           (Windows; SWTOR in windowed or fullscreen-windowed mode). Nothing is injected.");
    Console.WriteLine("--demo     Publishes synthetic events (no SWTOR needed)");
    Console.WriteLine("--replay   Publishes each line of a combat-log text file");
    Console.WriteLine("--watch    Tails new lines in the most recently modified .txt/.log file");
    Console.WriteLine("Options:");
    Console.WriteLine("--plugins <dir>   Plugin directory (default: <host>/plugins)");
    Console.WriteLine("--config <dir>    Settings and logs directory (default: settings in %APPDATA%/Ashla,");
    Console.WriteLine("                  logs in %LOCALAPPDATA%/Ashla/logs)");
    Console.WriteLine("--ui-scale <n>    Overrides the monitor DPI scale in --gui/--overlay mode (e.g. 1.5)");
    Console.WriteLine("--target-process <name>  Overlay: process whose main window to follow (default: swtor)");
    Console.WriteLine("--target-title <text>    Overlay: only follow a window whose title contains this text");
    Console.WriteLine("--overlay-rect x,y,w,h   Overlay: pin to a fixed screen rectangle instead (testing; any OS)");
    Console.WriteLine("--holocron-url <url>     Uses another plugin list instead of Holocron's (testing; http only on localhost)");
    Console.WriteLine("Without --gui, --replay exits when done and --watch runs until Ctrl+C.");
    Console.WriteLine("With --gui, data sources run in the background until the window is closed.");
}

using var singleInstance = new Mutex(initiallyOwned: false, @"Local\Ashla.Overlay");
if (overlay && AshlaBuild.IsPlayerBuild)
{
    bool acquired;
    try { acquired = singleInstance.WaitOne(0); }
    catch (AbandonedMutexException) { acquired = true; }
    if (!acquired)
    {
        logger.Warn("Ashla is already running.");
        if (OperatingSystem.IsWindows())
            NativeDialogs.ShowInfo("Ashla", "Ashla is already running. Look for its icon in the notification area (system tray).");
        return;
    }
}

var settings = AshlaSettingsStore.Load(configDir, logger);
ThemeSelection.Apply(settings.Current.Appearance);
var archive = new EncounterArchive(Path.Combine(configGiven ? configDir : localDir, "encounters"), new HostLogger("Fights"),
    () => settings.Current.Fights.KeepDays);
archive.Prune();
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd($"Ashla/{AshlaBuild.Version}");
var installedSources = new InstalledSources(Path.Combine(configDir, "installed-plugins.json"), new HostLogger("Plugins"));
var store = new PluginStore(
    new HolocronClient(http, Path.Combine(configGiven ? configDir : localDir, "holocron"), new HostLogger("Holocron")),
    new PluginInstaller(http, pluginsDir, installedSources, new HostLogger("Plugins")),
    installedSources);
store.Installer.CleanUp();
Catalog? launcherCatalog = null;
string iconPath = Path.Combine(AppContext.BaseDirectory, "ashla.ico");
var icons = IconFile.Load(iconPath);
var pluginState = PluginStateStore.Load(Path.Combine(configDir, "plugins.json"), logger);
float? UiScale() => uiScale ?? settings.Current.Overlay.UiScale;

if (launcher)
{
    var model = new LauncherModel(settings, new SystemInstallEnvironment(), new GameLauncher(new SystemProcessStarter()),
        () => Directory.Exists(DefaultCombatLogFolder()),
        new LauncherSettingsContext(pluginState, () => InstalledPlugins.Find(pluginsDir),
            Path.Combine(configDir, "imgui-overlay.ini"),
            OperatingSystem.IsWindows() ? IsHotkeyFree : null, archive, store),
        new HostLogger("Launcher"));
    model.CheckForUpdatesInBackground();
    var outcome = LauncherWindow.Run(model, AshlaBuild.Version, new LauncherFolders(pluginsDir, LogHub.Directory, DefaultCombatLogFolder()),
        UiScale(), icons, logger);
    if (model.CatalogLoaded) launcherCatalog = model.Catalog;
    if (outcome != LauncherOutcome.Play)
    {
        logger.Info("Closed from the launcher.");
        LogHub.Stop();
        return;
    }
}

IGameWindowSource? overlaySource = null;
if (overlay)
{
    if (overlayRect is { } fixedRect) overlaySource = new FixedWindowSource(fixedRect);
    else if (OperatingSystem.IsWindows())
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        overlaySource = new GameWindowTracker(new Win32WindowSystem(), targetProcess, targetTitle, () => clock.Elapsed);
    }
    else
    {
        logger.Error("Following a game window is only implemented on Windows. Use --overlay-rect x,y,w,h on this OS.");
        Environment.ExitCode = 2;
        return;
    }
}

logger.Info("External-only host. No process injection, memory reading or UI modification.");
var toasts = new ToastCenter();
var panels = new PanelRegistry(new HostLogger("UI"), isAvailable: gui, toasts);
using var pluginManager = new PluginManager(logger, id => new HostLogger(id), events, game, panels, pluginState, configDir,
    toasts, archive);
var recorder = new EncounterRecorder(recording =>
{
    if (settings.Current.Fights.Save) archive.Save(recording);
});
using var recorderSubscription = watch is not null ? events.Subscribe<CombatEvent>(recorder.Handle) : null;
var combatLogStatus = new CombatLogStatus();
var factions = new FactionWatcher();
using var factionSubscription = events.Subscribe<CombatEvent>(factions.Handle);
ThemeSelection.Follow(factions, settings, logger);
pluginManager.LoadFrom(pluginsDir);
logger.Info($"Loaded {pluginManager.Count} plugin(s). Game capabilities: {game.Capabilities}");
if (gui && settings.Current.Plugins.CheckForUpdates)
    _ = Task.Run(async () =>
    {
        try
        {
            var catalog = launcherCatalog ??
                          await store.Client.FetchAsync(HolocronClient.Repositories(settings.Current.Plugins), CancellationToken.None);
            var updates = PluginUpdates.Find(InstalledPlugins.Find(pluginsDir), catalog, installedSources);
            if (updates.Count == 0) return;
            logger.Info($"Plugin updates available: {string.Join(", ", updates.Select(u => $"{u.Name} {u.Installed} -> {u.Entry.Plugin.Version}"))}");
            toasts.Show(updates.Count == 1 ? "Plugin update available" : "Plugin updates available",
                string.Join(", ", updates.Select(u => $"{u.Name} {u.Entry.Plugin.Version}")) +
                ". Install from the launcher's Settings > Plugins next time you start Ashla.", ToastKind.Info);
        }
        catch (Exception error)
        {
            logger.Warn($"Checking for plugin updates failed: {error.Message}");
        }
    });

if (gui)
{
    using var cts = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
    var source = Task.Run(() => RunSourceAsync(cts.Token));
    var options = new RenderWindowOptions
    {
        Title = overlay ? "Ashla Overlay" : "Ashla",
        UiScaleOverride = UiScale(),
        IniFilePath = Path.Combine(configDir, overlay ? "imgui-overlay.ini" : "imgui.ini"),
        Icons = icons
    };
    Environment.ExitCode = GuiHost.Run(options, panels, pluginManager, logger, onShutdown: () =>
    {
        cts.Cancel();
        try { source.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException error) when (error.InnerExceptions.All(e => e is OperationCanceledException)) { }
        catch (AggregateException error) { logger.Error("Data source failed", error.InnerException); }
        if (watch is not null) recorder.Flush();
        pluginManager.Dispose();
    }, new GuiServices(AshlaBuild.Version, pluginsDir, configDir, watch is not null ? combatLogStatus : null,
        iconPath, settings.Current.Overlay.ParsedToggleHotkey(), toasts, settings.Current.Overlay.ToastCorner, settings,
        archive, watch ?? DefaultCombatLogFolder()),
        overlaySource, stop.Token);
    LogHub.Stop();
    return;
}

await RunSourceAsync(CancellationToken.None, console: true);

async Task RunSourceAsync(CancellationToken token, bool console = false)
{
    if (replay is not null)
    {
        await CombatLogSource.ReplayAsync(replay, events, logger, token);
        logger.Info("Replay complete.");
    }
    else if (watch is not null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (console) Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cts.Cancel(); };
        await CombatLogSource.WatchAsync(watch, events, logger, cts.Token, combatLogStatus);
    }
    else if (demo)
    {
        logger.Info("Emitting synthetic demo data (not SWTOR combat-log syntax)...");
        foreach (var line in new[]
        {
            "DEMO: player enters combat",
            "DEMO: player uses an ability",
            "DEMO: target takes damage",
            "DEMO: player receives healing",
            "DEMO: combat ends"
        })
            events.Publish(new CombatLogLine(DateTimeOffset.Now, "synthetic-demo", line));
        logger.Info("Demo complete.");
    }
    else if (console) PrintUsage();
}

internal static class AshlaBuild
{
#if ASHLA_PLAYER
    public static readonly bool IsPlayerBuild = true;
#else
    public static readonly bool IsPlayerBuild = false;
#endif

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var version = typeof(AshlaBuild).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0";
        int plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }
}
