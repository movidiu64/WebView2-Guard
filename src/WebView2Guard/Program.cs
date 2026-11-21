// WebView2 Guard — per-application internet whitelist for Microsoft Edge WebView2.
// Copyright (C) 2026 Ovidiu (movidiu64)
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License version 3 as published by the Free Software Foundation (GPL-3.0-only).
// This program is distributed WITHOUT ANY WARRANTY; see the GNU General Public License for details:
// <https://www.gnu.org/licenses/>.

using WebView2Guard.Core;
using WebView2Guard.Localization;
using WebView2Guard.UI;

namespace WebView2Guard;

internal static class Program
{
    /// <summary>Set by the UI to rebuild the main window (e.g. after a language change).</summary>
    public static bool RestartUi { get; set; }

    [STAThread]
    private static int Main(string[] args)
    {
        L.Init(Settings.Load().Language);

        if (args.Any(a => a.Equals("--sync", StringComparison.OrdinalIgnoreCase)))
            return RunSync();
        if (Cli.IsCommand(args))
            return Cli.Run(args);

        const string showEventName = @"Global\WebView2Guard.Show";
        const string quitEventName = @"Global\WebView2Guard.Quit";
        var restarted = args.Any(a => a.Equals("--restarted", StringComparison.OrdinalIgnoreCase));
        var startInTray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        using var single = new Mutex(true, @"Global\WebView2Guard.UI", out var first);
        if (!first && !restarted)
        {
            var older = OtherInstances().Where(o => AppVersion.Compare(AppVersion.Current, o.Version) > 0).ToList();
            if (older.Count > 0)
            {
                // A newer build was started while an older one runs (e.g. in the tray): take over so it can update.
                if (EventWaitHandle.TryOpenExisting(quitEventName, out var quit))
                {
                    using (quit) quit.Set();
                }
                else if (MessageBox.Show(L.F("msg.closeOlder", older[0].Version ?? "?", AppVersion.Current),
                             "WebView2 Guard", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    // Versions before the quit signal existed: close them directly.
                    foreach (var o in older)
                    {
                        try { o.Process.Kill(); o.Process.WaitForExit(5000); } catch { /* already gone */ }
                    }
                }
                else
                {
                    return 0;
                }
                foreach (var o in older) o.Process.Dispose();
            }
            else if (EventWaitHandle.TryOpenExisting(showEventName, out var show))
            {
                // Same or newer version already running (maybe hidden in the tray): show it and leave.
                using (show) show.Set();
                return 0;
            }
        }
        if (!first)
        {
            // The previous instance is closing (update / install restart): wait for it.
            try { first = single.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            MessageBox.Show(L.T("msg.alreadyRunning"), "WebView2 Guard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);
        using var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, quitEventName);
        ShowSignal = showEvent;
        QuitSignal = quitEvent;

        AppUpdater.CleanupOld();
        ApplicationConfiguration.Initialize();
        do
        {
            RestartUi = false;
            Application.Run(new MainForm(startInTray));
            startInTray = false;
        } while (RestartUi);
        return 0;
    }

    /// <summary>Signalled by a second launch: the running instance shows its window.</summary>
    public static EventWaitHandle? ShowSignal { get; private set; }

    /// <summary>Signalled by a newer build: the running (older) instance exits so the new one can take over.</summary>
    public static EventWaitHandle? QuitSignal { get; private set; }

    /// <summary>Other running WebView2 Guard UI processes (any file name, e.g. the downloaded exe).</summary>
    private static List<(System.Diagnostics.Process Process, string? Version)> OtherInstances()
    {
        var result = new List<(System.Diagnostics.Process, string?)>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            var keep = false;
            try
            {
                if (p.Id != Environment.ProcessId &&
                    p.ProcessName.StartsWith("WebView2Guard", StringComparison.OrdinalIgnoreCase) &&
                    p.MainModule?.FileName is { } path)
                {
                    var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                    if (info.ProductName == "WebView2 Guard")
                    {
                        result.Add((p, info.ProductVersion?.Split('+')[0]));
                        keep = true;
                    }
                }
            }
            catch
            {
                // no access / exited
            }
            if (!keep) p.Dispose();
        }
        return result;
    }

    /// <summary>Headless mode used by the scheduled task.</summary>
    private static int RunSync()
    {
        Log.Prefix = "[sync] ";
        try
        {
            RuntimeUpdater.AutoUpdate();
        }
        catch (Exception ex)
        {
            Log.Warn(L.F("log.autoUpdateFailed", ex.Message));
        }
        try
        {
            Guard.SyncFirewall();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            return 1;
        }
    }
}
