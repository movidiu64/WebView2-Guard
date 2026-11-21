// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Globalization;
using WebView2Guard.Core;
using WebView2Guard.Localization;
using static WebView2Guard.UI.Theme;

namespace WebView2Guard.UI;

/// <summary>Tab 3: firewall rules of the group, the sync task and the WFC note.</summary>
internal sealed class FirewallPage : UserControl
{
    private readonly IShell _shell;
    private readonly CardGrid _grid = new();
    private readonly ListView _rules = Ui.List();
    private readonly Label _rulesError = Ui.Lbl(string.Empty, Body, ErrText);
    private readonly StackCard _task = new();
    private readonly StackCard _wfc = new();

    public FirewallPage(IShell shell)
    {
        _shell = shell;
        BackColor = Bg;

        var title = Ui.Lbl(L.F("fw.title", FirewallManager.Group), Theme.Heading);
        title.Margin = P(0, 6, 16, 8);
        var sync = new GuardButton(L.T("fw.sync"), Glyph.Refresh, ButtonKind.Primary);
        sync.Click += async (_, _) => await _shell.SyncAsync();
        var removeAll = new GuardButton(L.T("fw.removeAll"), Glyph.Delete, ButtonKind.Danger);
        removeAll.Click += async (_, _) =>
        {
            if (!Ui.Confirm(_shell.Window, L.T("confirm.removeAllRules"), danger: true)) return;
            await _shell.RunAsync(FirewallManager.RemoveAll);
        };
        var header = Ui.Row(title, sync, removeAll);

        _rules.Columns.Add(L.T("col.rule"), S(230));
        _rules.Columns.Add(L.T("col.action"), S(100));
        _rules.Columns.Add(L.T("col.program"), S(560));
        _rules.Columns.Add(L.T("col.state"), S(230));
        var rulesCard = new Card { Padding = new Padding(1), Tag = 250 };
        _rulesError.Dock = DockStyle.Top;
        _rulesError.Visible = false;
        _rulesError.Padding = P(12, 8, 12, 8);
        rulesCard.Controls.Add(_rules);
        rulesCard.Controls.Add(_rulesError);

        BuildWfc();
        Controls.Add(_grid);
        _grid.SetRows(new Control[] { header }, new Control[] { rulesCard }, new Control[] { _task, _wfc });
    }

    public void Bind(GuardSnapshot s)
    {
        _rules.BeginUpdate();
        _rules.Items.Clear();
        foreach (var r in s.Rules)
        {
            var item = new ListViewItem(r.Name) { UseItemStyleForSubItems = false, Font = Strong, ToolTipText = r.DisplayName };
            item.SubItems.Add(r.IsAllow ? L.T("fw.allow") : L.T("fw.block"), r.IsAllow ? OkText : NeutralText, Surface, Strong);
            item.SubItems.Add(r.Program, Theme.Text, Surface, Mono);
            string state;
            Color color;
            if (!r.Enabled) { state = L.T("fw.stateDisabled"); color = WarnText; }
            else if (!r.FileExists) { state = L.T("fw.stateMissing"); color = WarnText; }
            else { state = L.T("fw.stateOk"); color = OkText; }
            item.SubItems.Add(state, color, Surface, Body);
            if (color == WarnText)
                foreach (ListViewItem.ListViewSubItem sub in item.SubItems) sub.BackColor = WarnRow;
            _rules.Items.Add(item);
        }
        _rules.EndUpdate();
        _rulesError.Visible = s.RulesError != null;
        _rulesError.Text = s.RulesError != null ? L.F("fw.error", s.RulesError) : string.Empty;

        BindTask(s);
        _grid.PerformLayout();
    }

    private void BindTask(GuardSnapshot s)
    {
        var t = s.Task;
        var pill = new Pill();
        var active = t is { Exists: true };
        if (active) pill.Set(L.T("pill.active"), OkBg, OkText);
        else pill.Set(L.T("pill.inactive"), NeutralBg, NeutralText);
        var head = StackCard.Heading(L.T("task.title"), pill);

        if (s.TaskError != null)
        {
            _task.SetContent(head, new WrapLabel(L.F("task.error", s.TaskError), Body, ErrText));
            return;
        }

        if (!active)
        {
            var installed = File.Exists(AppPaths.InstalledExe);
            var note = new WrapLabel(installed ? L.T("task.offNote") : L.T("task.needsInstall"), Body, NeutralText);
            var enable = new GuardButton(L.T("task.enable"), Glyph.Play, ButtonKind.Primary) { Enabled = installed };
            enable.Click += async (_, _) => await _shell.RunAsync(SyncTask.Create);
            _task.SetContent(head, note, Ui.Row(enable));
            return;
        }

        var lastRun = t!.LastRun is { } lr
            ? L.F("task.lastRunValue", lr.ToString("g", CultureInfo.CurrentCulture),
                t.LastResult == 0 ? L.T("task.ok") : L.F("task.failed", "0x" + t.LastResult.ToString("X", CultureInfo.InvariantCulture)))
            : L.T("task.never");
        var fields = StackCard.Fields(
            StackCard.Field(L.T("task.name"), SyncTask.FullName, mono: true),
            StackCard.Field(L.T("task.runsAs"), "SYSTEM"),
            StackCard.Field(L.T("task.triggers"), L.T("task.triggersValue")),
            StackCard.Field(L.T("task.lastRun"), lastRun, color: t.LastResult == 0 || t.LastRun == null ? null : WarnText),
            StackCard.Field(L.T("task.exe"), t.Command, full: true, mono: true));

        var run = new GuardButton(L.T("task.runNow"), Glyph.Play);
        run.Click += async (_, _) => await _shell.RunAsync(SyncTask.RunNow);
        var disable = new GuardButton(L.T("task.disable"), Glyph.Pause);
        disable.Click += async (_, _) => await _shell.RunAsync(SyncTask.Delete);
        _task.SetContent(head, fields, Ui.Row(run, disable));
    }

    private void BuildWfc()
    {
        var head = StackCard.Heading(L.T("wfc.title"));
        var text = new WrapLabel(L.T("wfc.text"), Body);
        var group = new TextBox
        {
            Text = FirewallManager.Group,
            ReadOnly = true,
            Font = Mono,
            Width = S(170),
            Margin = P(0, 4, 8, 8),
            AccessibleName = L.T("wfc.groupName")
        };
        var copy = new GuardButton(L.T("wfc.copy"), Glyph.Copy);
        copy.Click += (_, _) => { try { Clipboard.SetText(FirewallManager.Group); } catch { /* clipboard busy */ } };
        var note = new WrapLabel(L.T("wfc.note"), Small, TextMuted);
        _wfc.SetContent(head, text, Ui.Row(group, copy), note);
    }
}
