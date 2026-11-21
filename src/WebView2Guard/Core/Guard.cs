// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Text.Json;
using System.Text.Json.Serialization;
using WebView2Guard.Localization;

namespace WebView2Guard.Core;

public enum HostAccess { Blocked, Allowed, External }

public enum RuntimeKind { None, Evergreen, Allowed, Blocked, Other }

public enum OverallState { Active, Warning, Incomplete }

public enum ProblemAction { None, Install, UpdateInstall, CreateTask, Sync, OpenRuntime, OpenApps }

public sealed record Problem(OverallState Severity, string Text, ProblemAction Action);

public sealed record HostRow(
    string ExeName,
    string? Path,
    Arch Arch,
    HostAccess Access,
    string? PolicyFolder,
    bool Running,
    int Pid,
    int ProcessCount,
    string? RuntimeInUse,
    RuntimeKind RuntimeKind,
    DateTime? LastSeen,
    bool NeedsRestart,
    bool IsNew = false,
    string? Domains = null,
    string? ExternalArgs = null);

public sealed record CollectStep(int Step, int Total, string Text);

public sealed record GuardSnapshot(
    List<HostRow> Hosts,
    RuntimeInfo X64,
    RuntimeInfo X86,
    List<EvergreenInstall> Evergreen,
    List<FwRule> Rules,
    string? RulesError,
    SyncTaskStatus? Task,
    string? TaskError,
    OverallState State,
    List<Problem> Problems,
    StrictStatus? Strict = null);

/// <summary>Collects the whole state and performs the user-level actions.</summary>
public static class Guard
{
    // ---- state ---------------------------------------------------------------------------------

    /// <summary>Raised (on a background thread) with apps seen using WebView2 for the first time.</summary>
    public static event Action<IReadOnlyList<string>>? NewHostsDetected;

    public static List<HostRow> CollectHosts()
    {
        var running = ProcessScanner.FindHosts();
        var (catalog, newNames) = HostCatalog.MergeSeen(running);
        foreach (var n in newNames) Log.Info(L.F("log.newHost", n));
        // On the very first scan everything is "new": no notification flood.
        if (newNames.Count > 0 && catalog.Count > newNames.Count) NewHostsDetected?.Invoke(newNames);

        var policies = PolicyStore.ReadAll();
        var arguments = PolicyStore.ReadArgs();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        // msedgewebview2.exe is the runtime itself, never a host (older versions could record it).
        foreach (var c in catalog.Where(c => !c.ExeName.Equals(AppPaths.WebViewExe, StringComparison.OrdinalIgnoreCase)))
            names.Add(c.ExeName);
        foreach (var p in policies.Keys) names.Add(p);
        foreach (var h in running.Where(x => x.ExeName != "?" &&
                                             !x.ExeName.Equals(AppPaths.WebViewExe, StringComparison.OrdinalIgnoreCase)))
            names.Add(h.ExeName);

        var rows = new List<HostRow>();
        foreach (var name in names)
        {
            var entry = catalog.FirstOrDefault(c => c.ExeName.Equals(name, StringComparison.OrdinalIgnoreCase));
            var live = running.Where(r => r.ExeName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            policies.TryGetValue(name, out var folder);
            arguments.TryGetValue(name, out var args);
            var domains = DomainRules.Parse(args);

            var access = folder == null || StrictMode.IsBlockedCopy(folder) ? HostAccess.Blocked
                : PolicyStore.IsOurs(folder) ? HostAccess.Allowed
                : HostAccess.External;

            // Several instances (an old one still closing, a background helper): the newest decides.
            var first = live.OrderByDescending(l => l.Started).FirstOrDefault();
            var runtimePath = first?.RuntimeExePath;
            var kind = Classify(runtimePath);
            var arch = first?.Arch is { } a && a != Arch.Unknown ? a : entry?.Arch ?? Arch.Unknown;

            // Only the runtimes we control: an app on its own runtime ("other") does not get better by restarting.
            var needsRestart = first != null &&
                               ((access == HostAccess.Allowed && kind is RuntimeKind.Evergreen or RuntimeKind.Blocked) ||
                                (access == HostAccess.Blocked && kind == RuntimeKind.Allowed));

            rows.Add(new HostRow(
                name,
                first?.ExePath ?? entry?.Path,
                arch,
                access,
                folder,
                first != null,
                first?.Pid ?? 0,
                live.Sum(l => l.ProcessCount),
                runtimePath,
                kind,
                entry?.LastSeen, // stable while running, so unchanged rows compare equal
                needsRestart,
                entry?.FirstSeen is { } fs && DateTime.Now - fs < TimeSpan.FromHours(24),
                domains == null ? null : string.Join(", ", domains),
                args != null && domains == null ? args : null));
        }
        return rows;
    }

    private static RuntimeKind Classify(string? runtimeExe)
    {
        if (string.IsNullOrEmpty(runtimeExe)) return RuntimeKind.None;
        if (StrictMode.IsBlockedCopy(runtimeExe)) return RuntimeKind.Blocked;
        if (AppPaths.IsUnder(runtimeExe, AppPaths.RuntimeRoot)) return RuntimeKind.Allowed;
        if (AppPaths.IsEvergreenPath(runtimeExe)) return RuntimeKind.Evergreen;
        return RuntimeKind.Other;
    }

    /// <summary>The snapshot with a fresh app scan: problems and state are evaluated again (the banner follows).</summary>
    public static GuardSnapshot WithHosts(GuardSnapshot s, List<HostRow> hosts)
    {
        var strict = s.Strict ?? StrictMode.Status();
        var problems = Evaluate(hosts, s.X64, s.X86, s.Evergreen, s.Rules, s.RulesError, s.Task, strict);
        return s with { Hosts = hosts, Problems = problems, State = StateOf(problems), Strict = strict };
    }

    private static OverallState StateOf(List<Problem> problems) =>
        problems.Count == 0 ? OverallState.Active
        : problems.Any(p => p.Severity == OverallState.Incomplete) ? OverallState.Incomplete
        : OverallState.Warning;

    public static GuardSnapshot Collect(IProgress<CollectStep>? progress = null)
    {
        const int total = 4;
        progress?.Report(new CollectStep(1, total, L.T("status.scanApps")));
        var hosts = CollectHosts();

        progress?.Report(new CollectStep(2, total, L.T("status.readRuntimes")));
        var x64 = RuntimeManager.Get(Arch.X64);
        var x86 = RuntimeManager.Get(Arch.X86);
        var evergreen = RuntimeManager.Evergreen();

        progress?.Report(new CollectStep(3, total, L.T("status.readFirewall")));
        var (rules, rulesError, task, taskError) = ReadRulesAndTask();

        progress?.Report(new CollectStep(4, total, L.T("status.evaluate")));
        var strict = StrictMode.Status();
        var problems = Evaluate(hosts, x64, x86, evergreen, rules, rulesError, task, strict);
        var state = StateOf(problems);

        var snapshot = new GuardSnapshot(hosts, x64, x86, evergreen, rules, rulesError, task, taskError, state, problems, strict);
        SnapshotCache.Save(snapshot);
        return snapshot;
    }

    /// <summary>Firewall rules and task state in ONE PowerShell start (each start costs ~0.5 s).</summary>
    private static (List<FwRule> Rules, string? RulesError, SyncTaskStatus? Task, string? TaskError) ReadRulesAndTask()
    {
        const string marker = "@@WV2G@@";
        try
        {
            var output = PowerShell.Run(FirewallManager.ListScript + ";Write-Output '" + marker + "';" + SyncTask.QueryScript);
            var parts = output.Split(marker);
            if (parts.Length == 2)
                return (FirewallManager.Parse(parts[0]), null, SyncTask.Parse(parts[1]), null);
        }
        catch
        {
            // fall back to separate calls so each part reports its own error
        }

        List<FwRule> rules;
        string? rulesError = null;
        try { rules = FirewallManager.List(); }
        catch (Exception ex) { rules = new List<FwRule>(); rulesError = ex.Message; }

        SyncTaskStatus? task = null;
        string? taskError = null;
        try { task = SyncTask.Query(); }
        catch (Exception ex) { taskError = ex.Message; }
        return (rules, rulesError, task, taskError);
    }

    private static List<Problem> Evaluate(List<HostRow> hosts, RuntimeInfo x64, RuntimeInfo x86,
        List<EvergreenInstall> evergreen, List<FwRule> rules, string? rulesError, SyncTaskStatus? task,
        StrictStatus strict)
    {
        var problems = new List<Problem>();

        if (rulesError != null)
        {
            problems.Add(new Problem(OverallState.Incomplete, L.F("prob.rulesUnreadable", rulesError), ProblemAction.Sync));
        }
        else
        {
            var unblocked = evergreen
                .Where(e => !rules.Any(r => !r.IsAllow && r.Enabled && AppPaths.SamePath(r.ExpandedProgram, e.ExePath)))
                .Select(e => e.Version).ToList();
            if (unblocked.Count > 0)
                problems.Add(new Problem(OverallState.Incomplete,
                    L.F("prob.evergreenUnblocked", string.Join(", ", unblocked)), ProblemAction.Sync));

            foreach (var rt in new[] { x64, x86 }.Where(r => r.Installed))
            {
                var exe = AppPaths.RuntimeExe(rt.Arch);
                if (!rules.Any(r => r.IsAllow && r.Enabled && AppPaths.SamePath(r.ExpandedProgram, exe)))
                    problems.Add(new Problem(OverallState.Incomplete,
                        L.F("prob.allowRuleMissing", rt.Arch.Label()), ProblemAction.Sync));
            }
        }

        foreach (var a in AppPaths.RuntimeArchs)
        {
            var rt = a == Arch.X64 ? x64 : x86;
            var users = hosts.Where(h => h.Access == HostAccess.Allowed && h.Arch.RuntimeFor() == a).ToList();
            if (users.Count > 0 && !rt.Installed)
                problems.Add(new Problem(OverallState.Incomplete,
                    L.F("prob.runtimeMissing", a.Label(), string.Join(", ", users.Select(u => u.ExeName))),
                    ProblemAction.OpenRuntime));
        }

        var newestEvergreen = evergreen.FirstOrDefault();
        if (newestEvergreen != null && x64.Installed &&
            RuntimeManager.ParseVersion(x64.Version).Major < RuntimeManager.ParseVersion(newestEvergreen.Version).Major)
            problems.Add(new Problem(OverallState.Warning,
                L.F("prob.runtimeOld", "x64", x64.Version ?? "?", newestEvergreen.Version), ProblemAction.OpenRuntime));

        var installedVersion = AppVersion.OfFile(AppPaths.InstalledExe);
        if (!File.Exists(AppPaths.InstalledExe))
            problems.Add(new Problem(OverallState.Warning, L.T("prob.notInstalled"), ProblemAction.Install));
        else if (!AppPaths.IsRunningFromInstallDir && AppVersion.Compare(installedVersion, AppVersion.Current) < 0)
            problems.Add(new Problem(OverallState.Warning,
                L.F("prob.installedOlder", installedVersion ?? "?", AppVersion.Current), ProblemAction.UpdateInstall));
        else if (task is { Exists: false })
            problems.Add(new Problem(OverallState.Warning, L.T("prob.noTask"), ProblemAction.CreateTask));

        var restart = hosts.Where(h => h.NeedsRestart).Select(h => h.ExeName).ToList();
        if (restart.Count > 0)
            problems.Add(new Problem(OverallState.Warning,
                L.F("prob.needsRestart", string.Join(", ", restart)), ProblemAction.OpenApps));

        var global = hosts.FirstOrDefault(h => h.ExeName == StrictMode.AllApps);
        if (global != null && !StrictMode.IsBlockedCopy(global.PolicyFolder))
            problems.Add(new Problem(OverallState.Warning, L.T("prob.globalPolicy"), ProblemAction.OpenApps));
        if (strict is { Enabled: true, Active: false })
            problems.Add(new Problem(OverallState.Incomplete,
                L.T(x64.Installed || strict.Copies.Count > 0 ? "prob.strictInactive" : "prob.strictNeedsRuntime"),
                x64.Installed || strict.Copies.Count > 0 ? ProblemAction.Sync : ProblemAction.OpenRuntime));
        if (strict is { Active: true } && rulesError == null && strict.PolicyTarget is { } copy &&
            !rules.Any(r => !r.IsAllow && r.Enabled &&
                            AppPaths.SamePath(r.ExpandedProgram, Path.Combine(copy, AppPaths.WebViewExe))))
            problems.Add(new Problem(OverallState.Incomplete, L.T("prob.strictRuleMissing"), ProblemAction.Sync));

        return problems;
    }

    // ---- actions -------------------------------------------------------------------------------

    /// <summary>Routes the exe to our Fixed runtime of the matching architecture.</summary>
    public static Arch Allow(string exeName, Arch hostArch)
    {
        if (!PolicyStore.IsValidExeName(exeName)) throw new InvalidOperationException(L.F("err.badExeName", exeName));
        var a = hostArch.RuntimeFor();
        if (!File.Exists(AppPaths.RuntimeExe(a)))
            throw new InvalidOperationException(L.F("err.runtimeMissingForAllow", a.Label()));
        PolicyStore.Set(exeName, AppPaths.RuntimeDir(a));
        if (hostArch == Arch.Unknown) Log.Warn(L.F("log.archUnknownAssumedX64", exeName));
        Log.Info(L.F("log.policySet", exeName, AppPaths.RuntimeDir(a)));
        return a;
    }

    /// <summary>Lets the exe resolve only these host names (empty list = no restriction).</summary>
    public static void SetDomains(string exeName, IReadOnlyList<string> domains)
    {
        if (domains.Count == 0)
        {
            ClearDomains(exeName);
            return;
        }
        var bad = domains.Where(d => !DomainRules.IsValid(d)).ToList();
        if (bad.Count > 0) throw new InvalidOperationException(L.F("err.badDomains", string.Join(", ", bad)));
        if (PolicyStore.ReadArgs().TryGetValue(exeName, out var existing) && !DomainRules.IsOurs(existing))
            throw new InvalidOperationException(L.F("err.argsExternal", exeName, existing));
        PolicyStore.SetArgs(exeName, DomainRules.Build(domains));
        Log.Info(L.F("log.domainsSet", exeName, string.Join(", ", domains)));
    }

    public static void ClearDomains(string exeName)
    {
        if (PolicyStore.ReadArgs().TryGetValue(exeName, out var existing) && DomainRules.IsOurs(existing) &&
            PolicyStore.RemoveArgs(exeName))
            Log.Info(L.F("log.domainsCleared", exeName));
    }

    /// <summary>Removes the policy value; the exe falls back to Evergreen (blocked).</summary>
    public static void Block(string exeName)
    {
        if (PolicyStore.Remove(exeName)) Log.Info(L.F("log.policyRemoved", exeName));
    }

    public static SyncResult SyncFirewall()
    {
        // The window, the command line and the SYSTEM task can sync at the same moment: one at a time.
        using var gate = SyncGate.Enter();
        // Strict mode: copy first, then its block rule, and only then the "*" policy that sends apps to it.
        StrictMode.Prepare();
        var r = FirewallManager.Sync();
        if (StrictMode.Apply())
        {
            var again = FirewallManager.Sync();
            r = new SyncResult(r.Added + again.Added, r.Removed + again.Removed);
        }
        Log.Info(r.Added + r.Removed == 0 ? L.T("log.syncNoChange") : L.F("log.syncDone", r.Added, r.Removed));
        return r;
    }

    public sealed record UndoOptions(bool Policies, bool Firewall, bool Task, bool Runtimes);

    public static void UndoAll(UndoOptions o)
    {
        if (o.Policies)
        {
            StrictMode.RemoveAll();
            foreach (var kv in PolicyStore.ReadAll().Where(p => PolicyStore.IsOurs(p.Value)))
                Block(kv.Key);
            foreach (var kv in PolicyStore.ReadArgs().Where(p => DomainRules.IsOurs(p.Value)))
                ClearDomains(kv.Key);
        }
        if (o.Firewall) FirewallManager.RemoveAll();
        if (o.Task)
        {
            SyncTask.Delete();
            TrayTask.Delete();
            var settings = Settings.Load();
            settings.TrayMode = false;
            settings.Save();
        }
        if (o.Runtimes)
        {
            foreach (var a in AppPaths.RuntimeArchs)
                RuntimeManager.Remove(a);
            try
            {
                if (Directory.Exists(AppPaths.BlockedRoot)) Directory.Delete(AppPaths.BlockedRoot, recursive: true);
            }
            catch (Exception ex)
            {
                Log.Warn(AppPaths.BlockedRoot + ": " + ex.Message); // a copy still in use: delete it after a restart
            }
        }
        Log.Info(L.T("log.undoDone"));
    }

    public static int CountOurPolicies() => PolicyStore.ReadAll().Count(kv => PolicyStore.IsOurs(kv.Value));
}

/// <summary>Last snapshot on disk, shown immediately at start-up while the real scan runs.</summary>
/// <summary>Machine-wide lock around a sync (best effort: without access to the mutex, the sync runs anyway).</summary>
internal sealed class SyncGate : IDisposable
{
    private const string Name = @"Global\WebView2Guard.Sync";
    private Mutex? _mutex;
    private bool _owned;

    public static SyncGate Enter()
    {
        var gate = new SyncGate();
        try
        {
            gate._mutex = new Mutex(false, Name);
        }
        catch (UnauthorizedAccessException)
        {
            if (!Mutex.TryOpenExisting(Name, out gate._mutex)) return gate;
        }
        try
        {
            gate._owned = gate._mutex!.WaitOne(TimeSpan.FromMinutes(3));
        }
        catch (AbandonedMutexException)
        {
            gate._owned = true; // the other process died mid-sync: the sync is idempotent
        }
        return gate;
    }

    public void Dispose()
    {
        if (_owned) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}

public static class SnapshotCache
{
    private static string FilePath => Path.Combine(AppPaths.DataDir, "cache.json");

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static void Save(GuardSnapshot snapshot)
    {
        try
        {
            AppPaths.EnsureDataDir();
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // cache is optional
        }
    }

    public static (GuardSnapshot? Snapshot, DateTime SavedAt) Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return (null, default);
            var s = JsonSerializer.Deserialize<GuardSnapshot>(File.ReadAllText(FilePath), Json);
            return (s, File.GetLastWriteTime(FilePath));
        }
        catch
        {
            return (null, default);
        }
    }
}
