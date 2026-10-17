using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace FFmpegForge
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(15, 17, 22);
        public static readonly Color Panel = Color.FromArgb(22, 25, 32);
        public static readonly Color Card = Color.FromArgb(31, 35, 45);
        public static readonly Color CardHover = Color.FromArgb(41, 46, 59);
        public static readonly Color Border = Color.FromArgb(48, 54, 68);
        public static readonly Color Text = Color.FromArgb(233, 236, 242);
        public static readonly Color Muted = Color.FromArgb(139, 147, 167);
        public static readonly Color Accent1 = Color.FromArgb(255, 122, 24);
        public static readonly Color Accent2 = Color.FromArgb(255, 45, 120);
        public static readonly Color Success = Color.FromArgb(61, 220, 151);
        public static readonly Color Error = Color.FromArgb(255, 92, 108);
        public static readonly Color Selection = Color.FromArgb(52, 40, 52);

        public static float Scale = 1f;
        public static int S(int px) { return (int)Math.Round(px * Scale); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static LinearGradientBrush AccentBrush(RectangleF r)
        {
            if (r.Width < 1) r.Width = 1;
            if (r.Height < 1) r.Height = 1;
            return new LinearGradientBrush(r, Accent1, Accent2, LinearGradientMode.Horizontal);
        }
    }

    /// Rounded flat button with a gradient "Primary" variant and a "Toggled" state.
    public class FlatBtn : Control
    {
        bool hover, down;
        public bool Primary;
        bool toggled;
        public bool Toggled { get { return toggled; } set { toggled = value; Invalidate(); } }

        public FlatBtn(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 9.5f);
            ForeColor = Theme.Text;
            BackColor = Color.Transparent;
            Height = Theme.S(34);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.S(7)))
            {
                if (!Enabled)
                {
                    using (var b = new SolidBrush(Color.FromArgb(28, 31, 40))) g.FillPath(b, path);
                }
                else if (Primary || toggled)
                {
                    using (var b = Theme.AccentBrush(r))
                    {
                        if (down) b.SetSigmaBellShape(0.5f);
                        g.FillPath(b, path);
                    }
                    if (hover) using (var w = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) g.FillPath(w, path);
                }
                else
                {
                    using (var b = new SolidBrush(down ? Theme.Border : hover ? Theme.CardHover : Theme.Card)) g.FillPath(b, path);
                    using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
                }
            }
            Color fc = Enabled ? (Primary || toggled ? Color.White : ForeColor) : Color.FromArgb(90, 96, 112);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fc,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// Gradient progress bar.
    public class GradientBar : Control
    {
        double value;
        public double Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public string Caption = "";

        public GradientBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(10);
            BackColor = Theme.Panel;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var p = Theme.Round(r, Height / 2f))
            using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
            if (value > 0)
            {
                var f = new RectangleF(0, 0, Math.Max(Height, (float)((Width - 1) * value)), Height - 1);
                using (var p = Theme.Round(f, Height / 2f))
                using (var b = Theme.AccentBrush(new RectangleF(0, 0, Width, Height))) g.FillPath(b, p);
            }
        }
    }

    /// Application header with gradient title.
    public class Header : Control
    {
        public string Right1 = "";
        public Color Right1Color = Theme.Muted;
        public int FilterCount, FormatCount;

        public Header()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(92);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(26, 20, 28), Theme.Bg, LinearGradientMode.Horizontal))
                g.FillRectangle(bg, ClientRectangle);
            // halo
            using (var halo = new GraphicsPath())
            {
                halo.AddEllipse(-Theme.S(120), -Theme.S(140), Theme.S(520), Theme.S(320));
                using (var pb = new PathGradientBrush(halo))
                {
                    pb.CenterColor = Color.FromArgb(55, Theme.Accent2);
                    pb.SurroundColors = new[] { Color.FromArgb(0, Theme.Accent2) };
                    g.FillPath(pb, halo);
                }
            }
            // logo
            int ls = Theme.S(54);
            var lr = new RectangleF(Theme.S(22), (Height - ls) / 2f, ls, ls);
            DrawLogo(g, lr);

            float x = lr.Right + Theme.S(16);
            using (var f1 = new Font("Segoe UI", 24f, FontStyle.Bold))
            using (var f2 = new Font("Segoe UI", 9.75f))
            {
                string a = "FFmpeg", b = " FORGE";
                var sa = g.MeasureString(a, f1, PointF.Empty, StringFormat.GenericTypographic);
                var sb = g.MeasureString(b, f1, PointF.Empty, StringFormat.GenericTypographic);
                float y = Theme.S(14);
                using (var w = new SolidBrush(Color.White)) g.DrawString(a, f1, w, x, y, StringFormat.GenericTypographic);
                var gr = new RectangleF(x + sa.Width, y, sb.Width, sb.Height);
                using (var br = Theme.AccentBrush(gr)) g.DrawString(b, f1, br, x + sa.Width, y, StringFormat.GenericTypographic);
                using (var m = new SolidBrush(Theme.Muted))
                    g.DrawString("Convert anything into everything.   Video  •  Audio  •  Image   —   " + (FormatCount > 0 ? FormatCount : Formats.All.Count) + " output formats" + (FilterCount > 0 ? "  •  " + FilterCount + " filters" : ""), f2, m, x + Theme.S(2), y + sa.Height + Theme.S(2), StringFormat.GenericTypographic);

                if (!string.IsNullOrEmpty(Right1))
                {
                    var rs = g.MeasureString(Right1, f2);
                    using (var rb = new SolidBrush(Right1Color))
                        g.DrawString(Right1, f2, rb, Width - rs.Width - Theme.S(176), (Height - rs.Height) / 2f);
                }
            }
            using (var line = Theme.AccentBrush(new RectangleF(0, Height - 2, Width, 2)))
                g.FillRectangle(line, 0, Height - 2, Width, 2);
        }

        public static void DrawLogo(Graphics g, RectangleF lr)
        {
            using (var p = Theme.Round(lr, lr.Width * 0.24f))
            using (var b = new LinearGradientBrush(lr, Theme.Accent1, Theme.Accent2, 45f))
                g.FillPath(b, p);
            // stylised lightning bolt
            float u = lr.Width / 10f;
            var bolt = new PointF[] {
                new PointF(lr.X + 5.6f*u, lr.Y + 1.6f*u),
                new PointF(lr.X + 2.6f*u, lr.Y + 5.6f*u),
                new PointF(lr.X + 4.8f*u, lr.Y + 5.6f*u),
                new PointF(lr.X + 4.0f*u, lr.Y + 8.5f*u),
                new PointF(lr.X + 7.4f*u, lr.Y + 4.3f*u),
                new PointF(lr.X + 5.2f*u, lr.Y + 4.3f*u),
            };
            using (var w = new SolidBrush(Color.White)) g.FillPolygon(w, bolt);
        }
    }

    /// Dark, double-buffered ListView.
    public class DarkList : ListView
    {
        public DarkList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;
            OwnerDraw = true;
            View = View.Details;
            FullRowSelect = true;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
        }
    }

    /// Drag & drop zone shown when the list is empty.
    public class DropHint : Control
    {
        public bool Highlight;
        public DropHint()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(Theme.S(12), Theme.S(12), Width - Theme.S(24), Height - Theme.S(24));
            using (var p = Theme.Round(r, Theme.S(14)))
            {
                if (Highlight) using (var b = new SolidBrush(Color.FromArgb(25, Theme.Accent1))) g.FillPath(b, p);
                using (var pen = new Pen(Highlight ? Theme.Accent1 : Theme.Border, Theme.S(2)) { DashStyle = DashStyle.Dash })
                    g.DrawPath(pen, p);
            }
            int ls = Theme.S(64);
            var lr = new RectangleF((Width - ls) / 2f, Height / 2f - ls - Theme.S(16), ls, ls);
            Header.DrawLogo(g, lr);
            using (var f1 = new Font("Segoe UI Semibold", 15f))
            using (var f2 = new Font("Segoe UI", 9.75f))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                using (var t = new SolidBrush(Theme.Text))
                    g.DrawString("Drop your files here", f1, t, new RectangleF(0, lr.Bottom + Theme.S(14), Width, Theme.S(40)), sf);
                using (var m = new SolidBrush(Theme.Muted))
                    g.DrawString("or click to browse  —  videos, music, pictures, whole folders\n200 input extensions recognised", f2, m,
                        new RectangleF(0, lr.Bottom + Theme.S(50), Width, Theme.S(60)), sf);
            }
        }
    }
}
