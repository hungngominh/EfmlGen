using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EfmlGen.Wpf.Services;

/// <summary>Downloads the release installer and launches it.</summary>
public static class Updater
{
    /// <summary>
    /// Downloads the installer to %TEMP% and returns its full path. Reports 0..1 progress
    /// when the server provides a Content-Length.
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(
        string url, string latestVersion, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dest = Path.Combine(Path.GetTempPath(), $"EfmlGen-Setup-v{latestVersion}.exe");

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EfmlGen-Updater");

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long readTotal = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            readTotal += n;
            if (total > 0)
                progress?.Report((double)readTotal / total);
        }

        return dest;
    }

    /// <summary>
    /// Launches the downloaded installer silently (reuses existing install dir + settings).
    /// The caller must then shut the app down so the installer can overwrite the running executable.
    /// </summary>
    public static void RunInstaller(string installerPath) =>
        Process.Start(new ProcessStartInfo(installerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART")
        {
            UseShellExecute = true
        });
}
