// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WebView2Guard.Localization;

namespace WebView2Guard.Core;

/// <summary>State of strict mode, for the window and the status output.</summary>
public sealed record StrictStatus(bool Enabled, string? PolicyTarget, bool Active, List<string> Copies);

/// <summary>
/// Strict mode: the "*" BrowserExecutableFolder policy points every app without its own policy to a blocked
/// copy of the allowed x64 runtime (Runtime\Blocked\&lt;version&gt;, hard links where possible). The path of that
/// copy never changes with Windows updates, so its block rule is in place before any app can use it, with
/// no gap and no resident process. Allowed apps keep their own policy, which wins over "*". A 32-bit app
/// runs on the x64 copy as well.
/// Order on every sync: copy for the current allowed version → firewall rules → "*" policy → old copies
/// that nothing uses any more are deleted (a copy in use stays, with its rule, until the next sync).
/// </summary>
public static class StrictMode
{
    public const string AllApps = "*";
    private const string MarkerFile = "wv2guard-copy.txt";
    private static readonly Regex VersionDir = new(@"^\d+(\.\d+){3}$", RegexOptions.Compiled);

    public static bool IsBlockedCopy(string? path) =>
        !string.IsNullOrEmpty(path) && AppPaths.IsUnder(path, AppPaths.BlockedRoot);

    /// <summary>The finished copies (version folder with msedgewebview2.exe), newest first.</summary>
    public static List<(string Version, string Dir)> Copies()
    {
        if (!Directory.Exists(AppPaths.BlockedRoot)) return new();
        return Directory.GetDirectories(AppPaths.BlockedRoot)
            .Select(d => (Version: Path.GetFileName(d), Dir: d))
            .Where(c => VersionDir.IsMatch(c.Version) && File.Exists(Path.Combine(c.Dir, AppPaths.WebViewExe)))
            .OrderByDescending(c => RuntimeManager.ParseVersion(c.Version))
            .ToList();
    }

    public static string? PolicyTarget() =>
        PolicyStore.ReadAll().TryGetValue(AllApps, out var folder) ? folder : null;

    public static StrictStatus Status()
    {
        var target = PolicyTarget();
        var active = IsBlockedCopy(target) && File.Exists(Path.Combine(target!, AppPaths.WebViewExe));
        return new StrictStatus(Settings.Load().StrictMode, target, active, Copies().Select(c => c.Version).ToList());
    }

    /// <summary>Turns strict mode on; the copy, rule and policy are made by the sync that follows.</summary>
    public static void Enable()
    {
        if (!RuntimeManager.Get(Arch.X64).Installed && Copies().Count == 0)
            throw new InvalidOperationException(L.T("err.strictNeedsRuntime"));
        var target = PolicyTarget();
        if (target != null && !IsBlockedCopy(target))
            throw new InvalidOperationException(L.F("err.strictGlobalExternal", target));
        // The SYSTEM task runs the Program Files copy: an older one does not know the blocked copy and would
        // remove its rule at the next sync.
        var installed = AppVersion.OfFile(AppPaths.InstalledExe);
        if (installed != null && !AppPaths.IsRunningFromInstallDir && AppVersion.Compare(installed, AppVersion.Current) < 0)
            throw new InvalidOperationException(L.F("err.strictOldInstall", installed, AppVersion.Current));
        var s = Settings.Load();
        s.StrictMode = true;
        s.Save();
        Log.Info(L.T("log.strictOn"));
        Guard.SyncFirewall();
        if (!Status().Active) throw new InvalidOperationException(L.T("err.strictNotApplied"));
    }

    public static void Disable()
    {
        var s = Settings.Load();
        s.StrictMode = false;
        s.Save();
        if (IsBlockedCopy(PolicyTarget()) && PolicyStore.Remove(AllApps))
            Log.Info(L.F("log.policyRemoved", AllApps));
        Log.Info(L.T("log.strictOff"));
        Guard.SyncFirewall();
    }

    /// <summary>Step 1 of a sync: make sure a copy of the current allowed runtime exists.</summary>
    internal static void Prepare()
    {
        if (!Settings.Load().StrictMode) return;
        var rt = RuntimeManager.Get(Arch.X64);
        if (rt.Installed && rt.Version != null) EnsureCopy(rt.Version, rt.Dir);
    }

    /// <summary>
    /// Step 3 of a sync (after the rules): point "*" to the newest copy, delete unused old copies.
    /// Returns true when a copy was deleted (its rule then goes with a second rule sync).
    /// </summary>
    internal static bool Apply()
    {
        var enabled = Settings.Load().StrictMode;
        var copies = Copies();
        string? keep = null;
        if (enabled && copies.Count > 0)
        {
            var current = RuntimeManager.Get(Arch.X64);
            keep = copies.FirstOrDefault(c => c.Version == current.Version).Dir ?? copies[0].Dir;
            var target = PolicyTarget();
            if (target != null && !IsBlockedCopy(target))
            {
                Log.Warn(L.F("err.strictGlobalExternal", target));
                keep = target; // leave the other program's policy alone
            }
            else if (target == null || !AppPaths.SamePath(target, keep))
            {
                PolicyStore.Set(AllApps, keep);
                Log.Info(L.F("log.strictPolicy", keep));
            }
        }
        return Cleanup(keep);
    }

    /// <summary>Deletes the copies (and leftovers) that are not <paramref name="keep"/> and not in use.</summary>
    private static bool Cleanup(string? keep)
    {
        if (!Directory.Exists(AppPaths.BlockedRoot)) return false;
        var target = PolicyTarget();
        var deleted = false;
        foreach (var dir in Directory.GetDirectories(AppPaths.BlockedRoot))
        {
            if ((keep != null && AppPaths.SamePath(dir, keep)) || (target != null && AppPaths.SamePath(dir, target)))
                continue;
            if (ProcessScanner.ProcessesUnder(dir).Count > 0) continue; // still used: next sync
            try
            {
                Directory.Delete(dir, recursive: true);
                Log.Info(L.F("log.strictCopyRemoved", Path.GetFileName(dir)));
                deleted = true;
            }
            catch (Exception ex)
            {
                Log.Warn(Path.GetFileName(dir) + ": " + ex.Message);
            }
        }
        try
        {
            if (!Directory.EnumerateFileSystemEntries(AppPaths.BlockedRoot).Any()) Directory.Delete(AppPaths.BlockedRoot);
        }
        catch
        {
            // not empty / in use
        }
        return deleted;
    }

    /// <summary>Removes the policy and every copy that is not in use (Uninstall everything).</summary>
    public static void RemoveAll()
    {
        if (IsBlockedCopy(PolicyTarget()) && PolicyStore.Remove(AllApps))
            Log.Info(L.F("log.policyRemoved", AllApps));
        var s = Settings.Load();
        s.StrictMode = false;
        s.Save();
        Cleanup(null);
    }

    /// <summary>Builds Blocked\&lt;version&gt; from the allowed runtime: hard links on the same volume, else copies.</summary>
    public static string EnsureCopy(string version, string sourceDir)
    {
        var dir = Path.Combine(AppPaths.BlockedRoot, version);
        if (File.Exists(Path.Combine(dir, AppPaths.WebViewExe))) return dir;

        Directory.CreateDirectory(AppPaths.BlockedRoot);
        var tmp = dir + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); // incomplete earlier attempt
        Directory.CreateDirectory(tmp);

        var linked = 0;
        var copied = 0;
        foreach (var sub in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(tmp, Path.GetRelativePath(sourceDir, sub)));
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(tmp, Path.GetRelativePath(sourceDir, file));
            if (CreateHardLink(dest, file, IntPtr.Zero)) linked++;
            else
            {
                File.Copy(file, dest);
                copied++;
            }
        }
        File.WriteAllText(Path.Combine(tmp, MarkerFile), copied == 0 ? "hardlinks" : "copy");
        Directory.Move(tmp, dir);
        RuntimeManager.GrantAppContainer(AppPaths.BlockedRoot);
        Log.Info(L.F("log.strictCopy", version, linked, copied));
        return dir;
    }

    /// <summary>True when the copy shares its files with the allowed runtime (no extra disk space).</summary>
    public static bool IsHardLinked(string version)
    {
        try
        {
            return File.ReadAllText(Path.Combine(AppPaths.BlockedRoot, version, MarkerFile)).Trim() == "hardlinks";
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
