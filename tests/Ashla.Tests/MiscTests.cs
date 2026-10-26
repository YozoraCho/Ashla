using Ashla.Host;
using Ashla.Rendering;
using Hexa.NET.ImGui;
using Silk.NET.Input;

namespace Ashla.Tests;

public class PluginStateStoreTests
{
    private readonly TestLogger _log = new();

    [Fact]
    public void SetEnabled_PersistsAcrossLoads()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "nested", "plugins.json");

        var store = PluginStateStore.Load(file, _log);
        Assert.False(store.IsDisabled("A"));
        store.SetEnabled("A", false);
        store.SetEnabled("B", false);
        store.SetEnabled("B", true);

        var reloaded = PluginStateStore.Load(file, _log);
        Assert.True(reloaded.IsDisabled("A"));
        Assert.True(reloaded.IsDisabled("a"));
        Assert.False(reloaded.IsDisabled("B"));
        Assert.Contains("\"disabled\"", File.ReadAllText(file));
    }

    [Fact]
    public void CorruptFile_EnablesEverything_WithWarning()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "plugins.json");
        File.WriteAllText(file, "{ not json");

        var store = PluginStateStore.Load(file, _log);

        Assert.False(store.IsDisabled("Anything"));
        Assert.Single(_log.Warnings);
    }
}

public class RenderingHelperTests
{
    [Theory]
    [InlineData(Key.A, ImGuiKey.A)]
    [InlineData(Key.Z, ImGuiKey.Z)]
    [InlineData(Key.Number0, ImGuiKey.Key0)]
    [InlineData(Key.Number9, ImGuiKey.Key9)]
    [InlineData(Key.F1, ImGuiKey.F1)]
    [InlineData(Key.F24, ImGuiKey.F24)]
    [InlineData(Key.Keypad7, ImGuiKey.Keypad7)]
    [InlineData(Key.ControlLeft, ImGuiKey.LeftCtrl)]
    [InlineData(Key.Enter, ImGuiKey.Enter)]
    [InlineData(Key.BackSlash, ImGuiKey.Backslash)]
    [InlineData(Key.F25, ImGuiKey.None)]
    [InlineData(Key.Unknown, ImGuiKey.None)]
    public void KeyMap_MapsSilkKeysToImGui(Key key, ImGuiKey expected)
    {
        Assert.Equal(expected, KeyMap.ToImGui(key));
    }

    [Theory]
    [InlineData("libglfw.so.3.3", new[] { "libglfw.so.3.3", "libglfw.so.3", "libglfw.so" })]
    [InlineData("glfw3.dll", new[] { "glfw3.dll" })]
    [InlineData("libglfw.3.dylib", new[] { "libglfw.3.dylib" })]
    public void NativeLibraryNameVariants_StripLinuxVersionSuffixes(string name, string[] expected)
    {
        Assert.Equal(expected, NativeLibraryPaths.NameVariants(name));
    }
}

public class AshlaUiTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(9999, "9,999")]
    [InlineData(12345, "12.3k")]
    [InlineData(844208, "844.2k")]
    [InlineData(1234567, "1.23M")]
    [InlineData(2500000000, "2.5B")]
    public void Compact_ShortensLargeNumbers(double value, string expected)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try { Assert.Equal(expected, Ashla.UI.AshlaDraw.Compact(value)); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void ColorHelpers()
    {
        var c = new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f);
        Assert.Equal(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 0.25f), Ashla.UI.AshlaColors.WithAlpha(c, 0.25f));
        Assert.Equal(new System.Numerics.Vector4(1f, 1f, 1f, 1f), Ashla.UI.AshlaColors.Shade(c, 1f));
        Assert.Equal(new System.Numerics.Vector4(0f, 0f, 0f, 1f), Ashla.UI.AshlaColors.Shade(c, -1f));
    }
}

public class LoggingTests
{
    [Fact]
    public void LogBuffer_KeepsMostRecentEntries()
    {
        var buffer = new LogBuffer(capacity: 3);
        long before = buffer.Version;
        for (int i = 1; i <= 5; i++) buffer.Add(new LogEntry(DateTime.Now, LogLevel.Info, "t", $"line {i}"));

        Assert.Equal(["line 3", "line 4", "line 5"], buffer.Snapshot().Select(e => e.Message));
        Assert.Equal(before + 5, buffer.Version);
    }

    [Fact]
    public void FileLog_WritesLines_AndDeletesOldLogs()
    {
        using var temp = new TempDirectory();
        var old = Path.Combine(temp.Path, "ashla-2020-01-01.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
        try
        {
            LogHub.StartFileLog(temp.Path);
            new HostLogger("Test").Warn("hello from the test");

            Assert.False(File.Exists(old));
            Assert.NotNull(LogHub.FilePath);
            LogHub.Stop();
            Assert.Contains("WARN  Test: hello from the test", File.ReadAllText(LogHub.FilePath!));
            Assert.Contains(LogHub.Recent.Snapshot(), e => e.Message == "hello from the test");
        }
        finally
        {
            LogHub.Stop();
        }
    }
}

public class CombatLogStatusTests
{
    [Fact]
    public async Task Watch_ReportsMissingFolder_ThenActiveFileAndLines()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "CombatLogs");
        var log = new TestLogger();
        var status = new CombatLogStatus();
        var existsChanges = new List<bool>();
        status.DirectoryExistsChanged += existsChanges.Add;
        using var cts = new CancellationTokenSource();

        var watch = Task.Run(() => CombatLogSource.WatchAsync(folder, new EventBus(log), log, cts.Token, status));
        await WaitFor(() => status.DirectoryExists == false);
        Assert.Equal(folder, status.Directory);
        Assert.Contains(log.Warnings, w => w.Contains("Combat Logging"));

        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "combat_2026-10-23_00_40_00_000000.txt");
        File.WriteAllText(file, "");
        await WaitFor(() => status.ActiveFile == file);
        File.AppendAllText(file, "[00:40:01.000] [@P#1|(0,0,0,0)|(1/1)] [] [] [Event {836045448945472}: EnterCombat {836045448945489}]\r\n");
        await WaitFor(() => status.LastLine is not null);

        cts.Cancel();
        await watch.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([false, true], existsChanges);
    }

    [Fact]
    public async Task Watch_SkipsTheLogThatWasThereAtStart_ButReadsANewOneFromTheStart()
    {
        using var temp = new TempDirectory();
        const string line = "[00:40:01.000] [@P#1|(0,0,0,0)|(1/1)] [] [] [Event {836045448945472}: EnterCombat {836045448945489}]\r\n";
        var old = Path.Combine(temp.Path, "combat_2026-10-23_00_40_00_000000.txt");
        File.WriteAllText(old, line);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-5));
        var log = new TestLogger();
        var bus = new EventBus(log);
        int seen = 0;
        using var subscription = bus.Subscribe<Ashla.SDK.CombatEvent>(_ => Interlocked.Increment(ref seen));
        var status = new CombatLogStatus();
        using var cts = new CancellationTokenSource();

        var watch = Task.Run(() => CombatLogSource.WatchAsync(temp.Path, bus, log, cts.Token, status));
        await WaitFor(() => status.ActiveFile == old);
        var fresh = Path.Combine(temp.Path, "combat_2026-10-23_01_00_00_000000.txt");
        File.WriteAllText(fresh, line + line.Replace("EnterCombat {836045448945489}", "ExitCombat {836045448945490}"));
        await WaitFor(() => Volatile.Read(ref seen) == 2);

        cts.Cancel();
        await watch.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(fresh, status.ActiveFile);
        Assert.Contains(log.Lines, l => l.Contains("reading from the start"));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(50);
        }
    }
}

public class IconFileTests
{
    private static string AshlaIcon => Path.Combine(Repo.Root, "src", "Ashla.Host", "Assets", "ashla.ico");

    [Fact]
    public void AshlaIcon_HasEverySize_DecodedToRgba()
    {
        var images = IconFile.Load(AshlaIcon);

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 128, 256], images.Select(i => i.Width));
        Assert.All(images, i =>
        {
            Assert.Equal(i.Width, i.Height);
            Assert.Equal(i.Width * i.Height * 4, i.Rgba.Length);
        });
        var large = images[^1];
        Assert.Equal(0, large.Rgba[3]);
        Assert.Contains(Enumerable.Range(0, large.Width * large.Height), p => large.Rgba[p * 4 + 3] == 255);
    }

    [Fact]
    public void MissingOrDamagedFiles_GiveNoImages()
    {
        Assert.Empty(IconFile.Load(Path.Combine(Path.GetTempPath(), "does-not-exist.ico")));
        Assert.Empty(IconFile.Parse([1, 2, 3]));
        Assert.Empty(IconFile.Parse([0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 0, 0, 200, 0, 0, 0]));
    }
}
