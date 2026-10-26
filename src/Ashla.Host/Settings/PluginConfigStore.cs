using Ashla.SDK;

namespace Ashla.Host.Settings;

internal sealed class PluginConfigStore : IPluginConfig
{
    public const string FolderName = "pluginConfigs";

    private readonly object _gate = new();
    private readonly ILogger _log;
    private readonly string _description;

    public PluginConfigStore(string settingsDirectory, string pluginId, ILogger log)
    {
        FilePath = Path.Combine(settingsDirectory, FolderName, SafeFileName(pluginId) + ".json");
        _log = log;
        _description = $"settings for plugin {pluginId}";
    }

    public string FilePath { get; }

    public T Load<T>() where T : class, new()
    {
        lock (_gate) return JsonFile.Load<T>(FilePath, _log, _description);
    }

    public bool Save<T>(T settings) where T : class
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate) return JsonFile.Save(FilePath, settings, _log, _description);
    }

    internal static string SafeFileName(string pluginId)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var name = new string([.. pluginId.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim(' ', '.');
        return name.Length == 0 ? "_" : name;
    }
}

internal sealed class InMemoryPluginConfig : IPluginConfig
{
    private readonly object _gate = new();
    private string? _json;

    public string FilePath => "(not saved)";

    public T Load<T>() where T : class, new()
    {
        lock (_gate)
            return _json is null ? new T() : System.Text.Json.JsonSerializer.Deserialize<T>(_json, JsonFile.NewOptions()) ?? new T();
    }

    public bool Save<T>(T settings) where T : class
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate) _json = System.Text.Json.JsonSerializer.Serialize(settings, JsonFile.NewOptions());
        return true;
    }
}
