using System.Text.Json;
using System.Text.Json.Nodes;
using Ashla.Host;
using Ashla.Host.Launch;
using Ashla.Host.Overlays;
using Ashla.SDK;

namespace Ashla.Tests;

public class PluginManifestTests : IDisposable
{
    private readonly TestLogger _log = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CopyPlugin(string name)
    {
        var source = Path.GetDirectoryName(Repo.PluginOutput(name))!;
        var target = Directory.CreateDirectory(Path.Combine(_temp.Path, name)).FullName;
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        return target;
    }

    private static void EditManifest(string folder, Action<JsonObject> edit)
    {
        var path = Path.Combine(folder, PluginManifests.FileName);
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(json);
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private (PluginManager Manager, ToastCenter Toasts) Load()
    {
        var toasts = new ToastCenter();
        var manager = new PluginManager(_log, _ => _log, new EventBus(_log), new ExternalProvider(),
            new PanelRegistry(_log, isAvailable: false), PluginStateStore.InMemory(_log), toasts: toasts);
        manager.LoadFrom(_temp.Path);
        return (manager, toasts);
    }

    [Fact]
    public void BuiltPlugin_HasACompleteManifest()
    {
        var folder = Path.GetDirectoryName(Repo.PluginOutput("HelloWorldOverlay"))!;
        var check = PluginManifests.Check(folder, "HelloWorldOverlay");

        Assert.True(check.Ok, check.Problem);
        var manifest = check.Manifest!;
        Assert.Equal("Hello World Overlay", manifest.Name);
        Assert.Equal("Ashla contributors", manifest.Author);
        Assert.Equal(AshlaApi.Level, manifest.ApiLevel);
        Assert.Contains("sample", manifest.Tags);
        Assert.Equal("https://github.com/YozoraCho/Ashla", manifest.Repository);
        Assert.Equal(PluginManifests.Hash(Path.Combine(folder, "HelloWorldOverlay.dll")), manifest.Files["HelloWorldOverlay.dll"]);
        Assert.DoesNotContain(manifest.Files.Keys, f => f.EndsWith(".pdb") || f == PluginManifests.FileName);
    }

    [Fact]
    public void SdkPackage_MatchesTheCurrentApiLevel()
    {
        var targets = File.ReadAllText(Path.Combine(Repo.Root, "src", "Ashla.SDK", "build", "Ashla.SDK.targets"));
        Assert.Contains($"'$(AshlaApiLevel)' == ''\">{AshlaApi.Level}</AshlaApiLevel>", targets);
        Assert.Equal(AshlaApi.Level, typeof(AshlaApi).Assembly.GetName().Version!.Major);
    }

    [Fact]
    public void SampleWithoutAuthor_HasNoAuthorInItsManifest()
    {
        var manifest = PluginManifests.TryRead(Path.GetDirectoryName(Repo.PluginOutput("HelloWorld"))!)!;
        Assert.Null(manifest.Author);
        Assert.Equal("Hello World", manifest.Name);
    }

    [Fact]
    public void VerifiedPlugin_Loads_WithItsManifestDetails()
    {
        CopyPlugin("HelloWorldOverlay");
        var (manager, toasts) = Load();
        using (manager)
        {
            var record = Assert.Single(manager.GetSnapshot());
            Assert.Equal(PluginStatus.Loaded, record.Status);
            Assert.Equal("0.2.0", record.Version);
            Assert.Empty(toasts.Update(0f));
        }
    }

    [Fact]
    public void MissingManifest_IsRefused_WithAToast()
    {
        var folder = CopyPlugin("HelloWorldOverlay");
        File.Delete(Path.Combine(folder, PluginManifests.FileName));

        var (manager, toasts) = Load();
        using (manager)
        {
            var record = Assert.Single(manager.GetSnapshot());
            Assert.Equal(PluginStatus.Failed, record.Status);
            Assert.Contains("No manifest.json", record.Error);
            Assert.Equal("HelloWorldOverlay wasn't loaded", Assert.Single(toasts.Update(0f)).Title);
        }
    }

    [Fact]
    public void ChangedDll_IsRefused()
    {
        var folder = CopyPlugin("HelloWorldOverlay");
        File.AppendAllText(Path.Combine(folder, "HelloWorldOverlay.dll"), "tampered");

        var check = PluginManifests.Check(folder, "HelloWorldOverlay");

        Assert.False(check.Ok);
        Assert.Contains("HelloWorldOverlay.dll changed", check.Problem);
        var (manager, toasts) = Load();
        using (manager)
        {
            Assert.Equal(PluginStatus.Failed, Assert.Single(manager.GetSnapshot()).Status);
            Assert.Equal("Hello World Overlay wasn't loaded", Assert.Single(toasts.Update(0f)).Title);
        }
    }

    [Fact]
    public void ExtraDll_And_MissingFile_AreRefused()
    {
        var folder = CopyPlugin("HelloWorldOverlay");
        File.WriteAllText(Path.Combine(folder, "Sneaky.dll"), "extra");
        File.Delete(Path.Combine(folder, "HelloWorldOverlay.deps.json"));

        var problem = PluginManifests.Check(folder, "HelloWorldOverlay").Problem!;

        Assert.Contains("HelloWorldOverlay.deps.json is missing", problem);
        Assert.Contains("Sneaky.dll isn't in manifest.json", problem);
    }

    [Fact]
    public void PdbFiles_AreIgnored()
    {
        var folder = CopyPlugin("HelloWorldOverlay");
        File.WriteAllText(Path.Combine(folder, "HelloWorldOverlay.pdb"), "symbols");
        Assert.True(PluginManifests.Check(folder, "HelloWorldOverlay").Ok);
    }

    [Theory]
    [InlineData(99, "needs a newer Ashla")]
    [InlineData(0, "made for an older Ashla")]
    public void WrongApiLevel_IsRefused(int level, string expected)
    {
        var folder = CopyPlugin("HelloWorldOverlay");
        EditManifest(folder, json => json["ApiLevel"] = level);
        Assert.Contains(expected, PluginManifests.Check(folder, "HelloWorldOverlay").Problem);
    }

    [Fact]
    public void ManifestForAnotherPlugin_OrOutsideFiles_OrBadJson_AreRefused()
    {
        var folder = CopyPlugin("HelloWorldOverlay");
        EditManifest(folder, json => json["Id"] = "SomethingElse");
        Assert.Contains("belongs to \"SomethingElse\"", PluginManifests.Check(folder, "HelloWorldOverlay").Problem);

        EditManifest(folder, json =>
        {
            json["Id"] = "HelloWorldOverlay";
            json["Files"]!.AsObject()["../../evil.dll"] = "00";
        });
        Assert.Contains("outside the plugin's folder", PluginManifests.Check(folder, "HelloWorldOverlay").Problem);

        File.WriteAllText(Path.Combine(folder, PluginManifests.FileName), "{ not json");
        Assert.Contains("can't be read", PluginManifests.Check(folder, "HelloWorldOverlay").Problem);
    }

    [Fact]
    public void ListedButBrokenAssembly_StillFailsCleanly()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "Garbage")).FullName;
        var dll = Path.Combine(folder, "Garbage.dll");
        File.WriteAllText(dll, "not an assembly");
        File.WriteAllText(Path.Combine(folder, PluginManifests.FileName),
            $$"""{ "Id": "Garbage", "ApiLevel": {{AshlaApi.Level}}, "Files": { "Garbage.dll": "{{PluginManifests.Hash(dll)}}" } }""");

        var (manager, _) = Load();
        using (manager)
        {
            var record = Assert.Single(manager.GetSnapshot());
            Assert.Equal(PluginStatus.Failed, record.Status);
            Assert.DoesNotContain("manifest", record.Error);
        }
    }

    [Fact]
    public void Launcher_ListsTheManifestName_AndAnyProblem()
    {
        CopyPlugin("HelloWorldOverlay");
        var broken = CopyPlugin("HelloWorld");
        File.AppendAllText(Path.Combine(broken, "HelloWorld.dll"), "x");

        var plugins = InstalledPlugins.Find(_temp.Path).ToDictionary(p => p.Id);

        Assert.Equal("Hello World Overlay", plugins["HelloWorldOverlay"].DisplayName);
        Assert.Null(plugins["HelloWorldOverlay"].Problem);
        Assert.Contains("HelloWorld.dll changed", plugins["HelloWorld"].Problem);
    }
}
