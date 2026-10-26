using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ashla.Host.Settings;
using Ashla.SDK;

namespace Ashla.Host;

internal sealed class RepoIndex
{
    public int Format { get; set; } = 1;
    public string? Name { get; set; }
    public List<RepoPlugin> Plugins { get; set; } = [];
}

internal sealed class RepoPlugin
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string Version { get; set; } = "";
    public int ApiLevel { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Source { get; set; }
    public string Download { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = [];
    public string? Changes { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}

internal sealed record PluginRepository(string Url, string Name, bool Official);

internal sealed record CatalogEntry(RepoPlugin Plugin, PluginRepository Repository)
{
    public bool Compatible => Plugin.ApiLevel >= PluginManifests.OldestSupportedApiLevel && Plugin.ApiLevel <= AshlaApi.Level;
}

internal sealed record RepositoryResult(PluginRepository Repository, int Count, string? Problem, bool FromCache);

internal sealed record Catalog(IReadOnlyList<CatalogEntry> Entries, IReadOnlyList<RepositoryResult> Repositories)
{
    public static Catalog Empty { get; } = new([], []);

    public CatalogEntry? Find(string id, string? repositoryUrl)
    {
        var matches = Entries.Where(e => string.Equals(e.Plugin.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.FirstOrDefault(e => repositoryUrl is not null && e.Repository.Url == repositoryUrl)
               ?? matches.FirstOrDefault(e => e.Repository.Official)
               ?? (repositoryUrl is null ? null : matches.FirstOrDefault());
    }
}

internal static class PluginVersions
{
    public static int Compare(string? a, string? b)
    {
        var left = Parse(a);
        var right = Parse(b);
        if (left is not null && right is not null) return left.CompareTo(right);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNewer(string? candidate, string? installed) => Compare(candidate, installed) > 0;

    private static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var core = text.Trim().TrimStart('v', 'V');
        int cut = core.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) core = core[..cut];
        if (!core.Contains('.')) core += ".0";
        return Version.TryParse(core, out var version) ? version : null;
    }
}

internal sealed class HolocronClient(HttpClient http, string cacheDirectory, ILogger log)
{
    public const string OfficialUrl = "https://raw.githubusercontent.com/YozoraCho/Holocron/main/repo.json";

    public static string Official { get; set; } = OfficialUrl;

    public static bool IsAllowed(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    public const int SupportedFormat = 1;
    private const long MaxIndexBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<PluginRepository> Repositories(PluginSettings settings) =>
        [new PluginRepository(Official, "Holocron", true),
         .. settings.Repositories
             .Where(url => !string.Equals(url, Official, StringComparison.OrdinalIgnoreCase))
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .Select(url => new PluginRepository(url, ShortName(url), false))];

    public static string ShortName(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) && uri.Segments.Length > 2
                ? $"{uri.Segments[1].Trim('/')}/{uri.Segments[2].Trim('/')}"
                : uri.Host
            : url;

    public static string? CheckRepositoryUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return "That isn't a web address.";
        if (!IsAllowed(uri)) return "Repository addresses must start with https://.";
        return null;
    }

    public async Task<Catalog> FetchAsync(IReadOnlyList<PluginRepository> repositories, CancellationToken token)
    {
        var tasks = repositories.Select(repository => FetchOneAsync(repository, token)).ToList();
        var results = await Task.WhenAll(tasks);
        var entries = new List<CatalogEntry>();
        var summaries = new List<RepositoryResult>();
        foreach (var (repository, index, problem, cached) in results)
        {
            var plugins = index?.Plugins.Where(IsUsable).ToList() ?? [];
            entries.AddRange(plugins.Select(p => new CatalogEntry(p, repository)));
            summaries.Add(new RepositoryResult(repository, plugins.Count, problem, cached));
        }
        return new Catalog(entries, summaries);
    }

    private async Task<(PluginRepository, RepoIndex?, string?, bool)> FetchOneAsync(PluginRepository repository,
        CancellationToken token)
    {
        try
        {
            if (CheckRepositoryUrl(repository.Url) is { } bad) return (repository, null, bad, false);
            using var response = await http.GetAsync(repository.Url, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                return Cached(repository, $"{repository.Name} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            var bytes = await ReadLimitedAsync(response, MaxIndexBytes, token);
            var index = Parse(bytes);
            if (index is null) return Cached(repository, $"{repository.Name}'s plugin list can't be read.");
            if (index.Format > SupportedFormat)
                return (repository, null, $"{repository.Name} needs a newer Ashla to read its plugin list.", false);
            SaveCache(repository, bytes);
            return (repository, index, null, false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or InvalidDataException)
        {
            log.Warn($"Couldn't reach {repository.Url}: {error.Message}");
            return Cached(repository, $"Couldn't reach {repository.Name}.");
        }
    }

    private (PluginRepository, RepoIndex?, string?, bool) Cached(PluginRepository repository, string problem)
    {
        var path = CachePath(repository);
        try
        {
            if (File.Exists(path) && Parse(File.ReadAllBytes(path)) is { } index)
                return (repository, index, $"{problem} Showing the list from {File.GetLastWriteTime(path):g}.", true);
        }
        catch (IOException)
        {
        }
        return (repository, null, problem, false);
    }

    private static RepoIndex? Parse(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<RepoIndex>(bytes, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsUsable(RepoPlugin plugin) =>
        PluginInstaller.IsValidId(plugin.Id) && !string.IsNullOrWhiteSpace(plugin.Version) &&
        !string.IsNullOrWhiteSpace(plugin.Download) && plugin.Sha256.Length == 64;

    private void SaveCache(PluginRepository repository, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            File.WriteAllBytes(CachePath(repository), bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Couldn't keep a copy of {repository.Name}'s plugin list: {error.Message}");
        }
    }

    private string CachePath(PluginRepository repository) =>
        Path.Combine(cacheDirectory, Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(repository.Url)))[..16] + ".json");

    internal static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, long limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The download is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (memory.Length + read > limit) throw new InvalidDataException("The download is too large.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }
}

internal sealed class InstalledSource
{
    public string Repository { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime Installed { get; set; }
}

internal sealed class InstalledSourceFile
{
    public Dictionary<string, InstalledSource> Plugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class InstalledSources(string path, ILogger log)
{
    private readonly object _gate = new();
    private InstalledSourceFile? _file;

    public InstalledSource? Get(string id)
    {
        lock (_gate) return Load().Plugins.GetValueOrDefault(id);
    }

    public void Set(string id, InstalledSource source)
    {
        lock (_gate)
        {
            Load().Plugins[id] = source;
            JsonFile.Save(path, _file!, log, "the list of installed plugins");
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            if (Load().Plugins.Remove(id)) JsonFile.Save(path, _file!, log, "the list of installed plugins");
        }
    }

    private InstalledSourceFile Load()
    {
        if (_file is not null) return _file;
        _file = JsonFile.Load<InstalledSourceFile>(path, log, "the list of installed plugins");
        _file.Plugins = new Dictionary<string, InstalledSource>(_file.Plugins, StringComparer.OrdinalIgnoreCase);
        return _file;
    }
}

internal sealed record InstallResult(bool Ok, string Message);

internal sealed partial class PluginInstaller(HttpClient http, string pluginsDirectory, InstalledSources sources, ILogger log)
{
    public const long MaxDownloadBytes = 50L * 1024 * 1024;
    public const long MaxUnpackedBytes = 200L * 1024 * 1024;
    public const int MaxFiles = 500;

    public static bool IsValidId(string id) => !string.IsNullOrWhiteSpace(id) && IdPattern().IsMatch(id);

    public async Task<InstallResult> InstallAsync(CatalogEntry entry, CancellationToken token = default)
    {
        var plugin = entry.Plugin;
        string name = plugin.DisplayName;
        if (!IsValidId(plugin.Id)) return Fail($"{name} has an id Ashla can't use as a folder name.");
        if (!entry.Compatible)
            return Fail(plugin.ApiLevel > AshlaApi.Level
                ? $"{name} {plugin.Version} needs a newer Ashla."
                : $"{name} {plugin.Version} was made for an older Ashla.");
        if (!Uri.TryCreate(plugin.Download, UriKind.Absolute, out var download) || !HolocronClient.IsAllowed(download))
            return Fail($"{name}'s download address isn't https.");

        Directory.CreateDirectory(pluginsDirectory);
        string staging = Path.Combine(pluginsDirectory, $".installing-{plugin.Id}-{Guid.NewGuid():N}");
        try
        {
            byte[] zip;
            using (var response = await http.GetAsync(download, HttpCompletionOption.ResponseHeadersRead, token))
            {
                if (!response.IsSuccessStatusCode)
                    return Fail($"Downloading {name} failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
                zip = await HolocronClient.ReadLimitedAsync(response, MaxDownloadBytes, token);
            }
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(zip)), plugin.Sha256, StringComparison.OrdinalIgnoreCase))
                return Fail($"{name}'s download doesn't match {entry.Repository.Name}'s checksum, so it wasn't installed.");

            if (Extract(zip, staging) is { } unpackProblem) return Fail($"{name} couldn't be unpacked: {unpackProblem}");
            string folder = SingleFolder(staging);
            var check = PluginManifests.Check(folder, plugin.Id);
            if (!check.Ok) return Fail($"{name} wasn't installed: {check.Problem}");
            var manifest = check.Manifest!;
            if (PluginVersions.Compare(manifest.Version, plugin.Version) != 0)
                return Fail($"{name}'s download is version {manifest.Version}, but {entry.Repository.Name} lists {plugin.Version}.");
            if (plugin.Files.Count > 0 && !SameFiles(manifest.Files, plugin.Files))
                return Fail($"{name}'s files don't match the hashes {entry.Repository.Name} published, so it wasn't installed.");
            if (!File.Exists(Path.Combine(folder, plugin.Id + ".dll")))
                return Fail($"{name}'s download has no {plugin.Id}.dll.");

            string target = Path.Combine(pluginsDirectory, plugin.Id);
            string? previous = null;
            if (Directory.Exists(target))
            {
                previous = Path.Combine(pluginsDirectory, $".replaced-{plugin.Id}-{Guid.NewGuid():N}");
                Directory.Move(target, previous);
            }
            try
            {
                Directory.Move(folder, target);
            }
            catch
            {
                if (previous is not null) Directory.Move(previous, target);
                throw;
            }
            if (previous is not null) TryDelete(previous);
            sources.Set(plugin.Id, new InstalledSource
            {
                Repository = entry.Repository.Url, Version = plugin.Version, Installed = DateTime.Now
            });
            log.Info($"Installed {plugin.Id} {plugin.Version} from {entry.Repository.Url}.");
            return new InstallResult(true, $"{name} {plugin.Version} is installed.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Fail($"Installing {name} was cancelled.");
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException
                                          or InvalidDataException or UnauthorizedAccessException)
        {
            log.Warn($"Installing {plugin.Id} failed: {error.Message}");
            return Fail($"Installing {name} failed: {error.Message}");
        }
        finally
        {
            TryDelete(staging);
        }

        InstallResult Fail(string message)
        {
            log.Warn(message);
            return new InstallResult(false, message);
        }
    }

    public InstallResult Remove(string id, string name)
    {
        if (!IsValidId(id)) return new InstallResult(false, "That plugin can't be removed.");
        string target = Path.Combine(pluginsDirectory, id);
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            sources.Remove(id);
            log.Info($"Removed plugin {id}.");
            return new InstallResult(true, $"{name} was removed.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new InstallResult(false, $"{name} couldn't be removed: {error.Message}");
        }
    }

    public void CleanUp()
    {
        if (!Directory.Exists(pluginsDirectory)) return;
        foreach (var folder in Directory.EnumerateDirectories(pluginsDirectory, ".*"))
            if (Path.GetFileName(folder).StartsWith(".installing-", StringComparison.Ordinal) ||
                Path.GetFileName(folder).StartsWith(".replaced-", StringComparison.Ordinal))
                TryDelete(folder);
    }

    private static string? Extract(byte[] zip, string destination)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        if (archive.Entries.Count > MaxFiles) return "it has too many files.";
        long total = 0;
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var item in archive.Entries)
        {
            var full = Path.GetFullPath(Path.Combine(destination, item.FullName.Replace('\\', '/')));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "it contains a path outside its folder.";
            if (string.IsNullOrEmpty(item.Name))
            {
                Directory.CreateDirectory(full);
                continue;
            }
            total += item.Length;
            if (total > MaxUnpackedBytes) return "it's too large when unpacked.";
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            item.ExtractToFile(full, overwrite: false);
        }
        return null;
    }

    private static string SingleFolder(string staging)
    {
        var directories = Directory.GetDirectories(staging);
        return Directory.GetFiles(staging).Length == 0 && directories.Length == 1 ? directories[0] : staging;
    }

    private static bool SameFiles(Dictionary<string, string> manifest, Dictionary<string, string> published)
    {
        static Dictionary<string, string> Normalize(Dictionary<string, string> files) =>
            files.ToDictionary(p => p.Key.Replace('\\', '/'), p => p.Value.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
        var a = Normalize(manifest);
        var b = Normalize(published);
        return a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var hash) && hash == p.Value);
    }

    private void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Couldn't clean up {folder}: {error.Message}");
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex IdPattern();
}

internal sealed record PluginUpdate(string Id, string Name, string Installed, CatalogEntry Entry);

internal static class PluginUpdates
{
    public static IReadOnlyList<PluginUpdate> Find(IReadOnlyList<Launch.InstalledPlugin> installed, Catalog catalog,
        InstalledSources sources) =>
        [.. installed
            .Select(plugin => (plugin, entry: catalog.Find(plugin.Id, sources.Get(plugin.Id)?.Repository)))
            .Where(x => x.entry is { Compatible: true } && PluginVersions.IsNewer(x.entry.Plugin.Version, x.plugin.Version))
            .Select(x => new PluginUpdate(x.plugin.Id, x.plugin.DisplayName, x.plugin.Version, x.entry!))];
}
