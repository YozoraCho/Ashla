using Ashla.SDK;

namespace CombatCounter;

public sealed class CombatCounterPlugin : IAshlaPlugin
{
    private IDisposable? _subscription;
    private ILogger? _log;
    private int _received;

    public string Name => "Combat Line Counter";

    public void Initialize(IPluginContext context)
    {
        _log = context.Log;
        if (!context.GameState.Capabilities.HasFlag(GameCapabilities.CombatLog))
        {
            _log.Warn("[Combat Counter] Combat logs not supported by this provider.");
            return;
        }
        _subscription = context.Events.Subscribe<CombatLogLine>(line =>
        {
            var count = Interlocked.Increment(ref _received);
            if (count <= 5 || count % 100 == 0)
                _log?.Info($"[Combat Counter] {count} lines received; latest: {line.RawText}");
        });
        _log.Info("[Combat Counter] Subscribed to raw combat-log events.");
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _log?.Info($"[Combat Counter] Total lines: {_received}");
    }
}
