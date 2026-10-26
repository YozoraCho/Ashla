using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashla.SDK;

namespace Ashla.Host.Settings;

internal static class JsonFile
{
    public static JsonSerializerOptions NewOptions(UnloadSafeTypeResolver? resolver = null) => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = resolver ?? new UnloadSafeTypeResolver()
    };

    public static T Load<T>(string path, ILogger log, string description) where T : class, new()
    {
        try
        {
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), NewOptions()) ?? new T();
        }
        catch (JsonException error)
        {
            var kept = KeepBadCopy(path);
            log.Warn($"Could not read {description} ({error.Message}); using defaults." +
                     (kept is null ? "" : $" The unreadable file was kept as {kept}."));
            return new T();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            log.Warn($"Could not read {description} ({error.Message}); using defaults.");
            return new T();
        }
    }

    public static bool Save<T>(string path, T value, ILogger log, string description) where T : class
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(value, NewOptions());
        }
        catch (Exception error) when (error is NotSupportedException or JsonException or InvalidOperationException)
        {
            log.Error($"Could not save {description}: the settings type can't be written as JSON", error);
            return false;
        }

        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Error($"Could not save {description} to {path}", error);
            try { File.Delete(temp); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    private static string? KeepBadCopy(string path)
    {
        try
        {
            var bad = path + ".bad";
            File.Copy(path, bad, overwrite: true);
            return bad;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
