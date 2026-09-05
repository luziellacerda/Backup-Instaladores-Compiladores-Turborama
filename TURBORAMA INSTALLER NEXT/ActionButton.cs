using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TurboRama.Next
{
    internal enum ButtonAppearance { Secondary, Primary, Quiet, Navigation, Compound }

    // One renderer and input-state implementation for all app actions. Button's
    // native keyboard activation and accessibility remain intact.
    internal class ActionButton : Button
    {
        private bool hover, mouseDown, keyDown, selected;
        private float hoverLevel;
        private readonly Timer transition;
        private ButtonAppearance appearance;
        private Glyph icon;
        private bool trailingArrow;
        private string description = "";
        public bool Primary { get { return appearance == ButtonAppearance.Primary; } set { Appearance = value ? ButtonAppearance.Primary : ButtonAppearance.Secondary; } }
        public ButtonAppearance Appearance { get { return appearance; } set { if (appearance != value) { appearance = value; Invalidate(); } } }
        public Glyph Icon { get { return icon; } set { icon = value; Invalidate(); } }
        public bool TrailingArrow { get { return trailingArrow; } set { trailingArrow = value; Invalidate(); } }
        public string Description { get { return description; } set { description = value ?? ""; AccessibleDescription = description; Invalidate(); } }
        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); } }
        }
        internal bool IsPressed { get { return Enabled && (keyDown || (mouseDown && hover)); } }
        internal bool IsAnimating { get { return transition.Enabled; } }
        internal float HoverLevel { get { return hoverLevel; } }

        public ActionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false; BackColor = Color.Transparent;
            transition = new Timer { Interval = 16 };
            transition.Tick += Animate;
        }
        private void Animate(object sender, EventArgs args)
        {
            float target = Enabled && hover ? 1f : 0f;
            hoverLevel += (target - hoverLevel) * .38f;
            if (Math.Abs(target - hoverLevel) < .025f) { hoverLevel = target; transition.Stop(); }
            Invalidate();
        }
        private void SetHover(bool value)
        {
            hover = value;
            if (!Enabled || !Visible || SystemInformation.HighContrast || !AnimationsEnabled())
            { transition.Stop(); hoverLevel = Enabled && value ? 1 : 0; }
            else transition.Start();
            Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { SetHover(false); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        { if (Enabled && e.Button == MouseButtons.Left) { mouseDown = true; hover = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        { mouseDown = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { if (!Capture) { mouseDown = false; Invalidate(); } base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (Enabled && e.KeyCode == Keys.Space) { keyDown = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e)
        { if (e.KeyCode == Keys.Space) { keyDown = false; Invalidate(); } base.OnKeyUp(e); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            mouseDown = keyDown = hover = false; hoverLevel = 0;
            if (transition != null) transition.Stop();
            Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e);
        }
        protected override void OnVisibleChanged(EventArgs e)
        { if (!Visible && transition != null) { transition.Stop(); hoverLevel = 0; hover = mouseDown = keyDown = false; } base.OnVisibleChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keyDown = mouseDown = false; Invalidate(); base.OnLostFocus(e); }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ActionAccessibility(this); }
        private sealed class ActionAccessibility : ControlAccessibleObject
        {
            private readonly ActionButton owner;
            public ActionAccessibility(ActionButton owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
            public override string DefaultAction { get { return "Pressionar"; } }
            public override void DoDefaultAction() { if (owner.Enabled) owner.PerformClick(); }
            public override AccessibleStates State { get { return base.State | (owner.Selected ? AccessibleStates.Selected : AccessibleStates.None); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 12 || Height < 12) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float dpi = g.DpiX / 96f;
            int inset = Px(4, dpi), radius = Px(Appearance == ButtonAppearance.Compound ? 16 : 12, dpi);
            Rectangle surface = new Rectangle(inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
            if (surface.Width <= 2 || surface.Height <= 2) return;
            bool pressed = IsPressed, highContrast = SystemInformation.HighContrast;
            bool quiet = Appearance == ButtonAppearance.Quiet || Appearance == ButtonAppearance.Navigation;
            Color ink = Enabled ? (Primary ? Color.FromArgb(19, 35, 24) : Palette.Text) : Color.FromArgb(151, 162, 177);
            Color top, bottom, border;
            if (!Enabled) { top = bottom = Color.FromArgb(31, 35, 43); border = Color.FromArgb(51, 58, 70); }
            else if (Primary)
            {
                top = Mix(Color.FromArgb(211, 255, 153), Color.FromArgb(231, 255, 197), hoverLevel);
                bottom = Mix(Color.FromArgb(148, 225, 143), Color.FromArgb(173, 244, 169), hoverLevel);
                border = Color.FromArgb(219, 255, 183);
            }
            else if (Selected)
            { top = Color.FromArgb(44, 52, 48); bottom = Color.FromArgb(33, 41, 39); border = Color.FromArgb(77, 102, 79); ink = Color.FromArgb(212, 255, 167); }
            else
            {
                top = Mix(Color.FromArgb(40, 45, 57), Color.FromArgb(53, 62, 75), hoverLevel);
                bottom = Mix(Color.FromArgb(29, 34, 43), Color.FromArgb(39, 48, 59), hoverLevel);
                border = Mix(Color.FromArgb(72, 80, 95), Color.FromArgb(123, 141, 160), hoverLevel);
            }
            if (pressed) { top = bottom = Primary ? Color.FromArgb(139, 204, 128) : Color.FromArgb(25, 31, 39); }
            if (highContrast)
            { top = bottom = Selected ? SystemColors.Highlight : SystemColors.Control; ink = !Enabled ? SystemColors.GrayText : Selected ? SystemColors.HighlightText : SystemColors.ControlText; border = SystemColors.ControlText; }
            using (GraphicsPath outline = Shape.Round(surface, radius))
            {
                if (!highContrast && Enabled && !quiet && !pressed)
                {
                    Rectangle shadowRect = surface; shadowRect.Offset(0, Px(2, dpi));
                    using (GraphicsPath shadowPath = Shape.Round(shadowRect, radius))
                    using (Brush shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0))) g.FillPath(shadow, shadowPath);
                }
                if (highContrast || !quiet || Selected || hoverLevel > 0 || pressed)
                {
                    if (quiet && !Selected && !pressed && !highContrast)
                    { top = Color.FromArgb((int)(150 * hoverLevel), top); bottom = Color.FromArgb((int)(150 * hoverLevel), bottom); }
                    using (LinearGradientBrush material = new LinearGradientBrush(surface, top, bottom, 90f)) g.FillPath(material, outline);
                    if (!quiet || Selected || highContrast)
                    using (Pen stroke = new Pen(border, Math.Max(1f, dpi))) g.DrawPath(stroke, outline);
                    if (!quiet && !highContrast && Enabled && !pressed)
                    {
                        using (Pen highlight = new Pen(Color.FromArgb(Primary ? 120 : 38, Color.White), Math.Max(1f, dpi)))
                            g.DrawLine(highlight, surface.Left + radius, surface.Top + 1, surface.Right - radius, surface.Top + 1);
                    }
                }
            }
            if (Appearance == ButtonAppearance.Navigation && Selected)
            {
                int lineWidth = Math.Min(Px(28, dpi), surface.Width / 3);
                using (Pen rail = new Pen(highContrast ? SystemColors.HighlightText : Palette.Accent, Px(3, dpi)))
                { rail.StartCap = rail.EndCap = LineCap.Round; g.DrawLine(rail, Width / 2 - lineWidth / 2, surface.Bottom - Px(3, dpi), Width / 2 + lineWidth / 2, surface.Bottom - Px(3, dpi)); }
            }
            Rectangle body = Rectangle.Inflate(surface, -Px(14, dpi), 0);
            if (pressed) body.Offset(0, Px(1, dpi));
            if (Appearance == ButtonAppearance.Compound) DrawCompound(g, body, ink, dpi);
            else DrawContent(g, body, ink, dpi);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = new Rectangle(Px(1, dpi), Px(1, dpi), Width - Px(2, dpi) - 1, Height - Px(2, dpi) - 1);
                using (GraphicsPath ring = Shape.Round(focus, radius + Px(3, dpi)))
                using (Pen focusPen = new Pen(highContrast ? SystemColors.Highlight : Color.FromArgb(153, 211, 255), Px(2, dpi))) g.DrawPath(focusPen, ring);
            }
        }
        private void DrawContent(Graphics g, Rectangle body, Color ink, float dpi)
        {
            int glyphSize = Px(19, dpi), gap = Px(10, dpi);
            if (TrailingArrow)
            {
                int right = body.Right - glyphSize;
                VectorIcon.Draw(g, Glyph.ArrowRight, new RectangleF(right, body.Top + (body.Height - glyphSize) / 2, glyphSize, glyphSize), ink);
                body.Width = Math.Max(1, body.Width - glyphSize - gap);
            }
            if (Icon != Glyph.None)
            {
                VectorIcon.Draw(g, Icon, new RectangleF(body.Left, body.Top + (body.Height - glyphSize) / 2, glyphSize, glyphSize), ink);
                body.X += glyphSize + gap; body.Width = Math.Max(1, body.Width - glyphSize - gap);
            }
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (Icon == Glyph.None && !TrailingArrow) flags |= TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(g, Text, Font, body, ink, flags);
        }
        private void DrawCompound(Graphics g, Rectangle body, Color ink, float dpi)
        {
            bool highContrast = SystemInformation.HighContrast;
            int tileSize = Px(48, dpi), iconSize = Px(24, dpi), gap = Px(18, dpi);
            Rectangle tile = new Rectangle(body.Left + Px(6, dpi), body.Top + (body.Height - tileSize) / 2, tileSize, tileSize);
            using (GraphicsPath tilePath = Shape.Round(tile, Px(13, dpi)))
            using (Brush tileBrush = new SolidBrush(highContrast ? (Selected ? SystemColors.Highlight : SystemColors.Control) : Color.FromArgb(24, Palette.Violet))) g.FillPath(tileBrush, tilePath);
            VectorIcon.Draw(g, Icon, new RectangleF(tile.Left + (tileSize - iconSize) / 2, tile.Top + (tileSize - iconSize) / 2, iconSize, iconSize), highContrast ? ink : Enabled ? Palette.Violet : ink);
            Rectangle text = new Rectangle(tile.Right + gap, body.Top + Px(19, dpi), Math.Max(1, body.Right - tile.Right - gap - Px(32, dpi)), body.Height - Px(34, dpi));
            int titleHeight = TextRenderer.MeasureText(g, Text, Font).Height;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(text.X, text.Y, text.Width, titleHeight), ink,
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (Font detailFont = new Font(Font.FontFamily, Math.Max(9f, Font.Size - 1), FontStyle.Regular))
                TextRenderer.DrawText(g, Description, detailFont, new Rectangle(text.X, text.Y + titleHeight + Px(6, dpi), text.Width, Math.Max(1, text.Height - titleHeight)),
                    highContrast ? ink : Enabled ? Palette.Muted : ink, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            VectorIcon.Draw(g, Glyph.ArrowRight, new RectangleF(body.Right - Px(22, dpi), body.Top + (body.Height - Px(20, dpi)) / 2, Px(20, dpi), Px(20, dpi)), ink);
        }
        private static int Px(int logical, float dpi) { return Math.Max(1, (int)Math.Round(logical * dpi)); }
        private static bool AnimationsEnabled()
        {
            bool enabled;
            return SystemParametersInfo(0x1042, 0, out enabled, 0) && enabled;
        }
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
        private static Color Mix(Color from, Color to, float amount)
        { return Color.FromArgb((int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount)); }
        protected override void Dispose(bool disposing)
        { if (disposing && transition != null) transition.Dispose(); base.Dispose(disposing); }
    }
}
