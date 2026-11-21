// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WebView2Guard.Localization;

namespace WebView2Guard.Core;

public sealed record RuntimeDownload(string Version, Arch Arch, Uri Url);

/// <summary>
/// Finds the official Fixed Version .cab links on Microsoft's WebView2 page (the same links its Download button uses).
/// Only https URLs on Microsoft's download host are accepted; the files are then verified like a manual .cab.
/// </summary>
public static class RuntimeUpdater
{
    public const string PageUrl = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";
    private const string DownloadHost = "msedge.sf.dl.delivery.mp.microsoft.com";

    private static readonly Regex CabRx = new(
        @"https:(?:\\u002F|/){2}msedge\.sf\.dl\.delivery\.mp\.microsoft\.com(?:\\u002F|/)filestreamingservice(?:\\u002F|/)files(?:\\u002F|/)[0-9a-fA-F-]{36}(?:\\u002F|/)Microsoft\.WebView2\.FixedVersionRuntime\.(?<v>\d+\.\d+\.\d+\.\d+)\.(?<a>x64|x86|arm64)\.cab",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<List<RuntimeDownload>> ListAsync(CancellationToken ct = default)
    {
        var html = await Net.Client.GetStringAsync(PageUrl, ct);
        var list = ParseLinks(html);
        if (list.Count == 0) throw new InvalidOperationException(L.T("err.noRuntimeLinks"));
        return list;
    }

    /// <summary>Official .cab links found in the page HTML (escaped "\u002F" or plain "/").</summary>
    public static List<RuntimeDownload> ParseLinks(string html)
    {
        var list = new List<RuntimeDownload>();
        foreach (Match m in CabRx.Matches(html))
        {
            var url = m.Value.Replace("\\u002F", "/").Replace("\\u002f", "/");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !uri.Host.Equals(DownloadHost, StringComparison.OrdinalIgnoreCase))
                continue;
            var arch = m.Groups["a"].Value.ToLowerInvariant() switch
            {
                "x64" => Arch.X64,
                "x86" => Arch.X86,
                _ => Arch.Arm64
            };
            list.Add(new RuntimeDownload(m.Groups["v"].Value, arch, uri));
        }
        return list.DistinctBy(d => d.Url).ToList();
    }

    public static RuntimeDownload? Latest(IEnumerable<RuntimeDownload> list, Arch a) =>
        list.Where(d => d.Arch == a).OrderByDescending(d => RuntimeManager.ParseVersion(d.Version)).FirstOrDefault();

    public static bool IsNewer(RuntimeDownload d, RuntimeInfo installed) =>
        !installed.Installed || RuntimeManager.ParseVersion(d.Version) > RuntimeManager.ParseVersion(installed.Version);

    public static string DownloadFolder => Path.Combine(AppPaths.RuntimeRoot, "_download");

    public static async Task<string> DownloadAsync(RuntimeDownload d, IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(DownloadFolder);
        var file = Path.Combine(DownloadFolder, Path.GetFileName(d.Url.LocalPath));
        if (File.Exists(file)) File.Delete(file);
        await Net.DownloadAsync(d.Url, file, progress, ct);
        Log.Info(L.F("log.runtimeDownloaded", d.Arch.Label(), d.Version));
        return file;
    }

    public static void DeleteDownload(string? file)
    {
        try
        {
            if (file != null && File.Exists(file)) File.Delete(file);
        }
        catch
        {
            // left for the next run
        }
    }

    /// <summary>Headless daily check used by the sync task when the user enabled automatic runtime updates.</summary>
    public static void AutoUpdate()
    {
        var settings = Settings.Load();
        if (!settings.AutoUpdateRuntime) return;
        if (settings.LastRuntimeCheck is { } last && DateTime.Now - last < TimeSpan.FromHours(20)) return;
        settings.LastRuntimeCheck = DateTime.Now;
        settings.Save();

        var list = ListAsync().GetAwaiter().GetResult();
        foreach (var a in AppPaths.RuntimeArchs)
        {
            var rt = RuntimeManager.Get(a);
            if (!rt.Installed) continue;
            var latest = Latest(list, a);
            if (latest == null || !IsNewer(latest, rt)) continue;
            if (ProcessScanner.ProcessesUnder(AppPaths.RuntimeDir(a)).Count > 0)
            {
                Log.Info(L.F("log.autoUpdateBusy", a.Label(), latest.Version));
                continue;
            }
            string? cab = null;
            try
            {
                cab = DownloadAsync(latest, null).GetAwaiter().GetResult();
                RuntimeManager.InstallFromCab(cab);
            }
            finally
            {
                DeleteDownload(cab);
            }
        }
    }
}

public sealed record AppRelease(string Tag, string Version, bool Prerelease, Uri ExeUrl, Uri? SumsUrl, string ExeName,
    string PageUrl);

/// <summary>Updates WebView2 Guard itself from the GitHub releases of the project.</summary>
public static class AppUpdater
{
    private const string Api = "https://api.github.com/repos/movidiu64/WebView2-Guard/releases?per_page=10";

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = string.Empty;
        [JsonPropertyName("assets")] public List<AssetDto> Assets { get; set; } = new();
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string Url { get; set; } = string.Empty;
    }

    public static string AssetName => AppVersion.Flavor == "selfcontained"
        ? "WebView2Guard-win-x64-selfcontained.exe"
        : "WebView2Guard-win-x64.exe";

    /// <summary>Newest published release that has the asset for this build flavour.</summary>
    public static async Task<AppRelease?> LatestAsync(CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await Net.Client.GetStringAsync(Api, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // GitHub answers 404 (not 403) for a private repository when the request has no token.
            throw new InvalidOperationException(L.T("err.updateRepoNotFound"));
        }
        var releases = JsonSerializer.Deserialize<List<ReleaseDto>>(json) ?? new List<ReleaseDto>();
        foreach (var r in releases.Where(r => !r.Draft))
        {
            var exe = r.Assets.FirstOrDefault(a => a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase));
            if (exe == null) continue;
            var sums = r.Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
            return new AppRelease(r.TagName, r.TagName.TrimStart('v', 'V'), r.Prerelease, new Uri(exe.Url),
                sums != null ? new Uri(sums.Url) : null, exe.Name, r.HtmlUrl);
        }
        return null;
    }

    public static bool IsNewer(AppRelease r) => AppVersion.Compare(r.Version, AppVersion.Current) > 0;

    /// <summary>The exe that gets replaced: the Program Files copy if there is one, otherwise this one.</summary>
    public static string TargetExe => File.Exists(AppPaths.InstalledExe) ? AppPaths.InstalledExe : AppPaths.CurrentExe;

    /// <summary>Downloads next to the target as ".new" and checks it against SHA256SUMS.txt of the same release.</summary>
    public static async Task<string> DownloadAsync(AppRelease r, IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        if (r.SumsUrl == null) throw new InvalidOperationException(L.T("err.updateNoSums"));
        var file = TargetExe + ".new";
        await Net.DownloadAsync(r.ExeUrl, file, progress, ct);

        var sums = await Net.Client.GetStringAsync(r.SumsUrl, ct);
        var expected = sums.Split('\n')
            .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].Equals(r.ExeName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p[0])
            .FirstOrDefault();
        string actual;
        await using (var fs = File.OpenRead(file))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
        if (expected == null || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            throw new InvalidOperationException(L.T("err.updateHash"));
        }
        return file;
    }

    /// <summary>
    /// Puts the downloaded exe in place. A running exe cannot be overwritten but can be renamed, so the current one
    /// becomes ".old" (deleted at the next start). Returns the exe to start.
    /// </summary>
    public static string Apply(string newFile, string version)
    {
        var target = newFile[..^".new".Length];
        var old = target + ".old";
        if (File.Exists(old)) File.Delete(old);
        if (File.Exists(target)) File.Move(target, old);
        File.Move(newFile, target);
        Log.Info(L.F("log.appUpdated", version, target));
        return target;
    }

    /// <summary>Removes "WebView2Guard.exe.old" left by a previous update.</summary>
    public static void CleanupOld()
    {
        foreach (var dir in new[] { Path.GetDirectoryName(AppPaths.CurrentExe), AppPaths.InstallDir }.Distinct())
        {
            if (string.IsNullOrEmpty(dir)) continue;
            try
            {
                var old = Path.Combine(dir, AppPaths.AppName + ".exe.old");
                if (File.Exists(old)) File.Delete(old);
            }
            catch
            {
                // still in use: next time
            }
        }
    }
}
