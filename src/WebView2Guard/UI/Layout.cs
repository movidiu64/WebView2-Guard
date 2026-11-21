// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using static WebView2Guard.UI.Theme;

namespace WebView2Guard.UI;

/// <summary>Auto-sized label that wraps at the width given by its card.</summary>
internal sealed class WrapLabel : Label
{
    public WrapLabel(string text, Font? font = null, Color? color = null)
    {
        Text = text;
        Font = font ?? Body;
        ForeColor = color ?? Theme.Text;
        AutoSize = true;
        UseMnemonic = false;
        Margin = P(0, 0, 0, 8);
    }
}

/// <summary>A card whose content is a vertical stack; it measures its own height for a given width.</summary>
internal sealed class StackCard : Card
{
    public const string FieldsTag = "fields";
    public const string HalfTag = "field-half";
    public const string FullTag = "field-full";

    public FlowLayoutPanel Body { get; }

    public StackCard()
    {
        Body = Ui.Column();
        Body.Location = new Point(Padding.Left, Padding.Top);
        Controls.Add(Body);
    }

    private int _version;
    private int _measuredVersion = -1;
    private int _measuredWidth = -1;
    private int _measuredHeight;

    public void SetContent(params Control[] controls)
    {
        _version++;
        SuspendLayout();
        Body.SuspendLayout();
        foreach (Control c in Body.Controls.Cast<Control>().ToList()) c.Dispose();
        Body.Controls.Clear();
        Body.Controls.AddRange(controls.Where(c => c != null).ToArray());
        Body.ResumeLayout();
        ResumeLayout();
    }

    /// <summary>Applies wrap widths for <paramref name="width"/> and returns the height the card needs.</summary>
    public int Measure(int width)
    {
        // Measuring wraps every label: do it only when the width or the content changed.
        if (width == _measuredWidth && _version == _measuredVersion) return _measuredHeight;
        _measuredWidth = width;
        _measuredVersion = _version;
        _measuredHeight = MeasureCore(width);
        return _measuredHeight;
    }

    private int MeasureCore(int width)
    {
        var cw = Math.Max(S(120), width - Padding.Horizontal);
        Body.Location = new Point(Padding.Left, Padding.Top);
        Body.MaximumSize = new Size(cw, 0);
        foreach (Control c in Body.Controls)
        {
            switch (c)
            {
                case FlowLayoutPanel row when Equals(row.Tag, FieldsTag):
                    row.MaximumSize = new Size(cw, 0);
                    foreach (Control f in row.Controls)
                    {
                        var fw = Equals(f.Tag, FullTag) ? cw - S(4) : (cw - S(16)) / 2;
                        f.MinimumSize = new Size(fw, 0);
                        f.MaximumSize = new Size(fw, 0);
                        foreach (Control inner in f.Controls)
                            if (inner is Label valueLabel) valueLabel.MaximumSize = new Size(fw - S(4), 0);
                    }
                    break;
                case Label l:
                    l.MaximumSize = new Size(cw, 0);
                    break;
                case FlowLayoutPanel flow:
                    flow.MaximumSize = new Size(cw, 0);
                    foreach (Control inner in flow.Controls)
                        if (inner is WrapLabel wl) wl.MaximumSize = new Size(Math.Max(S(80), cw - S(40)), 0);
                    break;
                default:
                    c.Width = cw;
                    break;
            }
        }
        Body.PerformLayout();
        return Body.GetPreferredSize(new Size(cw, 0)).Height + Padding.Vertical;
    }

    /// <summary>Row of label/value fields, two per line (or full width).</summary>
    public static FlowLayoutPanel Fields(params Control[] fields)
    {
        var row = Ui.Row(fields);
        row.Tag = FieldsTag;
        row.Margin = P(0, 4, 0, 6);
        return row;
    }

    public static Control Field(string caption, string value, bool full = false, bool mono = false, Color? color = null)
    {
        var c = Ui.Lbl(caption, Small, TextMuted);
        c.Margin = P(0, 0, 0, 2);
        var v = new WrapLabel(value, mono ? Mono : Theme.Body, color) { Margin = new Padding(0) };
        var col = Ui.Column(c, v);
        col.Margin = P(0, 0, 12, 10);
        col.Tag = full ? FullTag : HalfTag;
        return col;
    }

    public static Control Heading(string title, Pill? pill = null)
    {
        var h = Ui.Lbl(title, Theme.Heading);
        h.Margin = P(0, 0, 10, 8);
        var row = pill == null ? Ui.Row(h) : Ui.Row(h, pill);
        row.Margin = P(0, 0, 0, 4);
        return row;
    }
}

/// <summary>
/// Vertical page of rows; a row with two controls is shown side by side when the page is wide enough.
/// A control whose Tag is an int has that fixed (96-DPI) height.
/// </summary>
internal sealed class CardGrid : Panel
{
    private readonly List<Control[]> _rows = new();
    private bool _inLayout;
    private bool _dirty = true;
    private int _lastWidth = -1;

    public CardGrid()
    {
        AutoScroll = true;
        BackColor = Bg;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
    }

    /// <summary>Content changed: lay out again now if shown, otherwise when the page becomes visible.</summary>
    public void MarkDirty()
    {
        _dirty = true;
        if (Visible) PerformLayout();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && _dirty) PerformLayout();
    }

    public void SetRows(params Control[][] rows)
    {
        SuspendLayout();
        foreach (Control c in Controls.Cast<Control>().ToList())
            if (!rows.Any(r => r.Contains(c))) c.Dispose();
        Controls.Clear();
        _rows.Clear();
        foreach (var r in rows)
        {
            _rows.Add(r);
            Controls.AddRange(r);
        }
        _dirty = true;
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_inLayout) return;
        if (!Visible)
        {
            // Children report Visible=false while the page is hidden: lay out once it is shown.
            _dirty = true;
            return;
        }
        var avail = ClientSize.Width;
        // Showing/hiding the page or a child repaint also raises Layout: skip unless something really changed.
        if (avail <= 0 || (!_dirty && avail == _lastWidth)) return;
        _inLayout = true;
        try
        {
            _dirty = false;
            _lastWidth = avail;
            var gap = S(12);
            var off = AutoScrollPosition;
            var y = 0;
            foreach (var row in _rows)
            {
                var visible = row.Where(c => c.Visible).ToArray();
                if (visible.Length == 0) continue;
                if (visible.Length == 2 && avail >= S(760))
                {
                    var w = (avail - gap) / 2;
                    var h = Math.Max(MeasureHeight(visible[0], w), MeasureHeight(visible[1], avail - w - gap));
                    visible[0].SetBounds(off.X, off.Y + y, w, h);
                    visible[1].SetBounds(off.X + w + gap, off.Y + y, avail - w - gap, h);
                    y += h + gap;
                }
                else
                {
                    foreach (var c in visible)
                    {
                        var h = MeasureHeight(c, avail);
                        c.SetBounds(off.X, off.Y + y, avail, h);
                        y += h + gap;
                    }
                }
            }
            var min = new Size(0, Math.Max(0, y - gap));
            if (AutoScrollMinSize != min) AutoScrollMinSize = min;
        }
        finally
        {
            _inLayout = false;
        }
    }

    private static int MeasureHeight(Control c, int width) => c switch
    {
        StackCard sc => sc.Measure(width),
        _ when c.Tag is int fixedHeight => S(fixedHeight),
        _ => c.GetPreferredSize(new Size(width, 0)).Height
    };
}
