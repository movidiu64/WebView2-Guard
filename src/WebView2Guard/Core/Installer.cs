// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using WebView2Guard.Localization;

namespace WebView2Guard.Core;

/// <summary>Copies the app to C:\Program Files\WebView2Guard (needed by the SYSTEM sync task).</summary>
public static class Installer
{
    /// <summary>True when built as a single-file app (the exe is everything).</summary>
    public static bool IsSingleFile =>
        !File.Exists(Path.Combine(AppContext.BaseDirectory, AppPaths.AppName + ".dll"));

    /// <summary>Installs/updates the program files. Returns the installed exe path.</summary>
    public static string InstallToProgramFiles()
    {
        var src = AppPaths.CurrentExe;
        if (string.IsNullOrEmpty(src)) throw new InvalidOperationException("Process path unknown.");
        Directory.CreateDirectory(AppPaths.InstallDir);

        if (!AppPaths.IsRunningFromInstallDir)
        {
            if (IsSingleFile)
            {
                File.Copy(src, AppPaths.InstalledExe, overwrite: true);
            }
            else
            {
                // Framework-dependent multi-file build (e.g. a Debug build): copy the whole output folder.
                CopyTree(AppContext.BaseDirectory, AppPaths.InstallDir);
            }
        }

        CreateShortcut();
        Log.Info(L.F("log.installed", AppPaths.InstalledExe));
        return AppPaths.InstalledExe;
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            if (AppPaths.IsUnder(dir, AppPaths.RuntimeRoot)) continue;
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        }
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (AppPaths.IsUnder(file, AppPaths.RuntimeRoot)) continue;
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
        }
    }

    private static void CreateShortcut()
    {
        try
        {
            var script =
                "$s=(New-Object -ComObject WScript.Shell).CreateShortcut(" + PowerShell.Q(AppPaths.StartMenuShortcut) + ");" +
                "$s.TargetPath=" + PowerShell.Q(AppPaths.InstalledExe) + ";" +
                "$s.WorkingDirectory=" + PowerShell.Q(AppPaths.InstallDir) + ";" +
                "$s.Description='WebView2 Guard';$s.Save()";
            PowerShell.Run(script);
        }
        catch (Exception ex)
        {
            Log.Warn(L.F("log.shortcutFailed", ex.Message));
        }
    }

    public static void RemoveShortcut()
    {
        try
        {
            if (File.Exists(AppPaths.StartMenuShortcut)) File.Delete(AppPaths.StartMenuShortcut);
        }
        catch
        {
            // ignore
        }
    }
}
