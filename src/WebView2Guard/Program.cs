// WebView2 Guard — per-application internet whitelist for Microsoft Edge WebView2.
// Copyright (C) 2026 movidiu64
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License for details: <https://www.gnu.org/licenses/>.

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

        const string showEventName = @"Global\WebView2Guard.Show";
        var restarted = args.Any(a => a.Equals("--restarted", StringComparison.OrdinalIgnoreCase));
        var startInTray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        using var single = new Mutex(true, @"Global\WebView2Guard.UI", out var first);
        if (!first && !restarted)
        {
            // Already running (maybe hidden in the tray): ask it to show its window and leave.
            if (EventWaitHandle.TryOpenExisting(showEventName, out var show))
            {
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
        ShowSignal = showEvent;

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
