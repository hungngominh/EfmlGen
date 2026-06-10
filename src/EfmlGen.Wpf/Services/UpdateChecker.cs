using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EfmlGen.Wpf.Services;

/// <summary>Result of an update check. Non-null only when a newer release exists.</summary>
public sealed record UpdateInfo(string CurrentVersion, string LatestVersion, string? InstallerUrl, string ReleaseUrl);

/// <summary>
/// Queries the GitHub Releases API for the latest EfmlGen release and reports whether
/// it is newer than the running build. All failures (offline, rate-limited, malformed)
/// surface as a null result rather than throwing — the caller treats "no update" and
/// "couldn't check" the same way.
/// </summary>
public static class UpdateChecker
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/hungngominh/EfmlGen/releases/latest";

    public static async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EfmlGen-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await http.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(tag))
                return null;

            var latest = tag.TrimStart('v', 'V');
            if (!IsNewer(latest, currentVersion))
                return null;

            var releaseUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            var installerUrl = FindInstallerAsset(root);

            return new UpdateInfo(currentVersion, latest, installerUrl, releaseUrl);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindInstallerAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name != null
                && name.StartsWith("EfmlGen-Setup", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            }
        }
        return null;
    }

    private static bool IsNewer(string latest, string current) =>
        Version.TryParse(Normalize(latest), out var l)
        && Version.TryParse(Normalize(current), out var c)
        && l > c;

    // Drop any pre-release/build suffix and pad to Major.Minor.Build for stable parsing.
    private static string Normalize(string v)
    {
        var cut = v.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
            v = v.Substring(0, cut);

        return v.Split('.').Length switch
        {
            1 => v + ".0.0",
            2 => v + ".0",
            _ => v
        };
    }
}
