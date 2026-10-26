using Ashla.Host.Overlay;
using Ashla.Host.Settings;
using Ashla.SDK;
using Ashla.UI;

namespace Ashla.Host.Launch;

internal enum LauncherOutcome
{
    Quit,

    Play
}

internal sealed record LauncherSettingsContext(
    PluginStateStore PluginState, Func<IReadOnlyList<InstalledPlugin>> FindPlugins, string OverlayLayoutFile,
    Func<Hotkey, bool>? IsHotkeyFree = null,
    EncounterArchive? Archive = null,
    PluginStore? Store = null);

internal sealed record PluginStore(HolocronClient Client, PluginInstaller Installer, InstalledSources Sources);

internal sealed class LauncherModel
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly AshlaSettingsStore _settings;
    private readonly IInstallEnvironment _environment;
    private readonly GameLauncher _launcher;
    private readonly Func<bool> _combatLogFolderExists;
    private readonly LauncherSettingsContext _context;
    private readonly ILogger _log;
    private DateTime _lastRefresh = DateTime.MinValue;

    public LauncherModel(AshlaSettingsStore settings, IInstallEnvironment environment, GameLauncher launcher,
        Func<bool> combatLogFolderExists, LauncherSettingsContext context, ILogger log)
    {
        _settings = settings;
        _environment = environment;
        _launcher = launcher;
        _combatLogFolderExists = combatLogFolderExists;
        _context = context;
        _log = log;
        ResolveInstallation();
        Refresh(force: true);
        if (!settings.Current.SetupDone) StartSetup(rerun: false);
    }

    public SettingsDraft? Editing { get; private set; }

    public float? UiScale => _settings.Current.Overlay.UiScale;

    public SwtorInstallation? Installation { get; private set; }

    public bool InstallationIsSaved { get; private set; }

    public string? SavedFolderProblem { get; private set; }

    public GameLaunchMethod Method => _settings.Current.Game.LaunchMethod ?? GameLauncher.Suggest(Installation);

    public bool CombatLogFolderExists { get; private set; }

    public bool GameRunning { get; private set; }

    public LaunchResult? LastResult { get; private set; }

    public LauncherOutcome? Outcome { get; private set; }

    public bool CanPlay => Method != GameLaunchMethod.SwtorLauncher || Installation is not null;

    public string PlayLabel => GameRunning || Method == GameLaunchMethod.Manual ? "Start Ashla" : "Play";

    public string MethodDescription => Method switch
    {
        _ when GameRunning => "SWTOR is already running. Ashla will appear over it.",
        GameLaunchMethod.Steam => "Play asks Steam to start SWTOR, then Ashla appears over the game.",
        GameLaunchMethod.SwtorLauncher => "Play opens the SWTOR launcher. Sign in there as usual; Ashla appears over the game.",
        _ => "You start SWTOR yourself. Ashla waits and appears over the game."
    };

    public void Refresh(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastRefresh < RefreshInterval) return;
        _lastRefresh = now;
        GameRunning = _launcher.IsGameRunning;
        CombatLogFolderExists = _combatLogFolderExists();
        PollStore();
    }

    public void OpenSettings(SettingsSection section = SettingsSection.Game) => Editing = NewDraft(section);

    private SettingsDraft NewDraft(SettingsSection section)
    {
        var plugins = _context.FindPlugins();
        return new SettingsDraft
        {
            Section = section,
            FolderInput = Installation?.Folder ?? _settings.Current.Game.InstallFolder ?? "",
            FolderIsAutomatic = !InstallationIsSaved,
            Method = Method,
            UiScale = _settings.Current.Overlay.UiScale,
            SaveFights = _settings.Current.Fights.Save,
            KeepDays = _settings.Current.Fights.KeepDays,
            ToggleHotkey = _settings.Current.Overlay.ParsedToggleHotkey(),
            ToastCorner = _settings.Current.Overlay.ToastCorner,
            Theme = _settings.Current.Appearance.Theme,
            CheckForUpdates = _settings.Current.Plugins.CheckForUpdates,
            Plugins = plugins,
            PluginEnabled = plugins.ToDictionary(p => p.Id, p => !_context.PluginState.IsDisabled(p.Id),
                StringComparer.OrdinalIgnoreCase),
            Error = SavedFolderProblem
        };
    }

    public void CancelSettings()
    {
        Editing = null;
        ThemeSelection.Apply(_settings.Current.Appearance);
    }

    public Faction? LastFaction => _settings.Current.Appearance.LastFaction;

    public AshlaPalette ThemeFor(ThemeChoice choice) => ThemeSelection.Resolve(choice, LastFaction);

    public void ChooseTheme(SettingsDraft draft, ThemeChoice choice)
    {
        draft.Theme = choice;
        AshlaTheme.Use(ThemeFor(choice));
    }

    public SwtorInstallation? DraftInstallation(SettingsDraft draft) =>
        draft.FolderIsAutomatic && string.IsNullOrWhiteSpace(draft.FolderInput)
            ? null
            : SwtorInstallFinder.FromUserPath(draft.FolderInput, _environment);

    public static void FolderTyped(SettingsDraft draft, string text)
    {
        draft.FolderInput = text;
        draft.FolderIsAutomatic = false;
        draft.Error = null;
        draft.Notice = null;
    }

    public void FolderPicked(SettingsDraft draft, string path)
    {
        var installation = SwtorInstallFinder.FromUserPath(path, _environment);
        draft.Notice = null;
        if (installation is null)
        {
            draft.Error = $"No {SwtorInstallation.LauncherFileName} there. Pick the {SwtorInstallation.LauncherFileName} in SWTOR's folder.";
            return;
        }
        draft.FolderInput = installation.Folder;
        draft.FolderIsAutomatic = false;
        draft.Error = null;
    }

    public Hotkey? ToggleHotkey => _settings.Current.Overlay.ParsedToggleHotkey();

    public static void StartHotkeyCapture(SettingsDraft draft)
    {
        draft.CapturingHotkey = true;
        draft.Error = null;
        draft.Notice = null;
    }

    public static void HotkeyPressed(SettingsDraft draft, Hotkey hotkey)
    {
        if (!draft.CapturingHotkey) return;
        if (hotkey.Problem is { } problem)
        {
            draft.Error = $"{hotkey}: {problem}";
            return;
        }
        draft.ToggleHotkey = hotkey;
        draft.CapturingHotkey = false;
        draft.Error = null;
    }

    public static void CancelHotkeyCapture(SettingsDraft draft)
    {
        draft.CapturingHotkey = false;
        draft.Error = null;
    }

    public static void ClearHotkey(SettingsDraft draft)
    {
        draft.ToggleHotkey = null;
        draft.CapturingHotkey = false;
        draft.Error = null;
    }

    public void FindAutomatically(SettingsDraft draft)
    {
        var found = SwtorInstallFinder.Find(_environment);
        draft.FolderInput = found?.Folder ?? "";
        draft.FolderIsAutomatic = true;
        draft.Error = found is null ? "SWTOR wasn't found in the usual places. Browse to its launcher.exe instead." : null;
        draft.Notice = found is null ? null : found.IsSteam ? "Found SWTOR in your Steam library." : "Found SWTOR.";
    }

    public bool SaveSettings()
    {
        if (Editing is not { } draft || !Apply(draft, setupDone: null)) return false;
        Editing = null;
        _log.Info("Settings saved.");
        return true;
    }

    private bool Apply(SettingsDraft draft, bool? setupDone)
    {
        if (CheckFolder(draft, out var folder) is { } folderProblem)
        {
            draft.Section = SettingsSection.Game;
            draft.Error = folderProblem;
            return false;
        }
        if (CheckHotkey(draft) is { } hotkeyProblem)
        {
            draft.Section = SettingsSection.Overlay;
            draft.Error = hotkeyProblem;
            return false;
        }

        var game = _settings.Current.Game;
        game.InstallFolder = folder;
        _settings.Current.Overlay.ToggleHotkey = draft.ToggleHotkey?.ToString() ?? "";
        game.LaunchMethod = draft.Method;
        _settings.Current.Overlay.UiScale = draft.UiScale;
        _settings.Current.Overlay.ToastCorner = draft.ToastCorner;
        _settings.Current.Appearance.Theme = draft.Theme;
        _settings.Current.Fights.Save = draft.SaveFights;
        _settings.Current.Fights.KeepDays = draft.KeepDays;
        _settings.Current.Plugins.CheckForUpdates = draft.CheckForUpdates;
        if (setupDone is { } done) _settings.Current.SetupDone = done;
        if (!_settings.Save())
        {
            draft.Error = "Settings couldn't be saved. The log has the reason.";
            return false;
        }

        foreach (var (id, enabled) in draft.PluginEnabled)
            if (_context.PluginState.IsDisabled(id) == enabled) _context.PluginState.SetEnabled(id, enabled);

        LastResult = null;
        ResolveInstallation();
        ThemeSelection.Apply(_settings.Current.Appearance);
        _context.Archive?.Prune();
        return true;
    }

    private string? CheckFolder(SettingsDraft draft, out string? folder)
    {
        folder = null;
        if (!(draft.FolderIsAutomatic || string.IsNullOrWhiteSpace(draft.FolderInput)))
        {
            var installation = SwtorInstallFinder.FromUserPath(draft.FolderInput, _environment);
            if (installation is null)
                return $"No {SwtorInstallation.LauncherFileName} in that folder. Pick the folder SWTOR is installed in, or clear the box to find it automatically.";
            folder = installation.Folder;
        }
        return null;
    }

    private string? CheckHotkey(SettingsDraft draft)
    {
        draft.CapturingHotkey = false;
        var savedHotkey = _settings.Current.Overlay.ParsedToggleHotkey();
        return draft.ToggleHotkey is { } hotkey && hotkey != savedHotkey && _context.IsHotkeyFree?.Invoke(hotkey) == false
            ? $"{hotkey} is already used by another program. Pick a different combination."
            : null;
    }

    public SettingsDraft? SetupDraft { get; private set; }

    public SetupStep SetupStep { get; private set; }

    public bool SetupIsRerun { get; private set; }

    public void StartSetup(bool rerun)
    {
        Editing = null;
        SetupDraft = NewDraft(SettingsSection.Game);
        SetupStep = SetupStep.Welcome;
        SetupIsRerun = rerun;
    }

    public void SetupBack()
    {
        if (SetupDraft is not { } draft || SetupStep == SetupStep.Welcome) return;
        draft.CapturingHotkey = false;
        draft.Error = null;
        draft.Notice = null;
        SetupStep--;
    }

    public void SetupNext()
    {
        if (SetupDraft is not { } draft) return;
        string? problem = SetupStep switch
        {
            SetupStep.Game => CheckFolder(draft, out _),
            SetupStep.Hotkey => CheckHotkey(draft),
            _ => null
        };
        draft.Error = problem;
        if (problem is not null) return;
        draft.Notice = null;

        if (SetupStep != SetupStep.Done)
        {
            if (SetupStep == SetupStep.Plugins) InstallPicks(draft);
            SetupStep++;
            if (SetupStep == SetupStep.CombatLogging) Refresh(force: true);
            if (SetupStep == SetupStep.Plugins && !CatalogLoaded) RefreshCatalog();
            return;
        }
        if (Installing) return;
        if (!Apply(draft, setupDone: true))
        {
            SetupStep = draft.Section == SettingsSection.Overlay ? SetupStep.Hotkey : SetupStep.Game;
            return;
        }
        SetupDraft = null;
        _log.Info("Setup finished.");
    }

    public void SkipSetup()
    {
        if (SetupDraft is null) return;
        SetupDraft = null;
        ThemeSelection.Apply(_settings.Current.Appearance);
        if (SetupIsRerun) return;
        _settings.Current.SetupDone = true;
        _settings.Save();
        _log.Info("Setup skipped.");
    }

    public void CheckCombatLogging() => Refresh(force: true);

    public EncounterArchive? Archive => _context.Archive;

    private Task<Catalog>? _fetch;
    private readonly Dictionary<string, Task<InstallResult>> _installs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InstallResult> _results = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<InstalledPlugin>? _installed;

    public bool StoreAvailable => _context.Store is not null;

    public Catalog Catalog { get; private set; } = Catalog.Empty;

    public bool CatalogLoaded { get; private set; }

    public bool Fetching => _fetch is not null;

    public IReadOnlyList<PluginRepository> Repositories => HolocronClient.Repositories(_settings.Current.Plugins);

    public IReadOnlyList<PluginUpdate> Updates { get; private set; } = [];

    public bool IsInstalling(string id) => _installs.ContainsKey(id);

    public bool Installing => _installs.Count > 0;

    public InstallResult? ResultOf(string id) => _results.GetValueOrDefault(id);

    public bool HasPlugins => (_installed ??= _context.FindPlugins()).Count > 0;

    public IReadOnlyList<CatalogEntry> SetupChoices =>
        [.. Catalog.Entries.Where(e => e.Repository.Official).OrderBy(e => e.Plugin.DisplayName, StringComparer.OrdinalIgnoreCase)];

    public bool IsInstalled(string id) =>
        (_installed ??= _context.FindPlugins()).Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private void InstallPicks(SettingsDraft draft)
    {
        foreach (var entry in SetupChoices)
            if (draft.PluginPicks.Contains(entry.Plugin.Id) && entry.Compatible && !IsInstalled(entry.Plugin.Id) &&
                ResultOf(entry.Plugin.Id) is not { Ok: true })
                Install(draft, entry);
    }

    public string? SourceOf(string id) => _context.Store?.Sources.Get(id)?.Repository;

    public void RefreshCatalog()
    {
        if (_context.Store is not { } store || _fetch is not null) return;
        var repositories = Repositories;
        _fetch = Task.Run(() => store.Client.FetchAsync(repositories, CancellationToken.None));
    }

    public void CheckForUpdatesInBackground()
    {
        if (_settings.Current.Plugins.CheckForUpdates) RefreshCatalog();
    }

    public void Install(SettingsDraft draft, CatalogEntry entry)
    {
        if (_context.Store is not { } store || _installs.ContainsKey(entry.Plugin.Id)) return;
        draft.Error = null;
        draft.Notice = null;
        _installs[entry.Plugin.Id] = Task.Run(() => store.Installer.InstallAsync(entry));
    }

    public void Remove(SettingsDraft draft, InstalledPlugin plugin)
    {
        if (_context.Store is not { } store) return;
        var result = store.Installer.Remove(plugin.Id, plugin.DisplayName);
        Report(draft, result);
        ReloadPlugins(draft);
    }

    public void AddRepository(SettingsDraft draft, string url)
    {
        url = url.Trim();
        if (HolocronClient.CheckRepositoryUrl(url) is { } problem)
        {
            draft.Notice = null;
            draft.Error = problem;
            return;
        }
        var list = _settings.Current.Plugins.Repositories;
        if (string.Equals(url, HolocronClient.Official, StringComparison.OrdinalIgnoreCase) ||
            list.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            draft.Error = null;
            draft.Notice = "That repository is already in the list.";
            return;
        }
        list.Add(url);
        _settings.Save();
        draft.Error = null;
        draft.Notice = $"Added {HolocronClient.ShortName(url)}.";
        CatalogLoaded = false;
        RefreshCatalog();
    }

    public void RemoveRepository(SettingsDraft draft, string url)
    {
        if (_settings.Current.Plugins.Repositories.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)) == 0)
            return;
        _settings.Save();
        draft.Error = null;
        draft.Notice = $"Removed {HolocronClient.ShortName(url)}. Plugins already installed from it stay installed.";
        Catalog = new Catalog([.. Catalog.Entries.Where(e => e.Repository.Url != url)],
            [.. Catalog.Repositories.Where(r => r.Repository.Url != url)]);
        UpdateUpdates();
    }

    private void PollStore()
    {
        if (_fetch is { IsCompleted: true } fetch)
        {
            _fetch = null;
            Catalog = fetch.IsCompletedSuccessfully ? fetch.Result : Catalog;
            CatalogLoaded = true;
            UpdateUpdates();
        }
        foreach (var (id, task) in _installs.Where(p => p.Value.IsCompleted).ToList())
        {
            _installs.Remove(id);
            var result = task.IsCompletedSuccessfully ? task.Result : new InstallResult(false, $"Installing {id} failed.");
            _results[id] = result;
            var draft = Editing ?? SetupDraft;
            if (draft is not null)
            {
                Report(draft, result);
                ReloadPlugins(draft);
            }
            else
            {
                _installed = null;
                UpdateUpdates();
            }
        }
    }

    private void UpdateUpdates()
    {
        if (_context.Store is not { } store) return;
        _installed = _context.FindPlugins();
        Updates = PluginUpdates.Find(_installed, Catalog, store.Sources);
    }

    private void ReloadPlugins(SettingsDraft draft)
    {
        var plugins = _context.FindPlugins();
        draft.Plugins = plugins;
        foreach (var plugin in plugins)
            if (!draft.PluginEnabled.ContainsKey(plugin.Id))
                draft.PluginEnabled[plugin.Id] = !_context.PluginState.IsDisabled(plugin.Id);
        UpdateUpdates();
    }

    private static void Report(SettingsDraft draft, InstallResult result)
    {
        draft.Error = result.Ok ? null : result.Message;
        draft.Notice = result.Ok ? result.Message : null;
    }

    public void ImportLog(SettingsDraft draft, string path)
    {
        if (_context.Archive is not { } archive) return;
        try
        {
            var result = archive.Import(path);
            draft.Error = result.Found == 0 ? result.Describe(Path.GetFileName(path)) : null;
            draft.Notice = result.Found == 0 ? null : result.Describe(Path.GetFileName(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            draft.Notice = null;
            draft.Error = $"Couldn't read {Path.GetFileName(path)}: {error.Message}";
        }
    }

    public void DeleteAllFights(SettingsDraft draft)
    {
        if (_context.Archive is not { } archive) return;
        int count = archive.DeleteAll();
        draft.Error = null;
        draft.Notice = $"Deleted {ImportResult.Plural(count, "saved fight")}.";
    }

    public void ResetPanelPositions(SettingsDraft draft)
    {
        try
        {
            if (File.Exists(_context.OverlayLayoutFile)) File.Delete(_context.OverlayLayoutFile);
            _log.Info("Overlay panel positions reset.");
            draft.Error = null;
            draft.Notice = "Panel positions reset.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Couldn't reset panel positions ({error.Message}).");
            draft.Notice = null;
            draft.Error = "Panel positions couldn't be reset. The log has the reason.";
        }
    }

    public void Play()
    {
        Refresh(force: true);
        if (!CanPlay)
        {
            LastResult = LaunchResult.Failure("Ashla doesn't know where SWTOR is installed. Set its folder in Settings.");
            OpenSettings(SettingsSection.Game);
            return;
        }
        var result = _launcher.Launch(Method, Installation);
        LastResult = result;
        _log.Info(result.Ok ? result.Message : $"Couldn't start SWTOR: {result.Message}");
        if (result.Ok) Outcome = LauncherOutcome.Play;
    }

    public void Quit() => Outcome = LauncherOutcome.Quit;

    private void ResolveInstallation()
    {
        SavedFolderProblem = null;
        var saved = _settings.Current.Game.InstallFolder;
        if (!string.IsNullOrWhiteSpace(saved))
        {
            var installation = SwtorInstallFinder.FromUserPath(saved, _environment);
            if (installation is not null)
            {
                Installation = installation;
                InstallationIsSaved = true;
                return;
            }
            SavedFolderProblem = $"The saved SWTOR folder no longer has {SwtorInstallation.LauncherFileName}: {saved}";
            _log.Warn(SavedFolderProblem);
        }
        Installation = SwtorInstallFinder.Find(_environment);
        InstallationIsSaved = false;
        if (Installation is not null) _log.Info($"Found SWTOR at {Installation.Folder}{(Installation.IsSteam ? " (Steam)" : "")}.");
    }
}
