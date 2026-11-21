// WebView2 Guard — per-application internet whitelist for Microsoft Edge WebView2.
// Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

namespace WebView2Guard.Core;

public enum Arch { Unknown, X86, X64, Arm64 }

public static class ArchExtensions
{
    public static string Label(this Arch a) => a switch
    {
        Arch.X86 => "x86",
        Arch.X64 => "x64",
        Arch.Arm64 => "arm64",
        _ => "?"
    };

    /// <summary>Runtime flavour used for a host of this architecture (unknown → x64).</summary>
    public static Arch RuntimeFor(this Arch a) => a == Arch.X86 ? Arch.X86 : Arch.X64;

    public static Arch FromMachine(ushort machine) => machine switch
    {
        0x8664 => Arch.X64,
        0x014C => Arch.X86,
        0xAA64 => Arch.Arm64,
        _ => Arch.Unknown
    };
}

/// <summary>All file-system locations used by the app.</summary>
public static class AppPaths
{
    public const string AppName = "WebView2Guard";
    public const string WebViewExe = "msedgewebview2.exe";

    public static readonly Arch[] RuntimeArchs = { Arch.X64, Arch.X86 };

    public static string ProgramFilesDir => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public static string ProgramFilesX86Dir => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    public static string InstallDir => Path.Combine(ProgramFilesDir, AppName);
    public static string InstalledExe => Path.Combine(InstallDir, AppName + ".exe");

    public static string RuntimeRoot => Path.Combine(InstallDir, "Runtime");
    public static string RuntimeDir(Arch a) => Path.Combine(RuntimeRoot, a.Label());
    public static string RuntimePrevDir(Arch a) => RuntimeDir(a) + ".prev";
    public static string RuntimeExe(Arch a) => Path.Combine(RuntimeDir(a), WebViewExe);

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName);
    public static string HostsFile => Path.Combine(DataDir, "hosts.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "guard.log");
    public static string BackupDir => Path.Combine(DataDir, "backups");

    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "WebView2 Guard.lnk");

    /// <summary>Folders that hold the system-wide Evergreen WebView2 runtime versions.</summary>
    public static IEnumerable<string> MachineEvergreenRoots =>
        new[]
        {
            Path.Combine(ProgramFilesX86Dir, "Microsoft", "EdgeWebView", "Application"),
            Path.Combine(ProgramFilesDir, "Microsoft", "EdgeWebView", "Application")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-user Evergreen installs (rare: installed without admin rights), as (user folder name, root) pairs.
    /// </summary>
    public static IEnumerable<(string User, string Root)> UserEvergreenRoots
    {
        get
        {
            var users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (string.IsNullOrEmpty(users) || !Directory.Exists(users)) yield break;
            string[] profiles;
            try { profiles = Directory.GetDirectories(users); }
            catch { yield break; }
            foreach (var profile in profiles)
            {
                var root = Path.Combine(profile, "AppData", "Local", "Microsoft", "EdgeWebView", "Application");
                bool exists;
                try { exists = Directory.Exists(root); }
                catch { exists = false; }
                if (exists) yield return (Path.GetFileName(profile), root);
            }
        }
    }

    public static IEnumerable<string> EvergreenRoots =>
        MachineEvergreenRoots.Concat(UserEvergreenRoots.Select(u => u.Root));

    public static string CurrentExe => Environment.ProcessPath ?? string.Empty;

    public static bool IsRunningFromInstallDir =>
        CurrentExe.Length > 0 && SamePath(CurrentExe, InstalledExe);

    public static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True if <paramref name="path"/> is <paramref name="dir"/> or inside it.</summary>
    public static bool IsUnder(string? path, string dir)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var p = Path.GetFullPath(path).TrimEnd('\\') + "\\";
            var d = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsEvergreenPath(string? path) => EvergreenRoots.Any(r => IsUnder(path, r));

    private static bool _dataDirReady;

    /// <summary>
    /// Creates ProgramData\WebView2Guard and restricts it to Administrators/SYSTEM (write) and Users (read),
    /// so standard users cannot plant files that the SYSTEM sync task reads.
    /// </summary>
    public static void EnsureDataDir()
    {
        if (_dataDirReady) return;
        var existed = Directory.Exists(DataDir);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BackupDir);
        if (!existed)
        {
            try
            {
                ProcessRunner.Run(ProcessRunner.Sys("icacls.exe"), new[]
                {
                    DataDir, "/inheritance:r",
                    "/grant:r", "*S-1-5-32-544:(OI)(CI)F",
                    "/grant:r", "*S-1-5-18:(OI)(CI)F",
                    "/grant:r", "*S-1-5-32-545:(OI)(CI)RX"
                }, 60_000);
            }
            catch
            {
                // Non-fatal: inherited ProgramData ACL still applies.
            }
        }
        _dataDirReady = true;
    }
}
