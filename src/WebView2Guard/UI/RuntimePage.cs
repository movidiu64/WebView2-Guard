// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Globalization;
using WebView2Guard.Core;
using WebView2Guard.Localization;
using static WebView2Guard.UI.Theme;

namespace WebView2Guard.UI;

/// <summary>Tab 2: the allowed Fixed runtimes (x64/x86) and the Evergreen versions.</summary>
internal sealed class RuntimePage : UserControl
{
    public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2";

    private readonly IShell _shell;
    private readonly CardGrid _grid = new();
    private readonly StackCard _warning = new() { Fill = WarnBg, Stroke = WarnBorder };
    private readonly StackCard _x64 = new();
    private readonly StackCard _x86 = new();
    private readonly StackCard _evergreen = new();
    private readonly StackCard _help = new();
    private List<RuntimeDownload>? _available;
    private DateTime? _checkedAt;

    public RuntimePage(IShell shell)
    {
        _shell = shell;
        BackColor = Bg;
        DoubleBuffered = true;
        Controls.Add(_grid);
        _warning.Visible = false;
        BindUpdates();
        _grid.SetRows(new Control[] { _warning }, new Control[] { _x64, _x86 }, new Control[] { _evergreen, _help });
    }

    private string? _signature;

    private static string ComputeSignature(GuardSnapshot s)
    {
        static string Rt(RuntimeInfo r) =>
            $"{r.Installed}/{r.Version}/{r.PreviousVersion}/{r.Marker?.InstalledAt:o}/{r.Marker?.Signer}";
        return string.Join("|",
            Rt(s.X64), Rt(s.X86),
            string.Join(",", s.Evergreen.Select(e => e.Version)),
            string.Join(",", s.Rules.Select(r => $"{r.Name}/{r.Action}/{r.Enabled}/{r.Program}")),
            string.Join(",", s.Hosts.Where(h => h.Access == HostAccess.Allowed).Select(h => h.ExeName + "/" + h.Arch)));
    }

    public void Bind(GuardSnapshot s)
    {
        // Rebuilding the cards is the expensive part: skip it when nothing shown here changed.
        var signature = ComputeSignature(s);
        if (signature == _signature) return;
        _signature = signature;

        SuspendLayout();
        BindWarning(s);
        BindRuntime(_x64, s.X64, s);
        BindRuntime(_x86, s.X86, s);
        BindEvergreen(s);
        ResumeLayout();
        _grid.MarkDirty();
    }

    private void BindWarning(GuardSnapshot s)
    {
        var newest = s.Evergreen.FirstOrDefault();
        var old = s.X64.Installed && newest != null &&
                  RuntimeManager.ParseVersion(s.X64.Version) < RuntimeManager.ParseVersion(newest.Version);
        _warning.Visible = old;
        if (!old) return;

        var icon = Ui.GlyphLabel(Glyph.Warning, WarnText, 11f);
        var text = new WrapLabel(L.F("rt.olderThanEvergreen", s.X64.Version ?? "?", newest!.Version), Body, WarnTextDark)
            { Margin = P(0, 1, 16, 0) };
        var link = new LinkLabel
        {
            Text = L.T("rt.help.link"),
            AutoSize = true,
            LinkColor = WarnTextDark,
            ActiveLinkColor = WarnText,
            Font = Strong,
            Margin = P(0, 1, 0, 0),
        };
        link.LinkClicked += (_, _) => MainForm.OpenUrl(DownloadUrl);
        var row = Ui.Row(icon, text, link);
        _warning.Padding = P(14, 10, 14, 4);
        _warning.SetContent(row);
    }

    private void BindRuntime(StackCard card, RuntimeInfo rt, GuardSnapshot s)
    {
        var pill = new Pill();
        if (rt.Installed) pill.Set(L.T("pill.installed"), OkBg, OkText);
        else pill.Set(L.T("pill.notInstalled"), NeutralBg, NeutralText);
        var head = StackCard.Heading(L.F("rt.card.title", rt.Arch.Label()), pill);

        var needed = rt.Arch == Arch.X64 ||
                     s.Hosts.Any(h => h.Access == HostAccess.Allowed && h.Arch.RuntimeFor() == rt.Arch);
        var online = new GuardButton(rt.Installed ? L.T("rt.checkUpdate") : L.T("rt.downloadInstall"),
            rt.Installed ? Glyph.Refresh : Glyph.Download,
            needed ? ButtonKind.Primary : ButtonKind.Secondary);
        online.Click += async (_, _) => await UpdateFromMicrosoftAsync(rt.Arch);
        var install = new GuardButton(rt.Installed ? L.T("rt.update") : L.T("rt.install"), Glyph.Document);
        install.Click += async (_, _) => await InstallFromCab();

        if (!rt.Installed)
        {
            var note = new WrapLabel(L.T(rt.Arch == Arch.X86 ? "rt.x86Note" : "rt.x64Note"), Body, NeutralText);
            card.SetContent(head, note, Ui.Row(online, install));
            return;
        }

        var users = s.Hosts.Count(h => h.Access == HostAccess.Allowed && h.Arch.RuntimeFor() == rt.Arch);
        var exe = AppPaths.RuntimeExe(rt.Arch);
        var hasRule = s.Rules.Any(r => r.IsAllow && r.Enabled && AppPaths.SamePath(r.ExpandedProgram, exe));
        var installedAt = rt.Marker?.InstalledAt is { } at && at.Year > 2000
            ? at.ToString("g", CultureInfo.CurrentCulture)
            : "—";

        var fields = StackCard.Fields(
            StackCard.Field(L.T("rt.version"), rt.Version ?? "?"),
            StackCard.Field(L.T("rt.installedAt"), installedAt),
            StackCard.Field(L.T("rt.path"), rt.Dir, full: true, mono: true),
            StackCard.Field(L.T("rt.signature"), string.IsNullOrEmpty(rt.Marker?.Signer) ? "—" : "✓ " + rt.Marker!.Signer,
                color: OkText),
            StackCard.Field(L.T("rt.previous"),
                rt.PreviousVersion != null ? L.F("rt.previousKept", rt.PreviousVersion) : "—"),
            StackCard.Field(L.T("rt.usedBy"), L.F("rt.usedByValue", users)),
            StackCard.Field(L.T("rt.rule"), hasRule ? L.T("rt.ruleActive") : L.T("rt.ruleMissing"),
                color: hasRule ? null : ErrText));

        var rollback = new GuardButton(L.T("rt.rollback"), Glyph.Undo) { Enabled = rt.PreviousVersion != null };
        rollback.Click += async (_, _) =>
        {
            if (!Ui.Confirm(_shell.Window, L.F("confirm.rollback", rt.PreviousVersion ?? "?", rt.Arch.Label()))) return;
            await _shell.RunAsync(() =>
            {
                RuntimeManager.Rollback(rt.Arch);
                Guard.SyncFirewall();
            });
        };
        var remove = new GuardButton(L.T("rt.remove"), Glyph.Delete, ButtonKind.Danger);
        remove.Click += async (_, _) =>
        {
            if (!Ui.Confirm(_shell.Window, L.F("confirm.removeRuntime", rt.Arch.Label()), danger: true)) return;
            await _shell.RunAsync(() =>
            {
                RuntimeManager.Remove(rt.Arch);
                Guard.SyncFirewall();
            });
        };

        card.SetContent(head, fields, Ui.Row(online, install, rollback, remove));
    }

    private void BindEvergreen(GuardSnapshot s)
    {
        var allBlocked = s.Evergreen.Count > 0 && s.Evergreen.All(e => IsBlocked(s, e));
        var pill = new Pill();
        if (allBlocked) pill.Set(L.T("pill.blocked"), NeutralBg, NeutralText);
        else pill.Set(L.T("pill.notBlocked"), ErrBg, ErrText);
        var head = StackCard.Heading(L.T("rt.evergreen.title"), pill);
        var root = new WrapLabel(AppPaths.EvergreenRoots.First() + "\\", Mono, TextMuted);

        var list = new List<Control> { head, root };
        if (s.Evergreen.Count == 0)
        {
            list.Add(new WrapLabel(L.T("rt.evergreen.none"), Body, TextMuted));
        }
        else
        {
            for (var i = 0; i < s.Evergreen.Count; i++)
            {
                var e = s.Evergreen[i];
                var blocked = IsBlocked(s, e);
                var ver = Ui.Lbl(e.Version, Strong);
                ver.Margin = P(0, 0, 10, 0);
                var tag = Ui.Lbl(i == 0 ? L.T("rt.evergreen.current") : L.T("rt.evergreen.leftover"), Small, TextMuted);
                tag.Margin = P(0, 1, 16, 0);
                var state = Ui.Lbl((blocked ? "✓ " : "✕ ") +
                                   (blocked ? L.T("rt.evergreen.ruleOk") : L.T("rt.evergreen.ruleMissing")),
                    Body, blocked ? OkText : ErrText);
                state.Margin = new Padding(0);
                var row = Ui.Row(ver, tag, state);
                row.Margin = P(0, 2, 0, 8);
                list.Add(row);
            }
        }
        list.Add(new WrapLabel(L.T("rt.evergreen.note"), Small, TextMuted));
        _evergreen.SetContent(list.ToArray());
    }

    private static bool IsBlocked(GuardSnapshot s, EvergreenInstall e) =>
        s.Rules.Any(r => !r.IsAllow && r.Enabled && AppPaths.SamePath(r.ExpandedProgram, e.ExePath));

    /// <summary>"Updates" card: automatic update switch, check now, latest version at Microsoft, manual steps.</summary>
    private void BindUpdates()
    {
        var head = StackCard.Heading(L.T("rt.updates.title"));

        var settings = Settings.Load();
        var auto = new CheckBox
        {
            Text = L.T("rt.updates.auto"),
            Checked = settings.AutoUpdateRuntime,
            AutoSize = true,
            Margin = P(0, 2, 0, 4),
        };
        auto.CheckedChanged += (_, _) =>
        {
            var st = Settings.Load();
            st.AutoUpdateRuntime = auto.Checked;
            st.Save();
            Log.Info(L.T(auto.Checked ? "log.autoUpdateOn" : "log.autoUpdateOff"));
        };
        var autoNote = new WrapLabel(L.T("rt.updates.autoNote"), Small, TextMuted);

        var latest = _available == null
            ? L.T("rt.updates.notChecked")
            : string.Join("   ·   ", AppPaths.RuntimeArchs
                .Select(a => (a, d: RuntimeUpdater.Latest(_available, a)))
                .Where(t => t.d != null)
                .Select(t => $"{t.a.Label()} {t.d!.Version}"));
        var fields = StackCard.Fields(
            StackCard.Field(L.T("rt.updates.latest"), latest, full: true),
            StackCard.Field(L.T("rt.updates.lastCheck"),
                _checkedAt?.ToString("g", CultureInfo.CurrentCulture)
                ?? settings.LastRuntimeCheck?.ToString("g", CultureInfo.CurrentCulture) ?? "—"),
            StackCard.Field(L.T("rt.updates.source"), "developer.microsoft.com"));

        var check = new GuardButton(L.T("rt.updates.checkNow"), Glyph.Refresh);
        check.Click += async (_, _) => await CheckAsync(showResult: true);
        var link = new LinkLabel
        {
            Text = L.T("rt.help.link"),
            AutoSize = true,
            LinkColor = Accent,
            Font = Strong,
            Margin = P(8, 9, 0, 0),
        };
        link.LinkClicked += (_, _) => MainForm.OpenUrl(DownloadUrl);
        var manual = new WrapLabel(L.T("rt.updates.manual"), Small, TextMuted) { Margin = P(0, 6, 0, 0) };

        _help.SetContent(head, auto, autoNote, fields, Ui.Row(check, link), manual);
        _grid.MarkDirty();
    }

    /// <summary>Reads the official download list. Returns null on error (already reported).</summary>
    private async Task<List<RuntimeDownload>?> CheckAsync(bool showResult)
    {
        _shell.ShowStatus(L.T("status.checkRuntime"));
        try
        {
            _available = await Task.Run(() => RuntimeUpdater.ListAsync());
            _checkedAt = DateTime.Now;
            _shell.ShowIdle();
            BindUpdates();
        }
        catch (Exception ex)
        {
            _shell.ShowIdle();
            Ui.Error(_shell.Window, L.F("err.runtimeCheck", ex.Message));
            return null;
        }

        if (showResult && _shell.Current is { } s)
        {
            var lines = AppPaths.RuntimeArchs
                .Select(a => (a, rt: a == Arch.X64 ? s.X64 : s.X86, d: RuntimeUpdater.Latest(_available, a)))
                .Where(t => t.rt.Installed && t.d != null)
                .Select(t => RuntimeUpdater.IsNewer(t.d!, t.rt)
                    ? L.F("rt.updates.newer", t.a.Label(), t.rt.Version ?? "?", t.d!.Version)
                    : L.F("rt.updates.current", t.a.Label(), t.rt.Version ?? "?"))
                .ToList();
            if (lines.Count == 0) lines.Add(L.T("rt.updates.nothingInstalled"));
            MessageBox.Show(_shell.Window, string.Join(Environment.NewLine, lines), L.T("rt.updates.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return _available;
    }

    /// <summary>Downloads the newest Fixed runtime for <paramref name="arch"/> from Microsoft and installs it.</summary>
    private async Task UpdateFromMicrosoftAsync(Arch arch)
    {
        var list = await CheckAsync(showResult: false);
        if (list == null || _shell.Current is not { } s) return;
        var rt = arch == Arch.X64 ? s.X64 : s.X86;
        var latest = RuntimeUpdater.Latest(list, arch);
        if (latest == null)
        {
            Ui.Error(_shell.Window, L.F("err.noRuntimeForArch", arch.Label()));
            return;
        }
        if (!RuntimeUpdater.IsNewer(latest, rt))
        {
            MessageBox.Show(_shell.Window, L.F("rt.updates.current", arch.Label(), rt.Version ?? "?"),
                L.T("rt.updates.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Ui.Confirm(_shell.Window, L.F("confirm.runtimeDownload", arch.Label(), latest.Version, rt.Version ?? "—")))
            return;
        using var dlg = new InstallDialog(latest);
        dlg.ShowDialog(_shell.Window);
        if (dlg.Changed) await _shell.RefreshAsync();
    }

    private async Task InstallFromCab()
    {
        using var dlg = new OpenFileDialog
        {
            Title = L.T("rt.install"),
            Filter = L.T("filter.cab"),
            CheckFileExists = true
        };
        if (dlg.ShowDialog(_shell.Window) != DialogResult.OK) return;
        using var install = new InstallDialog(dlg.FileName);
        install.ShowDialog(_shell.Window);
        if (install.Changed) await _shell.RefreshAsync();
    }
}
