// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebView2Guard.Localization;

namespace WebView2Guard.Core;

/// <param name="User">Profile folder name for a per-user install, null for the system-wide one.</param>
public sealed record EvergreenInstall(string Version, string ExePath, string? User = null);

/// <summary>Written next to an installed Fixed runtime.</summary>
public sealed class RuntimeMarker
{
    public string Version { get; set; } = string.Empty;
    public DateTime InstalledAt { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string Signer { get; set; } = string.Empty;
}

public sealed record RuntimeInfo(
    Arch Arch, bool Installed, string? Version, string Dir, RuntimeMarker? Marker, string? PreviousVersion);

public enum InstallStep { Extract, Signature, Architecture, InUse, Replace, Done }

public sealed record InstallProgress(InstallStep Step, string Detail);

public sealed class RuntimeBusyException : Exception
{
    public RuntimeBusyException(IReadOnlyList<ProcInfo> processes)
        : base(L.T("err.runtimeBusy")) => Processes = processes;

    public IReadOnlyList<ProcInfo> Processes { get; }
}

public static class RuntimeManager
{
    public const string MarkerFile = "wv2guard-runtime.json";
    private static readonly Regex VersionDir = new(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // ---- queries ----------------------------------------------------------------------------

    public static RuntimeInfo Get(Arch a)
    {
        var dir = AppPaths.RuntimeDir(a);
        var exe = AppPaths.RuntimeExe(a);
        var prev = VersionOf(Path.Combine(AppPaths.RuntimePrevDir(a), AppPaths.WebViewExe));
        if (!File.Exists(exe)) return new RuntimeInfo(a, false, null, dir, null, prev);
        return new RuntimeInfo(a, true, VersionOf(exe), dir, ReadMarker(dir), prev);
    }

    public static string? VersionOf(string exe)
    {
        try
        {
            return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).FileVersion : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Evergreen versions found on disk, newest first.</summary>
    public static List<EvergreenInstall> Evergreen()
    {
        var list = new List<EvergreenInstall>();
        var roots = AppPaths.MachineEvergreenRoots.Select(r => (User: (string?)null, Root: r))
            .Concat(AppPaths.UserEvergreenRoots.Select(u => (User: (string?)u.User, Root: u.Root)));
        foreach (var (user, root) in roots)
        {
            if (!Directory.Exists(root)) continue;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { continue; }
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (!VersionDir.IsMatch(name)) continue;
                var exe = Path.Combine(dir, AppPaths.WebViewExe);
                if (File.Exists(exe)) list.Add(new EvergreenInstall(name, exe, user));
            }
        }
        return list.OrderBy(e => e.User != null).ThenByDescending(e => ParseVersion(e.Version)).ToList();
    }

    public static Version ParseVersion(string? v) =>
        Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0);

    private static RuntimeMarker? ReadMarker(string dir)
    {
        try
        {
            var f = Path.Combine(dir, MarkerFile);
            return File.Exists(f) ? JsonSerializer.Deserialize<RuntimeMarker>(File.ReadAllText(f)) : null;
        }
        catch
        {
            return null;
        }
    }

    // ---- install / rollback / remove ---------------------------------------------------------

    /// <summary>
    /// Installs a Fixed Version runtime from a Microsoft .cab: expand → Authenticode check (Microsoft) →
    /// architecture from the PE header → refuse if in use → swap (old version kept as .prev) → App Container ACLs.
    /// </summary>
    public static (Arch Arch, string Version) InstallFromCab(string cabPath, IProgress<InstallProgress>? progress = null)
    {
        if (!File.Exists(cabPath)) throw new FileNotFoundException(L.T("err.cabMissing"), cabPath);

        EnsureRuntimeRoot();
        var staging = Path.Combine(AppPaths.RuntimeRoot, "_staging_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report(new InstallProgress(InstallStep.Extract, Path.GetFileName(cabPath)));
            var r = ProcessRunner.Run(ProcessRunner.Sys("expand.exe"), new[] { cabPath, "-F:*", staging }, 900_000);
            if (r.ExitCode != 0) throw new InvalidOperationException(L.F("err.expand", r.Combined));

            var exe = Directory.EnumerateFiles(staging, AppPaths.WebViewExe, SearchOption.AllDirectories)
                          .FirstOrDefault()
                      ?? throw new InvalidOperationException(L.T("err.noWebViewInCab"));
            var src = Path.GetDirectoryName(exe)!;

            progress?.Report(new InstallProgress(InstallStep.Signature, string.Empty));
            var signer = Signature.RequireMicrosoft(exe, Path.Combine(src, "msedge.dll"));

            var (arch, _) = PeArch.FromFile(exe);
            if (arch != Arch.X64 && arch != Arch.X86)
                throw new InvalidOperationException(L.F("err.arch", arch.Label()));
            var version = VersionOf(exe) ?? "?";
            progress?.Report(new InstallProgress(InstallStep.Architecture, $"{arch.Label()} · {version}"));

            var target = AppPaths.RuntimeDir(arch);
            var busy = ProcessScanner.ProcessesUnder(target);
            if (busy.Count > 0)
            {
                progress?.Report(new InstallProgress(InstallStep.InUse, string.Empty));
                throw new RuntimeBusyException(busy);
            }

            progress?.Report(new InstallProgress(InstallStep.Replace, string.Empty));
            var prev = AppPaths.RuntimePrevDir(arch);
            if (Directory.Exists(target))
            {
                DeleteDir(prev);
                Directory.Move(target, prev);
            }
            try
            {
                Directory.Move(src, target);
            }
            catch
            {
                if (!Directory.Exists(target) && Directory.Exists(prev)) Directory.Move(prev, target);
                throw;
            }

            var marker = new RuntimeMarker
            {
                Version = version,
                InstalledAt = DateTime.Now,
                SourceFile = Path.GetFileName(cabPath),
                Signer = signer
            };
            File.WriteAllText(Path.Combine(target, MarkerFile), JsonSerializer.Serialize(marker, Json));
            GrantAppContainer(target);

            progress?.Report(new InstallProgress(InstallStep.Done, string.Empty));
            Log.Info(L.F("log.runtimeInstalled", arch.Label(), version, signer));
            return (arch, version);
        }
        finally
        {
            DeleteDir(staging, quiet: true);
        }
    }

    /// <summary>Swaps the current runtime with the kept previous version.</summary>
    public static void Rollback(Arch a)
    {
        var target = AppPaths.RuntimeDir(a);
        var prev = AppPaths.RuntimePrevDir(a);
        if (!Directory.Exists(prev)) throw new InvalidOperationException(L.T("err.noPrevious"));
        ThrowIfBusy(target);
        ThrowIfBusy(prev);

        var tmp = target + ".swap";
        DeleteDir(tmp);
        if (Directory.Exists(target)) Directory.Move(target, tmp);
        Directory.Move(prev, target);
        if (Directory.Exists(tmp)) Directory.Move(tmp, prev);
        GrantAppContainer(target);
        Log.Info(L.F("log.runtimeRolledBack", a.Label(), VersionOf(AppPaths.RuntimeExe(a)) ?? "?"));
    }

    public static void Remove(Arch a)
    {
        var target = AppPaths.RuntimeDir(a);
        var prev = AppPaths.RuntimePrevDir(a);
        ThrowIfBusy(target);
        ThrowIfBusy(prev);
        DeleteDir(target);
        DeleteDir(prev);
        Log.Info(L.F("log.runtimeRemoved", a.Label()));
    }

    private static void ThrowIfBusy(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var busy = ProcessScanner.ProcessesUnder(dir);
        if (busy.Count > 0) throw new RuntimeBusyException(busy);
    }

    // ---- helpers ------------------------------------------------------------------------------

    public static void EnsureRuntimeRoot()
    {
        Directory.CreateDirectory(AppPaths.RuntimeRoot);
        GrantAppContainer(AppPaths.RuntimeRoot);
    }

    /// <summary>
    /// Since runtime 120 the renderer runs in an App Container; on Windows 10 the Fixed runtime folder must grant
    /// read/execute to ALL APPLICATION PACKAGES (S-1-15-2-1) and ALL RESTRICTED APPLICATION PACKAGES (S-1-15-2-2).
    /// </summary>
    public static void GrantAppContainer(string dir)
    {
        foreach (var sid in new[] { "*S-1-15-2-2:(OI)(CI)(RX)", "*S-1-15-2-1:(OI)(CI)(RX)" })
        {
            var r = ProcessRunner.Run(ProcessRunner.Sys("icacls.exe"), new[] { dir, "/grant", sid }, 300_000);
            if (r.ExitCode != 0) throw new InvalidOperationException(L.F("err.icacls", r.Combined));
        }
    }

    private static void DeleteDir(string dir, bool quiet = false)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch when (quiet)
        {
            // Leftover staging folders are harmless.
        }
        catch
        {
            // Could not delete (file in use?): move it aside so the name is free.
            var aside = dir + ".del-" + DateTime.Now.Ticks;
            Directory.Move(dir, aside);
        }
    }
}

/// <summary>Authenticode verification through Get-AuthenticodeSignature.</summary>
public static class Signature
{
    private sealed class SigDto
    {
        public string? Path { get; set; }
        public string? Status { get; set; }
        public string? Subject { get; set; }
    }

    /// <summary>Throws unless every existing file has a valid signature from Microsoft Corporation.</summary>
    public static string RequireMicrosoft(params string[] files)
    {
        var existing = files.Where(File.Exists).ToArray();
        if (existing.Length == 0) throw new InvalidOperationException(L.T("err.noWebViewInCab"));

        var list = string.Join(",", existing.Select(PowerShell.Q));
        var script =
            "$r=@(foreach($f in @(" + list + ")){$s=Get-AuthenticodeSignature -LiteralPath $f;" +
            "[pscustomobject]@{Path=$f;Status=[string]$s.Status;Subject=[string]$s.SignerCertificate.Subject}});" +
            "ConvertTo-Json -InputObject $r -Compress";
        var json = PowerShell.AsJsonArray(PowerShell.Run(script));
        var results = JsonSerializer.Deserialize<List<SigDto>>(json) ?? new List<SigDto>();

        string signer = string.Empty;
        foreach (var f in existing)
        {
            var s = results.FirstOrDefault(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase));
            var ok = s != null &&
                     string.Equals(s.Status, "Valid", StringComparison.OrdinalIgnoreCase) &&
                     (s.Subject ?? string.Empty).Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase);
            if (!ok)
                throw new InvalidOperationException(L.F("err.signature", Path.GetFileName(f),
                    s?.Status ?? "?", s?.Subject ?? "?"));
            signer = "Microsoft Corporation";
        }
        return signer;
    }
}
