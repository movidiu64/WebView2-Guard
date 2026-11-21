// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Globalization;
using System.Text;
using WebView2Guard.Core;
using WebView2Guard.Localization;
using static WebView2Guard.UI.Theme;

namespace WebView2Guard.UI;

/// <summary>Tab 4: the log file, newest first, with a level filter.</summary>
internal sealed class LogPage : UserControl
{
    private static readonly Font MonoBold = new(Mono, FontStyle.Bold);
    private readonly ListView _list = Ui.List();
    private readonly GuardButton[] _filters;
    private LogLevel? _filter;
    private List<LogEntry> _entries = new();

    public LogPage()
    {
        BackColor = Bg;
        DoubleBuffered = true;

        _filters = new[]
        {
            new GuardButton(L.T("log.all"), null, ButtonKind.Primary),
            new GuardButton(L.T("log.info")),
            new GuardButton(L.T("log.warn")),
            new GuardButton(L.T("log.error"))
        };
        LogLevel?[] levels = { null, LogLevel.Info, LogLevel.Warn, LogLevel.Error };
        for (var i = 0; i < _filters.Length; i++)
        {
            var level = levels[i];
            var button = _filters[i];
            button.Click += (_, _) => SetFilter(level, button);
        }
        var copy = new GuardButton(L.T("log.copy"), Glyph.Copy);
        copy.Click += (_, _) => CopyVisible();
        var open = new GuardButton(L.T("log.openFile"), Glyph.Document);
        open.Click += (_, _) => MainForm.RevealFile(AppPaths.LogFile);

        var toolbar = Ui.Row(_filters.Cast<Control>().Concat(new Control[] { copy, open }).ToArray());
        toolbar.Dock = DockStyle.Top;
        _filters[^1].Margin = P(0, 0, 24, 8);

        _list.Columns.Add(L.T("col.time"), S(150));
        _list.Columns.Add(L.T("col.level"), S(90));
        _list.Columns.Add(L.T("col.message"), S(900));
        _list.Font = Mono;
        var card = new Card { Padding = new Padding(1), Dock = DockStyle.Fill };
        card.Controls.Add(_list);

        var note = Ui.Lbl(L.F("log.fileNote", AppPaths.LogFile), Small, TextMuted);
        note.Dock = DockStyle.Bottom;
        note.Padding = P(0, 8, 0, 0);

        Controls.Add(card);
        Controls.Add(note);
        Controls.Add(toolbar);

        Log.Written += OnWritten;
        Disposed += (_, _) => Log.Written -= OnWritten;
    }

    private void OnWritten(LogEntry e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                _entries.Insert(0, e);
                if (Visible && Matches(e)) _list.Items.Insert(0, Item(e));
            }));
        }
        catch (InvalidOperationException)
        {
            // window closing
        }
    }

    public bool Loaded { get; private set; }

    public void Reload()
    {
        Loaded = true;
        _entries = Log.ReadRecent();
        Fill();
    }

    private void SetFilter(LogLevel? level, GuardButton active)
    {
        _filter = level;
        foreach (var b in _filters) b.Kind = b == active ? ButtonKind.Primary : ButtonKind.Secondary;
        Fill();
    }

    private bool Matches(LogEntry e) => _filter == null || e.Level == _filter;

    private void Fill()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _entries.Where(Matches)) _list.Items.Add(Item(e));
        _list.EndUpdate();
    }

    private static ListViewItem Item(LogEntry e)
    {
        var (text, color, back) = e.Level switch
        {
            LogLevel.Warn => (L.T("level.warn"), WarnText, Surface),
            LogLevel.Error => (L.T("level.error"), ErrText, ErrBg),
            _ => (L.T("level.info"), NeutralText, Surface)
        };
        var item = new ListViewItem(e.Time.ToString("dd.MM HH:mm:ss", CultureInfo.InvariantCulture))
        {
            UseItemStyleForSubItems = false,
            ForeColor = TextMuted,
            BackColor = back,
            ToolTipText = e.Message
        };
        item.SubItems.Add(text, color, back, MonoBold);
        item.SubItems.Add(e.Message, Theme.Text, back, Mono);
        return item;
    }

    private void CopyVisible()
    {
        var sb = new StringBuilder();
        foreach (var e in _entries.Where(Matches)) sb.AppendLine(Log.Format(e));
        try
        {
            if (sb.Length > 0) Clipboard.SetText(sb.ToString());
        }
        catch
        {
            // clipboard busy
        }
    }
}
