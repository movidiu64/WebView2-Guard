// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebView2Guard.Localization;

namespace WebView2Guard.Core;

public sealed record FwRule(string Name, string DisplayName, string Action, string Program, bool Enabled)
{
    [JsonIgnore] public bool IsAllow => Action.Equals("Allow", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public string ExpandedProgram => Environment.ExpandEnvironmentVariables(Program);
    [JsonIgnore] public bool FileExists => File.Exists(ExpandedProgram);
}

public sealed record SyncResult(int Added, int Removed);

/// <summary>
/// Outbound Windows Firewall rules in the group "WebView2 Guard":
/// Allow for each installed Fixed runtime, Block for each Evergreen version folder.
/// </summary>
public static class FirewallManager
{
    public const string Group = "WebView2 Guard";
    public const string Prefix = "WV2G-";

    private sealed class RuleDto
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public string? Action { get; set; }
        public string? Program { get; set; }
        public string? Enabled { get; set; }
    }

    private sealed record Desired(string Name, string DisplayName, string Action, string Program);

    /// <summary>PowerShell that prints the group's rules as JSON.</summary>
    public static string ListScript =>
        "$r=@(Get-NetFirewallRule -Group " + PowerShell.Q(Group) + " -ErrorAction SilentlyContinue | ForEach-Object {" +
        "$a=$_ | Get-NetFirewallApplicationFilter;" +
        "[pscustomobject]@{Name=$_.Name;DisplayName=$_.DisplayName;Action=[string]$_.Action;" +
        "Program=[string]$a.Program;Enabled=[string]$_.Enabled}});" +
        "ConvertTo-Json -InputObject $r -Compress";

    public static List<FwRule> List() => Parse(PowerShell.Run(ListScript));

    public static List<FwRule> Parse(string output)
    {
        var dtos = JsonSerializer.Deserialize<List<RuleDto>>(PowerShell.AsJsonArray(output)) ?? new List<RuleDto>();
        return dtos.Select(d => new FwRule(
                d.Name ?? string.Empty,
                d.DisplayName ?? string.Empty,
                d.Action ?? string.Empty,
                d.Program ?? string.Empty,
                string.Equals(d.Enabled, "True", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.IsAllow ? 0 : 1)
            .ThenByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<Desired> DesiredRules()
    {
        var desired = new List<Desired>();
        foreach (var a in AppPaths.RuntimeArchs)
        {
            var exe = AppPaths.RuntimeExe(a);
            if (File.Exists(exe))
                desired.Add(new Desired($"{Prefix}Allow-{a.Label()}", $"WebView2 Guard - Allow runtime {a.Label()}",
                    "Allow", exe));
        }
        foreach (var e in RuntimeManager.Evergreen())
            desired.Add(new Desired($"{Prefix}Block-{e.Version}", $"WebView2 Guard - Block Evergreen {e.Version}",
                "Block", e.ExePath));
        return desired;
    }

    private static bool Matches(FwRule r, Desired d) =>
        r.Enabled &&
        string.Equals(r.Name, d.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(r.Action, d.Action, StringComparison.OrdinalIgnoreCase) &&
        AppPaths.SamePath(r.ExpandedProgram, d.Program);

    /// <summary>Brings the rule group in line with what is on disk. Idempotent.</summary>
    public static SyncResult Sync()
    {
        var desired = DesiredRules();
        var existing = List();
        var sb = new StringBuilder();
        int added = 0, removed = 0;

        foreach (var r in existing.Where(x => !desired.Any(d => Matches(x, d))))
        {
            sb.Append("Remove-NetFirewallRule -Name ").Append(PowerShell.Q(r.Name)).Append(" -ErrorAction SilentlyContinue;");
            removed++;
            Log.Info(L.F("log.ruleRemoved", r.Name));
        }
        foreach (var d in desired.Where(y => !existing.Any(x => Matches(x, y))))
        {
            sb.Append("New-NetFirewallRule -Name ").Append(PowerShell.Q(d.Name))
                .Append(" -DisplayName ").Append(PowerShell.Q(d.DisplayName))
                .Append(" -Group ").Append(PowerShell.Q(Group))
                .Append(" -Direction Outbound -Action ").Append(d.Action)
                .Append(" -Program ").Append(PowerShell.Q(d.Program))
                .Append(" -Profile Any -Enabled True | Out-Null;");
            added++;
            Log.Info(L.F("log.ruleAdded", d.Name));
        }

        if (sb.Length > 0) PowerShell.Run(sb.ToString());
        return new SyncResult(added, removed);
    }

    public static void RemoveAll()
    {
        PowerShell.Run("Remove-NetFirewallRule -Group " + PowerShell.Q(Group) + " -ErrorAction SilentlyContinue");
        Log.Info(L.T("log.rulesRemovedAll"));
    }
}
