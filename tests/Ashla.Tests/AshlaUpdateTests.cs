using System.Net;
using System.Text;
using Ashla.Host;
using Ashla.Host.Launch;
using Ashla.Host.Settings;

namespace Ashla.Tests;

public class AshlaUpdateTests : IDisposable
{
    private const string Page = "https://github.com/YozoraCho/Ashla/releases/tag/v0.3.2";
    private const string Zip = "https://github.com/YozoraCho/Ashla/releases/download/v0.3.2/Ashla-0.3.2-win-x64.zip";
    private readonly TestLogger _log = new();
    private readonly TempDirectory _temp = new();
    private readonly FakeWeb _web = new();

    public void Dispose()
    {
        AshlaUpdateChecker.Url = AshlaUpdateChecker.OfficialUrl;
        _temp.Dispose();
    }

    private static string Release(string tag = "v0.3.2", bool prerelease = false, bool draft = false, string page = Page, string zip = Zip) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "{{page}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "published_at": "2026-10-27T03:00:00Z",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://github.com/x/notes.txt", "size": 10 },
            { "name": "Ashla-0.3.2-win-x64.zip", "browser_download_url": "{{zip}}", "size": 41234567 }
          ]
        }
        """;

    private static AshlaRelease? Parse(string json) => AshlaUpdateChecker.Newest(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void ReadsGitHubsLatestRelease()
    {
        var release = Parse(Release())!;

        Assert.Equal(("0.3.2", Page, Zip, 41234567L), (release.Version, release.Page, release.Download, release.Size));
        Assert.Equal(Zip, release.Target);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 3, 0, 0, TimeSpan.Zero), release.Published);
    }

    [Fact]
    public void IgnoresDraftsPrereleasesAndUnsafeAddresses()
    {
        Assert.Null(Parse(Release(prerelease: true)));
        Assert.Null(Parse(Release(draft: true)));
        Assert.Null(Parse(Release(page: "http://example.org/release")));
        Assert.Null(Parse(Release(tag: "nightly")));
        Assert.Null(Parse("not json"));
        Assert.Null(Parse("[]"));

        var noZip = Parse(Release(zip: "http://example.org/Ashla.zip"))!;
        Assert.Null(noZip.Download);
        Assert.Equal(Page, noZip.Target);
    }

    [Fact]
    public void FromTheReleasesList_PicksTheNewestAppRelease_NotTheSdk()
    {
        var list = $"[{Release(tag: "sdk-v1.2.0")}, {Release(tag: "v0.4.0", prerelease: true)}, {Release(tag: "v0.3.1")}, {Release()}, {Release(tag: "v0.2.0")}]";

        Assert.Equal("0.3.2", Parse(list)?.Version);
        Assert.Null(Parse($"[{Release(tag: "sdk-v1.2.0")}]"));
        Assert.Null(Parse("[]"));
    }

    [Theory]
    [InlineData("0.3.2", "0.3.1", true)]
    [InlineData("0.3.1", "0.3.1", false)]
    [InlineData("0.3.0", "0.3.1", false)]
    [InlineData("0.10.0", "0.9.5", true)]
    public void OnlyNewerVersionsCount(string latest, string current, bool newer) =>
        Assert.Equal(newer, AshlaUpdateChecker.Newer(new AshlaRelease(latest, Page, Zip, 0, null), current) is not null);

    [Fact]
    public async Task ChecksGitHub_AndShrugsOffFailures()
    {
        var checker = new AshlaUpdateChecker(new HttpClient(_web), _log);
        _web.Pages[AshlaUpdateChecker.OfficialUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(Release()));
        Assert.Equal("0.3.2", (await checker.LatestAsync(CancellationToken.None))?.Version);

        _web.Pages[AshlaUpdateChecker.OfficialUrl] = (HttpStatusCode.Forbidden, []);
        Assert.Null(await checker.LatestAsync(CancellationToken.None));
        Assert.Contains(_log.Warnings, w => w.Contains("GitHub answered 403"));

        _web.Pages.Clear();
        Assert.Null(await checker.LatestAsync(CancellationToken.None));
    }

    private LauncherModel NewModel(string current)
    {
        var context = new LauncherSettingsContext(PluginStateStore.Load(Path.Combine(_temp.Path, "plugins.json"), _log),
            () => [], Path.Combine(_temp.Path, "imgui-overlay.ini"),
            Updater: new AshlaUpdateChecker(new HttpClient(_web), _log), CurrentVersion: current);
        return new LauncherModel(AshlaSettingsStore.Load(_temp.Path, _log), new FakeEnvironment(), new GameLauncher(new FakeStarter()),
            () => true, context, _log);
    }

    private static void Settle(LauncherModel model)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            model.Refresh(force: true);
            if (!model.CheckingAshla) return;
            Thread.Sleep(10);
        } while (clock.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("The check didn't finish.");
    }

    [Fact]
    public void TheLauncher_ShowsANewerAshla()
    {
        _web.Pages[AshlaUpdateChecker.OfficialUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(Release()));

        var old = NewModel("0.3.1");
        old.CheckForUpdatesInBackground();
        Settle(old);
        Assert.True(old.AshlaChecked);
        Assert.Equal("0.3.2", old.AshlaUpdate?.Version);

        var current = NewModel("0.3.2");
        current.CheckAshla();
        Settle(current);
        Assert.Null(current.AshlaUpdate);
        Assert.Equal("0.3.2", current.AshlaLatest?.Version);
    }

    [Fact]
    public void TurningOffTheUpdateCheck_AlsoSkipsAshla()
    {
        var store = AshlaSettingsStore.Load(_temp.Path, _log);
        store.Current.Plugins.CheckForUpdates = false;
        store.Save();

        var model = NewModel("0.3.1");
        model.CheckForUpdatesInBackground();
        Settle(model);

        Assert.False(model.AshlaChecked);
        Assert.Empty(_web.Requests);
    }
}
