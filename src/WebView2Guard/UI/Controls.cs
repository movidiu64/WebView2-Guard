// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.ComponentModel;
using System.Drawing.Drawing2D;
using static WebView2Guard.UI.Theme;

namespace WebView2Guard.UI;

internal enum ButtonKind { Secondary, Primary, Danger, Ghost, WarnFilled, DangerFilled }

/// <summary>Flat rounded button with an optional icon glyph; keeps native Button keyboard/accessibility.</summary>
internal sealed class GuardButton : Button
{
    private bool _hover;
    private bool _pressed;
    private bool _iconOnly;
    private ButtonKind _kind;
    private string? _glyph;

    public GuardButton(string text, string? glyph = null, ButtonKind kind = ButtonKind.Secondary)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        _kind = kind;
        _glyph = glyph;
        Font = IsFilled(kind) ? Strong : Body;
        Text = text;
        Height = S(34);
        Margin = P(0, 0, 8, 8);
        AccessibleName = text;
        FitWidth();
    }

    private static bool IsFilled(ButtonKind k) =>
        k is ButtonKind.Primary or ButtonKind.WarnFilled or ButtonKind.DangerFilled;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            Font = IsFilled(value) ? Strong : Body;
            FitWidth();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph
    {
        get => _glyph;
        set { _glyph = value; FitWidth(); Invalidate(); }
    }

    /// <summary>Icon-only button: shows just the glyph; the text becomes the accessible name / tooltip.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IconOnly
    {
        get => _iconOnly;
        set { _iconOnly = value; FitWidth(); Invalidate(); }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
        FitWidth();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        FitWidth();
    }

    public void FitWidth()
    {
        if (_iconOnly)
        {
            Width = Height;
            return;
        }
        var text = TextRenderer.MeasureText(Text ?? string.Empty, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var glyph = _glyph != null ? S(24) : 0;
        Width = S(28) + glyph + text;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _pressed = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _pressed = false; Invalidate(); base.OnMouseUp(mevent); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(SolidBack(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill, stroke, fore;
        switch (_kind)
        {
            case ButtonKind.Primary:
                fill = _hover ? AccentHover : Accent; stroke = fill; fore = Color.White; break;
            case ButtonKind.WarnFilled:
                fill = WarnText; stroke = fill; fore = Color.White; break;
            case ButtonKind.DangerFilled:
                fill = ErrText; stroke = fill; fore = Color.White; break;
            case ButtonKind.Danger:
                fill = _hover ? ErrBg : Surface; stroke = ErrBorder; fore = ErrText; break;
            case ButtonKind.Ghost:
                fill = _hover ? HoverFill : SolidBack(Parent); stroke = fill; fore = Theme.Text; break;
            default:
                fill = _hover ? HoverFill : Surface; stroke = BorderStrong; fore = Theme.Text; break;
        }
        if (_pressed) fill = ControlPaint.Dark(fill, 0.05f);
        if (!Enabled)
        {
            var filled = IsFilled(_kind);
            fill = filled ? Color.FromArgb(0xC8, 0xC8, 0xC8) : Surface;
            stroke = Border;
            fore = filled ? Color.White : Color.FromArgb(0x8A, 0x8A, 0x8A);
        }

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = RoundRect(r, 5 * K))
        using (var b = new SolidBrush(fill))
        using (var p = new Pen(stroke))
        {
            g.FillPath(b, path);
            g.DrawPath(p, path);
        }

        const TextFormatFlags centered =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        if (_iconOnly)
        {
            TextRenderer.DrawText(g, _glyph ?? string.Empty, Icon(11f), ClientRectangle, fore, centered);
        }
        else
        {
            var x = S(14);
            if (_glyph != null)
            {
                TextRenderer.DrawText(g, _glyph, Icon(10.5f), new Rectangle(x, 0, S(18), Height), fore, centered);
                x += S(24);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - S(10), Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -S(3), -S(3)));
    }
}

/// <summary>Rounded panel with a border; children inherit its fill as BackColor.</summary>
internal class Card : Panel
{
    private Color _fill = Surface;
    private Color _stroke = Border;

    public Card()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = _fill;
        Padding = P(16);
        Margin = P(0, 0, 0, 12);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill
    {
        get => _fill;
        set
        {
            if (_fill == value) return;
            _fill = value;
            BackColor = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Stroke
    {
        get => _stroke;
        set
        {
            if (_stroke == value) return;
            _stroke = value;
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(SolidBack(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = RoundRect(r, 7 * K);
        using var b = new SolidBrush(_fill);
        using var p = new Pen(_stroke);
        g.FillPath(b, path);
        g.DrawPath(p, path);
    }
}

/// <summary>Small rounded status label ("Permis", "Blocat"…).</summary>
internal sealed class Pill : Label
{
    private static readonly Font PillFont = new("Segoe UI Semibold", 8.25f);
    private Color _fill = NeutralBg;

    public Pill()
    {
        AutoSize = true;
        Font = PillFont;
        Padding = P(8, 2, 8, 2);
        Margin = P(0, 2, 0, 2);
        TextAlign = ContentAlignment.MiddleCenter;
        UseMnemonic = false;
    }

    public void Set(string text, Color fill, Color fore)
    {
        if (Text == text && _fill == fill && ForeColor == fore) return;
        Text = text;
        _fill = fill;
        ForeColor = fore;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(SolidBack(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f);
        using var b = new SolidBrush(_fill);
        g.FillPath(b, path);
    }
}

/// <summary>Underlined tab strip (Aplicații / Runtime / Firewall / Jurnal).</summary>
internal sealed class TabStrip : Control
{
    private readonly List<string> _tabs = new();
    private readonly List<Rectangle> _hit = new();
    private int _selected;
    private int _hover = -1;

    public event EventHandler? SelectedChanged;

    public TabStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Height = S(44);
        Font = Body;
        AccessibleRole = AccessibleRole.PageTabList;
        Cursor = Cursors.Hand;
    }

    public void SetTabs(IEnumerable<string> tabs)
    {
        _tabs.Clear();
        _tabs.AddRange(tabs);
        if (_tabs.Count > 0) AccessibleName = _tabs[Math.Min(_selected, _tabs.Count - 1)];
        Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Selected
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _tabs.Count || value == _selected) return;
            _selected = value;
            AccessibleName = _tabs[value];
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        _hit.Clear();
        var x = S(12);
        for (var i = 0; i < _tabs.Count; i++)
        {
            var font = i == _selected ? Strong : Body;
            var w = TextRenderer.MeasureText(_tabs[i], font).Width + S(24);
            var rect = new Rectangle(x, 0, w, Height);
            _hit.Add(rect);
            var color = i == _selected ? Accent : i == _hover ? Theme.Text : TextMuted;
            TextRenderer.DrawText(g, _tabs[i], font, new Rectangle(x, 0, w, Height - S(3)), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (i == _selected)
            {
                using var b = new SolidBrush(Accent);
                g.FillRectangle(b, x + S(8), Height - S(3), w - S(16), S(3));
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(rect, -S(2), -S(6)));
            }
            x += w + S(4);
        }
        using var line = new Pen(Border);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = _hit.FindIndex(r => r.Contains(e.Location));
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var i = _hit.FindIndex(r => r.Contains(e.Location));
        if (i >= 0) Selected = i;
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left && _selected > 0) Selected = _selected - 1;
        if (e.KeyCode == Keys.Right && _selected < _tabs.Count - 1) Selected = _selected + 1;
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

/// <summary>ListView with double buffering (no flicker when rows are updated).</summary>
internal sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        DoubleBuffered = true; // LVS_EX_DOUBLEBUFFER on the native list view
    }
}

/// <summary>Factory helpers for labels and layouts.</summary>
internal static class Ui
{
    public static Label Lbl(string text, Font? font = null, Color? color = null, int maxWidth = 0)
    {
        var l = new System.Windows.Forms.Label
        {
            Text = text,
            Font = font ?? Body,
            ForeColor = color ?? Theme.Text,
            AutoSize = true,
            Margin = P(0, 0, 0, 6),
            UseMnemonic = false,
            BackColor = Color.Transparent
        };
        if (maxWidth > 0) l.MaximumSize = new Size(S(maxWidth), 0);
        return l;
    }

    public static Label GlyphLabel(string glyph, Color color, float size = 12f) => new()
    {
        Text = glyph,
        Font = Icon(size),
        ForeColor = color,
        AutoSize = true,
        Margin = P(0, 2, 6, 0),
        AccessibleRole = AccessibleRole.Graphic,
        UseMnemonic = false,
        BackColor = Color.Transparent
    };

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        f.Controls.AddRange(controls);
        return f;
    }

    public static FlowLayoutPanel Column(params Control[] controls)
    {
        var f = Row(controls);
        f.FlowDirection = FlowDirection.TopDown;
        f.WrapContents = false;
        return f;
    }

    /// <summary>"label / value" grid used on cards.</summary>
    public static TableLayoutPanel Fields(int columns = 2)
    {
        var t = new TableLayoutPanel
        {
            ColumnCount = columns,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = P(0, 4, 0, 8),
            BackColor = Color.Transparent
        };
        for (var i = 0; i < columns; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        return t;
    }

    public static Control Field(string label, string value, bool mono = false, Color? color = null, int maxWidth = 260)
    {
        var caption = Lbl(label, Small, TextMuted);
        caption.Margin = P(0, 0, 0, 2);
        var text = Lbl(value, mono ? Mono : Body, color ?? Theme.Text, maxWidth);
        text.Margin = new Padding(0);
        var col = Column(caption, text);
        col.Margin = P(0, 0, 12, 10);
        return col;
    }

    public static ListView List()
    {
        var lv = new BufferedListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Font = Body,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            ShowItemToolTips = true
        };
        // Taller rows: a 1×N image list sets the row height.
        lv.SmallImageList = new ImageList { ImageSize = new Size(1, S(28)) };
        return lv;
    }

    public static void Error(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, Localization.L.T("msg.error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static bool Confirm(IWin32Window? owner, string message, bool danger = false) =>
        MessageBox.Show(owner, message, "WebView2 Guard", MessageBoxButtons.YesNo,
            danger ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
}
