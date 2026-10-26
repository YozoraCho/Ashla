using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ashla.Host;
using Ashla.Host.Launch;
using Ashla.Host.Settings;
using Ashla.SDK;

namespace Ashla.Tests;

internal sealed class FakeWeb : HttpMessageHandler
{
    public Dictionary<string, (HttpStatusCode Status, byte[] Body)> Pages { get; } = [];
    public List<string> Requests { get; } = [];

    public void Json(string url, object value) => Pages[url] = (HttpStatusCode.OK, JsonSerializer.SerializeToUtf8Bytes(value));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        lock (Requests) Requests.Add(url);
        if (!Pages.TryGetValue(url, out var page))
            throw new HttpRequestException("No route to host");
        return Task.FromResult(new HttpResponseMessage(page.Status) { Content = new ByteArrayContent(page.Body) });
    }
}

public class HolocronTests : IDisposable
{
    private const string ZipUrl = "https://example.org/HelloWorldOverlay-0.2.0.zip";
    private const string CustomUrl = "https://raw.githubusercontent.com/someone/plugins/main/repo.json";
    private readonly TestLogger _log = new();
    private readonly TempDirectory _temp = new();
    private readonly FakeWeb _web = new();

    public void Dispose() => _temp.Dispose();

    private string PluginsDir => Path.Combine(_temp.Path, "plugins");
    private HttpClient Http => new(_web);
    private HolocronClient Client => new(Http, Path.Combine(_temp.Path, "cache"), _log);
    private InstalledSources Sources => new(Path.Combine(_temp.Path, "installed-plugins.json"), _log);
    private PluginInstaller Installer => new(Http, PluginsDir, Sources, _log);

    private static string Folder => Path.GetDirectoryName(Repo.PluginOutput("HelloWorldOverlay"))!;

    private static byte[] Zip(Action<ZipArchive>? change = null, bool inFolder = false)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(Folder).Where(f => !f.EndsWith(".pdb")))
                zip.CreateEntryFromFile(file, (inFolder ? "HelloWorldOverlay/" : "") + Path.GetFileName(file));
            change?.Invoke(zip);
        }
        return memory.ToArray();
    }

    private static RepoPlugin Entry(byte[] zip, string version = "0.2.0", int apiLevel = AshlaApi.Level) => new()
    {
        Id = "HelloWorldOverlay",
        Name = "Hello World Overlay",
        Version = version,
        ApiLevel = apiLevel,
        Download = ZipUrl,
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(zip)),
        Files = PluginManifests.TryRead(Folder)!.Files
    };

    private CatalogEntry Official(RepoPlugin plugin) =>
        new(plugin, new PluginRepository(HolocronClient.OfficialUrl, "Holocron", true));

    [Fact]
    public void Versions_CompareNumerically()
    {
        Assert.True(PluginVersions.IsNewer("0.10.0", "0.9.5"));
        Assert.True(PluginVersions.IsNewer("v1.2", "1.1.9"));
        Assert.False(PluginVersions.IsNewer("1.0.0", "1.0.0"));
        Assert.False(PluginVersions.IsNewer("1.0.0-beta", "1.0.0"));
        Assert.True(PluginVersions.IsNewer("2", "1.9"));
    }

    [Fact]
    public async Task Fetch_ReadsOfficialAndCustomRepositories()
    {
        var zip = Zip();
        _web.Json(HolocronClient.OfficialUrl, new RepoIndex { Name = "Holocron", Plugins = [Entry(zip)] });
        _web.Json(CustomUrl, new RepoIndex { Plugins = [Entry(zip, "0.3.0"), new RepoPlugin { Id = "../bad", Version = "1", Download = ZipUrl, Sha256 = new string('a', 64) }] });
        var repos = HolocronClient.Repositories(new PluginSettings { Repositories = [CustomUrl, HolocronClient.OfficialUrl] });

        var catalog = await Client.FetchAsync(repos, CancellationToken.None);

        Assert.Equal(2, repos.Count);
        Assert.Equal("someone/plugins", repos[1].Name);
        Assert.Equal(2, catalog.Entries.Count);
        Assert.All(catalog.Repositories, r => Assert.Null(r.Problem));
        Assert.True(catalog.Find("helloworldoverlay", null)!.Repository.Official);
        Assert.Equal("0.3.0", catalog.Find("HelloWorldOverlay", CustomUrl)!.Plugin.Version);
    }

    [Fact]
    public async Task Fetch_FallsBackToTheLastCopy_WhenOffline()
    {
        _web.Json(HolocronClient.OfficialUrl, new RepoIndex { Plugins = [Entry(Zip())] });
        var repos = HolocronClient.Repositories(new PluginSettings());
        await Client.FetchAsync(repos, CancellationToken.None);
        _web.Pages.Clear();

        var catalog = await Client.FetchAsync(repos, CancellationToken.None);

        Assert.Single(catalog.Entries);
        var result = Assert.Single(catalog.Repositories);
        Assert.True(result.FromCache);
        Assert.StartsWith("Couldn't reach Holocron.", result.Problem);
    }

    [Fact]
    public async Task Fetch_RefusesNewerFormats_AndPlainHttp()
    {
        _web.Json(HolocronClient.OfficialUrl, new RepoIndex { Format = 99 });
        var catalog = await Client.FetchAsync([new PluginRepository(HolocronClient.OfficialUrl, "Holocron", true),
            new PluginRepository("http://example.org/repo.json", "plain", false)], CancellationToken.None);
        Assert.Empty(catalog.Entries);
        Assert.Contains("newer Ashla", catalog.Repositories[0].Problem);
        Assert.Contains("https://", catalog.Repositories[1].Problem);
        Assert.Equal("Repository addresses must start with https://.", HolocronClient.CheckRepositoryUrl("http://x.org/r.json"));
        Assert.Null(HolocronClient.CheckRepositoryUrl(CustomUrl));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Install_VerifiesAndPutsThePluginInPlace(bool inFolder)
    {
        var zip = Zip(inFolder: inFolder);
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, zip);

        var result = await Installer.InstallAsync(Official(Entry(zip)));

        Assert.True(result.Ok, result.Message);
        Assert.Equal("Hello World Overlay 0.2.0 is installed.", result.Message);
        Assert.True(PluginManifests.Check(Path.Combine(PluginsDir, "HelloWorldOverlay"), "HelloWorldOverlay").Ok);
        Assert.Equal(HolocronClient.OfficialUrl, Sources.Get("HelloWorldOverlay")!.Repository);
        Assert.Single(Directory.GetDirectories(PluginsDir));
    }

    [Fact]
    public async Task Install_ReplacesAnOlderCopy()
    {
        var zip = Zip();
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, zip);
        var old = Directory.CreateDirectory(Path.Combine(PluginsDir, "HelloWorldOverlay")).FullName;
        File.WriteAllText(Path.Combine(old, "stale.txt"), "old");

        Assert.True((await Installer.InstallAsync(Official(Entry(zip)))).Ok);

        Assert.False(File.Exists(Path.Combine(old, "stale.txt")));
        Assert.Single(Directory.GetDirectories(PluginsDir));
    }

    [Fact]
    public async Task Install_RefusesAWrongChecksum_AndKeepsTheOldCopy()
    {
        var zip = Zip();
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, zip);
        var entry = Entry(zip);
        entry.Sha256 = new string('0', 64);
        var old = Directory.CreateDirectory(Path.Combine(PluginsDir, "HelloWorldOverlay")).FullName;
        File.WriteAllText(Path.Combine(old, "keep.txt"), "old");

        var result = await Installer.InstallAsync(Official(entry));

        Assert.False(result.Ok);
        Assert.Contains("doesn't match Holocron's checksum", result.Message);
        Assert.True(File.Exists(Path.Combine(old, "keep.txt")));
        Assert.Single(Directory.GetDirectories(PluginsDir));
    }

    [Fact]
    public async Task Install_RefusesFilesThatDontMatchThePublishedHashes()
    {
        var zip = Zip();
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, zip);
        var entry = Entry(zip);
        entry.Files = new Dictionary<string, string>(entry.Files) { ["HelloWorldOverlay.dll"] = new string('1', 64) };

        var result = await Installer.InstallAsync(Official(entry));

        Assert.False(result.Ok);
        Assert.Contains("don't match the hashes Holocron published", result.Message);
        Assert.False(Directory.Exists(Path.Combine(PluginsDir, "HelloWorldOverlay")));
    }

    [Fact]
    public async Task Install_RefusesTamperedZips_ZipSlip_AndWrongVersions()
    {
        var tampered = Zip(z => z.CreateEntry("Extra.dll").Open().Write("x"u8));
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, tampered);
        var result = await Installer.InstallAsync(Official(Entry(tampered)));
        Assert.Contains("Extra.dll isn't in manifest.json", result.Message);

        var slip = Zip(z => z.CreateEntry("../evil.txt").Open().Write("x"u8));
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, slip);
        result = await Installer.InstallAsync(Official(Entry(slip)));
        Assert.Contains("outside its folder", result.Message);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "evil.txt")));

        var good = Zip();
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, good);
        result = await Installer.InstallAsync(Official(Entry(good, version: "9.9.9")));
        Assert.Contains("is version 0.2.0", result.Message);

        result = await Installer.InstallAsync(Official(Entry(good, apiLevel: 99)));
        Assert.Contains("needs a newer Ashla", result.Message);
        Assert.Empty(Directory.GetDirectories(PluginsDir));
    }

    [Fact]
    public async Task Install_RefusesHttpDownloads_AndReportsNetworkErrors()
    {
        var zip = Zip();
        var entry = Entry(zip);
        entry.Download = "http://example.org/x.zip";
        Assert.Contains("isn't https", (await Installer.InstallAsync(Official(entry))).Message);

        var offline = await Installer.InstallAsync(Official(Entry(zip)));
        Assert.False(offline.Ok);
        Assert.StartsWith("Installing Hello World Overlay failed", offline.Message);
    }

    [Fact]
    public async Task Remove_DeletesTheFolderAndTheRecord()
    {
        var zip = Zip();
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, zip);
        await Installer.InstallAsync(Official(Entry(zip)));

        var result = Installer.Remove("HelloWorldOverlay", "Hello World Overlay");

        Assert.True(result.Ok);
        Assert.False(Directory.Exists(Path.Combine(PluginsDir, "HelloWorldOverlay")));
        Assert.Null(Sources.Get("HelloWorldOverlay"));
        Assert.False(Installer.Remove("..", "x").Ok);
    }

    [Fact]
    public void CleanUp_RemovesLeftoversFromAnInterruptedInstall()
    {
        Directory.CreateDirectory(Path.Combine(PluginsDir, ".installing-X-1"));
        Directory.CreateDirectory(Path.Combine(PluginsDir, ".replaced-X-2"));
        Directory.CreateDirectory(Path.Combine(PluginsDir, "Real"));
        Installer.CleanUp();
        Assert.Equal(["Real"], Directory.GetDirectories(PluginsDir).Select(Path.GetFileName));
    }

    [Fact]
    public void Updates_AreFoundForNewerCompatibleVersions_FromThePluginsOwnRepository()
    {
        var zip = Zip();
        var catalog = new Catalog([
            Official(Entry(zip, "0.2.0")),
            Official(Entry(zip, "0.3.0", apiLevel: 99)) with { },
            new CatalogEntry(Entry(zip, "0.5.0"), new PluginRepository(CustomUrl, "someone/plugins", false))
        ], []);
        var installed = new List<InstalledPlugin> { new("HelloWorldOverlay", "0.1.0", null, null, "Hello World Overlay") };

        var update = Assert.Single(PluginUpdates.Find(installed, new Catalog([catalog.Entries[0]], []), Sources));
        Assert.Equal("0.2.0", update.Entry.Plugin.Version);

        Sources.Set("HelloWorldOverlay", new InstalledSource { Repository = CustomUrl, Version = "0.1.0" });
        Assert.Equal("0.5.0", Assert.Single(PluginUpdates.Find(installed, catalog, Sources)).Entry.Plugin.Version);

        Assert.Empty(PluginUpdates.Find(installed, new Catalog([catalog.Entries[1]], []), new InstalledSources(Path.Combine(_temp.Path, "none.json"), _log)));
    }

    internal static byte[] PluginZip() => Zip();

    internal static RepoPlugin PluginEntry(byte[] zip, string version = "0.2.0") => Entry(zip, version);

    internal static string PluginFolder => Folder;
}

[Collection("AshlaTheme")]
public class LauncherStoreTests : IDisposable
{
    private const string ZipUrl = "https://example.org/HelloWorldOverlay-0.2.0.zip";
    private const string CustomUrl = "https://raw.githubusercontent.com/someone/plugins/main/repo.json";
    private readonly TestLogger _log = new();
    private readonly TempDirectory _temp = new();
    private readonly FakeWeb _web = new();

    public void Dispose() => _temp.Dispose();

    private string PluginsDir => Path.Combine(_temp.Path, "plugins");
    private AshlaSettings Saved => AshlaSettingsStore.Load(_temp.Path, _log).Current;

    private LauncherModel NewModel()
    {
        var http = new HttpClient(_web);
        var sources = new InstalledSources(Path.Combine(_temp.Path, "installed-plugins.json"), _log);
        var store = new PluginStore(new HolocronClient(http, Path.Combine(_temp.Path, "cache"), _log),
            new PluginInstaller(http, PluginsDir, sources, _log), sources);
        return new LauncherModel(AshlaSettingsStore.Load(_temp.Path, _log), new FakeEnvironment(), new GameLauncher(new FakeStarter()),
            () => true, new LauncherSettingsContext(PluginStateStore.Load(Path.Combine(_temp.Path, "plugins.json"), _log),
                () => InstalledPlugins.Find(PluginsDir), Path.Combine(_temp.Path, "imgui-overlay.ini"), Store: store), _log);
    }

    private static void Settle(LauncherModel model)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            model.Refresh(force: true);
            if (!model.Fetching && !model.IsInstalling("HelloWorldOverlay")) return;
            Thread.Sleep(10);
        } while (clock.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("The store didn't finish.");
    }

    private void PutOldPluginInPlace()
    {
        var target = Directory.CreateDirectory(Path.Combine(PluginsDir, "HelloWorldOverlay")).FullName;
        foreach (var file in Directory.EnumerateFiles(HolocronTests.PluginFolder).Where(f => !f.EndsWith(".pdb")))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        var manifest = Path.Combine(target, "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"Version\": \"0.2.0\"", "\"Version\": \"0.1.9\""));
    }

    [Fact]
    public void StartupCheck_FindsUpdates_AndInstallingOneClearsIt()
    {
        var zip = HolocronTests.PluginZip();
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, zip);
        _web.Json(HolocronClient.OfficialUrl, new RepoIndex { Plugins = [HolocronTests.PluginEntry(zip)] });
        PutOldPluginInPlace();
        var model = NewModel();

        model.CheckForUpdatesInBackground();
        Settle(model);

        var update = Assert.Single(model.Updates);
        Assert.Equal("0.1.9", update.Installed);
        Assert.Equal("0.2.0", update.Entry.Plugin.Version);

        model.OpenSettings(SettingsSection.Plugins);
        var draft = model.Editing!;
        model.Install(draft, update.Entry);
        Assert.True(model.IsInstalling("HelloWorldOverlay"));
        Settle(model);

        Assert.Equal("Hello World Overlay 0.2.0 is installed.", draft.Notice);
        Assert.Null(draft.Error);
        Assert.Equal("0.2.0", Assert.Single(draft.Plugins).Version);
        Assert.Empty(model.Updates);
        Assert.Equal(HolocronClient.OfficialUrl, model.SourceOf("HelloWorldOverlay"));
    }

    [Fact]
    public void FailedInstall_IsReported_AndRemoveDeletesThePlugin()
    {
        var zip = HolocronTests.PluginZip();
        _web.Json(HolocronClient.OfficialUrl, new RepoIndex { Plugins = [HolocronTests.PluginEntry(zip)] });
        var model = NewModel();
        model.RefreshCatalog();
        Settle(model);
        model.OpenSettings(SettingsSection.Plugins);
        var draft = model.Editing!;

        model.Install(draft, model.Catalog.Find("HelloWorldOverlay", null)!);
        Settle(model);
        Assert.StartsWith("Installing Hello World Overlay failed", draft.Error);
        Assert.Empty(draft.Plugins);

        _web.Pages[ZipUrl] = (HttpStatusCode.OK, zip);
        model.Install(draft, model.Catalog.Find("HelloWorldOverlay", null)!);
        Settle(model);
        var installed = Assert.Single(draft.Plugins);

        model.Remove(draft, installed);

        Assert.Empty(draft.Plugins);
        Assert.False(Directory.Exists(Path.Combine(PluginsDir, "HelloWorldOverlay")));
        Assert.Null(model.SourceOf("HelloWorldOverlay"));
    }

    [Fact]
    public void Repositories_AreSavedRightAway_AndChecked()
    {
        var model = NewModel();
        model.OpenSettings(SettingsSection.Plugins);
        var draft = model.Editing!;

        model.AddRepository(draft, "http://example.org/repo.json");
        Assert.Equal("Repository addresses must start with https://.", draft.Error);
        Assert.Empty(Saved.Plugins.Repositories);

        model.AddRepository(draft, $"  {CustomUrl} ");
        Assert.Null(draft.Error);
        Assert.Equal("Added someone/plugins.", draft.Notice);
        Assert.Equal([CustomUrl], Saved.Plugins.Repositories);
        Assert.Equal(2, model.Repositories.Count);

        model.AddRepository(draft, CustomUrl);
        Assert.Equal("That repository is already in the list.", draft.Notice);
        model.AddRepository(draft, HolocronClient.OfficialUrl);
        Assert.Equal("That repository is already in the list.", draft.Notice);

        model.CancelSettings();
        Assert.Equal([CustomUrl], Saved.Plugins.Repositories);

        model.OpenSettings(SettingsSection.Plugins);
        model.RemoveRepository(model.Editing!, CustomUrl);
        Assert.Empty(Saved.Plugins.Repositories);
        Assert.StartsWith("Removed someone/plugins.", model.Editing!.Notice);
    }

    private static void WalkTo(LauncherModel model, SetupStep step)
    {
        for (int i = 0; i < 10 && model.SetupStep != step; i++) model.SetupNext();
        Assert.Equal(step, model.SetupStep);
    }

    [Fact]
    public void SetupPlugins_StartUnticked_OnlyListHolocron_AndInstallBeforeFinish()
    {
        var zip = HolocronTests.PluginZip();
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, zip);
        _web.Json(HolocronClient.OfficialUrl, new RepoIndex { Plugins = [HolocronTests.PluginEntry(zip)] });
        _web.Json(CustomUrl, new RepoIndex { Plugins = [new RepoPlugin { Id = "Other", Version = "1.0", ApiLevel = 1, Download = ZipUrl, Sha256 = new string('a', 64) }] });
        var store = AshlaSettingsStore.Load(_temp.Path, _log);
        store.Current.Plugins.Repositories.Add(CustomUrl);
        store.Save();
        var model = NewModel();
        Assert.False(model.HasPlugins);

        WalkTo(model, SetupStep.Plugins);
        Settle(model);
        Assert.Equal("HelloWorldOverlay", Assert.Single(model.SetupChoices).Plugin.Id);
        Assert.Empty(model.SetupDraft!.PluginPicks);

        model.SetupDraft.PluginPicks.Add("HelloWorldOverlay");
        model.SetupNext();
        Assert.Equal(SetupStep.Done, model.SetupStep);
        Assert.True(model.Installing);
        model.SetupNext();
        Assert.NotNull(model.SetupDraft);

        Settle(model);
        Assert.True(model.ResultOf("HelloWorldOverlay")!.Ok);
        Assert.True(model.IsInstalled("HelloWorldOverlay"));
        Assert.True(model.HasPlugins);
        model.SetupNext();
        Assert.Null(model.SetupDraft);
        Assert.True(Saved.SetupDone);
    }

    [Fact]
    public void SetupPlugins_DownloadNothing_UnlessPicked_AndSayWhenOffline()
    {
        var model = NewModel();
        WalkTo(model, SetupStep.Plugins);
        Settle(model);
        Assert.Empty(model.SetupChoices);
        Assert.StartsWith("Couldn't reach Holocron", model.Catalog.Repositories.Single().Problem);

        var zip = HolocronTests.PluginZip();
        _web.Json(HolocronClient.OfficialUrl, new RepoIndex { Plugins = [HolocronTests.PluginEntry(zip)] });
        model.RefreshCatalog();
        Settle(model);
        Assert.Single(model.SetupChoices);
        model.SetupNext();
        model.SetupNext();
        Assert.Null(model.SetupDraft);
        Assert.DoesNotContain(ZipUrl, _web.Requests);
        Assert.False(model.HasPlugins);
    }

    [Fact]
    public void TurningOffTheUpdateCheck_IsSaved_AndStopsTheStartupCheck()
    {
        var model = NewModel();
        model.OpenSettings(SettingsSection.Plugins);
        Assert.True(model.Editing!.CheckForUpdates);
        model.Editing.CheckForUpdates = false;
        Assert.True(model.SaveSettings());
        Assert.False(Saved.Plugins.CheckForUpdates);

        var next = NewModel();
        next.CheckForUpdatesInBackground();
        Assert.False(next.Fetching);
        Assert.Empty(_web.Requests);
    }
}
