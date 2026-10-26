using System.Security.Cryptography;
using System.Text.Json;
using Ashla.SDK;

namespace Ashla.Host;

internal sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public int ApiLevel { get; set; }
    public string? Repository { get; set; }
    public List<string> Tags { get; set; } = [];
    public Dictionary<string, string> Files { get; set; } = [];
}

internal sealed record ManifestCheck(PluginManifest? Manifest, string? Problem)
{
    public bool Ok => Problem is null && Manifest is not null;
}

internal static class PluginManifests
{
    public const string FileName = "manifest.json";
    public const int OldestSupportedApiLevel = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static PluginManifest? TryRead(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), Options);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ManifestCheck Check(string folder, string id)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
            return new(null, $"No {FileName} in its folder. Get the plugin again from where you found it, or rebuild it " +
                             "with Ashla's plugin build targets.");
        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), Options);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(null, $"{FileName} can't be read: {error.Message}");
        }
        if (manifest is null) return new(null, $"{FileName} is empty.");
        if (!string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase))
            return new(manifest, $"{FileName} belongs to \"{manifest.Id}\", but the plugin's folder is \"{id}\".");
        if (manifest.ApiLevel > AshlaApi.Level)
            return new(manifest, $"It needs a newer Ashla (plugin API level {manifest.ApiLevel}; this Ashla has {AshlaApi.Level}).");
        if (manifest.ApiLevel < OldestSupportedApiLevel)
            return new(manifest, $"It was made for an older Ashla (plugin API level {manifest.ApiLevel}) and needs an update " +
                                 "from its author.");
        if (FilesProblem(folder, manifest) is { } problem) return new(manifest, problem);
        return new(manifest, null);
    }

    private static string? FilesProblem(string folder, PluginManifest manifest)
    {
        if (manifest.Files.Count == 0) return $"{FileName} lists no files.";
        var root = Path.GetFullPath(folder);
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<string>();
        var missing = new List<string>();
        foreach (var (name, expected) in manifest.Files)
        {
            var normalized = name.Replace('\\', '/');
            var full = Path.GetFullPath(Path.Combine(root, normalized));
            if (Path.IsPathRooted(normalized) || normalized.Split('/').Contains("..") ||
                !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return $"{FileName} lists a file outside the plugin's folder: {name}";
            listed.Add(normalized);
            if (!File.Exists(full))
            {
                missing.Add(normalized);
                continue;
            }
            if (!string.Equals(Hash(full), expected, StringComparison.OrdinalIgnoreCase)) changed.Add(normalized);
        }

        var unlisted = Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(file => !listed.Contains(file))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parts = new List<string>();
        if (changed.Count > 0) parts.Add($"{Join(changed)} changed");
        if (missing.Count > 0) parts.Add($"{Join(missing)} {(missing.Count == 1 ? "is" : "are")} missing");
        if (unlisted.Count > 0) parts.Add($"{Join(unlisted)} {(unlisted.Count == 1 ? "isn't" : "aren't")} in {FileName}");
        return parts.Count == 0
            ? null
            : $"Its files don't match {FileName}: {string.Join("; ", parts)}. Get the plugin again from where you found it.";
    }

    public static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Join(List<string> files) => files.Count <= 3
        ? string.Join(", ", files)
        : $"{string.Join(", ", files.Take(3))} and {files.Count - 3} more";
}
