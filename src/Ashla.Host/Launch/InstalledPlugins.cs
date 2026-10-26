namespace Ashla.Host.Launch;

internal sealed record InstalledPlugin(string Id, string Version, string? Author, string? Description, string? Name = null,
    string? Problem = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}

internal static class InstalledPlugins
{
    public static IReadOnlyList<InstalledPlugin> Find(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot)) return [];
        var plugins = new List<InstalledPlugin>();
        foreach (var directory in Directory.EnumerateDirectories(pluginsRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(directory);
            var file = Path.Combine(directory, $"{id}.dll");
            if (!File.Exists(file)) continue;
            var check = PluginManifests.Check(directory, id);
            var manifest = check.Manifest ?? PluginManifests.TryRead(directory);
            var metadata = manifest is not null ? PluginMetadata.FromManifest(manifest) : PluginMetadata.ReadFromFile(file);
            plugins.Add(new InstalledPlugin(id, metadata.Version, metadata.Author, metadata.Description, manifest?.Name,
                check.Problem));
        }
        return plugins;
    }
}
