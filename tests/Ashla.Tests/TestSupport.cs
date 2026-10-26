using System.Collections.Concurrent;
using System.Reflection;
using Ashla.Host.Overlays;
using Ashla.SDK;

namespace Ashla.Tests;

internal sealed class TestLogger : ILogger
{
    public ConcurrentQueue<string> Lines { get; } = new();
    public IEnumerable<string> Errors => Lines.Where(l => l.StartsWith("ERROR", StringComparison.Ordinal));
    public IEnumerable<string> Warnings => Lines.Where(l => l.StartsWith("WARN", StringComparison.Ordinal));

    public void Info(string message) => Lines.Enqueue($"INFO {message}");
    public void Warn(string message) => Lines.Enqueue($"WARN {message}");
    public void Error(string message, Exception? exception = null) => Lines.Enqueue($"ERROR {message} {exception?.Message}");
}

internal sealed class FakeChrome : IPanelChrome
{
    private int _depth;
    public List<string> Calls { get; } = [];
    public bool ContentVisible { get; set; } = true;
    public HashSet<string> CloseOnNextBegin { get; } = [];
    public int MaxDepth { get; private set; }

    public bool Begin(PanelEntry panel, string title, ref bool open)
    {
        Calls.Add($"Begin:{panel.WindowId}:{title}");
        MaxDepth = Math.Max(MaxDepth, ++_depth);
        if (CloseOnNextBegin.Remove(panel.WindowId)) open = false;
        return ContentVisible;
    }

    public void End()
    {
        Calls.Add("End");
        _depth--;
    }

    public int Balance => _depth;
}

internal sealed class TestPanel(string id, string title = "Test") : IPluginPanel
{
    private int _draws;
    public string Id { get; } = id;
    public string Title { get; set; } = title;
    public Action? OnDraw { get; set; }
    public int Draws => Volatile.Read(ref _draws);

    public void Draw()
    {
        Interlocked.Increment(ref _draws);
        OnDraw?.Invoke();
    }
}

internal static class Plugins
{
    public static PluginInfo Info(string id) => new(id, id, "1.0.0", null, null);
}

internal static class Repo
{
    private static string Metadata(string key) =>
        typeof(Repo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

    public static string Root => Metadata("AshlaRepoRoot");
    public static string Configuration => Metadata("AshlaConfiguration");

    public static string PluginOutput(string name) => Path.Combine(Root, "samples", name, "bin", Configuration, "net10.0", $"{name}.dll");
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ashla-tests-" + Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
