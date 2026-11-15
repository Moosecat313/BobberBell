using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BobberBell.UI.Controls;

/// <summary>Owner-drawn rounded button (gold filled or outlined) with hover/press states and a glass sheen.</summary>
internal sealed class ModernButton : Button
{
    private readonly bool filled;
    private readonly Color accent, accentHi, surface, text;
    private bool hover, down;

    public ModernButton(bool filled, Color accent, Color accentHi, Color surface, Color text)
    {
        this.filled = filled; this.accent = accent; this.accentHi = accentHi; this.surface = surface; this.text = text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
    }

    protected override bool ShowFocusCues => false;
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // Paint whatever is behind (card fill + glass band) so the rounded corners blend in.
        if (Parent != null) base.OnPaintBackground(e); else g.Clear(Theme.SurfaceOf(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(r, 6);
        Color fg;
        if (filled)
        {
            Color top = !Enabled ? Color.FromArgb(70, 62, 44) : down ? accent : hover ? accentHi : Color.FromArgb(214, 178, 108);
            Color bottom = !Enabled ? Color.FromArgb(58, 52, 38) : down ? Color.FromArgb(170, 134, 70) : accent;
            using var br = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), top, bottom, LinearGradientMode.Vertical);
            g.FillPath(br, path);
            fg = Enabled ? text : Color.FromArgb(120, 110, 90);
        }
        else
        {
            Color fill = !Enabled ? surface : down ? Color.FromArgb(40, accent) : hover ? Color.FromArgb(26, accent) : surface;
            using (var br = new SolidBrush(fill)) g.FillPath(br, path);
            Color border = !Enabled ? Color.FromArgb(60, accent) : hover ? accentHi : Color.FromArgb(170, accent);
            using var pen = new Pen(border, 1f);
            g.DrawPath(pen, path);
            fg = Enabled ? text : Color.FromArgb(90, text);
        }
        if (Enabled) PaintSheen(g, path, r);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    // Glass/metal sheen, clipped to the rounded shape: white gradient over the top ~45%,
    // a soft radial glint near the top-left (gold buttons), and a 1 px light inner top edge.
    private void PaintSheen(Graphics g, GraphicsPath path, RectangleF r)
    {
        var state = g.Save();
        g.SetClip(path, CombineMode.Intersect);
        int a = filled ? (down ? 22 : hover ? 75 : 55) : (down ? 8 : hover ? 30 : 20);
        var top = new RectangleF(r.X, r.Y, r.Width, Math.Max(2f, r.Height * 0.45f));
        using (var sheen = new LinearGradientBrush(new RectangleF(top.X, top.Y - 1, top.Width, top.Height + 2),
                   Color.FromArgb(a, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
            g.FillRectangle(sheen, top);
        if (filled && !down)
        {
            using var glint = new GraphicsPath();
            glint.AddEllipse(r.X - r.Width * 0.15f, r.Y - r.Height * 0.6f, r.Width * 0.7f, r.Height * 1.3f);
            using var pg = new PathGradientBrush(glint)
            {
                CenterPoint = new PointF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.12f),
                CenterColor = Color.FromArgb(hover ? 50 : 34, Color.White),
                SurroundColors = new[] { Color.FromArgb(0, Color.White) }
            };
            g.FillPath(pg, glint);
        }
        using (var edge = new Pen(Color.FromArgb(down ? 15 : filled ? 40 : 26, Color.White), 1f))
            g.DrawLine(edge, r.X + 6, r.Y + 1, r.Right - 6, r.Y + 1);
        g.Restore(state);
    }
}
