// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WebView2Guard.Core;

/// <summary>
/// HKLM\SOFTWARE\Policies\Microsoft\Edge\WebView2\BrowserExecutableFolder — one value per host exe name,
/// data = folder that contains msedgewebview2.exe. The WebView2 loader honours it for that exe.
/// </summary>
public static class PolicyStore
{
    public const string PoliciesKey = @"SOFTWARE\Policies\Microsoft\Edge\WebView2";
    public const string FolderKey = PoliciesKey + @"\BrowserExecutableFolder";
    public const string ArgsKey = PoliciesKey + @"\AdditionalBrowserArguments";
    private const int MaxBackups = 30;

    private static readonly Regex ExeNameRx =
        new(@"^[^\\/:*?""<>|]+\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsValidExeName(string name) => ExeNameRx.IsMatch(name);

    private static RegistryKey Hklm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    public static Dictionary<string, string> ReadAll() => ReadKey(FolderKey);

    /// <summary>AdditionalBrowserArguments per exe (used for the domain restriction).</summary>
    public static Dictionary<string, string> ReadArgs() => ReadKey(ArgsKey);

    public static void SetArgs(string exeName, string args)
    {
        if (!IsValidExeName(exeName)) throw new ArgumentException("Invalid exe name: " + exeName);
        Backup();
        using var hklm = Hklm();
        using var key = hklm.CreateSubKey(ArgsKey, writable: true);
        key.SetValue(exeName, args, RegistryValueKind.String);
    }

    public static bool RemoveArgs(string exeName)
    {
        using var hklm = Hklm();
        using var key = hklm.OpenSubKey(ArgsKey, writable: true);
        if (key?.GetValue(exeName) == null) return false;
        Backup();
        key.DeleteValue(exeName, throwOnMissingValue: false);
        return true;
    }

    private static Dictionary<string, string> ReadKey(string subKey)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var hklm = Hklm();
        using var key = hklm.OpenSubKey(subKey);
        if (key == null) return d;
        foreach (var name in key.GetValueNames())
            if (key.GetValue(name) is string s)
                d[name] = s;
        return d;
    }

    /// <summary>True when the folder is one of our Fixed runtimes.</summary>
    public static bool IsOurs(string folder) =>
        AppPaths.RuntimeArchs.Any(a => AppPaths.SamePath(folder, AppPaths.RuntimeDir(a)));

    public static void Set(string exeName, string folder)
    {
        if (!IsValidExeName(exeName)) throw new ArgumentException("Invalid exe name: " + exeName);
        Backup();
        using var hklm = Hklm();
        using var key = hklm.CreateSubKey(FolderKey, writable: true);
        key.SetValue(exeName, folder, RegistryValueKind.String);
    }

    public static bool Remove(string exeName)
    {
        using var hklm = Hklm();
        using var key = hklm.OpenSubKey(FolderKey, writable: true);
        if (key?.GetValue(exeName) == null) return false;
        Backup();
        key.DeleteValue(exeName, throwOnMissingValue: false);
        return true;
    }

    /// <summary>Exports the WebView2 policy key to ProgramData\WebView2Guard\backups. Returns the file or null.</summary>
    public static string? Backup()
    {
        try
        {
            using (var hklm = Hklm())
            using (var key = hklm.OpenSubKey(PoliciesKey))
            {
                if (key == null) return null;
            }
            AppPaths.EnsureDataDir();
            var file = Path.Combine(AppPaths.BackupDir,
                "policy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".reg");
            var r = ProcessRunner.Run(ProcessRunner.Sys("reg.exe"),
                new[] { "export", @"HKLM\" + PoliciesKey, file, "/y", "/reg:64" }, 30_000);
            if (r.ExitCode != 0) return null;
            Prune();
            return file;
        }
        catch
        {
            return null;
        }
    }

    private static void Prune()
    {
        var files = new DirectoryInfo(AppPaths.BackupDir).GetFiles("policy-*.reg")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(MaxBackups);
        foreach (var f in files)
        {
            try { f.Delete(); } catch { /* ignore */ }
        }
    }
}
