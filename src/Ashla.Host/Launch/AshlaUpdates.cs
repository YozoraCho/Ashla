using System.Text.Json;
using Ashla.SDK;

namespace Ashla.Host.Launch;

internal sealed record AshlaRelease(string Version, string Page, string? Download, long Size, DateTimeOffset? Published, string? Sha256 = null)
{
    public string Target => Download ?? Page;

    public bool CanInstall => Download is not null && Sha256 is not null;
}

internal sealed class AshlaUpdateChecker(HttpClient http, ILogger log)
{
    public const string OfficialUrl = "https://api.github.com/repos/YozoraCho/Ashla/releases?per_page=30";
    private const long MaxBytes = 2 * 1024 * 1024;

    public static string Url { get; set; } = OfficialUrl;

    public async Task<AshlaRelease?> LatestAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                log.Warn($"Couldn't check for a new Ashla: GitHub answered {(int)response.StatusCode}.");
                return null;
            }
            var bytes = await HolocronClient.ReadLimitedAsync(response, MaxBytes, token);
            var release = Newest(bytes);
            if (release is null) log.Warn("Couldn't check for a new Ashla: GitHub listed no Ashla release.");
            return release;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            log.Warn($"Couldn't check for a new Ashla: {error.Message}");
            return null;
        }
    }

    public static AshlaRelease? Newer(AshlaRelease? release, string current) =>
        release is not null && PluginVersions.IsNewer(release.Version, current) ? release : null;

    internal static AshlaRelease? Newest(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            IEnumerable<JsonElement> items = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray(),
                JsonValueKind.Object => [root],
                _ => []
            };
            AshlaRelease? best = null;
            foreach (var item in items)
                if (Read(item) is { } release && (best is null || PluginVersions.IsNewer(release.Version, best.Version))) best = release;
            return best;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AshlaRelease? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
        var tag = Text(root, "tag_name")?.Trim();
        var page = Text(root, "html_url");
        if (tag is null || page is null || !Allowed(page)) return null;
        if (tag.Length < 2 || (tag[0] != 'v' && tag[0] != 'V') || !char.IsDigit(tag[1])) return null;
        var version = tag[1..];

        string? download = null;
        string? sha256 = null;
        long size = 0;
        if (Zip(root, version) is { } asset && Text(asset, "browser_download_url") is { } url && Allowed(url))
        {
            download = url;
            size = asset.TryGetProperty("size", out var value) && value.TryGetInt64(out var bytes) ? bytes : 0;
            if (Text(asset, "digest") is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
                digest.Length == 71 && digest[7..].All(Uri.IsHexDigit))
                sha256 = digest[7..].ToLowerInvariant();
        }
        DateTimeOffset? published = Text(root, "published_at") is { } when && DateTimeOffset.TryParse(when, out var date) ? date : null;
        return new AshlaRelease(version, page, download, size, published, sha256);
    }

    private static JsonElement? Zip(JsonElement release, string version)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var expected = $"Ashla-{version}-win-x64.zip";
        JsonElement? fallback = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (Text(asset, "name") is not { } name) continue;
            if (string.Equals(name, expected, StringComparison.OrdinalIgnoreCase)) return asset;
            if (fallback is null && name.StartsWith("Ashla-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)) fallback = asset;
        }
        return fallback;
    }

    private static bool Allowed(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && HolocronClient.IsAllowed(uri);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
