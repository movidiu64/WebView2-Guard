// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Runtime.InteropServices;
using WebView2Guard.Core;
using WebView2Guard.Localization;

namespace WebView2Guard;

/// <summary>
/// Commands without a window, for scripts and tests. Exit codes: 0 done, 1 failed, 2 wrong arguments.
/// The app is a GUI exe: in PowerShell pipe the output (| Out-Host) so the shell waits for it.
/// </summary>
internal static class Cli
{
    public const int Ok = 0, Failed = 1, Usage = 2;

    private static readonly string[] Commands =
        { "--install", "--runtime", "--allow", "--block", "--domains", "--export", "--import", "--status", "--uninstall", "--help", "-h", "/?" };

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        AttachOutput();
        Log.Prefix = "[cli] ";
        var cmd = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        try
        {
            return cmd switch
            {
                "--install" => Install(),
                "--runtime" => Runtime(rest),
                "--allow" => Allow(rest),
                "--block" => Block(rest),
                "--domains" => Domains(rest),
                "--export" => rest.Length == 1 ? Export(rest[0]) : Help(),
                "--import" => rest.Length == 1 ? Import(rest[0]) : Help(),
                "--status" => Status(),
                "--uninstall" => Uninstall(rest),
                _ => Help(Ok)
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            Console.Error.WriteLine("error: " + ex.Message);
            return Failed;
        }
        finally
        {
            Console.Out.Flush();
        }
    }

    private static int Help(int code = Usage)
    {
        Console.WriteLine($"""
            WebView2 Guard {AppVersion.Current}

              (no arguments)                  open the window
              --tray                          start in the notification area
              --install                       install to Program Files, create the sync task, sync the firewall
              --sync                          sync the firewall rules (and update the runtime if set to)
              --runtime download [x64|x86]    download the newest Fixed runtime from Microsoft and install it
              --runtime <file.cab>            install a Fixed runtime package
              --allow <exe> [x64|x86]         let an app use the allowed runtime (default x64)
              --block <exe>                   send an app back to Evergreen (blocked)
              --domains <exe> <a.com,b.com>   allowed domains for an allowed app; "none" removes the limit
              --export <file.json>            save the allowed apps, their domains and the settings
              --import <file.json>            apply a saved configuration (adds to the current one)
              --status                        print the protection status
              --uninstall [--keep-runtimes]   remove policies, rules, tasks and (unless kept) the runtimes

            Exit codes: 0 done, 1 failed, 2 wrong arguments.
            """);
        return code;
    }

    private static int Install()
    {
        Installer.InstallToProgramFiles();
        SyncTask.Create();
        var r = Guard.SyncFirewall();
        Console.WriteLine($"Installed to {AppPaths.InstalledExe}; sync task created; rules added {r.Added}, removed {r.Removed}.");
        return Ok;
    }

    private static int Runtime(string[] a)
    {
        if (a.Length == 0) return Help();
        string? cab = null;
        var downloaded = false;
        try
        {
            if (a[0].Equals("download", StringComparison.OrdinalIgnoreCase))
            {
                var arch = ParseArch(a.ElementAtOrDefault(1));
                if (arch == null) return Help();
                var d = RuntimeUpdater.Latest(RuntimeUpdater.ListAsync().GetAwaiter().GetResult(), arch.Value)
                        ?? throw new InvalidOperationException(L.F("err.noRuntimeForArch", arch.Value.Label()));
                var installed = RuntimeManager.Get(arch.Value);
                if (!RuntimeUpdater.IsNewer(d, installed))
                {
                    Console.WriteLine($"Runtime {arch.Value.Label()} {installed.Version} is up to date.");
                    return Ok;
                }
                Console.WriteLine($"Downloading {d.Url}");
                cab = RuntimeUpdater.DownloadAsync(d, null).GetAwaiter().GetResult();
                downloaded = true;
            }
            else
            {
                if (a.Length != 1) return Help();
                cab = Path.GetFullPath(a[0]);
            }
            var (rtArch, version) = RuntimeManager.InstallFromCab(cab,
                new Progress<InstallProgress>(p => Console.WriteLine($"  {p.Step}: {p.Detail}")));
            Guard.SyncFirewall();
            Console.WriteLine($"Runtime {rtArch.Label()} {version} installed in {AppPaths.RuntimeDir(rtArch)}.");
            return Ok;
        }
        finally
        {
            if (downloaded) RuntimeUpdater.DeleteDownload(cab);
        }
    }

    private static int Allow(string[] a)
    {
        if (a.Length is < 1 or > 2) return Help();
        var arch = ParseArch(a.ElementAtOrDefault(1));
        if (arch == null) return Help();
        if (PolicyStore.ReadAll().TryGetValue(a[0], out var existing) && !PolicyStore.IsOurs(existing))
            throw new InvalidOperationException(L.F("err.policyExternal", a[0]));
        var rt = Guard.Allow(a[0], arch.Value);
        HostCatalog.Ensure(a[0], null, arch.Value);
        Console.WriteLine($"{a[0]}: allowed ({rt.Label()} runtime). Restart it if it is running.");
        return Ok;
    }

    private static int Block(string[] a)
    {
        if (a.Length != 1) return Help();
        Guard.ClearDomains(a[0]);
        Guard.Block(a[0]);
        Console.WriteLine($"{a[0]}: blocked.");
        return Ok;
    }

    private static int Domains(string[] a)
    {
        if (a.Length != 2) return Help();
        if (!PolicyStore.ReadAll().TryGetValue(a[0], out var folder) || !PolicyStore.IsOurs(folder))
            throw new InvalidOperationException(L.F("err.domainsNotAllowed", a[0]));
        var list = a[1].Equals("none", StringComparison.OrdinalIgnoreCase)
            ? new List<string>()
            : DomainRules.Normalize(a[1].Replace(',', '\n'));
        Guard.SetDomains(a[0], list);
        Console.WriteLine(list.Count == 0 ? $"{a[0]}: no domain limit." : $"{a[0]}: {string.Join(", ", list)}");
        return Ok;
    }

    private static int Export(string file)
    {
        ConfigTransfer.Export(Path.GetFullPath(file));
        Console.WriteLine($"Saved {Path.GetFullPath(file)}");
        return Ok;
    }

    private static int Import(string file)
    {
        var r = ConfigTransfer.Import(Path.GetFullPath(file));
        Console.WriteLine($"Allowed {r.Allowed} apps, {r.Added} new in the list.");
        foreach (var s in r.Skipped) Console.WriteLine("  skipped " + s);
        return r.Skipped.Count == 0 ? Ok : Failed;
    }

    private static int Status()
    {
        var s = Guard.Collect();
        Console.WriteLine($"WebView2 Guard {AppVersion.Current}: {s.State}");
        foreach (var e in s.Evergreen)
        {
            var blocked = s.Rules.Any(r => !r.IsAllow && AppPaths.SamePath(r.ExpandedProgram, e.ExePath));
            Console.WriteLine($"  Evergreen {e.Version}{(e.User != null ? " (" + e.User + ")" : "")}: {(blocked ? "blocked" : "NOT blocked")}");
        }
        foreach (var rt in new[] { s.X64, s.X86 })
            Console.WriteLine($"  Allowed runtime {rt.Arch.Label()}: {(rt.Installed ? rt.Version : "not installed")}");
        Console.WriteLine($"  Sync task: {(s.Task?.Exists == true ? "on" : "off")}");
        foreach (var p in s.Problems) Console.WriteLine($"  ! {p.Text}");
        foreach (var h in s.Hosts.OrderByDescending(h => h.Access == HostAccess.Allowed).ThenBy(h => h.ExeName))
            Console.WriteLine($"  {h.ExeName,-32} {h.Access,-8} {(h.Domains ?? "")}");
        return Ok;
    }

    private static int Uninstall(string[] a)
    {
        var keep = a.Any(x => x.Equals("--keep-runtimes", StringComparison.OrdinalIgnoreCase));
        if (a.Length > (keep ? 1 : 0)) return Help();
        Guard.UndoAll(new Guard.UndoOptions(Policies: true, Firewall: true, Task: true, Runtimes: !keep));
        Installer.RemoveShortcut();
        Console.WriteLine("Policies, rules and tasks removed" + (keep ? "." : ", runtimes deleted."));
        return Ok;
    }

    private static Arch? ParseArch(string? s) => s?.ToLowerInvariant() switch
    {
        null or "x64" => Arch.X64,
        "x86" => Arch.X86,
        _ => null
    };

    // ---- console ----------------------------------------------------------------------------------

    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    /// <summary>A GUI exe has no console: use the parent's, unless the output is already redirected.</summary>
    private static void AttachOutput()
    {
        if (GetStdHandle(StdOutputHandle) != IntPtr.Zero)
        {
            // Redirected to a file or pipe: write UTF-8 without touching any console code page.
            var utf8 = new System.Text.UTF8Encoding(false);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
            return;
        }
        AttachConsole(AttachParentProcess);
    }
}
