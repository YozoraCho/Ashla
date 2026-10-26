namespace Ashla.Host.Overlay;

internal readonly record struct WindowBounds(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static bool TryParse(string? text, out WindowBounds bounds)
    {
        bounds = default;
        var parts = text?.Split(',', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 4 }) return false;
        var values = new int[4];
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                return false;
        bounds = new WindowBounds(values[0], values[1], values[2], values[3]);
        return !bounds.IsEmpty;
    }
}

internal sealed record GameWindowState(
    bool Found,
    WindowBounds ClientBounds,
    bool Minimized,
    bool GameIsForeground,
    bool OverlayIsForeground)
{
    public static readonly GameWindowState NotFound = new(false, default, false, false, false);
}

internal interface IGameWindowSource
{
    string Description { get; }

    GameWindowState Poll(nint overlayHandle);
}

internal readonly record struct TopLevelWindow(nint Handle, string Title);

internal interface IWindowSystem
{
    IReadOnlyList<int> FindProcessIds(string processName);

    IReadOnlyList<TopLevelWindow> GetTopLevelWindows(int processId);

    bool IsWindow(nint handle);
    bool IsMinimized(nint handle);

    bool TryGetClientBounds(nint handle, out WindowBounds bounds);

    nint GetForegroundWindow();
}

internal sealed class GameWindowTracker(
    IWindowSystem windows,
    string processName,
    string? titleContains,
    Func<TimeSpan> clock,
    TimeSpan? rescanInterval = null) : IGameWindowSource
{
    private readonly string _processName = NormalizeProcessName(processName);
    private readonly TimeSpan _rescanInterval = rescanInterval ?? TimeSpan.FromSeconds(1);
    private TimeSpan? _lastScan;
    private nint _handle;

    public string Description => titleContains is null
        ? $"{_processName}.exe"
        : $"{_processName}.exe (title contains \"{titleContains}\")";

    public GameWindowState Poll(nint overlayHandle)
    {
        if (_handle != 0 && !windows.IsWindow(_handle)) _handle = 0;

        if (_handle == 0)
        {
            var now = clock();
            if (_lastScan is { } last && now - last < _rescanInterval) return GameWindowState.NotFound;
            _lastScan = now;
            _handle = FindGameWindow();
            if (_handle == 0) return GameWindowState.NotFound;
        }

        if (!windows.TryGetClientBounds(_handle, out var bounds)) return GameWindowState.NotFound;
        var foreground = windows.GetForegroundWindow();
        return new GameWindowState(
            Found: true,
            ClientBounds: bounds,
            Minimized: windows.IsMinimized(_handle),
            GameIsForeground: foreground != 0 && foreground == _handle,
            OverlayIsForeground: foreground != 0 && foreground == overlayHandle);
    }

    private nint FindGameWindow()
    {
        nint best = 0;
        long bestArea = -1;
        foreach (var processId in windows.FindProcessIds(_processName))
        foreach (var window in windows.GetTopLevelWindows(processId))
        {
            if (titleContains is not null && !window.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                continue;
            long area = windows.TryGetClientBounds(window.Handle, out var b) ? (long)b.Width * b.Height : 0;
            if (area > bestArea)
            {
                best = window.Handle;
                bestArea = area;
            }
        }
        return best;
    }

    internal static string NormalizeProcessName(string name)
    {
        name = name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}

internal sealed class FixedWindowSource(WindowBounds bounds) : IGameWindowSource
{
    public string Description => $"fixed rectangle {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}";

    public GameWindowState Poll(nint overlayHandle) =>
        new(Found: true, ClientBounds: bounds, Minimized: false, GameIsForeground: true, OverlayIsForeground: false);
}

internal sealed class OverlayVisibilityPolicy(TimeSpan? hideDelay = null)
{
    private readonly TimeSpan _hideDelay = hideDelay ?? TimeSpan.FromMilliseconds(250);
    private TimeSpan? _hideRequestedAt;
    private bool _visible;

    public bool Update(GameWindowState state, TimeSpan now)
    {
        bool hardHide = !state.Found || state.Minimized || state.ClientBounds.IsEmpty;
        bool focused = state.GameIsForeground || state.OverlayIsForeground;

        if (!hardHide && focused)
        {
            _hideRequestedAt = null;
            _visible = true;
        }
        else if (hardHide)
        {
            _hideRequestedAt = null;
            _visible = false;
        }
        else if (_visible)
        {
            _hideRequestedAt ??= now;
            if (now - _hideRequestedAt.Value >= _hideDelay)
            {
                _visible = false;
                _hideRequestedAt = null;
            }
        }
        return _visible;
    }
}
