using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BobberBell.UI.Controls;

/// <summary>Pill-style on/off switch drawn over the card surface.</summary>
internal sealed class ToggleSwitch : CheckBox
{
    private readonly Color off, on, border, textColor;
    public ToggleSwitch(Color off, Color on, Color border, Color textColor)
    {
        this.off = off; this.on = on; this.border = border; this.textColor = textColor;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Size = new Size(320, 24);
        Cursor = Cursors.Hand;
    }
    protected override bool ShowFocusCues => false;
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.SurfaceOf(Parent));
        var pill = new RectangleF(1, (Height - 18) / 2f, 34, 18);
        using (var path = Theme.RoundedRect(pill, 9))
        {
            using (var br = new SolidBrush(Checked ? on : off)) g.FillPath(br, path);
            using var pen = new Pen(Checked ? on : border, 1f);
            g.DrawPath(pen, path);
        }
        float kx = Checked ? pill.Right - 16 : pill.X + 2;
        using (var knob = new SolidBrush(Checked ? Color.FromArgb(20, 16, 8) : Color.FromArgb(150, 160, 180)))
            g.FillEllipse(knob, kx, pill.Y + 2, 14, 14);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(44, 0, Width - 44, Height), textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
    }
}
