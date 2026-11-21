// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Text.Json;
using System.Text.Json.Serialization;
using WebView2Guard.Localization;

namespace WebView2Guard.Core;

/// <summary>One app in an exported configuration.</summary>
public sealed class AppConfig
{
    public string ExeName { get; set; } = string.Empty;
    public bool Allowed { get; set; }

    /// <summary>Runtime architecture for allowed apps (x64 / x86); the app's own for the others.</summary>
    public Arch Arch { get; set; }

    /// <summary>Allowed domains; null = no restriction.</summary>
    public List<string>? Domains { get; set; }

    public string? Path { get; set; }
}

public sealed class SettingsConfig
{
    public string Language { get; set; } = "auto";
    public bool NotifyNewApps { get; set; } = true;
    public bool AutoUpdateRuntime { get; set; }
    public bool NotifyAppUpdates { get; set; } = true;
}

/// <summary>The decisions that can be moved to another PC: which apps are allowed, their domains, the settings.</summary>
public sealed class GuardConfig
{
    public const string FormatName = "WebView2Guard.Config";

    public string Format { get; set; } = FormatName;
    public int FormatVersion { get; set; } = 1;
    public string? AppVersion { get; set; }
    public DateTime? Exported { get; set; }
    public List<AppConfig> Apps { get; set; } = new();
    public SettingsConfig? Settings { get; set; }
}

public sealed record ImportResult(int Allowed, int Added, List<string> Skipped);

public static class ConfigTransfer
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The current decisions: allowed apps from the policies, the other known apps from the catalog.</summary>
    public static GuardConfig Current()
    {
        var policies = PolicyStore.ReadAll();
        var args = PolicyStore.ReadArgs();
        var apps = new Dictionary<string, AppConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var (exe, folder) in policies)
        {
            if (!PolicyStore.IsOurs(folder) || exe == "*") continue;
            apps[exe] = new AppConfig
            {
                ExeName = exe,
                Allowed = true,
                Arch = AppPaths.SamePath(folder, AppPaths.RuntimeDir(Arch.X86)) ? Arch.X86 : Arch.X64,
                Domains = args.TryGetValue(exe, out var a) && DomainRules.IsOurs(a) ? DomainRules.Parse(a) : null
            };
        }
        foreach (var h in HostCatalog.Load())
        {
            if (apps.TryGetValue(h.ExeName, out var known)) known.Path ??= h.Path;
            else apps[h.ExeName] = new AppConfig { ExeName = h.ExeName, Arch = h.Arch, Path = h.Path };
        }

        var s = Settings.Load();
        return new GuardConfig
        {
            AppVersion = Core.AppVersion.Current,
            Exported = DateTime.Now,
            Apps = apps.Values.OrderByDescending(x => x.Allowed).ThenBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase).ToList(),
            Settings = new SettingsConfig
            {
                Language = s.Language,
                NotifyNewApps = s.NotifyNewApps,
                AutoUpdateRuntime = s.AutoUpdateRuntime,
                NotifyAppUpdates = s.NotifyAppUpdates
            }
        };
    }

    public static string Serialize(GuardConfig c) => JsonSerializer.Serialize(c, Json);

    public static GuardConfig Parse(string json)
    {
        GuardConfig? c;
        try
        {
            c = JsonSerializer.Deserialize<GuardConfig>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(L.F("err.configFormat", ex.Message));
        }
        if (c == null || c.Format != GuardConfig.FormatName)
            throw new InvalidOperationException(L.F("err.configFormat", "format"));
        if (c.FormatVersion > 1)
            throw new InvalidOperationException(L.F("err.configFormat", "version " + c.FormatVersion));
        var bad = c.Apps.Where(a => !PolicyStore.IsValidExeName(a.ExeName) || a.ExeName == "*").Select(a => a.ExeName).ToList();
        if (bad.Count > 0) throw new InvalidOperationException(L.F("err.badExeName", string.Join(", ", bad)));
        return c;
    }

    public static void Export(string file)
    {
        File.WriteAllText(file, Serialize(Current()));
        Log.Info(L.F("log.configExported", file));
    }

    public static ImportResult Import(string file) => Apply(Parse(File.ReadAllText(file)));

    /// <summary>
    /// Adds the apps to the list and allows the ones marked allowed (with their domains). Merges: apps
    /// allowed here but not in the file stay allowed. An app whose runtime is missing is skipped.
    /// </summary>
    public static ImportResult Apply(GuardConfig c)
    {
        var allowed = 0;
        var known = HostCatalog.Load().Select(h => h.ExeName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var skipped = new List<string>();
        var policies = PolicyStore.ReadAll();

        foreach (var app in c.Apps)
        {
            if (!known.Contains(app.ExeName)) added++;
            HostCatalog.Ensure(app.ExeName, app.Path, app.Allowed ? Arch.Unknown : app.Arch);
            if (!app.Allowed) continue;
            try
            {
                if (policies.TryGetValue(app.ExeName, out var existing) && !PolicyStore.IsOurs(existing))
                    throw new InvalidOperationException(L.F("err.policyExternal", app.ExeName));
                Guard.Allow(app.ExeName, app.Arch == Arch.X86 ? Arch.X86 : Arch.X64);
                if (app.Domains is { Count: > 0 }) Guard.SetDomains(app.ExeName, app.Domains);
                else Guard.ClearDomains(app.ExeName);
                allowed++;
            }
            catch (InvalidOperationException ex)
            {
                skipped.Add(app.ExeName + ": " + ex.Message);
                Log.Warn(ex.Message);
            }
        }

        if (c.Settings is { } s)
        {
            var st = Settings.Load();
            if (s.Language is "auto" or "ro" or "en") st.Language = s.Language;
            st.NotifyNewApps = s.NotifyNewApps;
            st.AutoUpdateRuntime = s.AutoUpdateRuntime;
            st.NotifyAppUpdates = s.NotifyAppUpdates;
            st.Save();
        }
        Log.Info(L.F("log.configImported", allowed, added, skipped.Count));
        return new ImportResult(allowed, added, skipped);
    }
}
