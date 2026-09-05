using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TurboRama.Next
{
    internal static class Palette
    {
        public static readonly Color Background = Color.FromArgb(16, 18, 23);
        public static readonly Color Surface = Color.FromArgb(24, 27, 34);
        public static readonly Color Raised = Color.FromArgb(34, 38, 49);
        public static readonly Color Text = Color.FromArgb(242, 244, 248);
        public static readonly Color Muted = Color.FromArgb(165, 173, 185);
        public static readonly Color Accent = Color.FromArgb(185, 247, 99);
        public static readonly Color Violet = Color.FromArgb(182, 160, 255);
        public static readonly Color Line = Color.FromArgb(54, 60, 74);
        public static readonly Color Warning = Color.FromArgb(255, 198, 113);
    }

    internal static class Ui
    {
        public static Font Font(float size, bool bold = false)
        { return new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }
        public static Label Label(string text, float size = 10, Color? color = null, bool bold = false)
        {
            return new Label { AutoSize = true, Text = text, ForeColor = color ?? Palette.Text,
                BackColor = Color.Transparent, Font = Font(size, bold), Margin = new Padding(0, 0, 0, 10), UseMnemonic = false };
        }
        public static ActionButton Button(string name, string text, bool primary = false)
        {
            return new ActionButton { Name = name, AccessibleName = text, Text = text, Primary = primary,
                Size = new Size(180, 46), Margin = new Padding(0, 0, 12, 0), Font = Font(10, true) };
        }
        public static FlowLayoutPanel Stack()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Dock = DockStyle.Fill, BackColor = Palette.Background, Margin = Padding.Empty };
        }
        public static TableLayoutPanel Vertical()
        {
            TableLayoutPanel table = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }
        public static void AddRow(TableLayoutPanel table, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            table.Controls.Add(control, 0, row);
        }
        public static void FillStackWidth(FlowLayoutPanel flow)
        {
            EventHandler resize = delegate
            {
                int width = Math.Max(120, flow.ClientSize.Width - flow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
                foreach (Control child in flow.Controls)
                {
                    int available = Math.Max(40, width - child.Margin.Horizontal);
                    int fixedHeight = child.AutoSize ? 0 : child.Height;
                    child.MinimumSize = new Size(available, fixedHeight);
                    child.MaximumSize = new Size(available, fixedHeight);
                    child.Width = available;
                }
            };
            flow.SizeChanged += resize;
            flow.ControlAdded += delegate { resize(flow, EventArgs.Empty); };
            bool arranging = false;
            flow.Layout += delegate
            {
                if (arranging) return;
                arranging = true;
                try { resize(flow, EventArgs.Empty); }
                finally { arranging = false; }
            };
        }
    }

    internal sealed class ActionButton : Button
    {
        private bool hover;
        public bool Primary { get; set; }
        public bool Selected { get; set; }
        public ActionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Palette.Surface : Primary ? Palette.Accent : Selected ? Palette.Raised : hover ? Palette.Raised : Palette.Surface;
            Color ink = !Enabled ? Palette.Muted : Primary ? Palette.Background : Selected ? Palette.Accent : Palette.Text;
            using (GraphicsPath path = Shape.Round(new Rectangle(1, 1, Width - 3, Height - 3), 9))
            using (Brush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(Selected ? Palette.Accent : Palette.Line))
            { e.Graphics.FillPath(brush, path); if (!Primary) e.Graphics.DrawPath(pen, path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(10, 0, Width - 20, Height), ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(6, 6, Width - 12, Height - 12), ink, fill);
        }
    }

    internal static class Shape
    {
        public static GraphicsPath Round(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
    }

    internal sealed class CoreArtwork : Control
    {
        public CoreArtwork()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Surface; TabStop = false; AccessibleName = "Ilustração de controle de jogos";
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Min(Width / 340f, Height / 280f);
            g.TranslateTransform(Width / 2f, Height / 2f); g.ScaleTransform(scale, scale);
            using (Pen orbit = new Pen(Palette.Line, 1))
            using (Pen accent = new Pen(Palette.Accent, 3))
            using (Pen violet = new Pen(Palette.Violet, 3))
            using (Brush surface = new SolidBrush(Palette.Raised))
            using (Brush dot = new SolidBrush(Palette.Accent))
            {
                g.DrawEllipse(orbit, -119, -119, 238, 238); g.DrawEllipse(orbit, -96, -96, 192, 192);
                g.DrawArc(accent, -119, -119, 238, 238, 190, 62);
                g.DrawArc(violet, -119, -119, 238, 238, 25, 53);
                g.FillEllipse(dot, -111, 46, 9, 9); g.FillEllipse(dot, 83, -92, 6, 6);
                using (GraphicsPath pad = new GraphicsPath())
                {
                    pad.AddBezier(-64, -39, -86, -41, -107, 47, -82, 60);
                    pad.AddBezier(-82, 60, -66, 69, -46, 29, -35, 27);
                    pad.AddLine(-35, 27, 35, 27);
                    pad.AddBezier(35, 27, 46, 29, 66, 69, 82, 60);
                    pad.AddBezier(82, 60, 107, 47, 86, -41, 64, -39);
                    pad.CloseFigure(); g.FillPath(surface, pad); g.DrawPath(violet, pad);
                }
                g.DrawLine(accent, -69, -8, -39, -8); g.DrawLine(accent, -54, -23, -54, 7);
                g.DrawEllipse(accent, 53, -20, 11, 11); g.DrawEllipse(violet, 38, -3, 11, 11);
                g.DrawEllipse(orbit, -25, 4, 17, 17); g.DrawEllipse(orbit, 8, 4, 17, 17);
            }
            base.OnPaint(e);
        }
    }
}
