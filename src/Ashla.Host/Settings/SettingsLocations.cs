using Ashla.SDK;

namespace Ashla.Host.Settings;

internal static class SettingsLocations
{
    internal static readonly string[] OlderSettingsFiles = ["plugins.json", "imgui.ini", "imgui-overlay.ini"];

    public static string DefaultSettingsDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Ashla");

    public static string LocalDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Ashla");

    public static void CopyOlderSettings(string oldDirectory, string settingsDirectory, ILogger log)
    {
        if (string.Equals(Path.GetFullPath(oldDirectory), Path.GetFullPath(settingsDirectory), StringComparison.OrdinalIgnoreCase))
            return;
        foreach (var name in OlderSettingsFiles)
        {
            var from = Path.Combine(oldDirectory, name);
            var to = Path.Combine(settingsDirectory, name);
            if (!File.Exists(from) || File.Exists(to)) continue;
            try
            {
                Directory.CreateDirectory(settingsDirectory);
                File.Copy(from, to);
                log.Info($"Moved {name} to {settingsDirectory}.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log.Warn($"Could not copy {from} to {to} ({error.Message}).");
            }
        }
    }
}
