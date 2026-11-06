using System.Drawing.Drawing2D;

namespace PiceVoice;

static class Theme
{
    public static readonly Color Background = Color.FromArgb(12, 14, 22);
    public static readonly Color Surface = Color.FromArgb(22, 25, 38);
    public static readonly Color Surface2 = Color.FromArgb(29, 33, 49);
    public static readonly Color Purple = Color.FromArgb(132, 89, 255);
    public static readonly Color Cyan = Color.FromArgb(68, 220, 255);
    public static readonly Color Text = Color.FromArgb(244, 246, 255);
    public static readonly Color Muted = Color.FromArgb(145, 153, 180);
    public static readonly Color Success = Color.FromArgb(74, 224, 155);
    public static readonly Font Body = new("Segoe UI", 10);
    public static readonly Font Title = new("Segoe UI Semibold", 24);
    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 18;
    public Color BorderColor { get; set; } = Color.Transparent;
    public RoundedPanel() { DoubleBuffered = true; BackColor = Theme.Surface; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, path);
        if (BorderColor != Color.Transparent)
            using (var pen = new Pen(BorderColor)) e.Graphics.DrawPath(pen, path);
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var p = Theme.Round(new Rectangle(0, 0, Width, Height), Radius);
        Region = new Region(p);
    }
}

enum AppIcon { Mic, Wave, Board, Brain, Play, Stop, Search, Headphones, Help, Download, Check }

sealed class IconButton : Button
{
    public AppIcon Icon { get; set; }
    public int Radius { get; set; } = 14;
    public IconButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Surface2;
        ForeColor = Theme.Text;
        TextAlign = ContentAlignment.MiddleCenter;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
        var iconRect = string.IsNullOrWhiteSpace(Text)
            ? new Rectangle(Width / 2 - 10, Height / 2 - 10, 20, 20)
            : new Rectangle(16, Height / 2 - 10, 20, 20);
        IconPainter.Draw(e.Graphics, Icon, iconRect, ForeColor);
        if (!string.IsNullOrWhiteSpace(Text))
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(45, 0, Width - 55, Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var p = Theme.Round(new Rectangle(0, 0, Width, Height), Radius);
        Region = new Region(p);
    }
}

static class IconPainter
{
    public static void Draw(Graphics g, AppIcon icon, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = new Pen(c, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int x = r.X, y = r.Y, w = r.Width, h = r.Height;
        switch (icon)
        {
            case AppIcon.Mic:
                g.DrawArc(p, x + 5, y + 1, 10, 13, 0, 180); g.DrawLine(p, x + 3, y + 8, x + 3, y + 10); g.DrawArc(p, x + 3, y + 5, 14, 11, 0, 180); g.DrawLine(p, x + 10, y + 16, x + 10, y + 19); break;
            case AppIcon.Wave:
                for (int i = 0; i < 5; i++) { int hh = 5 + (i % 3) * 5; g.DrawLine(p, x + 2 + i * 4, y + 10 - hh / 2, x + 2 + i * 4, y + 10 + hh / 2); } break;
            case AppIcon.Board:
                for (int i = 0; i < 4; i++) g.DrawEllipse(p, x + (i % 2) * 10 + 1, y + (i / 2) * 10 + 1, 7, 7); break;
            case AppIcon.Brain:
                g.DrawArc(p, x + 2, y + 2, 9, 16, 85, 190); g.DrawArc(p, x + 9, y + 2, 9, 16, 265, 190); g.DrawLine(p, x + 10, y + 3, x + 10, y + 17); break;
            case AppIcon.Play:
                using (var b = new SolidBrush(c)) g.FillPolygon(b, [new(x + 5, y + 3), new(x + 17, y + 10), new(x + 5, y + 17)]); break;
            case AppIcon.Stop:
                using (var b = new SolidBrush(c)) g.FillRectangle(b, x + 4, y + 4, 12, 12); break;
            case AppIcon.Search:
                g.DrawEllipse(p, x + 2, y + 2, 11, 11); g.DrawLine(p, x + 12, y + 12, x + 18, y + 18); break;
            case AppIcon.Headphones:
                g.DrawArc(p, x + 2, y + 2, 16, 15, 180, 180); g.DrawArc(p, x + 1, y + 10, 6, 8, 90, 180); g.DrawArc(p, x + 13, y + 10, 6, 8, 270, 180); break;
            case AppIcon.Help:
                g.DrawEllipse(p, x + 1, y + 1, 18, 18); g.DrawArc(p, x + 6, y + 4, 8, 7, 190, 220); g.DrawLine(p, x + 10, y + 11, x + 10, y + 14); break;
            case AppIcon.Download:
                g.DrawLine(p, x + 10, y + 2, x + 10, y + 13); g.DrawLine(p, x + 5, y + 9, x + 10, y + 14); g.DrawLine(p, x + 15, y + 9, x + 10, y + 14); g.DrawLine(p, x + 3, y + 18, x + 17, y + 18); break;
            case AppIcon.Check:
                g.DrawLine(p, x + 2, y + 10, x + 8, y + 16); g.DrawLine(p, x + 8, y + 16, x + 18, y + 4); break;
        }
    }
}