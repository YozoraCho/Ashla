using System.Diagnostics;
using Ashla.Host;
using Ashla.Host.Launch;
using Ashla.Host.Overlay;
using Ashla.Host.Settings;

namespace Ashla.Tests;

public class SettingsTests
{
    private readonly TestLogger _log = new();

    [Fact]
    public void MissingFile_GivesDefaults_AndSaveRoundTrips()
    {
        using var temp = new TempDirectory();
        var store = AshlaSettingsStore.Load(temp.Path, _log);
        Assert.Null(store.Current.Game.InstallFolder);
        Assert.Null(store.Current.Game.LaunchMethod);

        store.Current.Game.InstallFolder = @"D:\Games\SWTOR";
        store.Current.Game.LaunchMethod = GameLaunchMethod.Steam;
        Assert.True(store.Save());

        var text = File.ReadAllText(Path.Combine(temp.Path, "settings.json"));
        Assert.Contains("\"Steam\"", text);
        Assert.False(File.Exists(Path.Combine(temp.Path, "settings.json.tmp")));

        var reloaded = AshlaSettingsStore.Load(temp.Path, _log);
        Assert.Equal(@"D:\Games\SWTOR", reloaded.Current.Game.InstallFolder);
        Assert.Equal(GameLaunchMethod.Steam, reloaded.Current.Game.LaunchMethod);
        Assert.Empty(_log.Warnings);
    }

    [Fact]
    public void CorruptFile_GivesDefaults_KeepsCopy_AndWarns()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ \"Game\": { \"InstallFolder\": ");

        var store = AshlaSettingsStore.Load(temp.Path, _log);

        Assert.Null(store.Current.Game.InstallFolder);
        Assert.Equal("{ \"Game\": { \"InstallFolder\": ", File.ReadAllText(path + ".bad"));
        Assert.Contains(_log.Warnings, w => w.Contains("settings.json.bad"));
    }

    [Fact]
    public void UnknownFieldsAndCase_AreTolerated()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"),
            "{ \"version\": 1, \"futureSection\": { \"x\": 1 }, \"game\": { \"launchMethod\": \"manual\" } }");

        var store = AshlaSettingsStore.Load(temp.Path, _log);

        Assert.Equal(GameLaunchMethod.Manual, store.Current.Game.LaunchMethod);
        Assert.Empty(_log.Warnings);
    }

    [Fact]
    public void PluginConfig_SavesPerPlugin_InPluginConfigsFolder()
    {
        using var temp = new TempDirectory();
        var meter = new PluginConfigStore(temp.Path, "CombatMeter", _log);
        var other = new PluginConfigStore(temp.Path, "Other", _log);

        Assert.Equal(0, meter.Load<SampleSettings>().Count);
        Assert.True(meter.Save(new SampleSettings { Count = 3, Name = "Tëst" }));

        Assert.Equal(Path.Combine(temp.Path, "pluginConfigs", "CombatMeter.json"), meter.FilePath);
        Assert.True(File.Exists(meter.FilePath));
        var loaded = new PluginConfigStore(temp.Path, "CombatMeter", _log).Load<SampleSettings>();
        Assert.Equal(3, loaded.Count);
        Assert.Equal("Tëst", loaded.Name);
        Assert.Equal(0, other.Load<SampleSettings>().Count);
    }

    [Theory]
    [InlineData("CombatMeter", "CombatMeter")]
    [InlineData("My:Plugin", "My_Plugin")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("..", "_")]
    public void PluginConfig_FileNames_AreSafe(string id, string expected)
    {
        Assert.Equal(expected, PluginConfigStore.SafeFileName(id));
    }

    [Fact]
    public void InMemoryPluginConfig_ReturnsCopies()
    {
        var config = new InMemoryPluginConfig();
        var original = new SampleSettings { Count = 1 };
        config.Save(original);
        original.Count = 99;

        Assert.Equal(1, config.Load<SampleSettings>().Count);
    }

    [Fact]
    public void OlderSettingsFiles_AreCopiedOnce_WithoutOverwriting()
    {
        using var temp = new TempDirectory();
        var oldDir = Path.Combine(temp.Path, "Local");
        var newDir = Path.Combine(temp.Path, "Roaming");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "plugins.json"), "old plugins");
        File.WriteAllText(Path.Combine(oldDir, "imgui-overlay.ini"), "old layout");
        Directory.CreateDirectory(newDir);
        File.WriteAllText(Path.Combine(newDir, "imgui-overlay.ini"), "new layout");

        SettingsLocations.CopyOlderSettings(oldDir, newDir, _log);

        Assert.Equal("old plugins", File.ReadAllText(Path.Combine(newDir, "plugins.json")));
        Assert.Equal("new layout", File.ReadAllText(Path.Combine(newDir, "imgui-overlay.ini")));
        Assert.True(File.Exists(Path.Combine(oldDir, "plugins.json")));
    }

    [Fact]
    public void PluginTypeContract_HandlesNestedListsEnumsAndAttributes()
    {
        var resolver = new UnloadSafeTypeResolver(t => t.DeclaringType == typeof(SettingsTests) || t == typeof(List<Nested>));
        var original = new RichSettings
        {
            Mode = Mode.Compact,
            Child = new Nested { Value = 5, Label = "a" },
            Items = [new Nested { Value = 1 }, new Nested { Value = 2, Label = "b" }],
            Counts = new() { ["x"] = 3 },
            Renamed = "kept",
            Ignored = "dropped"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original, JsonFile.NewOptions(resolver));
        var copy = System.Text.Json.JsonSerializer.Deserialize<RichSettings>(json, JsonFile.NewOptions(resolver))!;

        Assert.Contains("\"Compact\"", json);
        Assert.Contains("\"other_name\"", json);
        Assert.DoesNotContain("dropped", json);
        Assert.Contains("\"ReadOnly\"", json);
        Assert.Equal(Mode.Compact, copy.Mode);
        Assert.Equal(5, copy.Child!.Value);
        Assert.Equal("a", copy.Child.Label);
        Assert.Equal([1, 2], copy.Items.Select(i => i.Value));
        Assert.Equal("b", copy.Items[1].Label);
        Assert.Equal(3, copy.Counts["x"]);
        Assert.Equal("kept", copy.Renamed);
        Assert.Null(copy.Ignored);
    }

    [Fact]
    public void PluginTypeDetection_LooksAtGenericArgumentsAndArrays()
    {
        Assert.False(UnloadSafeTypeResolver.ComesFromCollectibleAssembly(typeof(List<int>)));
        Assert.False(UnloadSafeTypeResolver.ComesFromCollectibleAssembly(typeof(SampleSettings[])));
    }

    public enum Mode { Normal, Compact }

    public sealed class Nested
    {
        public int Value { get; set; }
        public string? Label { get; set; }
    }

    public sealed class RichSettings
    {
        public Mode Mode { get; set; }
        public Nested? Child { get; set; }
        public List<Nested> Items { get; set; } = [];
        public Dictionary<string, int> Counts { get; set; } = [];
        [System.Text.Json.Serialization.JsonPropertyName("other_name")] public string? Renamed { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public string? Ignored { get; set; }
        public int ReadOnly => 7;
    }

    public sealed class SampleSettings
    {
        public int Count { get; set; }
        public string? Name { get; set; }
    }
}

public class SwtorDetectionTests
{
    private const string Steam = @"C:\Program Files (x86)\Steam";
    private static readonly string SteamGame = Path.Combine(Steam, "steamapps", "common", "Star Wars - The Old Republic");
    private static readonly string Standalone =
        Path.Combine(@"C:\Program Files (x86)", "Electronic Arts", "BioWare", "Star Wars - The Old Republic");

    [Fact]
    public void FindsStandaloneInstall()
    {
        var env = new FakeEnvironment().WithLauncher(Standalone);
        var found = SwtorInstallFinder.Find(env);
        Assert.Equal(new SwtorInstallation(Standalone, IsSteam: false), found);
    }

    [Fact]
    public void FindsOlderStandaloneFolderName()
    {
        var folder = Path.Combine(@"C:\Program Files", "Electronic Arts", "BioWare", "SWTOR");
        var env = new FakeEnvironment().WithLauncher(folder);
        Assert.Equal(folder, SwtorInstallFinder.Find(env)?.Folder);
    }

    [Fact]
    public void FindsSteamInstall_InMainLibrary()
    {
        var env = new FakeEnvironment { SteamFolder = Steam }.WithLauncher(SteamGame).WithLauncher(Standalone);
        var found = SwtorInstallFinder.Find(env);
        Assert.Equal(new SwtorInstallation(SteamGame, IsSteam: true), found);
    }

    [Fact]
    public void FindsSteamInstall_InOtherLibrary()
    {
        var game = Path.Combine(@"E:\SteamLibrary", "steamapps", "common", "Star Wars - The Old Republic");
        var env = new FakeEnvironment { SteamFolder = Steam }.WithLauncher(game);
        env.Files[Path.Combine(Steam, "steamapps", "libraryfolders.vdf")] = LibraryFolders;

        Assert.Equal(new SwtorInstallation(game, IsSteam: true), SwtorInstallFinder.Find(env));
    }

    [Fact]
    public void NothingInstalled_ReturnsNull()
    {
        Assert.Null(SwtorInstallFinder.Find(new FakeEnvironment { SteamFolder = Steam }));
    }

    [Fact]
    public void ParsesSteamLibraryFile()
    {
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"E:\SteamLibrary"],
            SwtorInstallFinder.ParseLibraryFolders(LibraryFolders));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UserPath_AcceptsFolderOrLauncherExe(bool pickExe)
    {
        var env = new FakeEnvironment().WithLauncher(Standalone);
        var path = pickExe ? Path.Combine(Standalone, "launcher.exe") : Standalone;

        Assert.Equal(new SwtorInstallation(Standalone, IsSteam: false), SwtorInstallFinder.FromUserPath($"\"{path}\"", env));
        Assert.Null(SwtorInstallFinder.FromUserPath(@"C:\Somewhere\Else", env));
        Assert.Null(SwtorInstallFinder.FromUserPath("  ", env));
        Assert.True(SwtorInstallFinder.FromUserPath(SteamGame, new FakeEnvironment().WithLauncher(SteamGame))!.IsSteam);
    }

    private const string LibraryFolders = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
                "label"		""
                "apps"
                {
                    "228980"		"283712345"
                }
            }
            "1"
            {
                "path"		"E:\\SteamLibrary"
                "label"		""
                "apps"
                {
                    "1286830"		"52345678901"
                }
            }
        }
        """;
}

public class GameLauncherTests
{
    private readonly FakeStarter _starter = new();
    private static readonly SwtorInstallation Install = new(@"C:\Games\SWTOR", IsSteam: false);

    [Fact]
    public void Steam_OpensSteamUrlForSwtor()
    {
        var result = new GameLauncher(_starter).Launch(GameLaunchMethod.Steam, null);

        Assert.True(result.Ok);
        var started = Assert.Single(_starter.Started);
        Assert.Equal("steam://rungameid/1286830", started.FileName);
        Assert.True(started.UseShellExecute);
        Assert.Empty(started.Arguments);
    }

    [Fact]
    public void SwtorLauncher_StartsLauncherExe_FromItsFolder()
    {
        var result = new GameLauncher(_starter).Launch(GameLaunchMethod.SwtorLauncher, Install);

        Assert.True(result.Ok);
        var started = Assert.Single(_starter.Started);
        Assert.Equal(Path.Combine(Install.Folder, "launcher.exe"), started.FileName);
        Assert.Equal(Install.Folder, started.WorkingDirectory);
        Assert.Empty(started.Arguments);
    }

    [Fact]
    public void SwtorLauncher_WithoutFolder_Fails()
    {
        var result = new GameLauncher(_starter).Launch(GameLaunchMethod.SwtorLauncher, null);
        Assert.False(result.Ok);
        Assert.Empty(_starter.Started);
    }

    [Fact]
    public void Manual_StartsNothing()
    {
        Assert.True(new GameLauncher(_starter).Launch(GameLaunchMethod.Manual, Install).Ok);
        Assert.Empty(_starter.Started);
    }

    [Fact]
    public void GameAlreadyRunning_StartsNothing()
    {
        _starter.Running = true;
        var result = new GameLauncher(_starter).Launch(GameLaunchMethod.SwtorLauncher, Install);
        Assert.True(result.Ok);
        Assert.Contains("already running", result.Message);
        Assert.Empty(_starter.Started);
    }

    [Fact]
    public void StartFailure_IsReported()
    {
        _starter.Fail = true;
        var result = new GameLauncher(_starter).Launch(GameLaunchMethod.Steam, null);
        Assert.False(result.Ok);
        Assert.Contains("Steam", result.Message);
    }

    [Fact]
    public void Suggestion_FollowsInstallType()
    {
        Assert.Equal(GameLaunchMethod.Steam, GameLauncher.Suggest(new SwtorInstallation("x", IsSteam: true)));
        Assert.Equal(GameLaunchMethod.SwtorLauncher, GameLauncher.Suggest(Install));
        Assert.Equal(GameLaunchMethod.Manual, GameLauncher.Suggest(null));
    }
}

[Collection("AshlaTheme")]
public class LauncherModelTests : IDisposable
{
    private readonly TestLogger _log = new();
    private readonly FakeStarter _starter = new();
    private readonly TempDirectory _temp = new();
    private List<InstalledPlugin> _plugins = [];
    private readonly HashSet<string> _takenHotkeys = [];
    private static readonly string Standalone =
        Path.Combine(@"C:\Program Files (x86)", "Electronic Arts", "BioWare", "Star Wars - The Old Republic");

    public void Dispose() => _temp.Dispose();

    private string LayoutFile => Path.Combine(_temp.Path, "imgui-overlay.ini");
    private string PluginsFile => Path.Combine(_temp.Path, "plugins.json");

    private LauncherModel NewModel(FakeEnvironment env, bool logsExist = true) =>
        new(AshlaSettingsStore.Load(_temp.Path, _log), env, new GameLauncher(_starter), () => logsExist,
            new LauncherSettingsContext(PluginStateStore.Load(PluginsFile, _log), () => _plugins, LayoutFile,
                hotkey => !_takenHotkeys.Contains(hotkey.ToString()), Archive), _log);

    private EncounterArchive? _archive;

    private EncounterArchive Archive => _archive ??= new EncounterArchive(Path.Combine(_temp.Path, "encounters"), _log,
        () => AshlaSettingsStore.Load(_temp.Path, _log).Current.Fights.KeepDays, () => new DateTime(2026, 11, 1));

    private AshlaSettings Saved => AshlaSettingsStore.Load(_temp.Path, _log).Current;

    [Fact]
    public void DetectedInstall_SuggestsLauncher_AndPlayStartsIt()
    {
        var model = NewModel(new FakeEnvironment().WithLauncher(Standalone));

        Assert.Equal(Standalone, model.Installation?.Folder);
        Assert.False(model.InstallationIsSaved);
        Assert.Equal(GameLaunchMethod.SwtorLauncher, model.Method);
        Assert.True(model.CanPlay);
        Assert.Equal("Play", model.PlayLabel);

        model.Play();

        Assert.Equal(LauncherOutcome.Play, model.Outcome);
        Assert.Equal(Path.Combine(Standalone, "launcher.exe"), Assert.Single(_starter.Started).FileName);
    }

    [Fact]
    public void NothingFound_DefaultsToManual()
    {
        var model = NewModel(new FakeEnvironment(), logsExist: false);

        Assert.Null(model.Installation);
        Assert.Equal(GameLaunchMethod.Manual, model.Method);
        Assert.Equal("Start Ashla", model.PlayLabel);
        Assert.False(model.CombatLogFolderExists);
        model.Play();
        Assert.Equal(LauncherOutcome.Play, model.Outcome);
        Assert.Empty(_starter.Started);
    }

    [Fact]
    public void LauncherMethod_WithoutFolder_OpensSettingsInsteadOfClosing()
    {
        var model = NewModel(new FakeEnvironment());
        model.OpenSettings();
        model.Editing!.Method = GameLaunchMethod.SwtorLauncher;
        Assert.True(model.SaveSettings());

        Assert.False(model.CanPlay);
        model.Play();

        Assert.Null(model.Outcome);
        Assert.False(model.LastResult!.Ok);
        Assert.Equal(SettingsSection.Game, model.Editing?.Section);
    }

    [Fact]
    public void OpenSettings_CopiesCurrentSettings_AndCancelChangesNothing()
    {
        var model = NewModel(new FakeEnvironment().WithLauncher(Standalone));
        model.OpenSettings();
        var draft = model.Editing!;

        Assert.Equal(Standalone, draft.FolderInput);
        Assert.True(draft.FolderIsAutomatic);
        Assert.Equal(GameLaunchMethod.SwtorLauncher, draft.Method);
        Assert.Null(draft.UiScale);

        draft.Method = GameLaunchMethod.Manual;
        draft.UiScale = 1.5f;
        model.CancelSettings();

        Assert.Null(model.Editing);
        Assert.Equal(GameLaunchMethod.SwtorLauncher, model.Method);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "settings.json")));
    }

    [Fact]
    public void Save_WritesFolderMethodAndScale()
    {
        var env = new FakeEnvironment().WithLauncher(@"D:\SWTOR");
        var model = NewModel(env);
        model.OpenSettings();
        LauncherModel.FolderTyped(model.Editing!, @"D:\SWTOR\launcher.exe");
        model.Editing!.Method = GameLaunchMethod.Steam;
        model.Editing.UiScale = 1.25f;

        Assert.True(model.SaveSettings());

        Assert.Null(model.Editing);
        Assert.Equal(@"D:\SWTOR", Saved.Game.InstallFolder);
        Assert.Equal(GameLaunchMethod.Steam, Saved.Game.LaunchMethod);
        Assert.Equal(1.25f, Saved.Overlay.UiScale);
        Assert.Equal(1.25f, model.UiScale);
        Assert.True(model.InstallationIsSaved);
        Assert.Equal(@"D:\SWTOR", model.Installation?.Folder);
    }

    [Fact]
    public void Save_WithBadFolder_StaysOpenWithError()
    {
        var model = NewModel(new FakeEnvironment());
        model.OpenSettings();
        model.Editing!.Section = SettingsSection.Overlay;
        LauncherModel.FolderTyped(model.Editing, @"D:\Nope");

        Assert.False(model.SaveSettings());

        Assert.NotNull(model.Editing);
        Assert.Equal(SettingsSection.Game, model.Editing!.Section);
        Assert.Contains("launcher.exe", model.Editing.Error);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "settings.json")));
    }

    [Fact]
    public void EmptyFolderBox_MeansFindAutomatically()
    {
        var store = AshlaSettingsStore.Load(_temp.Path, _log);
        store.Current.Game.InstallFolder = @"D:\Old";
        store.Save();
        var model = NewModel(new FakeEnvironment().WithLauncher(Standalone).WithLauncher(@"D:\Old"));

        model.OpenSettings();
        Assert.False(model.Editing!.FolderIsAutomatic);
        LauncherModel.FolderTyped(model.Editing, "");
        Assert.True(model.SaveSettings());

        Assert.Null(Saved.Game.InstallFolder);
        Assert.Equal(Standalone, model.Installation?.Folder);
        Assert.False(model.InstallationIsSaved);
    }

    [Fact]
    public void FindAutomatically_FillsDraft_AndSavesAsAutomatic()
    {
        var steamGame = Path.Combine(@"C:\Steam", "steamapps", "common", "Star Wars - The Old Republic");
        var model = NewModel(new FakeEnvironment { SteamFolder = @"C:\Steam" }.WithLauncher(steamGame));
        model.OpenSettings();
        LauncherModel.FolderTyped(model.Editing!, "something else");

        model.FindAutomatically(model.Editing!);

        Assert.Equal(steamGame, model.Editing!.FolderInput);
        Assert.True(model.Editing.FolderIsAutomatic);
        Assert.Contains("Steam", model.Editing.Notice);
        Assert.True(model.SaveSettings());
        Assert.Null(Saved.Game.InstallFolder);
    }

    [Fact]
    public void FolderPicked_RejectsFolderWithoutLauncher()
    {
        var model = NewModel(new FakeEnvironment().WithLauncher(@"D:\SWTOR"));
        model.OpenSettings();

        model.FolderPicked(model.Editing!, @"D:\Other\launcher.exe");
        Assert.NotNull(model.Editing!.Error);

        model.FolderPicked(model.Editing, @"D:\SWTOR\launcher.exe");
        Assert.Null(model.Editing.Error);
        Assert.Equal(@"D:\SWTOR", model.Editing.FolderInput);
        Assert.False(model.Editing.FolderIsAutomatic);
    }

    [Fact]
    public void Plugins_AreListed_AndToggledOnSave()
    {
        _plugins = [new InstalledPlugin("CombatMeter", "0.1.0", "Ashla contributors", "Meter"), new InstalledPlugin("Other", "1.0.0", null, null)];
        var model = NewModel(new FakeEnvironment());
        model.OpenSettings(SettingsSection.Plugins);

        Assert.Equal(SettingsSection.Plugins, model.Editing!.Section);
        Assert.True(model.Editing.PluginEnabled["CombatMeter"]);
        model.Editing.PluginEnabled["Other"] = false;
        Assert.True(model.SaveSettings());

        var state = PluginStateStore.Load(PluginsFile, _log);
        Assert.True(state.IsDisabled("Other"));
        Assert.False(state.IsDisabled("CombatMeter"));

        model.OpenSettings();
        Assert.False(model.Editing!.PluginEnabled["Other"]);
    }

    [Fact]
    public void ResetPanelPositions_DeletesLayoutRightAway()
    {
        File.WriteAllText(LayoutFile, "[Window][Combat Meter]");
        var model = NewModel(new FakeEnvironment());

        model.OpenSettings(SettingsSection.Overlay);
        model.ResetPanelPositions(model.Editing!);

        Assert.False(File.Exists(LayoutFile));
        Assert.Equal("Panel positions reset.", model.Editing!.Notice);
        model.CancelSettings();
        Assert.False(File.Exists(LayoutFile));
    }

    [Fact]
    public void SavedFolderGone_FallsBackToDetection_AndSaysWhy()
    {
        var store = AshlaSettingsStore.Load(_temp.Path, _log);
        store.Current.Game.InstallFolder = @"D:\Removed";
        store.Save();

        var model = NewModel(new FakeEnvironment().WithLauncher(Standalone));

        Assert.Equal(Standalone, model.Installation?.Folder);
        Assert.False(model.InstallationIsSaved);
        Assert.Contains(@"D:\Removed", model.SavedFolderProblem);

        model.OpenSettings();
        Assert.Contains(@"D:\Removed", model.Editing!.Error);
    }

    [Fact]
    public void GameAlreadyRunning_ChangesLabelAndText()
    {
        _starter.Running = true;
        var model = NewModel(new FakeEnvironment().WithLauncher(Standalone));

        Assert.True(model.GameRunning);
        Assert.Equal("Start Ashla", model.PlayLabel);
        Assert.Contains("already running", model.MethodDescription);
    }

    [Fact]
    public void Hotkey_DefaultsToCtrlShiftH_AndCaptureSavesTheNewOne()
    {
        var model = NewModel(new FakeEnvironment());
        Assert.Equal(Hotkey.Default, model.ToggleHotkey);
        model.OpenSettings(SettingsSection.Overlay);
        var draft = model.Editing!;
        Assert.Equal(Hotkey.Default, draft.ToggleHotkey);

        LauncherModel.StartHotkeyCapture(draft);
        Assert.True(draft.CapturingHotkey);
        LauncherModel.HotkeyPressed(draft, Hotkey.Parse("Ctrl+Alt+K")!);
        Assert.False(draft.CapturingHotkey);

        Assert.True(model.SaveSettings());
        Assert.Equal("Ctrl+Alt+K", Saved.Overlay.ToggleHotkey);
        Assert.Equal(Hotkey.Parse("Ctrl+Alt+K"), model.ToggleHotkey);
        Assert.Contains("\"Ctrl+Alt+K\"", File.ReadAllText(Path.Combine(_temp.Path, "settings.json")));
    }

    [Fact]
    public void Hotkey_WithoutCtrlOrAlt_IsRefused_AndCaptureContinues()
    {
        var model = NewModel(new FakeEnvironment());
        model.OpenSettings(SettingsSection.Overlay);
        var draft = model.Editing!;
        LauncherModel.StartHotkeyCapture(draft);

        LauncherModel.HotkeyPressed(draft, Hotkey.Parse("Shift+K")!);

        Assert.True(draft.CapturingHotkey);
        Assert.Contains("Ctrl or Alt", draft.Error);
        Assert.Equal(Hotkey.Default, draft.ToggleHotkey);

        LauncherModel.HotkeyPressed(draft, Hotkey.Parse("Alt+F9")!);
        Assert.Null(draft.Error);
        Assert.Equal("Alt+F9", draft.ToggleHotkey?.ToString());
    }

    [Fact]
    public void Hotkey_EscKeepsTheOldOne_BackspaceClearsIt()
    {
        var model = NewModel(new FakeEnvironment());
        model.OpenSettings(SettingsSection.Overlay);
        var draft = model.Editing!;

        LauncherModel.StartHotkeyCapture(draft);
        LauncherModel.CancelHotkeyCapture(draft);
        Assert.False(draft.CapturingHotkey);
        Assert.Equal(Hotkey.Default, draft.ToggleHotkey);

        LauncherModel.StartHotkeyCapture(draft);
        LauncherModel.ClearHotkey(draft);
        Assert.Null(draft.ToggleHotkey);
        Assert.True(model.SaveSettings());

        Assert.Equal("", Saved.Overlay.ToggleHotkey);
        Assert.Null(model.ToggleHotkey);
    }

    [Fact]
    public void Hotkey_TakenByAnotherProgram_BlocksSave()
    {
        var model = NewModel(new FakeEnvironment());
        _takenHotkeys.Add("Ctrl+Alt+K");
        model.OpenSettings(SettingsSection.Game);
        var draft = model.Editing!;
        draft.ToggleHotkey = Hotkey.Parse("Ctrl+Alt+K");

        Assert.False(model.SaveSettings());
        Assert.NotNull(model.Editing);
        Assert.Equal(SettingsSection.Overlay, draft.Section);
        Assert.Contains("already used by another program", draft.Error);
        Assert.Equal(Hotkey.DefaultText, Saved.Overlay.ToggleHotkey);

        draft.ToggleHotkey = Hotkey.Parse("Ctrl+Alt+L");
        Assert.True(model.SaveSettings());
        Assert.Equal("Ctrl+Alt+L", Saved.Overlay.ToggleHotkey);
    }

    [Fact]
    public void Hotkey_Unchanged_IsNotCheckedAgain()
    {
        var model = NewModel(new FakeEnvironment());
        _takenHotkeys.Add(Hotkey.DefaultText);
        model.OpenSettings(SettingsSection.Overlay);
        Assert.True(model.SaveSettings());
    }

    [Fact]
    public void Setup_ShowsOnFirstStart_AndFinishSavesItsChoices()
    {
        var model = NewModel(new FakeEnvironment().WithLauncher(Standalone));
        Assert.NotNull(model.SetupDraft);
        Assert.Equal(SetupStep.Welcome, model.SetupStep);
        Assert.False(Saved.SetupDone);

        model.SetupNext();
        Assert.Equal(SetupStep.Game, model.SetupStep);
        Assert.True(model.SetupDraft!.FolderIsAutomatic);
        Assert.Equal(Standalone, model.SetupDraft.FolderInput);
        model.SetupDraft.Method = GameLaunchMethod.Steam;
        model.SetupNext();
        model.SetupNext();
        Assert.Equal(SetupStep.Hotkey, model.SetupStep);
        model.SetupDraft!.ToggleHotkey = Hotkey.Parse("Alt+F7");
        model.SetupNext();
        Assert.Equal(SetupStep.Toasts, model.SetupStep);
        Assert.Equal(ToastCorner.BottomRight, model.SetupDraft!.ToastCorner);
        model.SetupDraft.ToastCorner = ToastCorner.TopLeft;
        model.SetupNext();
        Assert.Equal(SetupStep.Theme, model.SetupStep);
        Assert.Equal(ThemeChoice.Auto, model.SetupDraft!.Theme);
        model.ChooseTheme(model.SetupDraft, ThemeChoice.Mandalorian);
        model.SetupNext();
        Assert.Equal(SetupStep.Plugins, model.SetupStep);
        Assert.Empty(model.SetupDraft!.PluginPicks);
        model.SetupNext();
        Assert.Equal(SetupStep.Done, model.SetupStep);
        Assert.False(Saved.SetupDone);

        model.SetupNext();

        Assert.Null(model.SetupDraft);
        var saved = Saved;
        Assert.True(saved.SetupDone);
        Assert.Equal(GameLaunchMethod.Steam, saved.Game.LaunchMethod);
        Assert.Equal("Alt+F7", saved.Overlay.ToggleHotkey);
        Assert.Equal(ToastCorner.TopLeft, saved.Overlay.ToastCorner);
        Assert.Equal(ThemeChoice.Mandalorian, saved.Appearance.Theme);
        Assert.Null(saved.Game.InstallFolder);
        Assert.Null(NewModel(new FakeEnvironment()).SetupDraft);
    }

    [Fact]
    public void Setup_Back_And_BadFolder_StayOnThePage()
    {
        var model = NewModel(new FakeEnvironment());
        model.SetupNext();
        LauncherModel.FolderTyped(model.SetupDraft!, @"D:\Nothing");

        model.SetupNext();

        Assert.Equal(SetupStep.Game, model.SetupStep);
        Assert.Contains("launcher.exe", model.SetupDraft!.Error);
        model.SetupBack();
        Assert.Equal(SetupStep.Welcome, model.SetupStep);
        Assert.Null(model.SetupDraft!.Error);
        model.SetupBack();
        Assert.Equal(SetupStep.Welcome, model.SetupStep);
    }

    [Fact]
    public void Setup_TakenHotkey_StaysOnTheHotkeyPage()
    {
        _takenHotkeys.Add("Ctrl+Alt+K");
        var model = NewModel(new FakeEnvironment());
        model.SetupNext();
        model.SetupNext();
        model.SetupNext();
        model.SetupDraft!.ToggleHotkey = Hotkey.Parse("Ctrl+Alt+K");

        model.SetupNext();

        Assert.Equal(SetupStep.Hotkey, model.SetupStep);
        Assert.Contains("already used by another program", model.SetupDraft!.Error);
    }

    [Fact]
    public void Setup_SkipTheFirstTime_IsRemembered_ButChangesNothing()
    {
        var model = NewModel(new FakeEnvironment());
        model.SetupNext();
        model.SetupDraft!.Method = GameLaunchMethod.Steam;

        model.SkipSetup();

        Assert.Null(model.SetupDraft);
        Assert.True(Saved.SetupDone);
        Assert.Null(Saved.Game.LaunchMethod);
    }

    [Fact]
    public void Setup_RunAgainFromSettings_CancelLeavesEverything()
    {
        var model = NewModel(new FakeEnvironment());
        model.SkipSetup();
        model.OpenSettings(SettingsSection.About);

        model.StartSetup(rerun: true);

        Assert.Null(model.Editing);
        Assert.True(model.SetupIsRerun);
        Assert.Equal(SetupStep.Welcome, model.SetupStep);
        model.SetupNext();
        model.SetupDraft!.Method = GameLaunchMethod.Steam;
        model.SkipSetup();
        Assert.Null(model.SetupDraft);
        Assert.Null(Saved.Game.LaunchMethod);
        Assert.True(Saved.SetupDone);
    }

    [Fact]
    public void Setup_CombatLoggingPage_ChecksAgain()
    {
        bool exists = false;
        var model = new LauncherModel(AshlaSettingsStore.Load(_temp.Path, _log), new FakeEnvironment(),
            new GameLauncher(_starter), () => exists,
            new LauncherSettingsContext(PluginStateStore.Load(PluginsFile, _log), () => _plugins, LayoutFile), _log);
        model.SetupNext();
        model.SetupNext();
        Assert.Equal(SetupStep.CombatLogging, model.SetupStep);
        Assert.False(model.CombatLogFolderExists);

        exists = true;
        model.CheckCombatLogging();

        Assert.True(model.CombatLogFolderExists);
    }

    [Fact]
    public void Fights_SettingsSave_AndShorterKeepingPrunesOldOnes()
    {
        var model = NewModel(new FakeEnvironment());
        model.SkipSetup();
        model.OpenSettings(SettingsSection.Fights);
        var draft = model.Editing!;
        Assert.True(draft.SaveFights);
        Assert.Equal(30, draft.KeepDays);

        model.ImportLog(draft, CombatLogFixtureTests.FixturePath);
        Assert.Equal("Saved 5 fights from combat_2026-10-22_04_06_32_959698.txt.", draft.Notice);
        Assert.Equal(5, Archive.Count);

        draft.KeepDays = 7;
        draft.SaveFights = false;
        Assert.True(model.SaveSettings());
        Assert.Equal(7, Saved.Fights.KeepDays);
        Assert.False(Saved.Fights.Save);
        Assert.Equal(0, Archive.Count);
    }

    [Fact]
    public void Fights_OpenLogTwice_AndDeleteAll()
    {
        var model = NewModel(new FakeEnvironment());
        model.OpenSettings(SettingsSection.Fights);
        var draft = model.Editing!;
        model.ImportLog(draft, CombatLogFixtureTests.FixturePath);
        model.ImportLog(draft, CombatLogFixtureTests.FixturePath);
        Assert.Equal("The 5 fights in combat_2026-10-22_04_06_32_959698.txt were already saved.", draft.Notice);

        model.ImportLog(draft, Path.Combine(_temp.Path, "missing.txt"));
        Assert.StartsWith("Couldn't read missing.txt", draft.Error);

        model.DeleteAllFights(draft);
        Assert.Equal("Deleted 5 saved fights.", draft.Notice);
        Assert.Equal(0, Archive.Count);
        model.CancelSettings();
        Assert.Equal(0, Archive.Count);
    }

    [Fact]
    public void Quit_SetsOutcome()
    {
        var model = NewModel(new FakeEnvironment());
        model.Quit();
        Assert.Equal(LauncherOutcome.Quit, model.Outcome);
    }

    [Fact]
    public void InstalledPlugins_ListsRealPluginFolders()
    {
        var folder = Path.Combine(_temp.Path, "plugins", "HelloWorldOverlay");
        Directory.CreateDirectory(folder);
        File.Copy(Repo.PluginOutput("HelloWorldOverlay"), Path.Combine(folder, "HelloWorldOverlay.dll"));
        Directory.CreateDirectory(Path.Combine(_temp.Path, "plugins", "NotAPlugin"));

        var plugin = Assert.Single(InstalledPlugins.Find(Path.Combine(_temp.Path, "plugins")));

        Assert.Equal("HelloWorldOverlay", plugin.Id);
        Assert.Equal("0.2.0", plugin.Version);
        Assert.Equal("Ashla contributors", plugin.Author);
        Assert.Contains("Hello, SWTOR!", plugin.Description);
        Assert.Empty(InstalledPlugins.Find(Path.Combine(_temp.Path, "missing")));
    }
}

internal sealed class FakeEnvironment : IInstallEnvironment
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SteamFolder { get; init; }
    public IEnumerable<string> ProgramFilesFolders { get; init; } = [@"C:\Program Files (x86)", @"C:\Program Files"];

    public FakeEnvironment WithLauncher(string folder)
    {
        Files[Path.Combine(folder, "launcher.exe")] = "";
        return this;
    }

    public bool FileExists(string path) => Files.ContainsKey(path);
    public string? ReadAllText(string path) => Files.GetValueOrDefault(path);
}

internal sealed class FakeStarter : IProcessStarter
{
    public List<ProcessStartInfo> Started { get; } = [];
    public bool Running { get; set; }
    public bool Fail { get; set; }

    public void Start(ProcessStartInfo startInfo)
    {
        if (Fail) throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");
        Started.Add(startInfo);
    }

    public bool IsRunning(string processName) => Running && processName == GameLauncher.GameProcessName;
}
