// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Diagnostics;
using System.Net;
using System.Reflection;

namespace WebView2Guard.Core;

public sealed record DownloadProgress(long Done, long? Total)
{
    public int? Percent => Total is > 0 ? (int)(Done * 100 / Total.Value) : null;
}

/// <summary>Shared HTTP client (used only for update checks and downloads the user starts or enables).</summary>
public static class Net
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WebView2Guard/" + AppVersion.Current.Replace('+', '.'));
        return c;
    }

    public static async Task DownloadAsync(Uri url, string file, IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using (var target = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
        {
            var buffer = new byte[1 << 16];
            long done = 0, reported = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (done - reported >= 512 * 1024)
                {
                    progress?.Report(new DownloadProgress(done, total));
                    reported = done;
                }
            }
            progress?.Report(new DownloadProgress(done, total));
        }
    }
}

/// <summary>Version of this build (set from the git tag by CI) and version comparison.</summary>
public static class AppVersion
{
    private static Assembly Asm => Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;

    /// <summary>e.g. "0.1.0-beta.2" (no "+commit" suffix).</summary>
    public static string Current =>
        (Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    /// <summary>"fd" (framework-dependent) or "selfcontained"; chooses which release asset to download.</summary>
    public static string Flavor =>
        Asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "Flavor")?.Value ?? "fd";

    public static string? OfFile(string exe)
    {
        try
        {
            return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>SemVer-like compare: 0.1.0 &gt; 0.1.0-beta.2 &gt; 0.1.0-beta.1.</summary>
    public static int Compare(string? a, string? b)
    {
        static (Version Core, string[] Pre) Parse(string? v)
        {
            v = (v ?? "0").Trim().TrimStart('v', 'V');
            var dash = v.IndexOf('-');
            var core = dash < 0 ? v : v[..dash];
            var pre = dash < 0 ? Array.Empty<string>() : v[(dash + 1)..].Split('.');
            return (Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0), pre);
        }

        var (ca, pa) = Parse(a);
        var (cb, pb) = Parse(b);
        var c = ca.CompareTo(cb);
        if (c != 0) return c;
        if (pa.Length == 0 || pb.Length == 0) return pb.Length.CompareTo(pa.Length); // release > prerelease
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var na = int.TryParse(pa[i], out var ia);
            var nb = int.TryParse(pb[i], out var ib);
            c = na && nb ? ia.CompareTo(ib) : string.CompareOrdinal(pa[i], pb[i]);
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }
}
