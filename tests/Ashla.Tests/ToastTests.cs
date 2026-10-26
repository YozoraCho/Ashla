using Ashla.Host;
using Ashla.Host.Gui;
using Ashla.Host.Overlays;
using Ashla.Host.Settings;
using Ashla.SDK;

namespace Ashla.Tests;

public class ToastTests
{
    private readonly TestLogger _log = new();

    [Fact]
    public void Toast_FadesIn_Stays_ThenFadesOutAndExpires()
    {
        var center = new ToastCenter();
        center.Show("Saved", null, ToastKind.Info);

        var toast = Assert.Single(center.Update(0f));
        Assert.Equal(0f, toast.Opacity);
        Assert.Equal("Ashla", toast.Source);
        Step(center, 1f);
        Assert.Equal(1f, toast.Opacity);
        Step(center, toast.Duration - 1f - Toast.FadeOut / 2);
        Assert.InRange(toast.Opacity, 0.1f, 0.9f);
        Step(center, 1f);
        Assert.Empty(center.Update(0f));
    }

    [Fact]
    public void Durations_AreLongerForWarningsAndErrors()
    {
        var info = new Toast(1, "a", null, ToastKind.Info, "Ashla");
        var warning = new Toast(2, "a", null, ToastKind.Warning, "Ashla");
        var error = new Toast(3, "a", null, ToastKind.Error, "Ashla");
        Assert.True(info.Duration < warning.Duration);
        Assert.True(warning.Duration < error.Duration);
    }

    [Fact]
    public void AtMostFourShow_TheRestWaitTheirTurn()
    {
        var center = new ToastCenter();
        for (int i = 1; i <= 6; i++) center.Show($"Toast {i}", null, ToastKind.Info);

        var visible = center.Update(0f);
        Assert.Equal(["Toast 1", "Toast 2", "Toast 3", "Toast 4"], visible.Select(t => t.Title));
        Assert.Equal(2, center.WaitingCount);

        Step(center, 7f);
        Assert.Equal(["Toast 5", "Toast 6"], center.Update(0f).Select(t => t.Title));
    }

    [Fact]
    public void TooManyWaiting_DropsTheOldestWaiting()
    {
        var center = new ToastCenter();
        for (int i = 1; i <= 30; i++) center.Show($"Toast {i}", null, ToastKind.Info);

        var visible = center.Update(0f);

        Assert.Equal("Toast 11", visible[0].Title);
        Assert.Equal(ToastCenter.MaxWaiting - ToastCenter.MaxVisible, center.WaitingCount);
    }

    [Fact]
    public void SameToastAgain_RestartsItsTime_AndCounts()
    {
        var center = new ToastCenter();
        center.Show("Timer done", "Kephess", ToastKind.Info, "Timers");
        var toast = Assert.Single(center.Update(0f));
        Step(center, 5f);

        center.Show("Timer done", "Kephess", ToastKind.Info, "Timers");
        center.Show("Timer done", "Kephess", ToastKind.Info, "Timers");

        Assert.Single(center.Update(0f));
        Assert.Equal(3, toast.Count);
        Assert.True(toast.Age <= Toast.FadeIn);
        center.Show("Timer done", "Other boss", ToastKind.Info, "Timers");
        Assert.Equal(2, center.Update(0f).Count);
    }

    [Fact]
    public void Hovering_KeepsAToast_ClickingDismissesIt()
    {
        var center = new ToastCenter();
        center.Show("Hover me", null, ToastKind.Info);
        var toast = Assert.Single(center.Update(0f));

        for (int i = 0; i < 200; i++) center.Update(0.1f, hoveredId: toast.Id);
        Assert.Single(center.Update(0f, hoveredId: toast.Id));

        center.Dismiss(toast.Id);
        Step(center, Toast.FadeOut + 0.05f, hovered: toast.Id);
        Assert.Empty(center.Update(0f));
    }

    [Fact]
    public void LongPausesInDrawing_DontSkipToasts()
    {
        var center = new ToastCenter();
        center.Show("While you were away", null, ToastKind.Warning);
        center.Update(0f);
        Assert.Single(center.Update(30f));
    }

    [Fact]
    public void LongOrBlankText_IsTrimmed_BlankTitlesIgnored()
    {
        var center = new ToastCenter();
        center.Show("   ", "text", ToastKind.Info);
        center.Show(new string('a', 500), "line one\nline two  ", ToastKind.Info);

        var toast = Assert.Single(center.Update(0f));
        Assert.Equal(ToastCenter.MaxTitleLength, toast.Title.Length);
        Assert.EndsWith("…", toast.Title);
        Assert.Equal("line one line two", toast.Text);
    }

    [Fact]
    public void ShowFromManyThreads_IsSafe()
    {
        var center = new ToastCenter();
        Parallel.For(0, 500, i => center.Show($"Toast {i % 50}", null, ToastKind.Info));
        Assert.Equal(ToastCenter.MaxVisible, center.Update(0f).Count);
        Assert.Equal(ToastCenter.MaxWaiting - ToastCenter.MaxVisible, center.WaitingCount);
    }

    [Fact]
    public void PluginToasts_CarryThePluginName_AndAreLogged()
    {
        var center = new ToastCenter();
        IToasts toasts = new PluginToasts(center, "Combat Meter", _log);

        toasts.Show("Fight saved", "Kephess, 4:12");
        toasts.Show("Couldn't save", kind: ToastKind.Error);
        toasts.Show("");

        var shown = center.Update(0f);
        Assert.All(shown, t => Assert.Equal("Combat Meter", t.Source));
        Assert.Equal(2, shown.Count);
        Assert.Contains("INFO Toast: Fight saved (Kephess, 4:12)", _log.Lines);
        Assert.Contains("WARN Toast: Couldn't save", _log.Lines);
        new PluginToasts(null, "Console", _log).Show("Only logged");
        Assert.Contains("INFO Toast: Only logged", _log.Lines);
    }

    [Fact]
    public void PluginContext_HasToasts_AndFailedPluginsAreToasted()
    {
        var center = new ToastCenter();
        var panels = new PanelRegistry(_log, isAvailable: true, center);
        using var manager = new PluginManager(_log, _ => _log, new EventBus(_log), new ExternalProvider(), panels,
            PluginStateStore.InMemory(_log), toasts: center);
        var meta = new PluginMetadata("1.0.0", null, null);

        IPluginContext? context = null;
        manager.Activate("Good", "/x/Good.dll", meta, () => new FakePlugin("Good Plugin", c => context = c), null);
        context!.Toasts.Show("Hello");
        manager.Activate("Bad", "/x/Bad.dll", meta,
            () => new FakePlugin("Bad Plugin", _ => throw new InvalidOperationException("nope")), null);

        var shown = center.Update(0f);
        Assert.Equal("Good Plugin", shown[0].Source);
        Assert.Equal("Bad Plugin couldn't load", shown[1].Title);
        Assert.Equal(ToastKind.Error, shown[1].Kind);
    }

    [Fact]
    public void PanelThatThrows_IsToasted()
    {
        var center = new ToastCenter();
        var panels = new PanelRegistry(_log, isAvailable: true, center);
        panels.Register(new PluginInfo("p", "Some Plugin", "1.0", null, null),
            new TestPanel("meter", "Meter") { OnDraw = () => throw new InvalidOperationException("boom") });

        panels.RenderFrame(new FakeChrome());

        var toast = Assert.Single(center.Update(0f));
        Assert.Equal("Meter stopped working", toast.Title);
        Assert.Equal("Some Plugin", toast.Source);
    }

    [Fact]
    public void CombatLogging_OffIsToasted_AndOnLaterToo_ButNotOnAtStart()
    {
        var center = new ToastCenter();
        var status = new CombatLogStatus();
        CombatLogToasts.Watch(status, center);
        status.SetDirectoryExists(false);
        status.SetDirectoryExists(true);
        Assert.Equal(["Combat logging looks off", "Combat logging found"], center.Update(0f).Select(t => t.Title));

        var quiet = new ToastCenter();
        var on = new CombatLogStatus();
        on.SetDirectoryExists(true);
        CombatLogToasts.Watch(on, quiet);
        Assert.Empty(quiet.Update(0f));

        var late = new ToastCenter();
        var off = new CombatLogStatus();
        off.SetDirectoryExists(false);
        CombatLogToasts.Watch(off, late);
        Assert.Equal("Combat logging looks off", Assert.Single(late.Update(0f)).Title);
    }

    [Fact]
    public void ToastCorner_DefaultsToBottomRight()
    {
        Assert.Equal(ToastCorner.BottomRight, new OverlaySettings().ToastCorner);
        Assert.Equal(4, Enum.GetValues<ToastKind>().Select(ToastRenderer.KindColor).Distinct().Count());
    }

    private static void Step(ToastCenter center, float seconds, long? hovered = null)
    {
        for (float done = 0; done < seconds; done += 0.05f) center.Update(Math.Min(0.05f, seconds - done), hovered);
    }

    private sealed class FakePlugin(string name, Action<IPluginContext> initialize) : IAshlaPlugin
    {
        public string Name { get; } = name;
        public void Initialize(IPluginContext context) => initialize(context);
        public void Dispose() { }
    }
}
