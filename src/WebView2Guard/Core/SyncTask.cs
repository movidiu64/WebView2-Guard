// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Globalization;
using System.Text.Json;
using WebView2Guard.Localization;

namespace WebView2Guard.Core;

public sealed record SyncTaskStatus(
    bool Exists, string State, DateTime? LastRun, long LastResult, DateTime? NextRun, string Command);

/// <summary>Scheduled task \WebView2Guard\Sync: runs "WebView2Guard.exe --sync" as SYSTEM at boot and hourly.</summary>
public static class SyncTask
{
    private const string TaskPath = @"\WebView2Guard\";
    private const string TaskName = "Sync";
    public const string FullName = TaskPath + TaskName;

    private sealed class Dto
    {
        public bool Exists { get; set; }
        public string? State { get; set; }
        public string? LastRun { get; set; }
        public long LastResult { get; set; }
        public string? NextRun { get; set; }
        public string? Execute { get; set; }
        public string? Arguments { get; set; }
    }

    private static string Target => "-TaskPath " + PowerShell.Q(TaskPath) + " -TaskName " + PowerShell.Q(TaskName);

    /// <summary>PowerShell that prints the task state as JSON.</summary>
    public static string QueryScript =>
        "$t=Get-ScheduledTask " + Target + " -ErrorAction SilentlyContinue;" +
        "if($t){$i=$t|Get-ScheduledTaskInfo;$x=$t.Actions|Select-Object -First 1;" +
        "[pscustomobject]@{Exists=$true;State=[string]$t.State;" +
        "LastRun=$(if($i.LastRunTime -and $i.LastRunTime.Year -gt 2000){$i.LastRunTime.ToString('o')}else{''});" +
        "LastResult=[int64]$i.LastTaskResult;" +
        "NextRun=$(if($i.NextRunTime){$i.NextRunTime.ToString('o')}else{''});" +
        "Execute=[string]$x.Execute;Arguments=[string]$x.Arguments}|ConvertTo-Json -Compress}" +
        "else{'{\"Exists\":false}'}";

    public static SyncTaskStatus Query() => Parse(PowerShell.Run(QueryScript));

    public static SyncTaskStatus Parse(string output)
    {
        var dto = JsonSerializer.Deserialize<Dto>(output.Trim()) ?? new Dto();
        return new SyncTaskStatus(
            dto.Exists,
            dto.State ?? string.Empty,
            ParseDate(dto.LastRun),
            dto.LastResult,
            ParseDate(dto.NextRun),
            ((dto.Execute ?? string.Empty) + " " + (dto.Arguments ?? string.Empty)).Trim());
    }

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d.ToLocalTime()
            : null;

    /// <summary>Creates (or replaces) the task. The exe must be the copy in Program Files.</summary>
    public static void Create()
    {
        var exe = AppPaths.InstalledExe;
        if (!File.Exists(exe)) throw new InvalidOperationException(L.T("err.notInstalled"));

        var script =
            "$a=New-ScheduledTaskAction -Execute " + PowerShell.Q(exe) + " -Argument '--sync';" +
            "$t1=New-ScheduledTaskTrigger -AtStartup;" +
            "$t2=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Hours 1);" +
            "$p=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest;" +
            "$s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable " +
            "-ExecutionTimeLimit (New-TimeSpan -Minutes 10) -MultipleInstances IgnoreNew;" +
            "Register-ScheduledTask " + Target + " -Action $a -Trigger @($t1,$t2) -Principal $p -Settings $s " +
            "-Description " + PowerShell.Q("WebView2 Guard: keeps the WebView2 firewall rules in sync.") +
            " -Force | Out-Null";
        PowerShell.Run(script);
        Log.Info(L.T("log.taskCreated"));
    }

    public static void Delete()
    {
        PowerShell.Run("Unregister-ScheduledTask " + Target + " -Confirm:$false -ErrorAction SilentlyContinue");
        Log.Info(L.T("log.taskDeleted"));
    }

    public static void RunNow()
    {
        PowerShell.Run("Start-ScheduledTask " + Target);
    }
}
