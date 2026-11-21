// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Diagnostics;
using System.Text;

namespace WebView2Guard.Core;

public sealed record RunResult(int ExitCode, string StdOut, string StdErr)
{
    public string Combined => (StdOut + Environment.NewLine + StdErr).Trim();
}

/// <summary>Runs external tools without a shell; arguments are passed as a list (no quoting issues).</summary>
public static class ProcessRunner
{
    public static string Sys(string exe) => Path.Combine(Environment.SystemDirectory, exe);

    public static RunResult Run(string fileName, IEnumerable<string> args, int timeoutMs = 120_000,
        Encoding? encoding = null, Action<ProcessStartInfo>? configure = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (encoding != null)
        {
            psi.StandardOutputEncoding = encoding;
            psi.StandardErrorEncoding = encoding;
        }
        foreach (var a in args) psi.ArgumentList.Add(a);
        configure?.Invoke(psi);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start " + fileName);
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish in {timeoutMs / 1000} s.");
        }
        p.WaitForExit();
        return new RunResult(p.ExitCode, stdout.Result, stderr.Result);
    }
}

/// <summary>Windows PowerShell 5.1 runner (NetSecurity, ScheduledTasks and Authenticode cmdlets).</summary>
public static class PowerShell
{
    private static string Exe =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Runs a script; throws with the PowerShell error text on failure. Returns stdout.</summary>
    public static string Run(string script, int timeoutMs = 180_000)
    {
        var full = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
                   "[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        var r = ProcessRunner.Run(Exe,
            new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded },
            timeoutMs, Encoding.UTF8,
            // Started from PowerShell 7, PSModulePath points at its modules, which Windows PowerShell 5.1
            // cannot load ("module could not be loaded"): let 5.1 use its own default path.
            psi => psi.Environment.Remove("PSModulePath"));
        if (r.ExitCode != 0)
            throw new InvalidOperationException("PowerShell: " + CleanError(r.Combined));
        return r.StdOut.Trim();
    }

    /// <summary>ConvertTo-Json may emit a single object instead of a one-element array; normalise to an array.</summary>
    public static string AsJsonArray(string json)
    {
        var t = json.Trim();
        if (t.Length == 0 || t == "null") return "[]";
        return t.StartsWith('{') ? "[" + t + "]" : t;
    }

    /// <summary>Single-quoted PowerShell string literal.</summary>
    public static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    private static readonly System.Text.RegularExpressions.Regex ClixmlError =
        new(@"<S S=""Error"">(.*?)</S>", System.Text.RegularExpressions.RegexOptions.Singleline);

    public static string CleanError(string text)
    {
        // With -EncodedCommand, errors come as CLIXML on stderr: keep the error strings, decode the escapes.
        var errors = ClixmlError.Matches(text).Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)
            .Replace("_x000D_", "").Replace("_x000A_", " ")).Where(x => x.Trim().Length > 0).ToList();
        if (errors.Count > 0)
        {
            var msg = string.Join("", errors).Trim();
            return msg.Length > 600 ? msg[..600] + "…" : msg;
        }
        // Strip CLIXML noise that PowerShell may emit on stderr with -EncodedCommand.
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !l.StartsWith("#< CLIXML") && !l.StartsWith("<Objs "))
            .ToArray();
        var joined = string.Join(" ", lines).Trim();
        return joined.Length > 600 ? joined[..600] + "…" : joined;
    }
}
