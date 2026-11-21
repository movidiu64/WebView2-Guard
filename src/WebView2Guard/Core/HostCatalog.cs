// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebView2Guard.Core;

public sealed class HostEntry
{
    public string ExeName { get; set; } = string.Empty;
    public string? Path { get; set; }
    public Arch Arch { get; set; }
    public DateTime? LastSeen { get; set; }
    public DateTime? FirstSeen { get; set; }
    public bool AddedManually { get; set; }
}

/// <summary>Apps seen using WebView2 (or added by hand), kept so they show up when not running.</summary>
public static class HostCatalog
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static List<HostEntry> Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(AppPaths.HostsFile)) return new List<HostEntry>();
                return JsonSerializer.Deserialize<List<HostEntry>>(File.ReadAllText(AppPaths.HostsFile), Json)
                       ?? new List<HostEntry>();
            }
            catch
            {
                return new List<HostEntry>();
            }
        }
    }

    private static void Save(List<HostEntry> list)
    {
        lock (Gate)
        {
            AppPaths.EnsureDataDir();
            var tmp = AppPaths.HostsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
            File.Move(tmp, AppPaths.HostsFile, overwrite: true);
        }
    }

    /// <summary>Adds/updates running hosts. Returns the new list and the names seen for the first time.</summary>
    public static (List<HostEntry> All, List<string> New) MergeSeen(IEnumerable<WebViewHost> seen)
    {
        var list = Load();
        var added = new List<string>();
        var changed = false;
        foreach (var h in seen)
        {
            if (h.ExeName == "?" || !PolicyStore.IsValidExeName(h.ExeName)) continue;
            var e = list.FirstOrDefault(x => x.ExeName.Equals(h.ExeName, StringComparison.OrdinalIgnoreCase));
            if (e == null)
            {
                e = new HostEntry { ExeName = h.ExeName, FirstSeen = DateTime.Now };
                list.Add(e);
                added.Add(h.ExeName);
                changed = true;
            }
            if (h.ExePath != null && h.ExePath != e.Path) { e.Path = h.ExePath; changed = true; }
            if (h.Arch != Arch.Unknown && h.Arch != e.Arch) { e.Arch = h.Arch; changed = true; }
            // LastSeen is only persisted every few minutes to avoid rewriting the file on every scan.
            if (e.LastSeen == null || DateTime.Now - e.LastSeen.Value > TimeSpan.FromMinutes(5)) changed = true;
            e.LastSeen = DateTime.Now;
        }
        if (changed)
        {
            try { Save(list); } catch { /* non-fatal */ }
        }
        return (list, added);
    }

    public static HostEntry AddManual(string exePath)
    {
        var list = Load();
        var name = System.IO.Path.GetFileName(exePath);
        var e = list.FirstOrDefault(x => x.ExeName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (e == null)
        {
            e = new HostEntry { ExeName = name, AddedManually = true };
            list.Add(e);
        }
        e.Path = exePath;
        var (arch, managed) = PeArch.FromFile(exePath);
        // An AnyCPU .NET exe is x86 in its header but runs as x64: leave it unknown until seen running.
        if (!(managed && arch == Arch.X86) && arch != Arch.Unknown) e.Arch = arch;
        Save(list);
        return e;
    }

    public static void Forget(string exeName)
    {
        var list = Load();
        if (list.RemoveAll(x => x.ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase)) > 0) Save(list);
    }
}

public sealed class Settings
{
    /// <summary>"auto", "ro" or "en".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Keep running in the notification area and start at logon.</summary>
    public bool TrayMode { get; set; }

    /// <summary>Show a notification when an app uses WebView2 for the first time.</summary>
    public bool NotifyNewApps { get; set; } = true;

    /// <summary>Let the sync task install newer Fixed runtimes from Microsoft (checked at most once a day).</summary>
    public bool AutoUpdateRuntime { get; set; }

    public DateTime? LastRuntimeCheck { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings();
        }
        catch
        {
            // fall through
        }
        return new Settings();
    }

    public void Save()
    {
        AppPaths.EnsureDataDir();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this));
    }
}
