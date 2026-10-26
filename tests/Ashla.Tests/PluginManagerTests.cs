using System.Reflection;
using System.Runtime.Loader;
using Ashla.Host;
using Ashla.Host.Overlays;
using Ashla.SDK;

namespace Ashla.Tests;

public class PluginManagerTests
{
    private sealed record Ping;

    private readonly TestLogger _log = new();
    private readonly EventBus _events;
    private readonly PanelRegistry _panels;

    public PluginManagerTests()
    {
        _events = new EventBus(_log);
        _panels = new PanelRegistry(_log, isAvailable: true);
    }

    private PluginManager NewManager(PluginStateStore? state = null) =>
        new(_log, _ => _log, _events, new ExternalProvider(), _panels, state ?? PluginStateStore.InMemory(_log));

    private static readonly PluginMetadata Meta = new("2.1.0", "Someone", "A plugin");

    [Fact]
    public void Activate_InitializesPlugin_WithItsOwnInfo()
    {
        using var manager = NewManager();
        var plugin = new RecordingPlugin("Recorder");

        Assert.True(manager.Activate("Recorder", "/x/Recorder.dll", Meta, () => plugin, null));

        var info = plugin.Context!.Plugin;
        Assert.Equal(new PluginInfo("Recorder", "Recorder", "2.1.0", "Someone", "A plugin"), info);
        var record = Assert.Single(manager.GetSnapshot());
        Assert.Equal(PluginStatus.Loaded, record.Status);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void FailedInitialize_DisposesPlugin_AndRemovesWhatItRegistered()
    {
        using var manager = NewManager();
        int eventCalls = 0;
        var plugin = new RecordingPlugin("Broken")
        {
            OnInitialize = context =>
            {
                context.Overlays.Register(new TestPanel("panel"));
                context.Events.Subscribe<Ping>(_ => eventCalls++);
                throw new InvalidOperationException("init failed");
            }
        };

        Assert.False(manager.Activate("Broken", "/x/Broken.dll", Meta, () => plugin, null));
        _events.Publish(new Ping());

        Assert.True(plugin.Disposed);
        Assert.Empty(_panels.Snapshot());
        Assert.Equal(0, eventCalls);
        var record = Assert.Single(manager.GetSnapshot());
        Assert.Equal(PluginStatus.Failed, record.Status);
        Assert.Equal("init failed", record.Error);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void Dispose_DisposesInReverseOrder_AndCleansUpLeftovers()
    {
        var order = new List<string>();
        var manager = NewManager();
        int eventCalls = 0;
        var first = new RecordingPlugin("First") { OnDispose = () => order.Add("First") };
        var leaky = new RecordingPlugin("Leaky")
        {
            OnInitialize = context =>
            {
                context.Overlays.Register(new TestPanel("forgotten"));
                context.Events.Subscribe<Ping>(_ => eventCalls++);
            },
            OnDispose = () => order.Add("Leaky")
        };
        manager.Activate("First", "/x/First.dll", Meta, () => first, null);
        manager.Activate("Leaky", "/x/Leaky.dll", Meta, () => leaky, null);
        Assert.Single(_panels.Snapshot());

        manager.Dispose();
        _events.Publish(new Ping());

        Assert.Equal(["Leaky", "First"], order);
        Assert.Empty(_panels.Snapshot());
        Assert.Equal(0, eventCalls);
        Assert.Contains(_log.Warnings, w => w.Contains("1 panel(s)"));
        Assert.Contains(_log.Warnings, w => w.Contains("1 event subscription(s)"));
        Assert.All(manager.GetSnapshot(), r => Assert.Equal(PluginStatus.Unloaded, r.Status));
        manager.Dispose();
        Assert.Equal(2, order.Count);
    }

    [Fact]
    public void ThrowingDispose_DoesNotStopOtherPlugins()
    {
        var manager = NewManager();
        var good = new RecordingPlugin("Good");
        var bad = new RecordingPlugin("Bad") { OnDispose = () => throw new InvalidOperationException("dispose") };
        manager.Activate("Good", "/x/Good.dll", Meta, () => good, null);
        manager.Activate("Bad", "/x/Bad.dll", Meta, () => bad, null);

        manager.Dispose();

        Assert.True(good.Disposed);
        Assert.Single(_log.Errors);
    }

    [Fact]
    public void LoadFrom_SkipsDisabledPlugins_AndReportsBadAssemblies()
    {
        using var temp = new TempDirectory();
        foreach (var name in new[] { "Disabled", "Garbage" })
        {
            Directory.CreateDirectory(Path.Combine(temp.Path, name));
            File.WriteAllText(Path.Combine(temp.Path, name, $"{name}.dll"), "not an assembly");
        }
        Directory.CreateDirectory(Path.Combine(temp.Path, "NoDll"));
        var state = PluginStateStore.InMemory(_log);
        state.SetEnabled("Disabled", false);
        using var manager = NewManager(state);

        manager.LoadFrom(temp.Path);

        var records = manager.GetSnapshot().ToDictionary(r => r.Id);
        Assert.Equal(2, records.Count);
        Assert.Equal(PluginStatus.Disabled, records["Disabled"].Status);
        Assert.False(records["Disabled"].EnabledOnNextStart);
        Assert.Equal(PluginStatus.Failed, records["Garbage"].Status);
        Assert.NotNull(records["Garbage"].Error);
    }

    [Fact]
    public void LoadFrom_RealOverlayPlugin_RegistersPanel_AndUnregistersOnDispose()
    {
        using var temp = CopyPlugins("HelloWorldOverlay", "HelloWorld");
        var manager = NewManager();

        manager.LoadFrom(temp.Path);

        var records = manager.GetSnapshot().ToDictionary(r => r.Id);
        Assert.Equal(PluginStatus.Loaded, records["HelloWorldOverlay"].Status);
        Assert.Equal(PluginStatus.Loaded, records["HelloWorld"].Status);
        Assert.Equal("Hello World Overlay", records["HelloWorldOverlay"].Name);
        Assert.Equal("0.2.0", records["HelloWorldOverlay"].Version);
        Assert.Equal("Ashla contributors", records["HelloWorldOverlay"].Author);
        Assert.Null(records["HelloWorld"].Author);

        var panel = Assert.Single(_panels.Snapshot());
        Assert.Equal("HelloWorldOverlay/hello", panel.WindowId);
        Assert.Equal("Hello, SWTOR!", panel.Panel.Title);
        Assert.Equal("HelloWorldOverlay", panel.Owner.Id);
        Assert.IsType<PluginLoadContext>(AssemblyLoadContext.GetLoadContext(panel.Panel.GetType().Assembly));

        manager.Dispose();

        Assert.Empty(_panels.Snapshot());
        Assert.DoesNotContain(_log.Warnings, w => w.Contains("panel(s)"));
    }

    [Fact]
    public void LoadFrom_InConsoleMode_OverlayPluginLoadsWithoutPanel()
    {
        using var temp = CopyPlugins("HelloWorldOverlay");
        var consolePanels = new PanelRegistry(_log, isAvailable: false);
        using var manager = new PluginManager(_log, _ => _log, _events, new ExternalProvider(), consolePanels,
            PluginStateStore.InMemory(_log));

        manager.LoadFrom(temp.Path);

        Assert.Equal(PluginStatus.Loaded, Assert.Single(manager.GetSnapshot()).Status);
        Assert.Empty(consolePanels.Snapshot());
    }

    [Fact]
    public void UnloadedOverlayPlugin_LoadContextIsCollected()
    {
        using var temp = CopyPlugins("HelloWorldOverlay");
        var weak = LoadAndUnload(temp.Path);

        for (int i = 0; i < 20 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private WeakReference LoadAndUnload(string pluginsDir)
    {
        var manager = NewManager();
        manager.LoadFrom(pluginsDir);
        var panel = Assert.Single(_panels.Snapshot());
        var weak = new WeakReference(AssemblyLoadContext.GetLoadContext(panel.Panel.GetType().Assembly));
        manager.Dispose();
        return weak;
    }

    [Fact]
    public void PluginConfig_IsStoredUnderSettings_AndDoesNotPinUnloadedPlugin()
    {
        using var plugins = CopyPlugins("HelloWorldOverlay");
        using var settings = new TempDirectory();
        var configFile = Path.Combine(settings.Path, "pluginConfigs", "HelloWorldOverlay.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);
        File.WriteAllText(configFile, "{ \"Clicks\": 2 }");

        var weak = LoadAndUnloadWithSettings(plugins.Path, settings.Path);
        for (int i = 0; i < 20 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive);
        Assert.Empty(_log.Warnings);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private WeakReference LoadAndUnloadWithSettings(string pluginsDir, string settingsDir)
    {
        var manager = new PluginManager(_log, _ => _log, _events, new ExternalProvider(), _panels,
            PluginStateStore.InMemory(_log), settingsDir);
        manager.LoadFrom(pluginsDir);
        Assert.Equal(PluginStatus.Loaded, Assert.Single(manager.GetSnapshot()).Status);
        var panel = Assert.Single(_panels.Snapshot());
        var weak = new WeakReference(AssemblyLoadContext.GetLoadContext(panel.Panel.GetType().Assembly));
        manager.Dispose();
        return weak;
    }

    [Fact]
    public void PluginContext_ConfigPointsAtPluginConfigsFolder()
    {
        using var settings = new TempDirectory();
        var plugin = new RecordingPlugin("Configured");
        var manager = new PluginManager(_log, _ => _log, _events, new ExternalProvider(), _panels,
            PluginStateStore.InMemory(_log), settings.Path);
        using (manager)
        {
            manager.Activate("Configured", "/fake/Configured.dll", Meta, () => plugin, null);
            Assert.Equal(Path.Combine(settings.Path, "pluginConfigs", "Configured.json"), plugin.Context!.Config.FilePath);
        }
    }

    [Fact]
    public void PluginLoadContext_SharesSdkAndImGuiWithHost()
    {
        var path = Repo.PluginOutput("HelloWorldOverlay");
        var context = new PluginLoadContext(path);
        try
        {
            var sdk = context.LoadFromAssemblyName(new AssemblyName("Ashla.SDK"));
            var imgui = context.LoadFromAssemblyName(new AssemblyName("Hexa.NET.ImGui"));
            var hexaRuntime = context.LoadFromAssemblyName(new AssemblyName("HexaGen.Runtime"));
            var ui = context.LoadFromAssemblyName(new AssemblyName("Ashla.UI"));

            Assert.Same(typeof(IAshlaPlugin).Assembly, sdk);
            Assert.Same(typeof(Hexa.NET.ImGui.ImGui).Assembly, imgui);
            Assert.Same(typeof(HexaGen.Runtime.Utils).Assembly, hexaRuntime);
            Assert.Same(typeof(Ashla.UI.AshlaColors).Assembly, ui);
            Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(imgui));
        }
        finally
        {
            context.Unload();
        }
    }

    [Theory]
    [InlineData("HelloWorldOverlay")]
    public void UiPluginOutput_DoesNotShipHostSharedAssemblies(string plugin)
    {
        var folder = Path.GetDirectoryName(Repo.PluginOutput(plugin))!;
        Assert.True(File.Exists(Path.Combine(folder, $"{plugin}.dll")));
        Assert.False(File.Exists(Path.Combine(folder, "Hexa.NET.ImGui.dll")));
        Assert.False(File.Exists(Path.Combine(folder, "HexaGen.Runtime.dll")));
        Assert.False(File.Exists(Path.Combine(folder, "Ashla.SDK.dll")));
        Assert.False(File.Exists(Path.Combine(folder, "Ashla.UI.dll")));
        Assert.False(Directory.Exists(Path.Combine(folder, "runtimes")));
    }

    [Fact]
    public void PluginMetadata_ReadsVersionAuthorAndDescription()
    {
        var metadata = PluginMetadata.ReadFromFile(Repo.PluginOutput("HelloWorldOverlay"));
        Assert.Equal("0.2.0", metadata.Version);
        Assert.Equal("Ashla contributors", metadata.Author);
        Assert.Contains("Hello, SWTOR!", metadata.Description);

        Assert.Equal("unknown", PluginMetadata.ReadFromFile("/does/not/exist.dll").Version);
    }

    private static TempDirectory CopyPlugins(params string[] names)
    {
        var temp = new TempDirectory();
        foreach (var name in names)
        {
            var source = Path.GetDirectoryName(Repo.PluginOutput(name))!;
            Assert.True(Directory.Exists(source), $"Build the {name} plugin first ({source}).");
            var target = Directory.CreateDirectory(Path.Combine(temp.Path, name)).FullName;
            foreach (var file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        return temp;
    }

    private sealed class RecordingPlugin(string name) : IAshlaPlugin
    {
        public string Name { get; } = name;
        public IPluginContext? Context { get; private set; }
        public bool Disposed { get; private set; }
        public Action<IPluginContext>? OnInitialize { get; init; }
        public Action? OnDispose { get; init; }

        public void Initialize(IPluginContext context)
        {
            Context = context;
            OnInitialize?.Invoke(context);
        }

        public void Dispose()
        {
            Disposed = true;
            OnDispose?.Invoke();
        }
    }
}
