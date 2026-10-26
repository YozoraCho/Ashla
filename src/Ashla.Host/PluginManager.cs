using Ashla.Host.Overlays;
using Ashla.Host.Settings;
using Ashla.SDK;

namespace Ashla.Host;

internal enum PluginStatus
{
    Loaded,
    Failed,
    Disabled,
    Unloaded
}

internal sealed record PluginRecord(
    string Id,
    string Name,
    string Version,
    string? Author,
    string? Description,
    string AssemblyPath,
    PluginStatus Status,
    string? Error,
    bool EnabledOnNextStart);

internal sealed class PluginManager(
    ILogger log,
    Func<string, ILogger> createPluginLogger,
    IEventBus events,
    IGameState gameState,
    PanelRegistry panels,
    PluginStateStore state,
    string? settingsDirectory = null,
    ToastCenter? toasts = null,
    IEncounterArchive? archive = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private bool _disposed;

    public int Count
    {
        get { lock (_gate) return _entries.Count(e => e.Status == PluginStatus.Loaded); }
    }

    public PluginStateStore State => state;

    public IReadOnlyList<PluginRecord> GetSnapshot()
    {
        lock (_gate)
            return [.. _entries.Select(e => new PluginRecord(
                e.Id, e.Name, e.Metadata.Version, e.Metadata.Author, e.Metadata.Description,
                e.AssemblyPath, e.Status, e.Error, !state.IsDisabled(e.Id)))];
    }

    public void LoadFrom(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            log.Info($"No plugins installed yet ({pluginsRoot} doesn't exist).");
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(pluginsRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(directory);
            var file = Path.Combine(directory, $"{id}.dll");
            if (!File.Exists(file)) continue;
            LoadOne(id, Path.GetFullPath(file));
        }
    }

    private void LoadOne(string id, string assemblyPath)
    {
        var folder = Path.GetDirectoryName(assemblyPath)!;
        var manifest = PluginManifests.TryRead(folder);
        var metadata = manifest is not null ? PluginMetadata.FromManifest(manifest) : PluginMetadata.ReadFromFile(assemblyPath);
        var displayName = string.IsNullOrWhiteSpace(manifest?.Name) ? id : manifest.Name;
        if (state.IsDisabled(id))
        {
            Add(new Entry(id, displayName, assemblyPath, metadata) { Status = PluginStatus.Disabled });
            log.Info($"Plugin '{id}' is disabled; skipping.");
            return;
        }

        var check = PluginManifests.Check(folder, id);
        if (!check.Ok)
        {
            log.Warn($"Plugin '{id}' wasn't loaded: {check.Problem}");
            toasts?.Show($"{displayName} wasn't loaded", check.Problem, ToastKind.Error);
            Add(new Entry(id, displayName, assemblyPath, metadata) { Status = PluginStatus.Failed, Error = check.Problem });
            return;
        }

        var loadContext = new PluginLoadContext(assemblyPath);
        Type pluginType;
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IAshlaPlugin).IsAssignableFrom(t))
                .ToArray();
            if (types.Length != 1)
                throw new InvalidOperationException("A plugin assembly must contain exactly one IAshlaPlugin implementation.");
            pluginType = types[0];
        }
        catch (Exception error)
        {
            log.Error($"Could not load {assemblyPath}", error);
            ToastLoadFailure(id);
            Add(new Entry(id, id, assemblyPath, metadata) { Status = PluginStatus.Failed, Error = error.Message });
            loadContext.Unload();
            return;
        }

        Activate(id, assemblyPath, metadata,
            () => (IAshlaPlugin?)Activator.CreateInstance(pluginType)
                  ?? throw new InvalidOperationException("Couldn't construct the plugin."),
            loadContext);
    }

    internal bool Activate(string id, string assemblyPath, PluginMetadata metadata,
        Func<IAshlaPlugin> create, PluginLoadContext? loadContext)
    {
        IAshlaPlugin? plugin = null;
        PluginScope? scope = null;
        string name = id;
        try
        {
            plugin = create();
            name = SafeName(plugin, id);
            var info = new PluginInfo(id, name, metadata.Version, metadata.Author, metadata.Description);
            var pluginLog = createPluginLogger(id);
            IPluginConfig config = settingsDirectory is null
                ? new InMemoryPluginConfig()
                : new PluginConfigStore(settingsDirectory, id, pluginLog);
            scope = new PluginScope(info, pluginLog, events, gameState, panels, config, toasts, archive);
            var missing = MissingCapabilities(plugin.GetType(), scope.Context.Capabilities);
            if (missing.Count > 0)
            {
                string needs = string.Join(", ", missing.Select(CapabilityName));
                log.Warn($"Plugin '{name}' needs {needs}, {(missing.Count == 1 ? "which isn't" : "which aren't")} " +
                         "available in this configuration; it was not loaded.");
                toasts?.Show($"{name} needs features that aren't available",
                    $"It needs {needs}. The Plugin Manager has the details.", ToastKind.Error);
                try { plugin.Dispose(); } catch (Exception e) { log.Error("Plugin cleanup failed", e); }
                scope.Close();
                loadContext?.Unload();
                Add(new Entry(id, name, assemblyPath, metadata)
                {
                    Status = PluginStatus.Failed, Error = $"Needs {needs}, not available in this configuration."
                });
                return false;
            }
            plugin.Initialize(scope.Context);
            Add(new Entry(id, name, assemblyPath, metadata)
            {
                Status = PluginStatus.Loaded, Instance = plugin, Scope = scope, LoadContext = loadContext
            });
            log.Info($"Loaded plugin: {name} {metadata.Version}");
            return true;
        }
        catch (Exception error)
        {
            log.Error($"Could not initialize plugin '{id}' ({assemblyPath})", error);
            try { plugin?.Dispose(); } catch (Exception e) { log.Error("Plugin cleanup failed", e); }
            scope?.Close();
            loadContext?.Unload();
            ToastLoadFailure(name);
            Add(new Entry(id, name, assemblyPath, metadata) { Status = PluginStatus.Failed, Error = error.Message });
            return false;
        }
    }

    private static IReadOnlyList<Type> MissingCapabilities(Type pluginType, IAshlaCapabilities available) =>
        [.. pluginType.GetCustomAttributes(typeof(RequiresCapabilityAttribute), inherit: false)
            .Cast<RequiresCapabilityAttribute>()
            .Select(a => a.Capability)
            .Distinct()
            .Where(c => !available.Available.Contains(c))];

    private static string CapabilityName(Type capability) =>
        capability.Name.Length > 1 && capability.Name[0] == 'I' && char.IsUpper(capability.Name[1])
            ? capability.Name[1..]
            : capability.Name;

    private void ToastLoadFailure(string name) =>
        toasts?.Show($"{name} couldn't load", "The Plugin Manager has the reason.", ToastKind.Error);

    private static string SafeName(IAshlaPlugin plugin, string fallback)
    {
        try { return string.IsNullOrWhiteSpace(plugin.Name) ? fallback : plugin.Name; }
        catch (Exception) { return fallback; }
    }

    private void Add(Entry entry)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _entries.Add(entry);
        }
    }

    public void Dispose()
    {
        Entry[] loaded;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            loaded = [.. _entries.Where(e => e.Status == PluginStatus.Loaded)];
        }

        foreach (var entry in loaded.Reverse())
        {
            try { entry.Instance?.Dispose(); }
            catch (Exception e) { log.Error($"Failed to dispose {entry.Name}", e); }
            entry.Scope?.Close();
            entry.LoadContext?.Unload();
            lock (_gate)
            {
                entry.Status = PluginStatus.Unloaded;
                entry.Instance = null;
                entry.Scope = null;
                entry.LoadContext = null;
            }
        }
    }

    private sealed class Entry(string id, string name, string assemblyPath, PluginMetadata metadata)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string AssemblyPath { get; } = assemblyPath;
        public PluginMetadata Metadata { get; } = metadata;
        public PluginStatus Status { get; set; }
        public string? Error { get; init; }
        public IAshlaPlugin? Instance { get; set; }
        public PluginScope? Scope { get; set; }
        public PluginLoadContext? LoadContext { get; set; }
    }
}
