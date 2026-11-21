// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace WebView2Guard.Core;

public sealed record ProcInfo(int Pid, int ParentPid, string Name, string? Path, string? CommandLine,
    DateTime Started = default);

/// <summary>A running app that hosts a WebView2 browser process.</summary>
public sealed record WebViewHost(
    string ExeName, string? ExePath, int Pid, Arch Arch, string RuntimeExePath, int ProcessCount,
    DateTime Started = default);

public static class ProcessScanner
{
    private static readonly Regex ExeNameArg =
        new(@"--webview-exe-name=(?:""(?<n>[^""]+)""|(?<n>[^\s""]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string Columns = "ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine, CreationDate";

    /// <summary>All processes (expensive: reads every command line). Used only for "is this folder in use".</summary>
    public static Dictionary<int, ProcInfo> Snapshot() => Query(null).ToDictionary(p => p.Pid);

    private static DateTime ParseWmiDate(string? value)
    {
        try
        {
            return string.IsNullOrEmpty(value) ? default : ManagementDateTimeConverter.ToDateTime(value);
        }
        catch
        {
            return default;
        }
    }

    private static List<ProcInfo> Query(string? where)
    {
        var list = new List<ProcInfo>();
        var wql = "SELECT " + Columns + " FROM Win32_Process" + (where == null ? string.Empty : " WHERE " + where);
        using var searcher = new ManagementObjectSearcher(wql);
        using var collection = searcher.Get();
        foreach (var mo in collection)
        {
            using (mo)
            {
                list.Add(new ProcInfo(
                    Convert.ToInt32(mo["ProcessId"]),
                    Convert.ToInt32(mo["ParentProcessId"]),
                    (mo["Name"] as string) ?? string.Empty,
                    mo["ExecutablePath"] as string,
                    mo["CommandLine"] as string,
                    ParseWmiDate(mo["CreationDate"] as string)));
            }
        }
        return list;
    }

    /// <summary>
    /// Only the WebView2 processes and their parents: much lighter than a full snapshot, so it can run every few seconds.
    /// </summary>
    private static Dictionary<int, ProcInfo> WebViewSnapshot()
    {
        var webviews = Query("Name='" + AppPaths.WebViewExe + "'");
        var d = webviews.ToDictionary(p => p.Pid);
        var parentIds = webviews.Select(w => w.ParentPid).Where(id => id > 0 && !d.ContainsKey(id)).Distinct().ToList();
        if (parentIds.Count > 0)
            foreach (var p in Query(string.Join(" OR ", parentIds.Select(id => "ProcessId=" + id))))
                d[p.Pid] = p;
        return d;
    }

    /// <summary>
    /// Finds WebView2 browser processes (msedgewebview2.exe without --type=) and their host app:
    /// the name comes from --webview-exe-name when present, otherwise from the parent process.
    /// </summary>
    public static List<WebViewHost> FindHosts(Dictionary<int, ProcInfo>? snapshot = null)
    {
        var snap = snapshot ?? WebViewSnapshot();
        var webviews = snap.Values
            .Where(p => p.Name.Equals(AppPaths.WebViewExe, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var browsers = webviews
            .Where(p => p.CommandLine == null || p.CommandLine.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) < 0)
            .ToList();

        var result = new List<WebViewHost>();
        foreach (var b in browsers)
        {
            snap.TryGetValue(b.ParentPid, out var parent);
            var m = ExeNameArg.Match(b.CommandLine ?? string.Empty);
            var name = m.Success ? m.Groups["n"].Value.Trim() : parent?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = "?";
            name = System.IO.Path.GetFileName(name);

            var parentMatches = parent != null &&
                                !parent.Name.Equals(AppPaths.WebViewExe, StringComparison.OrdinalIgnoreCase) &&
                                parent.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
            var arch = parentMatches ? ArchOfPid(parent!.Pid) : Arch.Unknown;
            var children = webviews.Count(p => p.ParentPid == b.Pid);

            result.Add(new WebViewHost(
                name,
                parentMatches ? parent!.Path : null,
                parentMatches ? parent!.Pid : 0,
                arch,
                b.Path ?? string.Empty,
                1 + children,
                b.Started));
        }
        return result;
    }

    /// <summary>Processes whose executable lives inside <paramref name="dir"/>.</summary>
    public static List<ProcInfo> ProcessesUnder(string dir, Dictionary<int, ProcInfo>? snapshot = null)
    {
        var snap = snapshot ?? Snapshot();
        return snap.Values.Where(p => AppPaths.IsUnder(p.Path, dir)).ToList();
    }

    // ---- architecture of a running process -------------------------------------------------

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    public static Arch ArchOfPid(int pid)
    {
        if (pid <= 0) return Arch.Unknown;
        var h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return Arch.Unknown;
        try
        {
            if (!IsWow64Process2(h, out var processMachine, out var nativeMachine)) return Arch.Unknown;
            // IMAGE_FILE_MACHINE_UNKNOWN (0) means "not running under WOW64", i.e. native architecture.
            return ArchExtensions.FromMachine(processMachine == 0 ? nativeMachine : processMachine);
        }
        catch (EntryPointNotFoundException)
        {
            return Arch.Unknown;
        }
        finally
        {
            CloseHandle(h);
        }
    }
}

/// <summary>Reads the target machine from a PE file header.</summary>
public static class PeArch
{
    /// <summary>
    /// Returns the PE machine type. A .NET "AnyCPU" exe is marked x86 in the header but usually runs
    /// as x64; such files are reported with <c>IsManaged = true</c> so callers can treat the result as unsure.
    /// </summary>
    public static (Arch Arch, bool IsManaged) FromFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt16() != 0x5A4D) return (Arch.Unknown, false); // "MZ"
            fs.Position = 0x3C;
            var peOffset = br.ReadInt32();
            fs.Position = peOffset;
            if (br.ReadUInt32() != 0x00004550) return (Arch.Unknown, false); // "PE\0\0"
            var machine = br.ReadUInt16();
            fs.Position = peOffset + 24; // optional header
            var magic = br.ReadUInt16();
            var dataDirStart = peOffset + 24 + (magic == 0x20B ? 112 : 96);
            fs.Position = dataDirStart + 14 * 8; // IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR
            br.ReadUInt32(); // RVA
            var clrSize = br.ReadUInt32();
            return (ArchExtensions.FromMachine(machine), clrSize != 0);
        }
        catch
        {
            return (Arch.Unknown, false);
        }
    }
}
