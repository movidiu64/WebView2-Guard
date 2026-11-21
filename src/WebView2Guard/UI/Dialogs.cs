// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using WebView2Guard.Core;
using WebView2Guard.Localization;
using static WebView2Guard.UI.Theme;

namespace WebView2Guard.UI;

/// <summary>Base look for the app's dialogs: white body, grey footer with buttons on the right.</summary>
internal abstract class GuardDialog : Form
{
    protected readonly FlowLayoutPanel BodyPanel = Ui.Column();
    protected readonly FlowLayoutPanel Footer = new()
    {
        Dock = DockStyle.Bottom,
        FlowDirection = FlowDirection.RightToLeft,
        BackColor = Color.FromArgb(0xF7, 0xF7, 0xF7),
        WrapContents = false
    };

    protected GuardDialog(string title, string glyph, Color glyphColor)
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = title;
        Font = Body;
        BackColor = Surface;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(S(560), S(440));

        Footer.Height = S(66);
        Footer.Padding = P(16, 16, 16, 12);
        Footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, 0, Footer.Width, 0);
        };

        var icon = Ui.GlyphLabel(glyph, glyphColor, 13f);
        icon.Margin = P(0, 3, 10, 0);
        var heading = Ui.Lbl(title, Title);
        var head = Ui.Row(icon, heading);
        head.Margin = P(0, 0, 0, 6);

        BodyPanel.Location = new Point(S(22), S(18));
        BodyPanel.MaximumSize = new Size(S(516), 0);
        BodyPanel.Controls.Add(head);

        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Surface };
        scroller.Controls.Add(BodyPanel);
        Controls.Add(scroller);
        Controls.Add(Footer);
    }

    protected static Label Wrapped(string text, Font? font = null, Color? color = null)
    {
        var l = Ui.Lbl(text, font, color, maxWidth: 510);
        l.Margin = P(0, 0, 0, 10);
        return l;
    }
}

/// <summary>Installs a Fixed runtime from a .cab and shows each verification step.</summary>
internal sealed class InstallDialog : GuardDialog
{
    private readonly string _cab;
    private readonly (Label Icon, Label Text)[] _steps;
    private readonly Card _busyCard = new() { Fill = WarnBg, Stroke = WarnBorder, Padding = P(12) };
    private readonly Label _busyText = Ui.Lbl(string.Empty, Body, WarnTextDark, maxWidth: 480);
    private readonly Label _result = Wrapped(string.Empty);
    private readonly GuardButton _cancel = new(L.T("inst.cancel"));
    private readonly GuardButton _retry = new(L.T("inst.retry"), Glyph.Refresh, ButtonKind.Primary);
    private readonly GuardButton _close = new(L.T("inst.close"), null, ButtonKind.Primary);
    private int _current = -1;
    private bool _running;

    /// <summary>True if anything on disk or in the firewall changed.</summary>
    public bool Changed { get; private set; }

    public InstallDialog(string cabPath)
        : base(L.T("inst.title"), Glyph.Download, Accent)
    {
        _cab = cabPath;
        var file = Ui.Lbl(Path.GetFileName(cabPath), Mono, TextMuted, maxWidth: 510);
        file.Margin = P(0, 0, 0, 14);
        BodyPanel.Controls.Add(file);

        var keys = new[] { "inst.step.extract", "inst.step.signature", "inst.step.arch", "inst.step.inUse", "inst.step.replace" };
        _steps = new (Label, Label)[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            var icon = Ui.GlyphLabel(Glyph.Circle, TextMuted, 11f);
            icon.Margin = P(0, 1, 10, 0);
            var text = Ui.Lbl(L.T(keys[i]), Body, TextMuted, maxWidth: 470);
            text.Margin = new Padding(0);
            var row = Ui.Row(icon, text);
            row.WrapContents = false;
            row.Margin = P(0, 0, 0, 10);
            BodyPanel.Controls.Add(row);
            _steps[i] = (icon, text);
        }

        _busyCard.AutoSize = true;
        _busyCard.Margin = P(0, 6, 0, 10);
        _busyCard.Visible = false;
        _busyCard.Controls.Add(_busyText);
        _busyText.Location = new Point(S(12), S(10));
        BodyPanel.Controls.Add(_busyCard);
        _result.Visible = false;
        BodyPanel.Controls.Add(_result);

        _close.Visible = false;
        _retry.Visible = false;
        _cancel.Click += (_, _) => Close();
        _close.Click += (_, _) => Close();
        _retry.Click += async (_, _) => await RunInstall();
        Footer.Controls.Add(_close);
        Footer.Controls.Add(_retry);
        Footer.Controls.Add(_cancel);
        CancelButton = _cancel;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RunInstall();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running) e.Cancel = true; // never interrupt a folder swap
        base.OnFormClosing(e);
    }

    private void Mark(int index, string glyph, Color color, string? text = null)
    {
        var (icon, label) = _steps[index];
        icon.Text = glyph;
        icon.ForeColor = color;
        label.ForeColor = color == TextMuted ? TextMuted : color == OkText ? Theme.Text : color;
        if (text != null) label.Text = text;
    }

    private void Advance(InstallProgress p)
    {
        var index = p.Step switch
        {
            InstallStep.Extract => 0,
            InstallStep.Signature => 1,
            InstallStep.Architecture => 2,
            InstallStep.InUse => 3,
            InstallStep.Replace => 4,
            _ => 5
        };
        for (var i = Math.Max(0, _current); i < Math.Min(index, _steps.Length); i++)
        {
            var text = i switch
            {
                1 => L.T("inst.step.signatureOk"),
                3 => L.T("inst.step.inUseOk"),
                _ => null
            };
            Mark(i, Glyph.Check, OkText, text);
        }
        if (p.Step == InstallStep.Architecture)
        {
            Mark(2, Glyph.Check, OkText, L.F("inst.step.archValue", p.Detail));
            index = 3;
        }
        if (index < _steps.Length && p.Step != InstallStep.InUse)
            Mark(index, Glyph.Refresh, Accent);
        _current = index;
    }

    private async Task RunInstall()
    {
        _running = true;
        _cancel.Enabled = false;
        _retry.Visible = false;
        _busyCard.Visible = false;
        _result.Visible = false;
        UseWaitCursor = true;
        _current = -1;
        for (var i = 0; i < _steps.Length; i++) Mark(i, Glyph.Circle, TextMuted);
        _steps[1].Text.Text = L.T("inst.step.signature");
        _steps[2].Text.Text = L.T("inst.step.arch");
        _steps[3].Text.Text = L.T("inst.step.inUse");

        var progress = new Progress<InstallProgress>(Advance);
        try
        {
            var (arch, version) = await Task.Run(() => RuntimeManager.InstallFromCab(_cab, progress));
            Changed = true;
            await Task.Run(() => Guard.SyncFirewall());
            for (var i = 0; i < _steps.Length; i++)
                if (_steps[i].Icon.Text != Glyph.Check) Mark(i, Glyph.Check, OkText);
            ShowResult(L.F("inst.done", arch.Label(), version), OkText);
            Finish(success: true);
        }
        catch (RuntimeBusyException busy)
        {
            Mark(3, Glyph.Warning, WarnText, L.T("inst.step.inUseBusy"));
            _busyText.Text = L.T("inst.busy") + Environment.NewLine + Environment.NewLine +
                             string.Join(Environment.NewLine, busy.Processes.Select(p => $"{p.Name} · PID {p.Pid}"));
            _busyCard.Visible = true;
            _retry.Visible = true;
            _cancel.Enabled = true;
            _running = false;
            UseWaitCursor = false;
        }
        catch (Exception ex)
        {
            if (_current >= 0 && _current < _steps.Length) Mark(_current, Glyph.Error, ErrText);
            Log.Error(L.F("inst.failed", ex.Message));
            ShowResult(L.F("inst.failed", ex.Message), ErrText);
            Finish(success: false);
        }
    }

    private void ShowResult(string text, Color color)
    {
        _result.Text = text;
        _result.ForeColor = color;
        _result.Font = Strong;
        _result.Visible = true;
    }

    private void Finish(bool success)
    {
        _running = false;
        UseWaitCursor = false;
        _cancel.Visible = false;
        _retry.Visible = false;
        _close.Visible = true;
        _close.Kind = success ? ButtonKind.Primary : ButtonKind.Secondary;
        AcceptButton = _close;
        CancelButton = _close;
        _close.Focus();
    }
}

/// <summary>"Uninstall everything" with a choice of what to remove.</summary>
internal sealed class UndoDialog : GuardDialog
{
    private readonly CheckBox _policies;
    private readonly CheckBox _rules;
    private readonly CheckBox _task;
    private readonly CheckBox _runtimes;

    public Guard.UndoOptions Options =>
        new(_policies.Checked, _rules.Checked, _task.Checked, _runtimes.Checked);

    public UndoDialog(int policyCount, int ruleCount)
        : base(L.T("undo.title"), Glyph.Undo, ErrText)
    {
        BodyPanel.Controls.Add(Wrapped(L.T("undo.text"), Body, Color.FromArgb(0x4A, 0x4A, 0x4A)));
        var legend = Ui.Lbl(L.T("undo.legend"), Strong, TextMuted);
        legend.Margin = P(0, 4, 0, 6);
        BodyPanel.Controls.Add(legend);

        _policies = Check(L.F("undo.policies", policyCount), true);
        _rules = Check(L.F("undo.rules", ruleCount), true);
        _task = Check(L.T("undo.task"), true);
        _runtimes = Check(L.T("undo.runtimes"), false);

        var note = Wrapped(L.T("undo.note"), Small, Color.FromArgb(0x4A, 0x4A, 0x4A));
        note.Margin = P(0, 10, 0, 0);
        BodyPanel.Controls.Add(note);

        var ok = new GuardButton(L.T("undo.confirm"), null, ButtonKind.DangerFilled) { DialogResult = DialogResult.OK };
        var cancel = new GuardButton(L.T("inst.cancel")) { DialogResult = DialogResult.Cancel };
        Footer.Controls.Add(ok);
        Footer.Controls.Add(cancel);
        CancelButton = cancel;
    }

    private CheckBox Check(string text, bool on)
    {
        var c = new CheckBox
        {
            Text = text,
            Checked = on,
            AutoSize = true,
            Margin = P(0, 4, 0, 8),
            MaximumSize = new Size(S(510), 0)
        };
        BodyPanel.Controls.Add(c);
        return c;
    }
}
