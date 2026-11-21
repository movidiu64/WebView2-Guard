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

    public RuntimePage(IShell shell)
    {
        _shell = shell;
        BackColor = Bg;
        Controls.Add(_grid);
        _warning.Visible = false;
        BuildHelp();
        _grid.SetRows(new Control[] { _warning }, new Control[] { _x64, _x86 }, new Control[] { _evergreen, _help });
    }

    public void Bind(GuardSnapshot s)
    {
        SuspendLayout();
        BindWarning(s);
        BindRuntime(_x64, s.X64, s);
        BindRuntime(_x86, s.X86, s);
        BindEvergreen(s);
        ResumeLayout();
        _grid.PerformLayout();
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
            BackColor = Color.Transparent
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

        var install = new GuardButton(rt.Installed ? L.T("rt.update") : L.T("rt.install"), Glyph.Download,
            rt.Installed || s.Hosts.Any(h => h.Access == HostAccess.Allowed && h.Arch.RuntimeFor() == rt.Arch)
                ? ButtonKind.Primary
                : ButtonKind.Secondary);
        install.Click += async (_, _) => await InstallFromCab();

        if (!rt.Installed)
        {
            var note = new WrapLabel(L.T(rt.Arch == Arch.X86 ? "rt.x86Note" : "rt.x64Note"), Body, NeutralText);
            card.SetContent(head, note, Ui.Row(install));
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

        card.SetContent(head, fields, Ui.Row(install, rollback, remove));
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

    private void BuildHelp()
    {
        var head = StackCard.Heading(L.T("rt.help.title"));
        var steps = new List<Control> { head };
        var n = 1;
        foreach (var key in new[] { "rt.help.step1", "rt.help.step2", "rt.help.step3", "rt.help.step4" })
        {
            var num = Ui.Lbl(n++ + ".", Strong);
            num.Margin = P(0, 0, 6, 0);
            var text = new WrapLabel(L.T(key), Body) { Margin = new Padding(0) };
            var row = Ui.Row(num, text);
            row.WrapContents = false;
            row.Margin = P(0, 0, 0, 8);
            steps.Add(row);
        }
        var link = new LinkLabel
        {
            Text = L.T("rt.help.link"),
            AutoSize = true,
            LinkColor = Accent,
            Font = Strong,
            Margin = P(0, 4, 0, 0),
            BackColor = Color.Transparent
        };
        link.LinkClicked += (_, _) => MainForm.OpenUrl(DownloadUrl);
        steps.Add(link);
        _help.SetContent(steps.ToArray());
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
