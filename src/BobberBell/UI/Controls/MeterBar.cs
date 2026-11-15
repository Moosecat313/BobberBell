using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BobberBell.UI.Controls;

/// <summary>Rounded live meter (similarity or volume) with a gradient fill and a threshold-free 0–100 value.</summary>
internal sealed class MeterBar : Control
{
    private readonly Color track, fillA, fillB;
    private int value;
    // Runtime-only properties (never designer-serialized); required by the .NET 10 WinForms analyzer (WFO1000).
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Minimum { get; set; } = 0;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum { get; set; } = 100;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set { int v = Math.Max(Minimum, Math.Min(Maximum, value)); if (v != this.value) { this.value = v; Invalidate(); } }
    }

    public MeterBar(Color track, Color fillA, Color fillB)
    {
        this.track = track; this.fillA = fillA; this.fillB = fillB;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Parent != null) base.OnPaintBackground(e); else g.Clear(Theme.SurfaceOf(Parent)); // keeps the card's glass band behind the bar
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float rad = Height / 2f;
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        // Inset glass track: a touch darker than the card plus a faint rim, so it reads on top of the glass band.
        using (var path = Theme.RoundedRect(r, rad))
        using (var br = new SolidBrush(Color.FromArgb(track.R / 3, track.G / 2, track.B / 2)))
        using (var rim = new Pen(Color.FromArgb(70, 52, 82, 124)))
        {
            g.FillPath(br, path);
            g.DrawPath(rim, path);
        }
        float frac = Maximum > Minimum ? (value - Minimum) / (float)(Maximum - Minimum) : 0;
        float w = (Width - 1) * frac;
        if (w >= 2)
        {
            var fr = new RectangleF(0, 0, Math.Max(w, Height), Height - 1);
            using var path = Theme.RoundedRect(fr, rad);
            using var br = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), fillA, fillB, LinearGradientMode.Horizontal);
            g.FillPath(br, path);
            using var sheen = new LinearGradientBrush(new RectangleF(0, -1, Width, Height / 2f + 2),
                Color.FromArgb(70, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical);
            var state = g.Save(); g.SetClip(path, CombineMode.Intersect);
            g.FillRectangle(sheen, 0, 0, Width, Height / 2f);
            g.Restore(state);
        }
    }
}
