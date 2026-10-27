using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Ashla.Host;
using Ashla.Host.Launch;
using Ashla.Host.Settings;

namespace Ashla.Tests;

public class AshlaSelfUpdaterTests : IDisposable
{
    private const string ZipUrl = "https://github.com/YozoraCho/Ashla/releases/download/v0.3.2/Ashla-0.3.2-win-x64.zip";
    private const string Page = "https://github.com/YozoraCho/Ashla/releases/tag/v0.3.2";
    private readonly TestLogger _log = new();
    private readonly TempDirectory _temp = new();
    private readonly FakeWeb _web = new();

    public void Dispose() => _temp.Dispose();

    private string App => Path.Combine(_temp.Path, "Ashla");

    private void InstallOldAshla()
    {
        Directory.CreateDirectory(Path.Combine(App, "plugins", "TacticalReadout"));
        File.WriteAllText(Path.Combine(App, "Ashla.exe"), "old exe");
        File.WriteAllText(Path.Combine(App, "glfw3.dll"), "old glfw");
        File.WriteAllText(Path.Combine(App, "README.txt"), "old readme");
        File.WriteAllText(Path.Combine(App, "plugins", "TacticalReadout", "TacticalReadout.dll"), "my plugin");
        File.WriteAllText(Path.Combine(App, "notes I made.txt"), "mine");
    }

    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        return memory.ToArray();
    }

    private static readonly (string, string)[] NewAshla =
    [
        ("Ashla.exe", "new exe"),
        ("glfw3.dll", "new glfw"),
        ("README.txt", "new readme"),
        ("cimgui.dll", "new cimgui"),
        ("plugins/Evil/Evil.dll", "should never land"),
        ("../outside.txt", "should never land")
    ];

    private AshlaRelease Serve(byte[] zip, string? sha256 = null)
    {
        _web.Pages[ZipUrl] = (HttpStatusCode.OK, zip);
        return new AshlaRelease("0.3.2", Page, ZipUrl, zip.Length, null, sha256 ?? Convert.ToHexStringLower(SHA256.HashData(zip)));
    }

    private AshlaSelfUpdater Updater => new(new HttpClient(_web), App, _log);

    private string Read(params string[] path) => File.ReadAllText(Path.Combine([App, .. path]));

    [Fact]
    public async Task ReplacesAshlasFiles_AndLeavesPluginsAndEverythingElseAlone()
    {
        InstallOldAshla();
        var steps = new List<string>();

        var result = await Updater.UpdateAsync(Serve(Zip(NewAshla)), new Progress(steps), CancellationToken.None);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(Path.Combine(Path.GetFullPath(App), "Ashla.exe"), result.NewExe);
        Assert.Equal(("new exe", "new glfw", "new readme", "new cimgui"), (Read("Ashla.exe"), Read("glfw3.dll"), Read("README.txt"), Read("cimgui.dll")));
        Assert.Equal("my plugin", Read("plugins", "TacticalReadout", "TacticalReadout.dll"));
        Assert.Equal("mine", Read("notes I made.txt"));
        Assert.False(Directory.Exists(Path.Combine(App, "plugins", "Evil")));
        Assert.False(File.Exists(Path.Combine(_temp.Path, "outside.txt")));
        Assert.Equal(["Downloading Ashla 0.3.2...", "Checking the download...", "Installing..."], steps.Select(s => s.Contains("MB") ? "Downloading Ashla 0.3.2..." : s));

        var old = Assert.Single(Directory.GetDirectories(App, ".update-old-*"));
        Assert.Equal("old exe", File.ReadAllText(Path.Combine(old, "Ashla.exe")));
        Assert.Empty(Directory.GetDirectories(App, ".update-new-*"));
        Updater.CleanUp();
        Assert.Empty(Directory.GetDirectories(App, ".update-*"));
    }

    [Fact]
    public async Task AWrongChecksum_ChangesNothing()
    {
        InstallOldAshla();

        var result = await Updater.UpdateAsync(Serve(Zip(NewAshla), new string('0', 64)), null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("doesn't match the checksum", result.Message);
        Assert.Equal("old exe", Read("Ashla.exe"));
        Assert.Empty(Directory.GetDirectories(App, ".update-*"));
    }

    [Fact]
    public async Task AZipWithoutAshla_IsRefused()
    {
        InstallOldAshla();

        var result = await Updater.UpdateAsync(Serve(Zip(("README.txt", "hi"))), null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("it has no Ashla.exe", result.Message);
        Assert.Equal("old readme", Read("README.txt"));
    }

    [Fact]
    public async Task AZipWithEverythingInOneFolder_IsUnpackedIntoPlace()
    {
        InstallOldAshla();

        var result = await Updater.UpdateAsync(Serve(Zip(("Ashla-0.3.2-win-x64/Ashla.exe", "new exe"), ("Ashla-0.3.2-win-x64/glfw3.dll", "new glfw"))),
            null, CancellationToken.None);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(("new exe", "new glfw"), (Read("Ashla.exe"), Read("glfw3.dll")));
        Assert.False(Directory.Exists(Path.Combine(App, "Ashla-0.3.2-win-x64")));
    }

    [Fact]
    public async Task IfAFileCantBeReplaced_TheOldFilesGoBack()
    {
        InstallOldAshla();
        Directory.CreateDirectory(Path.Combine(App, "README.txt.blocked"));
        File.Delete(Path.Combine(App, "README.txt"));
        Directory.CreateDirectory(Path.Combine(App, "README.txt"));

        var result = await Updater.UpdateAsync(Serve(Zip(("Ashla.exe", "new exe"), ("glfw3.dll", "new glfw"), ("README.txt", "new readme"))),
            null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("left as it was", result.Message);
        Assert.Equal(("old exe", "old glfw"), (Read("Ashla.exe"), Read("glfw3.dll")));
        Assert.Equal("my plugin", Read("plugins", "TacticalReadout", "TacticalReadout.dll"));
    }

    [Fact]
    public void TheChecksum_ComesFromGitHubsAssetDigest()
    {
        var digest = new string('a', 64);
        var json = $$"""
            [{ "tag_name": "v0.3.2", "html_url": "{{Page}}", "assets": [
              { "name": "Ashla-0.3.2-win-x64.zip", "browser_download_url": "{{ZipUrl}}", "size": 5, "digest": "sha256:{{digest}}" } ] },
             { "tag_name": "v0.3.1", "html_url": "{{Page}}", "assets": [
              { "name": "Ashla-0.3.1-win-x64.zip", "browser_download_url": "{{ZipUrl}}", "size": 5, "digest": "md5:abc" } ] }]
            """;
        var release = AshlaUpdateChecker.Newest(Encoding.UTF8.GetBytes(json))!;
        Assert.Equal(digest, release.Sha256);
        Assert.True(release.CanInstall);

        var older = AshlaUpdateChecker.Newest(Encoding.UTF8.GetBytes(json.Replace("v0.3.2", "v0.3.0")))!;
        Assert.Equal("0.3.1", older.Version);
        Assert.Null(older.Sha256);
        Assert.False(older.CanInstall);
    }

    private LauncherModel NewModel(AshlaRelease release, List<string> restarts)
    {
        var json = $$"""
            [{ "tag_name": "v0.3.2", "html_url": "{{Page}}", "assets": [
              { "name": "Ashla-0.3.2-win-x64.zip", "browser_download_url": "{{ZipUrl}}", "size": {{release.Size}}, "digest": "sha256:{{release.Sha256}}" } ] }]
            """;
        _web.Pages[AshlaUpdateChecker.OfficialUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(json));
        var http = new HttpClient(_web);
        var context = new LauncherSettingsContext(PluginStateStore.Load(Path.Combine(_temp.Path, "plugins.json"), _log),
            () => [], Path.Combine(_temp.Path, "imgui-overlay.ini"),
            Updater: new AshlaUpdateChecker(http, _log), CurrentVersion: "0.3.1",
            SelfUpdater: new AshlaSelfUpdater(http, App, _log), Restart: restarts.Add);
        return new LauncherModel(AshlaSettingsStore.Load(_temp.Path, _log), new FakeEnvironment(), new GameLauncher(new FakeStarter()),
            () => true, context, _log);
    }

    private static void Settle(LauncherModel model)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            model.Refresh(force: true);
            if (!model.CheckingAshla && !model.UpdatingAshla) return;
            Thread.Sleep(10);
        } while (clock.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("The update didn't finish.");
    }

    [Fact]
    public void UpdateNow_InstallsAndRestarts()
    {
        InstallOldAshla();
        var restarts = new List<string>();
        var model = NewModel(Serve(Zip(NewAshla)), restarts);
        model.CheckAshla();
        Settle(model);
        Assert.True(model.CanUpdateInPlace);

        model.UpdateAshla();
        Assert.True(model.UpdatingAshla);
        model.Play();
        Assert.Null(model.Outcome);
        Settle(model);

        Assert.Equal([Path.Combine(Path.GetFullPath(App), "Ashla.exe")], restarts);
        Assert.Equal(LauncherOutcome.Quit, model.Outcome);
        Assert.Equal("new exe", Read("Ashla.exe"));
    }

    [Fact]
    public void AFailedUpdate_SaysWhy_AndOffersTheDownload()
    {
        InstallOldAshla();
        var restarts = new List<string>();
        var model = NewModel(Serve(Zip(NewAshla)) with { Sha256 = new string('1', 64) }, restarts);
        model.CheckAshla();
        Settle(model);

        model.UpdateAshla();
        Settle(model);

        Assert.Empty(restarts);
        Assert.Null(model.Outcome);
        Assert.Contains("doesn't match the checksum", model.UpdateError);
        Assert.Equal("old exe", Read("Ashla.exe"));
    }

    private sealed class Progress(List<string> steps) : IProgress<string>
    {
        public void Report(string value) => steps.Add(value);
    }
}
