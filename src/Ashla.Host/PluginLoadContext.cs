using System.Reflection;
using System.Runtime.Loader;
using Ashla.SDK;

namespace Ashla.Host;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    internal static readonly IReadOnlySet<string> SharedAssemblyNames =
        new HashSet<string>(StringComparer.Ordinal) { "Ashla.SDK", "Hexa.NET.ImGui", "HexaGen.Runtime", "Ashla.UI" };

    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginAssemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(pluginAssemblyPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name && SharedAssemblyNames.Contains(name))
        {
            return name == typeof(IAshlaPlugin).Assembly.GetName().Name
                ? typeof(IAshlaPlugin).Assembly
                : Default.LoadFromAssemblyName(assemblyName);
        }

        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolved is null ? null : LoadFromAssemblyPath(resolved);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
