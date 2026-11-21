// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Globalization;
using WebView2Guard.Core;
using WebView2Guard.Localization;
using static WebView2Guard.UI.Theme;

namespace WebView2Guard.UI;

/// <summary>Tab 1: apps that use WebView2, with Allow / Block.</summary>
internal sealed class AppsPage : UserControl
{
    private readonly IShell _shell;
    private readonly ListView _list = Ui.List();
    private readonly GuardButton _refresh = new(L.T("apps.refresh"), Glyph.Refresh);
    private readonly GuardButton _allow = new(L.T("apps.allow"), Glyph.Check, ButtonKind.Primary);
    private readonly GuardButton _block = new(L.T("apps.block"), Glyph.Blocked);
    private readonly GuardButton _add = new(L.T("apps.add"), Glyph.Add);
    private readonly GuardButton _forget = new(L.T("apps.forget"), Glyph.Delete);
    private readonly CheckBox _onlyRunning = new() { Text = L.T("apps.onlyRunning"), AutoSize = true };
    private readonly TextBox _search = new() { Width = S(180) };
    private readonly Label _counts = Ui.Lbl(string.Empty, Small, TextMuted);
    private readonly Details _details;
    private List<HostRow> _rows = new();
    private HostRow? _detailsRow;
    private bool _detailsShown;

    public AppsPage(IShell shell)
    {
        _shell = shell;
        BackColor = Bg;
        DoubleBuffered = true;
        _details = new Details(this);

        // toolbar
        var searchLabel = Ui.Lbl(L.T("apps.search"), Body, TextMuted);
        searchLabel.Margin = P(16, 8, 6, 0);
        _search.Margin = P(0, 4, 0, 8);
        _search.AccessibleName = L.T("apps.search");
        _search.PlaceholderText = L.T("apps.searchPlaceholder");
        _onlyRunning.Margin = P(8, 7, 0, 8);
        var toolbar = Ui.Row(_refresh, _allow, _block, _add, _forget, _onlyRunning, searchLabel, _search);
        toolbar.Dock = DockStyle.Top;
        toolbar.Margin = new Padding(0);

        // list
        _list.Columns.Add(L.T("col.app"), S(260));
        _list.Columns.Add(L.T("col.access"), S(110));
        _list.Columns.Add(L.T("col.running"), S(120));
        _list.Columns.Add(L.T("col.runtime"), S(190));
        _list.Columns.Add(L.T("col.arch"), S(60));
        _list.Columns.Add(L.T("col.lastSeen"), S(140));
        _list.SelectedIndexChanged += (_, _) => UpdateSelection();
        _list.DoubleClick += (_, _) => { if (_allow.Enabled) _allow.PerformClick(); };
        var listCard = new Card { Padding = new Padding(1), Dock = DockStyle.Fill, Margin = P(0, 0, 12, 0) };
        listCard.Controls.Add(_list);

        _details.Dock = DockStyle.Fill;
        _details.Margin = new Padding(0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Bg, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(330)));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(listCard, 0, 0);
        body.Controls.Add(_details, 1, 0);

        // footer
        var hintIcon = Ui.GlyphLabel(Glyph.Info, TextMuted, 9f);
        var hint = Ui.Lbl(L.T("footer.hint"), Small, TextMuted);
        hint.Margin = P(0, 1, 0, 0);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 3, RowCount = 1, Height = S(30), BackColor = Bg };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hintIcon.Anchor = AnchorStyles.Left;
        hint.Anchor = AnchorStyles.Left;
        _counts.Anchor = AnchorStyles.Right;
        _counts.Margin = new Padding(0);
        footer.Controls.Add(hintIcon, 0, 0);
        footer.Controls.Add(hint, 1, 0);
        footer.Controls.Add(_counts, 2, 0);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(toolbar);

        _refresh.Click += async (_, _) => await _shell.RefreshAsync();
        _allow.Click += async (_, _) => await AllowSelected();
        _block.Click += async (_, _) => await BlockSelected();
        _add.Click += async (_, _) => await AddExe();
        _forget.Click += async (_, _) => await ForgetSelected();
        _onlyRunning.CheckedChanged += (_, _) => Fill();
        _search.TextChanged += (_, _) => Fill();
        UpdateSelection();
    }

    public HostRow? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as HostRow : null;

    public void Bind(GuardSnapshot s)
    {
        if (_rows.SequenceEqual(s.Hosts) && _list.Items.Count > 0) return;
        _rows = s.Hosts;
        Fill();
    }

    private void Fill()
    {
        var keep = Selected?.ExeName;
        var q = _search.Text.Trim();
        var rows = _rows
            .Where(r => !_onlyRunning.Checked || r.Running)
            .Where(r => q.Length == 0 || r.ExeName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Running)
            .ThenBy(r => r.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sameRows = _list.Items.Count == rows.Count &&
                       _list.Items.Cast<ListViewItem>()
                           .Select(i => ((HostRow)i.Tag!).ExeName)
                           .SequenceEqual(rows.Select(r => r.ExeName), StringComparer.OrdinalIgnoreCase);

        _list.BeginUpdate();
        if (sameRows)
        {
            // Same rows in the same order: update in place, so selection and scroll position stay put.
            for (var i = 0; i < rows.Count; i++)
                if (!Equals(_list.Items[i].Tag, rows[i]))
                    Populate(_list.Items[i], rows[i]);
        }
        else
        {
            var top = _list.TopItem?.Index ?? 0;
            _list.Items.Clear();
            foreach (var r in rows)
            {
                var item = new ListViewItem { UseItemStyleForSubItems = false, Font = Strong };
                for (var c = 1; c < _list.Columns.Count; c++) item.SubItems.Add(string.Empty);
                Populate(item, r);
                _list.Items.Add(item);
                if (keep != null && r.ExeName.Equals(keep, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
            }
            if (top > 0 && top < _list.Items.Count) _list.TopItem = _list.Items[top];
        }
        _list.EndUpdate();

        _counts.Text = L.F("footer.counts", _rows.Count,
            _rows.Count(r => r.Access == HostAccess.Allowed), _rows.Count(r => r.NeedsRestart));
        UpdateSelection();
    }

    private static void Populate(ListViewItem item, HostRow r)
    {
        item.Tag = r;
        item.Text = r.ExeName == "*" ? L.T("apps.globalName") : r.ExeName;
        item.ToolTipText = r.Path ?? r.PolicyFolder ?? string.Empty;

        var back = r.Access == HostAccess.External ? ErrBg : r.NeedsRestart ? WarnRow : Surface;
        var muted = !r.Running && r.Access != HostAccess.External;
        var (accessText, accessColor) = AccessLabel(r.Access);

        Set(item.SubItems[0], item.Text, Theme.Text, Strong);
        Set(item.SubItems[1], accessText, accessColor, Strong);
        Set(item.SubItems[2], r.Running ? L.F("running.yes", r.Pid) : L.T("running.no"), muted ? TextMuted : Theme.Text, Body);
        Set(item.SubItems[3], RuntimeText(r), r.NeedsRestart ? WarnText : muted ? TextMuted : Theme.Text, r.NeedsRestart ? Strong : Body);
        Set(item.SubItems[4], r.Arch == Arch.Unknown ? "—" : r.Arch.Label(), muted ? TextMuted : Theme.Text, Body);
        Set(item.SubItems[5], SeenText(r), muted ? TextMuted : Theme.Text, Body);

        void Set(ListViewItem.ListViewSubItem sub, string text, Color fore, Font font)
        {
            sub.Text = text;
            sub.ForeColor = fore;
            sub.BackColor = back;
            sub.Font = font;
        }
    }

    public static (string Text, Color Color) AccessLabel(HostAccess a) => a switch
    {
        HostAccess.Allowed => (L.T("access.allowed"), OkText),
        HostAccess.External => (L.T("access.external"), ErrText),
        _ => (L.T("access.blocked"), NeutralText)
    };

    public static string RuntimeText(HostRow r)
    {
        var text = r.RuntimeKind switch
        {
            RuntimeKind.Evergreen => L.T("rt.evergreen"),
            RuntimeKind.Allowed => L.F("rt.allowed", ArchOfRuntime(r.RuntimeInUse)),
            RuntimeKind.Other => L.T("rt.other"),
            _ => "—"
        };
        return r.NeedsRestart ? text + " · " + L.T("rt.restart") : text;
    }

    private static string ArchOfRuntime(string? exe) =>
        AppPaths.IsUnder(exe, AppPaths.RuntimeDir(Arch.X86)) ? "x86" : "x64";

    public static string SeenText(HostRow r)
    {
        if (r.Running) return L.T("seen.now");
        if (r.LastSeen is not { } t) return "—";
        var time = t.ToString("HH:mm", CultureInfo.CurrentCulture);
        if (t.Date == DateTime.Today) return L.F("seen.today", time);
        if (t.Date == DateTime.Today.AddDays(-1)) return L.F("seen.yesterday", time);
        return t.ToString("g", CultureInfo.CurrentCulture);
    }

    private void UpdateSelection()
    {
        var r = Selected;
        var isGlobal = r?.ExeName == "*";
        _allow.Enabled = r != null && !isGlobal && r.Access != HostAccess.Allowed;
        _block.Enabled = r != null && r.Access != HostAccess.Blocked;
        _forget.Enabled = r != null && r.Access == HostAccess.Blocked && !isGlobal;
        if (!_detailsShown || !Equals(_detailsRow, r))
        {
            _details.Show(r);
            _detailsRow = r;
            _detailsShown = true;
        }
    }

    // ---- actions -------------------------------------------------------------------------------

    internal async Task AllowSelected()
    {
        var r = Selected;
        if (r == null || r.ExeName == "*") return;
        if (r.Access == HostAccess.External &&
            !Ui.Confirm(_shell.Window, L.F("confirm.replaceExternal", r.ExeName, r.PolicyFolder ?? "?")))
            return;

        var arch = r.Arch;
        if (arch == Arch.Unknown && r.Path != null && File.Exists(r.Path))
        {
            var (peArch, managed) = PeArch.FromFile(r.Path);
            if (!(managed && peArch == Arch.X86)) arch = peArch;
        }
        await _shell.RunAsync(() => Guard.Allow(r.ExeName, arch));
    }

    internal async Task BlockSelected()
    {
        var r = Selected;
        if (r == null) return;
        if (r.Access == HostAccess.External &&
            !Ui.Confirm(_shell.Window, L.F("confirm.removeExternal", r.ExeName, r.PolicyFolder ?? "?"), danger: true))
            return;
        await _shell.RunAsync(() => Guard.Block(r.ExeName));
    }

    private async Task AddExe()
    {
        using var dlg = new OpenFileDialog
        {
            Title = L.T("dlg.addExe"),
            Filter = L.T("filter.exe"),
            CheckFileExists = true
        };
        if (dlg.ShowDialog(_shell.Window) != DialogResult.OK) return;
        var path = dlg.FileName;
        await _shell.RunAsync(() => HostCatalog.AddManual(path));
    }

    private async Task ForgetSelected()
    {
        var r = Selected;
        if (r == null) return;
        if (r.Access != HostAccess.Blocked)
        {
            Ui.Error(_shell.Window, L.T("msg.forgetAllowedFirst"));
            return;
        }
        await _shell.RunAsync(() => HostCatalog.Forget(r.ExeName));
    }

    // ---- details panel ---------------------------------------------------------------------------

    private sealed class Details : Card
    {
        private readonly AppsPage _page;
        private readonly FlowLayoutPanel _stack = Ui.Column();
        private readonly ToolTip _tip = new();

        public Details(AppsPage page)
        {
            _page = page;
            _stack.Dock = DockStyle.Fill;
            _stack.AutoSize = false;
            _stack.AutoScroll = true;
            Controls.Add(_stack);
        }

        private int Inner => Math.Max(S(200), Width - Padding.Horizontal - S(8));

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            foreach (Control c in _stack.Controls)
                if (c is Label l && l.AutoSize) l.MaximumSize = new Size(Inner, 0);
        }

        public void Show(HostRow? r)
        {
            _stack.SuspendLayout();
            foreach (Control c in _stack.Controls.Cast<Control>().ToList()) c.Dispose();
            _stack.Controls.Clear();

            if (r == null)
            {
                _stack.Controls.Add(Wrap(Ui.Lbl(L.T("details.none"), Body, TextMuted)));
                _stack.ResumeLayout();
                return;
            }

            var name = Ui.Lbl(r.ExeName == "*" ? L.T("apps.globalName") : r.ExeName, Theme.Heading);
            name.Margin = P(0, 0, 10, 0);
            var pill = new Pill();
            var (text, color) = AccessLabel(r.Access);
            pill.Set(text, r.Access switch
            {
                HostAccess.Allowed => OkBg,
                HostAccess.External => ErrBg,
                _ => NeutralBg
            }, color);
            var head = Ui.Row(name, pill);
            head.Margin = P(0, 0, 0, 12);
            _stack.Controls.Add(head);

            AddField(L.T("details.path"), r.Path ?? "—", mono: true);
            AddField(L.T("details.process"), r.Running
                ? L.F("details.processValue", r.Pid, r.Arch == Arch.Unknown ? "?" : r.Arch.Label(), r.ProcessCount)
                : L.T("details.notRunning"));
            AddField(L.T("details.runtime"), r.RuntimeInUse ?? "—", mono: true,
                color: r.NeedsRestart ? WarnText : null);
            AddField(L.T("details.policy"), r.PolicyFolder ?? L.T("details.policyNone"), mono: r.PolicyFolder != null);

            var buttons = Ui.Row();
            buttons.Margin = P(0, 4, 0, 8);
            if (r.ExeName != "*" && r.Access != HostAccess.Allowed)
            {
                var allow = new GuardButton(L.T("apps.allow"), Glyph.Check, ButtonKind.Primary);
                allow.Click += async (_, _) => await _page.AllowSelected();
                buttons.Controls.Add(allow);
            }
            if (r.Access != HostAccess.Blocked)
            {
                var block = new GuardButton(L.T("apps.block"), Glyph.Blocked);
                block.Click += async (_, _) => await _page.BlockSelected();
                buttons.Controls.Add(block);
            }
            if (r.Path != null && File.Exists(r.Path))
            {
                var folder = new GuardButton(L.T("details.openFolder"), Glyph.Folder);
                var path = r.Path;
                folder.Click += (_, _) => MainForm.RevealFile(path);
                buttons.Controls.Add(folder);
            }
            if (r.ExeName != "*")
            {
                var copy = new GuardButton(L.T("details.copy"), Glyph.Copy) { IconOnly = true };
                var exe = r.ExeName;
                copy.Click += (_, _) => { try { Clipboard.SetText(exe); } catch { /* clipboard busy */ } };
                _tip.SetToolTip(copy, L.T("details.copy"));
                buttons.Controls.Add(copy);
            }
            _stack.Controls.Add(buttons);

            var note = r.Access == HostAccess.Allowed
                ? L.T("details.noteBlock")
                : L.F("details.noteAllow", r.ExeName, r.Arch.RuntimeFor().Label());
            _stack.Controls.Add(Wrap(Ui.Lbl(note, Small, TextMuted)));
            _stack.ResumeLayout();
        }

        private void AddField(string caption, string value, bool mono = false, Color? color = null)
        {
            var c = Ui.Lbl(caption, Small, TextMuted);
            c.Margin = P(0, 0, 0, 2);
            _stack.Controls.Add(c);
            var v = Wrap(Ui.Lbl(value, mono ? Mono : Body, color));
            v.Margin = P(0, 0, 0, 10);
            _stack.Controls.Add(v);
        }

        private Label Wrap(Label l)
        {
            l.MaximumSize = new Size(Inner, 0);
            return l;
        }
    }
}
