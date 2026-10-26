using Ashla.Host.Overlay;

namespace Ashla.Tests;

public class GameWindowTrackerTests
{
    private sealed class FakeWindowSystem : IWindowSystem
    {
        public Dictionary<string, List<int>> Processes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, List<TopLevelWindow>> Windows { get; } = [];
        public Dictionary<nint, WindowBounds> Bounds { get; } = [];
        public HashSet<nint> Minimized { get; } = [];
        public nint Foreground { get; set; }
        public int ProcessScans { get; private set; }

        public void AddWindow(int pid, nint handle, string title, WindowBounds bounds)
        {
            if (!Windows.TryGetValue(pid, out var list)) Windows[pid] = list = [];
            list.Add(new TopLevelWindow(handle, title));
            Bounds[handle] = bounds;
        }

        public void CloseWindow(nint handle)
        {
            Bounds.Remove(handle);
            foreach (var list in Windows.Values) list.RemoveAll(w => w.Handle == handle);
        }

        public IReadOnlyList<int> FindProcessIds(string processName)
        {
            ProcessScans++;
            return Processes.TryGetValue(processName, out var ids) ? ids : [];
        }

        public IReadOnlyList<TopLevelWindow> GetTopLevelWindows(int processId) =>
            Windows.TryGetValue(processId, out var list) ? list : [];

        public bool IsWindow(nint handle) => Bounds.ContainsKey(handle);
        public bool IsMinimized(nint handle) => Minimized.Contains(handle);
        public bool TryGetClientBounds(nint handle, out WindowBounds bounds) => Bounds.TryGetValue(handle, out bounds);
        public nint GetForegroundWindow() => Foreground;
    }

    private readonly FakeWindowSystem _system = new();
    private TimeSpan _now = TimeSpan.FromSeconds(100);

    private GameWindowTracker NewTracker(string process = "swtor.exe", string? title = null) =>
        new(_system, process, title, () => _now, TimeSpan.FromSeconds(1));

    [Fact]
    public void NotRunning_IsNotFound_AndRescansAreThrottled()
    {
        var tracker = NewTracker();

        Assert.False(tracker.Poll(0).Found);
        Assert.False(tracker.Poll(0).Found);
        Assert.Equal(1, _system.ProcessScans);

        _now += TimeSpan.FromSeconds(1.1);
        tracker.Poll(0);
        Assert.Equal(2, _system.ProcessScans);
    }

    [Fact]
    public void FindsLargestTitledWindowOfTheProcess()
    {
        _system.Processes["swtor"] = [42];
        _system.AddWindow(42, 10, "Splash", new WindowBounds(0, 0, 200, 100));
        _system.AddWindow(42, 11, "Star Wars: The Old Republic", new WindowBounds(50, 60, 1920, 1080));
        _system.Foreground = 11;

        var state = NewTracker().Poll(overlayHandle: 99);

        Assert.True(state.Found);
        Assert.Equal(new WindowBounds(50, 60, 1920, 1080), state.ClientBounds);
        Assert.True(state.GameIsForeground);
        Assert.False(state.OverlayIsForeground);
    }

    [Fact]
    public void TitleFilter_SelectsMatchingWindow()
    {
        _system.Processes["notepad"] = [7];
        _system.AddWindow(7, 1, "big other window", new WindowBounds(0, 0, 3000, 2000));
        _system.AddWindow(7, 2, "notes.txt - Notepad", new WindowBounds(10, 10, 800, 600));

        var state = NewTracker("notepad", "notes.txt").Poll(0);

        Assert.Equal(new WindowBounds(10, 10, 800, 600), state.ClientBounds);
    }

    [Fact]
    public void ReportsMinimizedAndOverlayFocus_AndFollowsMoves()
    {
        _system.Processes["swtor"] = [1];
        _system.AddWindow(1, 5, "SWTOR", new WindowBounds(0, 0, 1280, 720));
        var tracker = NewTracker();
        tracker.Poll(0);

        _system.Bounds[5] = new WindowBounds(100, 100, 1600, 900);
        _system.Minimized.Add(5);
        _system.Foreground = 77;
        var state = tracker.Poll(overlayHandle: 77);

        Assert.Equal(new WindowBounds(100, 100, 1600, 900), state.ClientBounds);
        Assert.True(state.Minimized);
        Assert.True(state.OverlayIsForeground);
        Assert.False(state.GameIsForeground);
        Assert.Equal(1, _system.ProcessScans);
    }

    [Fact]
    public void ClosedWindow_IsLost_ThenFoundAgainWhenGameRestarts()
    {
        _system.Processes["swtor"] = [1];
        _system.AddWindow(1, 5, "SWTOR", new WindowBounds(0, 0, 1280, 720));
        var tracker = NewTracker();
        Assert.True(tracker.Poll(0).Found);

        _system.CloseWindow(5);
        _system.Processes.Clear();
        _now += TimeSpan.FromSeconds(2);
        Assert.False(tracker.Poll(0).Found);

        _system.Processes["swtor"] = [2];
        _system.AddWindow(2, 6, "SWTOR", new WindowBounds(0, 0, 1920, 1080));
        _now += TimeSpan.FromSeconds(2);
        Assert.Equal(1920, tracker.Poll(0).ClientBounds.Width);
    }

    [Theory]
    [InlineData("swtor.exe", "swtor")]
    [InlineData(" SWTOR.EXE ", "SWTOR")]
    [InlineData("swtor", "swtor")]
    public void ProcessNameNormalization(string input, string expected)
    {
        Assert.Equal(expected, GameWindowTracker.NormalizeProcessName(input));
    }
}

public class OverlayVisibilityPolicyTests
{
    private static readonly WindowBounds Screen = new(0, 0, 1920, 1080);
    private static GameWindowState Focused => new(true, Screen, false, true, false);
    private static GameWindowState Unfocused => new(true, Screen, false, false, false);

    private readonly OverlayVisibilityPolicy _policy = new(TimeSpan.FromMilliseconds(250));

    [Fact]
    public void ShowsWhileGameOrOverlayHasFocus()
    {
        Assert.True(_policy.Update(Focused, TimeSpan.Zero));
        Assert.True(_policy.Update(Focused with { GameIsForeground = false, OverlayIsForeground = true }, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void FocusLoss_HidesAfterGracePeriod()
    {
        _policy.Update(Focused, TimeSpan.Zero);

        Assert.True(_policy.Update(Unfocused, TimeSpan.FromMilliseconds(100)));
        Assert.True(_policy.Update(Unfocused, TimeSpan.FromMilliseconds(300)));
        Assert.False(_policy.Update(Unfocused, TimeSpan.FromMilliseconds(400)));
    }

    [Fact]
    public void BriefFocusGap_DoesNotFlicker()
    {
        _policy.Update(Focused, TimeSpan.Zero);
        Assert.True(_policy.Update(Unfocused, TimeSpan.FromMilliseconds(100)));
        Assert.True(_policy.Update(Focused, TimeSpan.FromMilliseconds(200)));
        Assert.True(_policy.Update(Unfocused, TimeSpan.FromMilliseconds(300)));
        Assert.True(_policy.Update(Unfocused, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void NotFoundMinimizedOrEmpty_HideImmediately()
    {
        _policy.Update(Focused, TimeSpan.Zero);
        Assert.False(_policy.Update(GameWindowState.NotFound, TimeSpan.FromMilliseconds(1)));
        _policy.Update(Focused, TimeSpan.FromMilliseconds(2));
        Assert.False(_policy.Update(Focused with { Minimized = true }, TimeSpan.FromMilliseconds(3)));
        _policy.Update(Focused, TimeSpan.FromMilliseconds(4));
        Assert.False(_policy.Update(Focused with { ClientBounds = new WindowBounds(0, 0, 0, 0) }, TimeSpan.FromMilliseconds(5)));
    }

    [Fact]
    public void NeverShownWithoutFocus()
    {
        Assert.False(_policy.Update(Unfocused, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("10,20,1280,720", true, 10, 20, 1280, 720)]
    [InlineData(" -1920, 0, 1920, 1080 ", true, -1920, 0, 1920, 1080)]
    [InlineData("10,20,0,720", false, 0, 0, 0, 0)]
    [InlineData("10,20,1280", false, 0, 0, 0, 0)]
    [InlineData("a,b,c,d", false, 0, 0, 0, 0)]
    [InlineData(null, false, 0, 0, 0, 0)]
    public void OverlayRectParsing(string? text, bool ok, int x, int y, int w, int h)
    {
        Assert.Equal(ok, WindowBounds.TryParse(text, out var bounds));
        if (ok) Assert.Equal(new WindowBounds(x, y, w, h), bounds);
    }

    [Fact]
    public void FixedSource_IsAlwaysFoundAndFocused()
    {
        var state = new FixedWindowSource(new WindowBounds(1, 2, 3, 4)).Poll(0);
        Assert.True(state.Found);
        Assert.True(state.GameIsForeground);
        Assert.Equal(new WindowBounds(1, 2, 3, 4), state.ClientBounds);
    }
}
