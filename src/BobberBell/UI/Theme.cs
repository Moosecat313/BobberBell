using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BobberBell.Interop;

namespace BobberBell.UI;

/// <summary>Shared painting helpers for the dark navy/gold theme (rounded shapes, glass band, dark title bar).</summary>
internal static class Theme
{
    /// <summary>Builds a rounded-rectangle path.</summary>
    internal static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1f, radius * 2);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Returns the opaque surface color behind a control (walking up through transparent parents).</summary>
    internal static Color SurfaceOf(Control? c)
    {
        while (c != null && c.BackColor == Color.Transparent) c = c.Parent;
        if (c == null) return Color.Black;
        return Equals(c.Tag, "card") ? Color.FromArgb(4, 18, 48) : c.BackColor;
    }

    // Fake glassmorphism: a faint white gradient over the top of a card (no blur). Pages draw the same band,
    // shifted by their offset inside the card, so the band runs seamlessly behind transparent labels.
    internal const int GlassBandHeight = 70;
    internal static void PaintGlassBand(Graphics g, int width, int offsetY, GraphicsPath? clip = null)
    {
        var band = new Rectangle(0, -offsetY, width, GlassBandHeight);
        if (band.Bottom <= 0) return;
        var state = g.Save();
        if (clip != null) g.SetClip(clip, CombineMode.Intersect);
        using (var br = new LinearGradientBrush(new Rectangle(band.X, band.Y - 1, band.Width, band.Height + 2),
                   Color.FromArgb(16, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
            g.FillRectangle(br, band);
        g.Restore(state);
    }

    /// <summary>Applies dark scrollbars to a list or text control.</summary>
    internal static void ApplyDarkScrollbars(Control c)
    {
        void apply() { try { NativeMethods.SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
        if (c.IsHandleCreated) apply(); else c.HandleCreated += (s, e) => apply();
    }

    /// <summary>Dark title bar with the theme caption and text colors (Windows 11; ignored elsewhere).</summary>
    internal static void ApplyDarkTitleBar(Form f, Color caption, Color text)
    {
        try
        {
            int on = 1;
            NativeMethods.DwmSetWindowAttribute(f.Handle, 20, ref on, 4);           // immersive dark mode
            int cap = caption.R | (caption.G << 8) | (caption.B << 16);
            NativeMethods.DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);          // caption color (Win11)
            int txt = text.R | (text.G << 8) | (text.B << 16);
            NativeMethods.DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);          // title text color (Win11)
            int corner = 2;
            NativeMethods.DwmSetWindowAttribute(f.Handle, 33, ref corner, 4);       // rounded corners
        }
        catch { }
    }
}
