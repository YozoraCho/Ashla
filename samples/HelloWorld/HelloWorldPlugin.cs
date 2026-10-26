using Ashla.SDK;

namespace HelloWorld;

public sealed class HelloWorldPlugin : IAshlaPlugin
{
    private ILogger? _log;
    public string Name => "Hello World";

    public void Initialize(IPluginContext context)
    {
        _log = context.Log;
        _log.Info("[Hello World] Loaded from a separate DLL!");
        if (!context.GameState.TryGetLocalPlayer(out _))
            _log.Info("[Hello World] Local player state is unavailable in external mode (expected).");
    }

    public void Dispose() => _log?.Info("[Hello World] Unloading.");
}
