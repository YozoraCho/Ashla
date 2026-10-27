using Ashla.Host.Gui;
using Ashla.Host.Overlays;

namespace Ashla.Tests;

public class PanelRegistryTests
{
    private readonly TestLogger _log = new();
    private readonly FakeChrome _chrome = new();

    private PanelRegistry NewRegistry(bool available = true) => new(_log, available);

    [Fact]
    public void Register_WithoutRenderer_ThrowsNotSupported()
    {
        var registry = NewRegistry(available: false);
        Assert.False(registry.IsAvailable);
        Assert.Throws<NotSupportedException>(() => registry.Register(Plugins.Info("p"), new TestPanel("a")));
    }

    [Fact]
    public void RenderFrame_DrawsVisiblePanelInsideChrome()
    {
        var registry = NewRegistry();
        var panel = new TestPanel("hello", "Hello, SWTOR!");
        registry.Register(Plugins.Info("Plugin"), panel);

        registry.RenderFrame(_chrome);

        Assert.Equal(1, panel.Draws);
        Assert.Equal(["Begin:Plugin/hello:Hello, SWTOR!", "End"], _chrome.Calls);
    }

    [Fact]
    public void CollapsedWindow_SkipsDraw_ButStillEnds()
    {
        var registry = NewRegistry();
        var panel = new TestPanel("a");
        registry.Register(Plugins.Info("p"), panel);
        _chrome.ContentVisible = false;

        registry.RenderFrame(_chrome);

        Assert.Equal(0, panel.Draws);
        Assert.Equal(0, _chrome.Balance);
        Assert.Equal(2, _chrome.Calls.Count);
    }

    [Fact]
    public void Dispose_RemovesPanel_AndStopsDrawing()
    {
        var registry = NewRegistry();
        var panel = new TestPanel("a");
        var registration = registry.Register(Plugins.Info("p"), panel);

        registration.Dispose();
        registry.RenderFrame(_chrome);

        Assert.False(registration.IsRegistered);
        Assert.Empty(registry.Snapshot());
        Assert.Equal(0, panel.Draws);
        registration.Dispose();
    }

    [Fact]
    public void Visibility_ControlsDrawing()
    {
        var registry = NewRegistry();
        var panel = new TestPanel("a");
        var registration = registry.Register(Plugins.Info("p"), panel);

        registration.IsVisible = false;
        registry.RenderFrame(_chrome);
        Assert.Equal(0, panel.Draws);

        registration.IsVisible = true;
        registry.RenderFrame(_chrome);
        Assert.Equal(1, panel.Draws);
    }

    [Fact]
    public void CloseButton_HidesPanel_UntilShownAgain()
    {
        var registry = NewRegistry();
        var panel = new TestPanel("a");
        var registration = registry.Register(Plugins.Info("p"), panel);
        _chrome.CloseOnNextBegin.Add("p/a");

        registry.RenderFrame(_chrome);
        registry.RenderFrame(_chrome);

        Assert.False(registration.IsVisible);
        Assert.True(registration.IsRegistered);
        Assert.Equal(1, panel.Draws);
    }

    [Fact]
    public void ThrowingPanel_IsSuspended_WithoutAffectingOthers()
    {
        var registry = NewRegistry();
        var bad = new TestPanel("bad") { OnDraw = () => throw new InvalidOperationException("boom") };
        var good = new TestPanel("good");
        var badEntry = registry.Register(Plugins.Info("p1"), bad);
        registry.Register(Plugins.Info("p2"), good);

        registry.RenderFrame(_chrome);
        registry.RenderFrame(_chrome);

        Assert.Equal(1, bad.Draws);
        Assert.Equal(2, good.Draws);
        Assert.Equal(0, _chrome.Balance);
        Assert.IsType<InvalidOperationException>(badEntry.Fault);
        Assert.Single(_log.Errors);

        badEntry.ClearFault();
        bad.OnDraw = null;
        registry.RenderFrame(_chrome);
        Assert.Equal(2, bad.Draws);
        Assert.Null(badEntry.Fault);
    }

    [Fact]
    public void ThrowingTitle_IsSuspended_BeforeBegin()
    {
        var registry = NewRegistry();
        var panel = new ThrowingTitlePanel();
        var entry = registry.Register(Plugins.Info("p"), panel);

        registry.RenderFrame(_chrome);

        Assert.Empty(_chrome.Calls);
        Assert.NotNull(entry.Fault);
    }

    [Fact]
    public void HiddenPanel_IsListedByItsTitle_BeforeItsFirstDraw()
    {
        var registry = NewRegistry();
        var entry = (PanelEntry)registry.Register(Plugins.Info("p"), new TestPanel("settings", "My Settings"));
        entry.IsVisible = false;

        Assert.Equal("My Settings", entry.LastTitle);
        Assert.Equal("t", ((PanelEntry)registry.Register(Plugins.Info("p"), new ThrowingTitlePanel())).LastTitle);
    }

    [Fact]
    public void EmptyTitle_FallsBackToPanelId()
    {
        var registry = NewRegistry();
        registry.Register(Plugins.Info("p"), new TestPanel("panel-id", " "));
        registry.RenderFrame(_chrome);
        Assert.Equal("Begin:p/panel-id:panel-id", _chrome.Calls[0]);
    }

    [Fact]
    public void DuplicatePanelId_IsRejectedPerPlugin_ButAllowedAcrossPlugins()
    {
        var registry = NewRegistry();
        registry.Register(Plugins.Info("p1"), new TestPanel("main"));

        Assert.Throws<InvalidOperationException>(() => registry.Register(Plugins.Info("p1"), new TestPanel("main")));
        registry.Register(Plugins.Info("p2"), new TestPanel("main"));
        Assert.Equal(2, registry.Snapshot().Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPanelId_IsRejected(string id)
    {
        Assert.Throws<ArgumentException>(() => NewRegistry().Register(Plugins.Info("p"), new TestPanel(id)));
    }

    [Fact]
    public void RemoveAll_RemovesOnlyThatPluginsPanels()
    {
        var registry = NewRegistry();
        var a1 = registry.Register(Plugins.Info("a"), new TestPanel("1"));
        registry.Register(Plugins.Info("a"), new TestPanel("2"));
        var b1 = registry.Register(Plugins.Info("b"), new TestPanel("1"));

        Assert.Equal(2, registry.RemoveAll("a"));

        Assert.False(a1.IsRegistered);
        Assert.True(b1.IsRegistered);
        Assert.Single(registry.Snapshot());
    }

    [Fact]
    public async Task DisposeFromAnotherThread_WaitsForInProgressDraw()
    {
        var registry = NewRegistry();
        using var drawing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var panel = new TestPanel("slow")
        {
            OnDraw = () =>
            {
                drawing.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        };
        var registration = registry.Register(Plugins.Info("p"), panel);

        var render = Task.Run(() => registry.RenderFrame(_chrome));
        Assert.True(drawing.Wait(TimeSpan.FromSeconds(10)));

        var dispose = Task.Run(registration.Dispose);
        await Task.WhenAny(dispose, Task.Delay(200));
        Assert.False(dispose.IsCompleted);

        release.Set();
        await dispose.WaitAsync(TimeSpan.FromSeconds(10));
        await render.WaitAsync(TimeSpan.FromSeconds(10));

        registry.RenderFrame(_chrome);
        Assert.Equal(1, panel.Draws);
    }

    [Fact]
    public async Task DisposeFromInsideOwnDraw_DoesNotDeadlock()
    {
        var registry = NewRegistry();
        PanelEntry? registration = null;
        var panel = new TestPanel("self");
        panel.OnDraw = () => registration!.Dispose();
        registration = registry.Register(Plugins.Info("p"), panel);

        await Task.Run(() => registry.RenderFrame(_chrome)).WaitAsync(TimeSpan.FromSeconds(10));
        registry.RenderFrame(_chrome);

        Assert.Equal(1, panel.Draws);
        Assert.False(registration.IsRegistered);
        Assert.Equal(0, _chrome.Balance);
    }

    [Fact]
    public async Task ConcurrentRegistration_WhileRendering_IsSafe()
    {
        var registry = NewRegistry();
        using var stop = new CancellationTokenSource();
        var render = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested) registry.RenderFrame(new FakeChrome());
        });

        Parallel.For(0, 200, i =>
        {
            var registration = registry.Register(Plugins.Info($"p{i % 10}"), new TestPanel($"panel{i}"));
            if (i % 2 == 0) registration.Dispose();
        });
        stop.Cancel();
        await render.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(100, registry.Snapshot().Count);
    }

    [Fact]
    public void PluginOverlayRegistry_Close_RemovesLeftovers_AndRejectsNewPanels()
    {
        var registry = NewRegistry();
        var scoped = new PluginOverlayRegistry(registry, Plugins.Info("p"));
        var kept = scoped.Register(new TestPanel("a"));
        scoped.Register(new TestPanel("b")).Dispose();

        Assert.True(scoped.IsAvailable);
        Assert.Equal(1, scoped.Close());

        Assert.False(kept.IsRegistered);
        Assert.False(scoped.IsAvailable);
        Assert.Throws<ObjectDisposedException>(() => scoped.Register(new TestPanel("c")));
        Assert.Equal(0, scoped.Close());
    }

    [Theory]
    [InlineData("Hello", "Hello")]
    [InlineData("A ## B", "A # B")]
    [InlineData("A ### B", "A # B")]
    [InlineData("####", "#")]
    public void SanitizeTitle_NeutralizesImGuiIdMarkers(string input, string expected)
    {
        Assert.Equal(expected, ImGuiPanelChrome.SanitizeTitle(input));
    }

    [Fact]
    public void ClickThroughPanels_TakeNoMouseInput()
    {
        var flags = ImGuiPanelChrome.LookFlags(new Ashla.SDK.PanelAppearance { ShowTitleBar = false, ClickThrough = true });

        Assert.True(flags.HasFlag(Hexa.NET.ImGui.ImGuiWindowFlags.NoInputs));
        Assert.True(flags.HasFlag(Hexa.NET.ImGui.ImGuiWindowFlags.NoTitleBar));
        Assert.False(ImGuiPanelChrome.LookFlags(new Ashla.SDK.PanelAppearance()).HasFlag(Hexa.NET.ImGui.ImGuiWindowFlags.NoMouseInputs));
        Assert.Equal(Hexa.NET.ImGui.ImGuiWindowFlags.None, ImGuiPanelChrome.LookFlags(null));
    }

    private sealed class ThrowingTitlePanel : Ashla.SDK.IPluginPanel
    {
        public string Id => "t";
        public string Title => throw new InvalidOperationException("title");
        public void Draw() { }
    }
}
