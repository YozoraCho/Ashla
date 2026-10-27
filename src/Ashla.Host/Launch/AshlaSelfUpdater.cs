using System.IO.Compression;
using System.Security.Cryptography;
using Ashla.SDK;

namespace Ashla.Host.Launch;

internal sealed record UpdateResult(bool Ok, string Message, string? NewExe = null);

internal sealed class AshlaSelfUpdater(HttpClient http, string appFolder, ILogger log)
{
    public const long MaxDownloadBytes = 300L * 1024 * 1024;
    public const long MaxUnpackedBytes = 600L * 1024 * 1024;
    public const int MaxFiles = 200;
    public const string ExeName = "Ashla.exe";
    private const string StagingPrefix = ".update-new-";
    private const string OldPrefix = ".update-old-";
    private static readonly string[] Kept = ["plugins"];

    public string AppFolder { get; } = Path.GetFullPath(appFolder);

    public static bool CanUpdateItself(string appFolder) =>
        AshlaBuild.IsPlayerBuild && OperatingSystem.IsWindows() &&
        Environment.ProcessPath is { } exe &&
        string.Equals(Path.GetFileName(exe), ExeName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFullPath(Path.GetDirectoryName(exe)!), Path.GetFullPath(appFolder).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public async Task<UpdateResult> UpdateAsync(AshlaRelease release, IProgress<string>? progress, CancellationToken token)
    {
        if (release.Download is not { } url || release.Sha256 is not { } expected)
            return new UpdateResult(false, "This release can't be installed automatically. Use Download instead.");
        string? staging = null;
        try
        {
            string size = release.Size > 0 ? $" ({release.Size / (1024 * 1024.0):0.#} MB)" : "";
            progress?.Report($"Downloading Ashla {release.Version}{size}...");
            byte[] zip;
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                if (!response.IsSuccessStatusCode) return new UpdateResult(false, $"The download failed: GitHub answered {(int)response.StatusCode}.");
                zip = await HolocronClient.ReadLimitedAsync(response, MaxDownloadBytes, token);
            }

            progress?.Report("Checking the download...");
            var actual = Convert.ToHexStringLower(SHA256.HashData(zip));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return new UpdateResult(false, "The download doesn't match the checksum GitHub published for it, so it wasn't installed.");

            staging = Path.Combine(AppFolder, StagingPrefix + Guid.NewGuid().ToString("N"));
            if (Extract(zip, staging) is { } problem) return new UpdateResult(false, $"The update wasn't installed: {problem}");

            progress?.Report("Installing...");
            var applied = Apply(staging);
            if (applied.Ok) log.Info($"Updated to Ashla {release.Version}.");
            return applied;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log.Warn($"Updating Ashla failed: {error.Message}");
            return new UpdateResult(false, $"The update failed: {error.Message}");
        }
        finally
        {
            if (staging is not null) TryDelete(staging);
        }
    }

    internal string? Extract(byte[] zip, string destination)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        if (archive.Entries.Count > MaxFiles) return "it has too many files.";
        var files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        string prefix = CommonFolder(files.Select(e => e.FullName.Replace('\\', '/')));
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        long total = 0;
        bool hasExe = false;
        Directory.CreateDirectory(destination);
        foreach (var entry in files)
        {
            var relative = entry.FullName.Replace('\\', '/')[prefix.Length..];
            if (relative.Length == 0) continue;
            var first = relative.Split('/')[0];
            if (first.StartsWith('.') || Kept.Contains(first, StringComparer.OrdinalIgnoreCase)) continue;
            var full = Path.GetFullPath(Path.Combine(destination, relative));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "it contains a path outside its folder.";
            total += entry.Length;
            if (total > MaxUnpackedBytes) return "it's too large once unpacked.";
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
            if (string.Equals(relative, ExeName, StringComparison.OrdinalIgnoreCase)) hasExe = true;
        }
        return hasExe ? null : $"it has no {ExeName}.";
    }

    private static string CommonFolder(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Count == 0 || list.Any(n => !n.Contains('/'))) return "";
        var first = list[0][..(list[0].IndexOf('/') + 1)];
        return list.All(n => n.StartsWith(first, StringComparison.OrdinalIgnoreCase)) &&
               list.Any(n => string.Equals(n[first.Length..], ExeName, StringComparison.OrdinalIgnoreCase))
            ? first
            : "";
    }

    internal UpdateResult Apply(string staging)
    {
        string old = Path.Combine(AppFolder, OldPrefix + Guid.NewGuid().ToString("N"));
        var replaced = new List<(string Target, string Backup)>();
        var added = new List<string>();
        var files = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(staging, f))
            .OrderBy(f => string.Equals(f, ExeName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        try
        {
            foreach (var relative in files)
            {
                var target = Path.Combine(AppFolder, relative);
                if (File.Exists(target))
                {
                    var backup = Path.Combine(old, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Move(target, backup);
                    replaced.Add((target, backup));
                }
                else
                {
                    added.Add(target);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(Path.Combine(staging, relative), target);
            }
            return new UpdateResult(true, "Updated.", Path.Combine(AppFolder, ExeName));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Installing the update failed, putting the old files back: {error.Message}");
            foreach (var target in added) TryDeleteFile(target);
            foreach (var (target, backup) in replaced)
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(backup, target);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                {
                    log.Error($"Couldn't put {Path.GetFileName(target)} back. It's in {old}.", restore);
                }
            }
            return new UpdateResult(false, $"The update couldn't be installed, so Ashla was left as it was: {error.Message}");
        }
    }

    public void CleanUp()
    {
        if (!Directory.Exists(AppFolder)) return;
        foreach (var folder in Directory.EnumerateDirectories(AppFolder, ".update-*"))
            TryDelete(folder);
    }

    private static void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFile(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
