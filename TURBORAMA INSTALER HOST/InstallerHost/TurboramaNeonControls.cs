using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace InstallerHost
{
    internal enum NeonButtonKind
    {
        Primary,
        Secondary,
        Quiet,
        Danger
    }

    /// <summary>
    /// DPI boundary for the visual tree created at runtime. All children are
    /// authored in 96-DPI logical units before this control is parented. Once
    /// attached, WinForms AutoScaleMode.Dpi performs the only tree transform.
    /// </summary>
    internal sealed class NeonDpiViewport : UserControl
    {
        public NeonDpiViewport()
        {
            this.AutoScaleDimensions = new SizeF(96f, 96f);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = TurboramaPremiumTheme.Background;
            this.ForeColor = TurboramaPremiumTheme.Text;
            this.Margin = Padding.Empty;
            this.Padding = Padding.Empty;
            this.TabStop = false;
            this.AccessibleRole = AccessibleRole.Pane;
            this.AccessibleName = "Conteúdo do instalador Turborama";
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }
    }

    internal static class NeonDrawing
    {
        public static Color WithAlpha(Color color, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color.R, color.G, color.B);
        }

        public static Color Blend(Color first, Color second, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(first.A + ((second.A - first.A) * amount)),
                (int)(first.R + ((second.R - first.R) * amount)),
                (int)(first.G + ((second.G - first.G) * amount)),
                (int)(first.B + ((second.B - first.B) * amount)));
        }

        public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return path;
            }

            int safeRadius = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
            if (safeRadius == 0)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            int diameter = safeRadius * 2;
            Rectangle arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// Double-buffered, gradient surface used by the runtime theme. It is kept
    /// outside designer files so the original WinForms event wiring stays intact.
    /// </summary>
    internal class NeonSurfacePanel : Panel
    {
        private bool hovered;

        public NeonSurfacePanel()
        {
            this.SurfaceColor = TurboramaPremiumTheme.Surface;
            this.SurfaceColor2 = TurboramaPremiumTheme.SurfaceRaised;
            this.BorderColor = TurboramaPremiumTheme.Border;
            this.AccentColor = TurboramaPremiumTheme.Cyan;
            this.CornerRadius = 14;
            this.GlowStrength = 32;
            this.ShowAccent = true;
            this.ShowGrid = false;
            this.Interactive = false;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            this.BackColor = Color.Transparent;
        }

        public Color SurfaceColor { get; set; }

        public Color SurfaceColor2 { get; set; }

        public Color BorderColor { get; set; }

        public Color AccentColor { get; set; }

        public int CornerRadius { get; set; }

        public int GlowStrength { get; set; }

        public bool ShowAccent { get; set; }

        public bool ShowGrid { get; set; }

        public bool Interactive { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (this.Interactive)
            {
                this.hovered = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (this.Interactive && !this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)))
            {
                this.hovered = false;
                this.Invalidate();
            }
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control != null)
            {
                e.Control.MouseEnter += this.Child_MouseEnter;
                e.Control.MouseLeave += this.Child_MouseLeave;
            }
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            if (e.Control != null)
            {
                e.Control.MouseEnter -= this.Child_MouseEnter;
                e.Control.MouseLeave -= this.Child_MouseLeave;
            }
            base.OnControlRemoved(e);
        }

        private void Child_MouseEnter(object sender, EventArgs e)
        {
            if (this.Interactive)
            {
                this.hovered = true;
                this.Invalidate();
            }
        }

        private void Child_MouseLeave(object sender, EventArgs e)
        {
            if (this.Interactive && !this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)))
            {
                this.hovered = false;
                this.Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (this.Width <= 1 || this.Height <= 1)
            {
                base.OnPaintBackground(e);
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float dpiScale = Math.Max(1f, this.DeviceDpi / 96f);
            Rectangle bounds = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color top = this.hovered
                ? NeonDrawing.Blend(this.SurfaceColor, this.AccentColor, 0.09f)
                : this.SurfaceColor;
            Color bottom = this.hovered
                ? NeonDrawing.Blend(this.SurfaceColor2, this.AccentColor, 0.05f)
                : this.SurfaceColor2;

            using (GraphicsPath path = NeonDrawing.RoundedRectangle(bounds, (int)Math.Round(this.CornerRadius * dpiScale)))
            using (LinearGradientBrush fill = new LinearGradientBrush(bounds, top, bottom, LinearGradientMode.Vertical))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.SetClip(path);

                if (this.ShowGrid)
                {
                    using (Pen grid = new Pen(NeonDrawing.WithAlpha(this.AccentColor, 12), 1f))
                    {
                        int gridStep = Math.Max(24, (int)Math.Round(32f * dpiScale));
                        for (int x = gridStep / 3; x < bounds.Width; x += gridStep)
                        {
                            e.Graphics.DrawLine(grid, x, 0, x, bounds.Height);
                        }
                        for (int y = gridStep / 3; y < bounds.Height; y += gridStep)
                        {
                            e.Graphics.DrawLine(grid, 0, y, bounds.Width, y);
                        }
                    }
                }

                e.Graphics.ResetClip();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (this.Width <= 1 || this.Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float dpiScale = Math.Max(1f, this.DeviceDpi / 96f);
            Rectangle bounds = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = NeonDrawing.RoundedRectangle(bounds, (int)Math.Round(this.CornerRadius * dpiScale)))
            {
                int glow = this.hovered ? Math.Min(100, this.GlowStrength + 28) : this.GlowStrength;
                if (glow > 0)
                {
                    for (int width = 5; width >= 2; width--)
                    {
                        int alpha = Math.Max(4, glow / (width + 1));
                        using (Pen glowPen = new Pen(NeonDrawing.WithAlpha(this.AccentColor, alpha), width))
                        {
                            glowPen.Alignment = PenAlignment.Inset;
                            e.Graphics.DrawPath(glowPen, path);
                        }
                    }
                }

                Color border = this.hovered
                    ? NeonDrawing.Blend(this.BorderColor, this.AccentColor, 0.68f)
                    : this.BorderColor;
                using (Pen borderPen = new Pen(border, this.hovered ? 1.5f : 1f))
                {
                    borderPen.Alignment = PenAlignment.Inset;
                    e.Graphics.DrawPath(borderPen, path);
                }
            }

            if (this.ShowAccent)
            {
                int accentWidth = Math.Max((int)Math.Round(48f * dpiScale), Math.Min((int)Math.Round(180f * dpiScale), this.Width / 3));
                Rectangle accentBounds = new Rectangle((int)Math.Round(18f * dpiScale), 0, accentWidth, Math.Max(2, (int)Math.Round(2f * dpiScale)));
                using (LinearGradientBrush accent = new LinearGradientBrush(
                    accentBounds,
                    this.AccentColor,
                    Color.Transparent,
                    LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillRectangle(accent, accentBounds);
                }
            }
        }
    }

    internal sealed class NeonBackdropPanel : Panel
    {
        public NeonBackdropPanel()
        {
            this.BackColor = TurboramaPremiumTheme.Background;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (this.Width <= 0 || this.Height <= 0)
            {
                base.OnPaintBackground(e);
                return;
            }

            Rectangle bounds = this.ClientRectangle;
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds,
                TurboramaPremiumTheme.Background,
                TurboramaPremiumTheme.BackgroundDeep,
                24f))
            {
                e.Graphics.FillRectangle(background, bounds);
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            DrawAmbientGlow(e.Graphics, new Point(this.Width - 40, 42), Math.Max(180, this.Width / 2), TurboramaPremiumTheme.Violet);
            DrawAmbientGlow(e.Graphics, new Point(48, this.Height - 32), Math.Max(150, this.Width / 3), TurboramaPremiumTheme.Cyan);

            using (Pen gridPen = new Pen(NeonDrawing.WithAlpha(TurboramaPremiumTheme.Cyan, 9), 1f))
            {
                for (int x = 20; x < this.Width; x += 40)
                {
                    e.Graphics.DrawLine(gridPen, x, 0, x, this.Height);
                }
                for (int y = 20; y < this.Height; y += 40)
                {
                    e.Graphics.DrawLine(gridPen, 0, y, this.Width, y);
                }
            }
        }

        private static void DrawAmbientGlow(Graphics graphics, Point center, int diameter, Color color)
        {
            int rings = 7;
            for (int i = rings; i >= 1; i--)
            {
                int size = Math.Max(1, diameter * i / rings);
                int alpha = Math.Max(1, 3 + ((rings - i) * 2));
                Rectangle circle = new Rectangle(center.X - (size / 2), center.Y - (size / 2), size, size);
                using (SolidBrush brush = new SolidBrush(NeonDrawing.WithAlpha(color, alpha)))
                {
                    graphics.FillEllipse(brush, circle);
                }
            }
        }
    }

    internal sealed class NeonLedIndicator : Control
    {
        public NeonLedIndicator()
        {
            this.LedColor = TurboramaPremiumTheme.Green;
            this.Size = new Size(12, 12);
            this.TabStop = false;
            this.AccessibleRole = AccessibleRole.StaticText;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            this.BackColor = Color.Transparent;
        }

        public Color LedColor { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int size = Math.Max(2, Math.Min(this.Width, this.Height) - 6);
            Rectangle glow = new Rectangle((this.Width - size) / 2, (this.Height - size) / 2, size, size);
            using (Pen halo = new Pen(NeonDrawing.WithAlpha(this.LedColor, 74), 5f))
            using (SolidBrush fill = new SolidBrush(this.LedColor))
            {
                e.Graphics.DrawEllipse(halo, glow);
                e.Graphics.FillEllipse(fill, glow);
            }
        }
    }

    internal sealed class NeonProgressMirror : Control
    {
        private readonly Timer refreshTimer;
        private ProgressBar source;
        private int displayedValue;
        private int displayedMaximum = 100;

        public NeonProgressMirror()
        {
            this.BackColor = TurboramaPremiumTheme.Surface;
            this.ForeColor = TurboramaPremiumTheme.Cyan;
            this.TabStop = false;
            this.AccessibleRole = AccessibleRole.ProgressBar;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            this.refreshTimer = new Timer();
            this.refreshTimer.Interval = 80;
            this.refreshTimer.Tick += this.RefreshTimer_Tick;
            this.refreshTimer.Start();
        }

        public void Bind(ProgressBar progressBar)
        {
            this.source = progressBar;
            this.SyncFromSource();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.refreshTimer.Stop();
                this.refreshTimer.Tick -= this.RefreshTimer_Tick;
                this.refreshTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, this.Width - 1), Math.Max(1, this.Height - 1));
            int radius = Math.Max(4, (int)Math.Round(7f * Math.Max(1f, this.DeviceDpi / 96f)));
            using (GraphicsPath track = NeonDrawing.RoundedRectangle(bounds, radius))
            using (SolidBrush trackFill = new SolidBrush(TurboramaPremiumTheme.InputBackground))
            using (Pen trackBorder = new Pen(TurboramaPremiumTheme.Border, 1f))
            {
                e.Graphics.FillPath(trackFill, track);
                e.Graphics.DrawPath(trackBorder, track);
            }

            float ratio = this.displayedMaximum <= 0 ? 0f : Math.Max(0f, Math.Min(1f, this.displayedValue / (float)this.displayedMaximum));
            int fillWidth = (int)Math.Round(bounds.Width * ratio);
            if (fillWidth > 2)
            {
                Rectangle fillBounds = new Rectangle(1, 1, Math.Min(bounds.Width - 1, fillWidth), Math.Max(1, bounds.Height - 2));
                using (GraphicsPath fillPath = NeonDrawing.RoundedRectangle(fillBounds, Math.Max(2, radius - 1)))
                using (LinearGradientBrush fill = new LinearGradientBrush(fillBounds, TurboramaPremiumTheme.Cyan, TurboramaPremiumTheme.Green, LinearGradientMode.Horizontal))
                using (Pen glow = new Pen(NeonDrawing.WithAlpha(TurboramaPremiumTheme.Cyan, 64), 3f))
                {
                    e.Graphics.FillPath(fill, fillPath);
                    e.Graphics.DrawPath(glow, fillPath);
                }
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            this.SyncFromSource();
        }

        private void SyncFromSource()
        {
            if (this.source == null || this.source.IsDisposed)
            {
                this.Visible = false;
                return;
            }

            int nextMaximum = Math.Max(1, this.source.Maximum - this.source.Minimum);
            int nextValue = Math.Max(0, this.source.Value - this.source.Minimum);
            bool changed = nextMaximum != this.displayedMaximum || nextValue != this.displayedValue;
            this.displayedMaximum = nextMaximum;
            this.displayedValue = nextValue;
            this.Visible = this.source.Visible;
            this.AccessibleDescription = "Progresso: " + ((int)Math.Round((this.displayedValue * 100d) / this.displayedMaximum)).ToString() + "%";
            if (changed)
            {
                this.Invalidate();
            }
        }
    }

    internal sealed class NeonBrandMark : Control
    {
        public NeonBrandMark()
        {
            this.Size = new Size(62, 54);
            this.ForeColor = TurboramaPremiumTheme.Text;
            this.TabStop = false;
            this.AccessibleRole = AccessibleRole.Graphic;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            this.BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(3, 3, Math.Max(1, this.Width - 7), Math.Max(1, this.Height - 7));
            PointF[] hexagon = CreateHexagon(bounds);

            using (SolidBrush fill = new SolidBrush(NeonDrawing.WithAlpha(TurboramaPremiumTheme.Cyan, 18)))
            {
                e.Graphics.FillPolygon(fill, hexagon);
            }
            using (Pen outerGlow = new Pen(NeonDrawing.WithAlpha(TurboramaPremiumTheme.Cyan, 55), 4f))
            {
                e.Graphics.DrawPolygon(outerGlow, hexagon);
            }
            using (Pen border = new Pen(TurboramaPremiumTheme.Cyan, 1.4f))
            {
                e.Graphics.DrawPolygon(border, hexagon);
            }

            Rectangle textBounds = new Rectangle(0, 0, this.Width, this.Height);
            using (Font font = new Font("Segoe UI Semibold", 12.5f, FontStyle.Bold, GraphicsUnit.Point))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "TR",
                    font,
                    textBounds,
                    this.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private static PointF[] CreateHexagon(Rectangle bounds)
        {
            float quarter = bounds.Width * 0.24f;
            return new PointF[]
            {
                new PointF(bounds.Left + quarter, bounds.Top),
                new PointF(bounds.Right - quarter, bounds.Top),
                new PointF(bounds.Right, bounds.Top + (bounds.Height / 2f)),
                new PointF(bounds.Right - quarter, bounds.Bottom),
                new PointF(bounds.Left + quarter, bounds.Bottom),
                new PointF(bounds.Left, bounds.Top + (bounds.Height / 2f))
            };
        }
    }

    internal sealed class NeonStepRail : Control
    {
        private static readonly string[] DefaultSteps = new string[]
        {
            "Boas-vindas",
            "Licença",
            "Requisitos",
            "Instalação",
            "Progresso",
            "Conclusão"
        };

        private int activeIndex;

        public NeonStepRail()
        {
            this.activeIndex = 0;
            this.ForeColor = TurboramaPremiumTheme.Text;
            this.TabStop = false;
            this.AccessibleRole = AccessibleRole.ProgressBar;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            this.BackColor = Color.Transparent;
        }

        public int ActiveIndex
        {
            get { return this.activeIndex; }
            set
            {
                this.activeIndex = Math.Max(0, Math.Min(DefaultSteps.Length - 1, value));
                this.AccessibleDescription = "Etapa " + (this.activeIndex + 1).ToString() + " de " + DefaultSteps.Length.ToString() + ": " + DefaultSteps[this.activeIndex];
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (this.Width < 80 || this.Height < 80)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float dpiScale = Math.Max(1f, this.DeviceDpi / 96f);
            int rowHeight = Math.Max((int)Math.Round(18f * dpiScale), Math.Min((int)Math.Round(34f * dpiScale), this.Height / DefaultSteps.Length));
            int railX = (int)Math.Round(19f * dpiScale);
            int startY = Math.Max((int)Math.Round(2f * dpiScale), (this.Height - (rowHeight * DefaultSteps.Length)) / 2);

            using (Pen rail = new Pen(TurboramaPremiumTheme.Border, 1.5f))
            {
                e.Graphics.DrawLine(
                    rail,
                    railX,
                    startY + (rowHeight / 2),
                    railX,
                    startY + ((DefaultSteps.Length - 1) * rowHeight) + (rowHeight / 2));
            }

            using (Font stepFont = new Font("Segoe UI Semibold", 8.4f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font numberFont = new Font("Segoe UI Semibold", 7.2f, FontStyle.Bold, GraphicsUnit.Point))
            {
                for (int index = 0; index < DefaultSteps.Length; index++)
                {
                    bool active = index == this.activeIndex;
                    bool complete = index < this.activeIndex;
                    int top = startY + (index * rowHeight);

                    if (active)
                    {
                        int inset = Math.Max(2, (int)Math.Round(2f * dpiScale));
                        Rectangle selected = new Rectangle(inset, top + 1, this.Width - (inset * 2), rowHeight - 2);
                        using (GraphicsPath selectedPath = NeonDrawing.RoundedRectangle(selected, (int)Math.Round(9f * dpiScale)))
                        using (LinearGradientBrush selectedFill = new LinearGradientBrush(
                            selected,
                            NeonDrawing.WithAlpha(TurboramaPremiumTheme.Cyan, 30),
                            NeonDrawing.WithAlpha(TurboramaPremiumTheme.Violet, 15),
                            LinearGradientMode.Horizontal))
                        {
                            e.Graphics.FillPath(selectedFill, selectedPath);
                        }
                    }

                    Color nodeColor = complete
                        ? TurboramaPremiumTheme.Green
                        : (active ? TurboramaPremiumTheme.Cyan : TurboramaPremiumTheme.Dim);
                    int nodeSize = Math.Max(14, (int)Math.Round(16f * dpiScale));
                    Rectangle node = new Rectangle(railX - (nodeSize / 2), top + (rowHeight / 2) - (nodeSize / 2), nodeSize, nodeSize);
                    if (active || complete)
                    {
                        using (Pen glow = new Pen(NeonDrawing.WithAlpha(nodeColor, 62), 5f))
                        {
                            e.Graphics.DrawEllipse(glow, node);
                        }
                    }
                    using (SolidBrush nodeFill = new SolidBrush(active || complete ? TurboramaPremiumTheme.SurfaceRaised : TurboramaPremiumTheme.Surface))
                    using (Pen nodeBorder = new Pen(nodeColor, active ? 2f : 1.2f))
                    {
                        e.Graphics.FillEllipse(nodeFill, node);
                        e.Graphics.DrawEllipse(nodeBorder, node);
                    }

                    string marker = complete ? "✓" : (index + 1).ToString("00");
                    TextRenderer.DrawText(
                        e.Graphics,
                        marker,
                        numberFont,
                        node,
                        nodeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                    int labelLeft = (int)Math.Round(38f * dpiScale);
                    Rectangle labelBounds = new Rectangle(labelLeft, top, Math.Max((int)Math.Round(30f * dpiScale), this.Width - labelLeft - (int)Math.Round(4f * dpiScale)), rowHeight);
                    Color labelColor = active
                        ? TurboramaPremiumTheme.Text
                        : (complete ? TurboramaPremiumTheme.TextMuted : TurboramaPremiumTheme.Dim);
                    TextRenderer.DrawText(
                        e.Graphics,
                        DefaultSteps[index],
                        stepFont,
                        labelBounds,
                        labelColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }
        }
    }

    internal static class NeonInteraction
    {
        private static readonly ConditionalWeakTable<Button, NeonButtonState> ButtonStates = new ConditionalWeakTable<Button, NeonButtonState>();
        private static readonly ConditionalWeakTable<TextBoxBase, NeonFieldState> FieldStates = new ConditionalWeakTable<TextBoxBase, NeonFieldState>();

        public static void StyleButton(Button button, NeonButtonKind kind)
        {
            if (button == null)
            {
                return;
            }

            NeonButtonState state = ButtonStates.GetValue(button, CreateButtonState);
            state.Kind = kind;
            state.Refresh();
        }

        public static void StyleField(TextBoxBase field)
        {
            if (field == null)
            {
                return;
            }

            NeonFieldState state = FieldStates.GetValue(field, CreateFieldState);
            state.Refresh();
        }

        private static NeonButtonState CreateButtonState(Button button)
        {
            return new NeonButtonState(button);
        }

        private static NeonFieldState CreateFieldState(TextBoxBase field)
        {
            return new NeonFieldState(field);
        }

        private sealed class NeonButtonState
        {
            private readonly Button button;
            private bool hovered;
            private bool pressed;

            public NeonButtonState(Button button)
            {
                this.button = button;
                this.Kind = NeonButtonKind.Secondary;
                button.MouseEnter += this.Button_MouseEnter;
                button.MouseLeave += this.Button_MouseLeave;
                button.MouseDown += this.Button_MouseDown;
                button.MouseUp += this.Button_MouseUp;
                button.KeyDown += this.Button_KeyDown;
                button.KeyUp += this.Button_KeyUp;
                button.GotFocus += this.Button_FocusChanged;
                button.LostFocus += this.Button_FocusChanged;
                button.EnabledChanged += this.Button_EnabledChanged;
                button.Resize += this.Button_Resize;
            }

            public NeonButtonKind Kind { get; set; }

            public void Refresh()
            {
                Color accent;
                Color background;
                Color hover;
                Color pressedColor;
                Color foreground = TurboramaPremiumTheme.Text;

                switch (this.Kind)
                {
                    case NeonButtonKind.Primary:
                        accent = TurboramaPremiumTheme.Cyan;
                        background = Color.FromArgb(10, 62, 76);
                        hover = Color.FromArgb(12, 91, 105);
                        pressedColor = Color.FromArgb(15, 113, 122);
                        break;
                    case NeonButtonKind.Danger:
                        accent = TurboramaPremiumTheme.Danger;
                        background = Color.FromArgb(42, 18, 30);
                        hover = Color.FromArgb(72, 25, 43);
                        pressedColor = Color.FromArgb(91, 31, 51);
                        break;
                    case NeonButtonKind.Quiet:
                        accent = TurboramaPremiumTheme.BorderStrong;
                        background = TurboramaPremiumTheme.Surface;
                        hover = TurboramaPremiumTheme.SurfaceHover;
                        pressedColor = TurboramaPremiumTheme.SurfaceRaised;
                        foreground = TurboramaPremiumTheme.TextMuted;
                        break;
                    default:
                        accent = TurboramaPremiumTheme.Violet;
                        background = Color.FromArgb(21, 21, 43);
                        hover = Color.FromArgb(39, 31, 68);
                        pressedColor = Color.FromArgb(53, 38, 85);
                        break;
                }

                this.button.FlatStyle = FlatStyle.Flat;
                this.button.UseVisualStyleBackColor = false;
                this.button.FlatAppearance.BorderSize = this.button.Focused ? 2 : 1;
                this.button.FlatAppearance.BorderColor = this.button.Enabled ? accent : TurboramaPremiumTheme.Border;
                this.button.FlatAppearance.MouseOverBackColor = hover;
                this.button.FlatAppearance.MouseDownBackColor = pressedColor;
                this.button.BackColor = !this.button.Enabled
                    ? TurboramaPremiumTheme.SurfaceDisabled
                    : (this.pressed ? pressedColor : (this.hovered || this.button.Focused ? hover : background));
                this.button.ForeColor = this.button.Enabled ? foreground : TurboramaPremiumTheme.Dim;
                this.button.Font = TurboramaPremiumTheme.CreateFont(9.2f, FontStyle.Bold);
                this.button.Cursor = this.button.Enabled ? Cursors.Hand : Cursors.Default;
                this.button.AccessibleRole = AccessibleRole.PushButton;
                ApplyRoundedRegion(this.button);
                this.button.Invalidate();
            }

            private void Button_MouseEnter(object sender, EventArgs e)
            {
                this.hovered = true;
                this.Refresh();
            }

            private void Button_MouseLeave(object sender, EventArgs e)
            {
                this.hovered = false;
                this.pressed = false;
                this.Refresh();
            }

            private void Button_MouseDown(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    this.pressed = true;
                    this.Refresh();
                }
            }

            private void Button_MouseUp(object sender, MouseEventArgs e)
            {
                this.pressed = false;
                this.Refresh();
            }

            private void Button_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                {
                    this.pressed = true;
                    this.Refresh();
                }
            }

            private void Button_KeyUp(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                {
                    this.pressed = false;
                    this.Refresh();
                }
            }

            private void Button_FocusChanged(object sender, EventArgs e)
            {
                this.Refresh();
            }

            private void Button_EnabledChanged(object sender, EventArgs e)
            {
                this.Refresh();
            }

            private void Button_Resize(object sender, EventArgs e)
            {
                ApplyRoundedRegion(this.button);
            }

            private static void ApplyRoundedRegion(Button button)
            {
                if (button.Width <= 2 || button.Height <= 2)
                {
                    return;
                }

                Region oldRegion = button.Region;
                int radius = Math.Max(6, (int)Math.Round(8f * Math.Max(1f, button.DeviceDpi / 96f)));
                using (GraphicsPath path = NeonDrawing.RoundedRectangle(new Rectangle(0, 0, button.Width, button.Height), radius))
                {
                    button.Region = new Region(path);
                }
                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }
        }

        private sealed class NeonFieldState
        {
            private readonly TextBoxBase field;

            public NeonFieldState(TextBoxBase field)
            {
                this.field = field;
                field.GotFocus += this.Field_FocusChanged;
                field.LostFocus += this.Field_FocusChanged;
                field.EnabledChanged += this.Field_FocusChanged;
            }

            public void Refresh()
            {
                this.field.BorderStyle = BorderStyle.FixedSingle;
                this.field.BackColor = this.field.Focused
                    ? TurboramaPremiumTheme.SurfaceFocus
                    : TurboramaPremiumTheme.InputBackground;
                this.field.ForeColor = this.field.Enabled ? TurboramaPremiumTheme.Text : TurboramaPremiumTheme.Dim;
                this.field.Font = TurboramaPremiumTheme.CreateFont(9.2f, FontStyle.Regular);
                this.field.Invalidate();
            }

            private void Field_FocusChanged(object sender, EventArgs e)
            {
                this.Refresh();
            }
        }
    }
}
