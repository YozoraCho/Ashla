using System.Text.RegularExpressions;

namespace Ashla.Host.Launch;

internal sealed record SwtorInstallation(string Folder, bool IsSteam)
{
    public const string LauncherFileName = "launcher.exe";

    public string LauncherPath => Path.Combine(Folder, LauncherFileName);
}

internal interface IInstallEnvironment
{
    bool FileExists(string path);
    string? ReadAllText(string path);

    IEnumerable<string> ProgramFilesFolders { get; }

    string? SteamFolder { get; }
}

internal static partial class SwtorInstallFinder
{
    public const string SteamAppId = "1286830";
    private const string GameFolderName = "Star Wars - The Old Republic";

    private static readonly string[] StandaloneSubfolders =
    [
        Path.Combine("Electronic Arts", "BioWare", GameFolderName),
        Path.Combine("Electronic Arts", "BioWare", "SWTOR"),
    ];

    public static SwtorInstallation? Find(IInstallEnvironment environment)
    {
        foreach (var library in SteamLibraries(environment))
        {
            var folder = Path.Combine(library, "steamapps", "common", GameFolderName);
            if (environment.FileExists(Path.Combine(folder, SwtorInstallation.LauncherFileName)))
                return new SwtorInstallation(folder, IsSteam: true);
        }
        foreach (var programFiles in environment.ProgramFilesFolders)
        foreach (var subfolder in StandaloneSubfolders)
        {
            var folder = Path.Combine(programFiles, subfolder);
            if (environment.FileExists(Path.Combine(folder, SwtorInstallation.LauncherFileName)))
                return new SwtorInstallation(folder, IsSteam: false);
        }
        return null;
    }

    public static SwtorInstallation? FromUserPath(string? path, IInstallEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim().Trim('"').TrimEnd('\\', '/');
        int slash = path.LastIndexOfAny(['\\', '/']);
        var fileName = slash >= 0 ? path[(slash + 1)..] : path;
        var folder = string.Equals(fileName, SwtorInstallation.LauncherFileName, StringComparison.OrdinalIgnoreCase)
            ? (slash > 0 ? path[..slash] : null)
            : path;
        if (string.IsNullOrEmpty(folder) || !environment.FileExists(Path.Combine(folder, SwtorInstallation.LauncherFileName)))
            return null;
        bool steam = folder.Replace('\\', '/').Contains("/steamapps/common/", StringComparison.OrdinalIgnoreCase);
        return new SwtorInstallation(folder, steam);
    }

    internal static IEnumerable<string> SteamLibraries(IInstallEnvironment environment)
    {
        if (environment.SteamFolder is not { } steam) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Normalize(steam) };
        yield return steam;
        var vdf = environment.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"));
        if (vdf is null) yield break;
        foreach (var library in ParseLibraryFolders(vdf))
            if (seen.Add(Normalize(library))) yield return library;
    }

    internal static IEnumerable<string> ParseLibraryFolders(string vdf) =>
        LibraryPathRegex().Matches(vdf).Select(m => m.Groups["path"].Value.Replace(@"\\", @"\"));

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

    [GeneratedRegex("\"path\"\\s+\"(?<path>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryPathRegex();
}

internal sealed class SystemInstallEnvironment : IInstallEnvironment
{
    public bool FileExists(string path) => File.Exists(path);

    public string? ReadAllText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    public IEnumerable<string> ProgramFilesFolders
    {
        get
        {
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };
            return folders.Where(f => !string.IsNullOrEmpty(f)).Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }

    public string? SteamFolder
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return key?.GetValue("SteamPath") is string path && path.Length > 0 ? path.Replace('/', '\\') : null;
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return null;
            }
        }
    }
}
