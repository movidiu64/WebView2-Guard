// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace WebView2Guard.UI;

/// <summary>Colours, fonts and icon glyphs (Windows 11 look, see docs/design).</summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(0xF3, 0xF3, 0xF3);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(0xFA, 0xFA, 0xFA);
    public static readonly Color Border = Color.FromArgb(0xE1, 0xE1, 0xE1);
    public static readonly Color BorderStrong = Color.FromArgb(0xD1, 0xD1, 0xD1);
    public static readonly Color Text = Color.FromArgb(0x1B, 0x1B, 0x1B);
    public static readonly Color TextMuted = Color.FromArgb(0x5C, 0x5C, 0x5C);
    public static readonly Color Accent = Color.FromArgb(0x00, 0x5F, 0xB8);
    public static readonly Color AccentHover = Color.FromArgb(0x00, 0x4A, 0x8F);
    public static readonly Color HoverFill = Color.FromArgb(0xF0, 0xF0, 0xF0);

    public static readonly Color OkText = Color.FromArgb(0x0E, 0x70, 0x0E);
    public static readonly Color OkBg = Color.FromArgb(0xDF, 0xF6, 0xDD);
    public static readonly Color OkBorder = Color.FromArgb(0xB7, 0xE4, 0xB5);

    public static readonly Color WarnText = Color.FromArgb(0x8A, 0x53, 0x00);
    public static readonly Color WarnTextDark = Color.FromArgb(0x5C, 0x3A, 0x00);
    public static readonly Color WarnBg = Color.FromArgb(0xFF, 0xF4, 0xCE);
    public static readonly Color WarnBorder = Color.FromArgb(0xF2, 0xD5, 0x7E);
    public static readonly Color WarnRow = Color.FromArgb(0xFF, 0xF8, 0xE1);

    public static readonly Color ErrText = Color.FromArgb(0xB3, 0x26, 0x1E);
    public static readonly Color ErrTextDark = Color.FromArgb(0x6E, 0x17, 0x12);
    public static readonly Color ErrBg = Color.FromArgb(0xFD, 0xE7, 0xE9);
    public static readonly Color ErrBorder = Color.FromArgb(0xF1, 0xB5, 0xB0);

    public static readonly Color NeutralBg = Color.FromArgb(0xEB, 0xEB, 0xEB);
    public static readonly Color NeutralText = Color.FromArgb(0x42, 0x42, 0x42);

    /// <summary>System DPI factor (the app is SystemAware; layout sizes are written for 96 DPI and scaled with S).</summary>
    public static readonly float K = ReadDpiFactor();

    public static int S(int px) => (int)Math.Round(px * K);

    public static Padding P(int all) => new(S(all));
    public static Padding P(int left, int top, int right, int bottom) => new(S(left), S(top), S(right), S(bottom));

    private static float ReadDpiFactor()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96f;
        }
        catch
        {
            return 1f;
        }
    }

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11.25f);
    public static readonly Font Title = new("Segoe UI Semibold", 13.5f);
    public static readonly Font Mono = new("Consolas", 9f);

    private static readonly string IconFamily = PickIconFamily();
    private static readonly Dictionary<float, Font> IconFonts = new();

    public static Font Icon(float size)
    {
        if (!IconFonts.TryGetValue(size, out var f))
        {
            f = new Font(IconFamily, size);
            IconFonts[size] = f;
        }
        return f;
    }

    private static string PickIconFamily()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    /// <summary>Segoe MDL2 / Fluent icon code points.</summary>
    internal static class Glyph
    {
        public const string Shield = "";
        public const string Check = "";
        public const string Warning = "";
        public const string Error = "";
        public const string Info = "";
        public const string Refresh = "";
        public const string Add = "";
        public const string Delete = "";
        public const string Blocked = "";
        public const string Folder = "";
        public const string Copy = "";
        public const string Download = "";
        public const string Undo = "";
        public const string Play = "";
        public const string Pause = "";
        public const string ChevronDown = "";
        public const string Document = "";
        public const string External = "";
        public const string Circle = "";
        public const string Globe = "\uE774";
    }

    /// <summary>First opaque background colour up the parent chain (transparent panels report Transparent).</summary>
    public static Color SolidBack(Control? c)
    {
        while (c != null)
        {
            if (c.BackColor.A == 255) return c.BackColor;
            c = c.Parent;
        }
        return Bg;
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Max(1f, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
