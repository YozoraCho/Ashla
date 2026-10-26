using Ashla.Host;
using Ashla.Host.Overlays;
using Ashla.SDK;

namespace Ashla.Tests;

public class CapabilityTests
{
    private readonly TestLogger _log = new();
    private readonly EventBus _events;

    public CapabilityTests() => _events = new EventBus(_log);

    private static readonly PluginMetadata Meta = new("1.0.0", null, null);

    private (PluginManager Manager, ToastCenter Toasts) NewManager(bool overlayAvailable)
    {
        var toasts = new ToastCenter();
        var panels = new PanelRegistry(_log, isAvailable: overlayAvailable, toasts);
        var manager = new PluginManager(_log, _ => _log, _events, new ExternalProvider(), panels,
            PluginStateStore.InMemory(_log), toasts: toasts);
        return (manager, toasts);
    }

    [Fact]
    public void Context_ExposesTheAlwaysOnCapabilities()
    {
        var (manager, _) = NewManager(overlayAvailable: true);
        var plugin = new CapturingPlugin();
        Assert.True(manager.Activate("Cap", "/x/Cap.dll", Meta, () => plugin, null));

        var caps = plugin.Context!.Capabilities;
        Assert.True(caps.Has<IEventBus>());
        Assert.True(caps.Has<IGameState>());
        Assert.True(caps.Has<IToasts>());
        Assert.True(caps.Has<IOverlayRegistry>());
        Assert.False(caps.Has<IEncounterArchive>());
        Assert.Same(plugin.Context.Events, caps.Get<IEventBus>());
        Assert.Null(caps.Get<IEncounterArchive>());
        Assert.Contains(typeof(IGameState), caps.Available);
    }

    [Fact]
    public void OverlayCapability_FollowsRendererAvailability()
    {
        var (console, _) = NewManager(overlayAvailable: false);
        var plugin = new CapturingPlugin();
        console.Activate("Cap", "/x/Cap.dll", Meta, () => plugin, null);
        Assert.False(plugin.Context!.Capabilities.Has<IOverlayRegistry>());
    }

    [Fact]
    public void PluginWithNoRequirements_LoadsEvenWithoutARenderer()
    {
        var (manager, _) = NewManager(overlayAvailable: false);
        Assert.True(manager.Activate("Plain", "/x/Plain.dll", Meta, () => new CapturingPlugin(), null));
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void PluginRequiringAnAvailableCapability_Loads()
    {
        var (manager, _) = NewManager(overlayAvailable: true);
        Assert.True(manager.Activate("UI", "/x/UI.dll", Meta, () => new NeedsOverlay(), null));
        Assert.Equal(PluginStatus.Loaded, Assert.Single(manager.GetSnapshot()).Status);
    }

    [Fact]
    public void PluginRequiringAMissingCapability_FailsCleanly_WithoutInitializing()
    {
        var (manager, toasts) = NewManager(overlayAvailable: false);
        var plugin = new NeedsOverlay();

        Assert.False(manager.Activate("UI", "/x/UI.dll", Meta, () => plugin, null));

        Assert.False(plugin.Initialized);
        Assert.True(plugin.Disposed);
        var record = Assert.Single(manager.GetSnapshot());
        Assert.Equal(PluginStatus.Failed, record.Status);
        Assert.Contains("OverlayRegistry", record.Error);
        Assert.Equal(0, manager.Count);
        Assert.Equal(ToastKind.Error, Assert.Single(toasts.Update(0f)).Kind);
    }

    [Fact]
    public void OnlyTheMissingCapability_IsNamed()
    {
        var (manager, _) = NewManager(overlayAvailable: true);
        Assert.False(manager.Activate("Both", "/x/Both.dll", Meta, () => new NeedsOverlayAndArchive(), null));
        var record = Assert.Single(manager.GetSnapshot());
        Assert.Contains("EncounterArchive", record.Error);
        Assert.DoesNotContain("OverlayRegistry", record.Error);
    }

    private sealed class CapturingPlugin : IAshlaPlugin
    {
        public IPluginContext? Context { get; private set; }
        public string Name => "Capturing";
        public void Initialize(IPluginContext context) => Context = context;
        public void Dispose() { }
    }

    [RequiresCapability(typeof(IOverlayRegistry))]
    private sealed class NeedsOverlay : IAshlaPlugin
    {
        public bool Initialized { get; private set; }
        public bool Disposed { get; private set; }
        public string Name => "Needs Overlay";
        public void Initialize(IPluginContext context) => Initialized = true;
        public void Dispose() => Disposed = true;
    }

    [RequiresCapability(typeof(IOverlayRegistry))]
    [RequiresCapability(typeof(IEncounterArchive))]
    private sealed class NeedsOverlayAndArchive : IAshlaPlugin
    {
        public string Name => "Needs Both";
        public void Initialize(IPluginContext context) { }
        public void Dispose() { }
    }
}
