// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using WebView2Guard.Localization;

namespace WebView2Guard.Core;

public enum HostAccess { Blocked, Allowed, External }

public enum RuntimeKind { None, Evergreen, Allowed, Other }

public enum OverallState { Active, Warning, Incomplete }

public enum ProblemAction { None, Install, CreateTask, Sync, OpenRuntime, OpenApps }

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
    bool NeedsRestart);

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
    List<Problem> Problems);

/// <summary>Collects the whole state and performs the user-level actions.</summary>
public static class Guard
{
    // ---- state ---------------------------------------------------------------------------------

    public static List<HostRow> CollectHosts()
    {
        var running = ProcessScanner.FindHosts();
        var (catalog, newNames) = HostCatalog.MergeSeen(running);
        foreach (var n in newNames) Log.Info(L.F("log.newHost", n));

        var policies = PolicyStore.ReadAll();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in catalog) names.Add(c.ExeName);
        foreach (var p in policies.Keys) names.Add(p);
        foreach (var h in running.Where(x => x.ExeName != "?")) names.Add(h.ExeName);

        var rows = new List<HostRow>();
        foreach (var name in names)
        {
            var entry = catalog.FirstOrDefault(c => c.ExeName.Equals(name, StringComparison.OrdinalIgnoreCase));
            var live = running.Where(r => r.ExeName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            policies.TryGetValue(name, out var folder);

            var access = folder == null ? HostAccess.Blocked
                : PolicyStore.IsOurs(folder) ? HostAccess.Allowed
                : HostAccess.External;

            var first = live.FirstOrDefault();
            var runtimePath = first?.RuntimeExePath;
            var kind = Classify(runtimePath);
            var arch = first?.Arch is { } a && a != Arch.Unknown ? a : entry?.Arch ?? Arch.Unknown;

            var needsRestart = first != null &&
                               ((access == HostAccess.Allowed && kind != RuntimeKind.Allowed) ||
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
                first != null ? DateTime.Now : entry?.LastSeen,
                needsRestart));
        }
        return rows;
    }

    private static RuntimeKind Classify(string? runtimeExe)
    {
        if (string.IsNullOrEmpty(runtimeExe)) return RuntimeKind.None;
        if (AppPaths.IsUnder(runtimeExe, AppPaths.RuntimeRoot)) return RuntimeKind.Allowed;
        if (AppPaths.IsEvergreenPath(runtimeExe)) return RuntimeKind.Evergreen;
        return RuntimeKind.Other;
    }

    public static GuardSnapshot Collect()
    {
        var hosts = CollectHosts();
        var x64 = RuntimeManager.Get(Arch.X64);
        var x86 = RuntimeManager.Get(Arch.X86);
        var evergreen = RuntimeManager.Evergreen();

        List<FwRule> rules;
        string? rulesError = null;
        try { rules = FirewallManager.List(); }
        catch (Exception ex) { rules = new List<FwRule>(); rulesError = ex.Message; }

        SyncTaskStatus? task = null;
        string? taskError = null;
        try { task = SyncTask.Query(); }
        catch (Exception ex) { taskError = ex.Message; }

        var problems = Evaluate(hosts, x64, x86, evergreen, rules, rulesError, task);
        var state = problems.Count == 0 ? OverallState.Active
            : problems.Any(p => p.Severity == OverallState.Incomplete) ? OverallState.Incomplete
            : OverallState.Warning;

        return new GuardSnapshot(hosts, x64, x86, evergreen, rules, rulesError, task, taskError, state, problems);
    }

    private static List<Problem> Evaluate(List<HostRow> hosts, RuntimeInfo x64, RuntimeInfo x86,
        List<EvergreenInstall> evergreen, List<FwRule> rules, string? rulesError, SyncTaskStatus? task)
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

        if (!File.Exists(AppPaths.InstalledExe))
            problems.Add(new Problem(OverallState.Warning, L.T("prob.notInstalled"), ProblemAction.Install));
        else if (task is { Exists: false })
            problems.Add(new Problem(OverallState.Warning, L.T("prob.noTask"), ProblemAction.CreateTask));

        var restart = hosts.Where(h => h.NeedsRestart).Select(h => h.ExeName).ToList();
        if (restart.Count > 0)
            problems.Add(new Problem(OverallState.Warning,
                L.F("prob.needsRestart", string.Join(", ", restart)), ProblemAction.OpenApps));

        if (hosts.Any(h => h.ExeName == "*"))
            problems.Add(new Problem(OverallState.Warning, L.T("prob.globalPolicy"), ProblemAction.OpenApps));

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

    /// <summary>Removes the policy value; the exe falls back to Evergreen (blocked).</summary>
    public static void Block(string exeName)
    {
        if (PolicyStore.Remove(exeName)) Log.Info(L.F("log.policyRemoved", exeName));
    }

    public static SyncResult SyncFirewall()
    {
        var r = FirewallManager.Sync();
        Log.Info(r.Added + r.Removed == 0 ? L.T("log.syncNoChange") : L.F("log.syncDone", r.Added, r.Removed));
        return r;
    }

    public sealed record UndoOptions(bool Policies, bool Firewall, bool Task, bool Runtimes);

    public static void UndoAll(UndoOptions o)
    {
        if (o.Policies)
            foreach (var kv in PolicyStore.ReadAll().Where(p => PolicyStore.IsOurs(p.Value)))
                Block(kv.Key);
        if (o.Firewall) FirewallManager.RemoveAll();
        if (o.Task) SyncTask.Delete();
        if (o.Runtimes)
            foreach (var a in AppPaths.RuntimeArchs)
                RuntimeManager.Remove(a);
        Log.Info(L.T("log.undoDone"));
    }

    public static int CountOurPolicies() => PolicyStore.ReadAll().Count(kv => PolicyStore.IsOurs(kv.Value));
}
