using System.Runtime.InteropServices;
using Silk.NET.Core.Loader;

namespace Ashla.Rendering;

internal static class NativeLibraryPaths
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        if (PathResolver.Default is DefaultPathResolver resolver)
            resolver.Resolvers.Insert(0, ResolveFromRuntimesFolder);
    }

    internal static IEnumerable<string> ResolveFromRuntimesFolder(string libraryName)
    {
        if (string.IsNullOrEmpty(libraryName) || Path.IsPathRooted(libraryName)) yield break;

        foreach (var rid in CandidateRids())
        foreach (var name in NameVariants(libraryName))
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", name);
            if (File.Exists(candidate)) yield return candidate;
        }

        foreach (var name in NameVariants(libraryName))
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(candidate)) yield return candidate;
        }
    }

    internal static IEnumerable<string> NameVariants(string libraryName)
    {
        yield return libraryName;
        var name = libraryName;
        int so = name.IndexOf(".so.", StringComparison.Ordinal);
        while (so >= 0 && name.Length > so + 3)
        {
            int lastDot = name.LastIndexOf('.');
            if (lastDot <= so + 2 || !name[(lastDot + 1)..].All(char.IsAsciiDigit)) break;
            name = name[..lastDot];
            yield return name;
        }
    }

    private static IEnumerable<string> CandidateRids()
    {
        var current = RuntimeInformation.RuntimeIdentifier;
        yield return current;

        string? os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "osx"
            : null;
        if (os is null) yield break;
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var portable = $"{os}-{arch}";
        if (!string.Equals(portable, current, StringComparison.OrdinalIgnoreCase)) yield return portable;
        if (os == "osx") yield return "osx";
    }
}
