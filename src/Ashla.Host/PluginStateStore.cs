using System.Text.Json;
using Ashla.SDK;

namespace Ashla.Host;

internal sealed class PluginStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string? _filePath;
    private readonly ILogger _log;
    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);

    private PluginStateStore(string? filePath, ILogger log)
    {
        _filePath = filePath;
        _log = log;
    }

    public static PluginStateStore InMemory(ILogger log) => new(null, log);

    public static PluginStateStore Load(string filePath, ILogger log)
    {
        var store = new PluginStateStore(filePath, log);
        try
        {
            if (File.Exists(filePath))
            {
                var data = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(filePath), JsonOptions);
                foreach (var id in data?.Disabled ?? [])
                    if (!string.IsNullOrWhiteSpace(id)) store._disabled.Add(id);
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            log.Warn($"Could not read plugin state from {filePath} ({error.Message}); all plugins are enabled.");
        }
        return store;
    }

    public string? FilePath => _filePath;

    public bool IsDisabled(string pluginId)
    {
        lock (_gate) return _disabled.Contains(pluginId);
    }

    public void SetEnabled(string pluginId, bool enabled)
    {
        string[] snapshot;
        lock (_gate)
        {
            bool changed = enabled ? _disabled.Remove(pluginId) : _disabled.Add(pluginId);
            if (!changed) return;
            snapshot = [.. _disabled.Order(StringComparer.OrdinalIgnoreCase)];
        }
        Save(snapshot);
    }

    private void Save(string[] disabled)
    {
        if (_filePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new StateFile { Disabled = [.. disabled] }, JsonOptions));
            File.Move(temp, _filePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Could not save plugin state to {_filePath}", error);
        }
    }

    private sealed class StateFile
    {
        [System.Text.Json.Serialization.JsonPropertyName("disabled")]
        public List<string> Disabled { get; set; } = [];
    }
}
