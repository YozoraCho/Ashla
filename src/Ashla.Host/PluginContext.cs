using System.Diagnostics;
using Ashla.Host.Overlays;
using Ashla.SDK;

namespace Ashla.Host;

internal sealed class PluginContext(
    PluginInfo plugin, ILogger log, IEventBus events, IGameState gameState, IOverlayRegistry overlays,
    IPluginConfig config, IToasts toasts, IEncounterArchive encounters, IAshlaCapabilities capabilities)
    : IPluginContext
{
    public PluginInfo Plugin { get; } = plugin;
    public ILogger Log { get; } = log;
    public IEventBus Events { get; } = events;
    public IGameState GameState { get; } = gameState;
    public IOverlayRegistry Overlays { get; } = overlays;
    public IPluginConfig Config { get; } = config;
    public IToasts Toasts { get; } = toasts;
    public IEncounterArchive Encounters { get; } = encounters;
    public IAshlaCapabilities Capabilities { get; } = capabilities;
}

internal sealed class CapabilitySet : IAshlaCapabilities
{
    private readonly Dictionary<Type, object> _items;

    public CapabilitySet(IEnumerable<KeyValuePair<Type, object>> items) =>
        _items = new Dictionary<Type, object>(items);

    public bool Has<T>() where T : class => _items.ContainsKey(typeof(T));

    public T? Get<T>() where T : class => _items.TryGetValue(typeof(T), out var value) ? (T)value : null;

    public IReadOnlyCollection<Type> Available => _items.Keys;
}

internal sealed class PluginScope
{
    private readonly ScopedEventBus _events;
    private readonly PluginOverlayRegistry _overlays;
    private readonly PluginEncounterArchive _encounters;
    private readonly ILogger _log;

    public PluginScope(PluginInfo info, ILogger log, IEventBus events, IGameState gameState, PanelRegistry panels,
        IPluginConfig config, ToastCenter? toasts = null, IEncounterArchive? archive = null)
    {
        _log = log;
        _events = new ScopedEventBus(events);
        _overlays = new PluginOverlayRegistry(panels, info);
        _encounters = new PluginEncounterArchive(archive);
        var pluginToasts = new PluginToasts(toasts, info.Name, log);
        var capabilities = new List<KeyValuePair<Type, object>>
        {
            new(typeof(IEventBus), _events),
            new(typeof(IGameState), gameState),
            new(typeof(IToasts), pluginToasts),
        };
        if (panels.IsAvailable) capabilities.Add(new(typeof(IOverlayRegistry), _overlays));
        if (_encounters.IsAvailable) capabilities.Add(new(typeof(IEncounterArchive), _encounters));
        Context = new PluginContext(info, log, _events, gameState, _overlays, config, pluginToasts, _encounters,
            new CapabilitySet(capabilities));
    }

    public IPluginContext Context { get; }

    public void Close()
    {
        int panels = _overlays.Close();
        int subscriptions = _events.Close() + _encounters.Close();
        if (panels > 0) _log.Warn($"Plugin left {panels} panel(s) registered; the host removed them.");
        if (subscriptions > 0) _log.Warn($"Plugin left {subscriptions} event subscription(s); the host removed them.");
    }
}

internal sealed record PluginMetadata(string Version, string? Author, string? Description)
{
    public static PluginMetadata FromManifest(PluginManifest manifest) => new(
        string.IsNullOrWhiteSpace(manifest.Version) ? "unknown" : manifest.Version,
        string.IsNullOrWhiteSpace(manifest.Author) ? null : manifest.Author,
        string.IsNullOrWhiteSpace(manifest.Description) ? null : manifest.Description);

    public static PluginMetadata ReadFromFile(string assemblyPath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(assemblyPath);
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            var version = info.ProductVersion ?? info.FileVersion ?? "unknown";
            var plus = version.IndexOf('+');
            if (plus > 0) version = version[..plus];
            var author = string.IsNullOrWhiteSpace(info.CompanyName) || info.CompanyName == assemblyName
                ? null
                : info.CompanyName;
            var description = string.IsNullOrWhiteSpace(info.Comments) ? null : info.Comments;
            return new PluginMetadata(version, author, description);
        }
        catch (Exception)
        {
            return new PluginMetadata("unknown", null, null);
        }
    }
}
