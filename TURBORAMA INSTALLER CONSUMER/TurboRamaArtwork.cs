using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TurboRama.Next;

namespace InstallerHost
{
    // The image is embedded, never fetched at runtime. Every repaint owns and
    // clears its complete dirty region; no parent-buffer copying or overlays.
    internal class TurboRamaArtwork : Panel
    {
        internal const string ResourceName = "InstallerHost.resources.art.turborama-f15-arcade-v1.png";
        private static readonly Lazy<Bitmap> Artwork = new Lazy<Bitmap>(LoadArtwork);
        private readonly Timer glowTimer;
        private float phase;
        private readonly bool banner;
        private Bitmap surfaceCache;
        private Color surfaceColor;
        internal bool IsGlowRunning { get { return glowTimer.Enabled; } }
        internal static Size ArtworkSize { get { return Artwork.Value.Size; } }
        // Fit the WHOLE image, right-aligned. Never crop a part of the aircraft
        // to fill a short/wide wizard body.
        internal static Rectangle FitArtwork(Size viewport)
        {
            Size source = ArtworkSize;
            float scale = Math.Min(viewport.Width / (float)source.Width, viewport.Height / (float)source.Height);
            int width = Math.Max(1, (int)(source.Width * scale));
            int height = Math.Max(1, (int)(source.Height * scale));
            return new Rectangle(viewport.Width - width, (viewport.Height - height) / 2, width, height);
        }

        internal TurboRamaArtwork(bool bannerMode)
        {
            banner = bannerMode;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Background; TabStop = false;
            AccessibleName = banner ? "TurboRama — LZ Games" : "F-15 com turbinas acesas e fliperamas em neon";
            glowTimer = new Timer { Interval = 64 };
            glowTimer.Tick += delegate
            {
                Form form = FindForm();
                if (!Visible || (form != null && form.WindowState == FormWindowState.Minimized)) return;
                if (!CanAnimate()) { glowTimer.Stop(); phase = 0; Invalidate(); return; }
                phase = (phase + 0.045f) % ((float)Math.PI * 2);
                Invalidate(new Rectangle(0, Math.Max(0, Height - 8), Width, 8));
            };
        }
        private static Bitmap LoadArtwork()
        {
            using (Stream stream = typeof(TurboRamaArtwork).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidDataException("Arte TurboRama ausente do pacote.");
                using (Image image = Image.FromStream(stream)) return new Bitmap(image);
            }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RefreshMotion(); }
        protected override void OnHandleDestroyed(EventArgs e) { glowTimer.Stop(); base.OnHandleDestroyed(e); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); RefreshMotion(); }
        private void RefreshMotion()
        {
            if (glowTimer == null) return;
            glowTimer.Enabled = banner && IsHandleCreated && Visible && CanAnimate();
        }
        private static bool CanAnimate()
        {
            bool enabled;
            return !SystemInformation.HighContrast && !SystemInformation.TerminalServerSession &&
                SystemParametersInfo(0x1042, 0, out enabled, 0) && enabled;
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint parameter,
            [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (Brush clear = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Control : BackColor))
                e.Graphics.FillRectangle(clear, Rectangle.Intersect(e.ClipRectangle, ClientRectangle));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            OnPaintBackground(e);
            if (Width < 2 || Height < 2) return;
            Graphics g = e.Graphics;
            if (!SystemInformation.HighContrast)
            {
                if (surfaceCache == null || surfaceCache.Size != ClientSize || surfaceColor != BackColor)
                {
                    if (surfaceCache != null) { surfaceCache.Dispose(); surfaceCache = null; }
                    surfaceCache = CreateSurface(); surfaceColor = BackColor;
                }
                g.DrawImageUnscaled(surfaceCache, Point.Empty);
            }
            if (banner)
            {
                DrawBrand(g);
                int glow = 135 + (int)(30 * Math.Sin(phase));
                using (LinearGradientBrush rail = new LinearGradientBrush(new Rectangle(0, Height - 7, Width, 7),
                    SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(glow, Palette.Accent),
                    Color.FromArgb(0, Palette.Accent), 0f))
                {
                    g.FillRectangle(rail, 0, Height - 2, Width, 2);
                }
            }
            base.OnPaint(e);
        }
        private void DrawBrand(Graphics graphics)
        {
            float dpi = graphics.DpiX / 96f;
            if (SystemInformation.HighContrast)
            {
                using (Font title = new Font("Segoe UI", 25, FontStyle.Bold))
                using (Font subtitle = new Font("Segoe UI Semibold", 8.5f))
                {
                    TextRenderer.DrawText(graphics, "TURBORAMA", title, new Rectangle(0, 2, Width, Height - 25),
                        SystemColors.ControlText, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                    TextRenderer.DrawText(graphics, "LZ GAMES  //  PERFORMANCE INSTALL SYSTEM", subtitle, new Rectangle(3, Height - 28, Width - 3, 22),
                        SystemColors.ControlText, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                }
                return;
            }
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                int markWidth = (int)(22 * dpi);
                int markTop = (int)(10 * dpi);
                using (Pen violet = new Pen(Color.FromArgb(210, Palette.Violet), Math.Max(1.5f, 2f * dpi)))
                using (Pen cyan = new Pen(Color.FromArgb(190, 132, 224, 255), Math.Max(1f, 1.4f * dpi)))
                {
                    violet.StartCap = violet.EndCap = LineCap.Round;
                    cyan.StartCap = cyan.EndCap = LineCap.Round;
                    graphics.DrawLine(violet, 0, markTop + (int)(17 * dpi), markWidth, markTop);
                    graphics.DrawLine(violet, (int)(7 * dpi), markTop + (int)(24 * dpi), markWidth + (int)(7 * dpi), markTop + (int)(7 * dpi));
                    graphics.DrawLine(cyan, 0, markTop + (int)(25 * dpi), (int)(12 * dpi), markTop + (int)(25 * dpi));
                }
                float logoX = 31 * dpi;
                using (FontFamily family = BrandFamily())
                using (GraphicsPath logo = new GraphicsPath())
                using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    logo.AddString("TURBORAMA", family, (int)(FontStyle.Bold | FontStyle.Italic), 29 * dpi,
                        new PointF(logoX, -1 * dpi), format);
                    RectangleF bounds = logo.GetBounds();
                    using (Pen aura = new Pen(Color.FromArgb(34, Palette.Violet), Math.Max(3f, 6f * dpi)))
                    using (Pen edge = new Pen(Color.FromArgb(185, 220, 232, 255), Math.Max(1f, 1.15f * dpi)))
                    using (LinearGradientBrush fill = new LinearGradientBrush(bounds, Palette.Text,
                        Color.FromArgb(196, 178, 255), 0f))
                    {
                        graphics.DrawPath(aura, logo);
                        graphics.FillPath(fill, logo);
                        graphics.DrawPath(edge, logo);
                    }
                }
                int subtitleY = Height - (int)(26 * dpi);
                using (Font subtitle = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(graphics, "LZ GAMES", subtitle,
                        new Rectangle((int)logoX + 2, subtitleY, (int)(73 * dpi), (int)(20 * dpi)), Palette.Accent,
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                    TextRenderer.DrawText(graphics, "//  PERFORMANCE INSTALL SYSTEM", subtitle,
                        new Rectangle((int)logoX + (int)(76 * dpi), subtitleY, Width - (int)logoX - (int)(76 * dpi), (int)(20 * dpi)), Palette.Muted,
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                }
            }
            finally { graphics.Restore(state); }
        }
        private static FontFamily BrandFamily()
        {
            try { return new FontFamily("Bahnschrift"); }
            catch (ArgumentException) { return new FontFamily("Segoe UI"); }
        }
        private Bitmap CreateSurface()
        {
            // Compose the entire static background once per size. This prevents
            // GDI+ image/gradient rounding from varying between parent and child
            // dirty clips, and avoids resampling the photograph on every repaint.
            Bitmap surface = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(surface))
                {
                    graphics.Clear(BackColor);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    if (!banner)
                    {
                        Rectangle target = FitArtwork(ClientSize);
                        using (System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes())
                        {
                            attributes.SetWrapMode(WrapMode.TileFlipXY);
                            graphics.DrawImage(Artwork.Value, target, 0, 0, ArtworkSize.Width, ArtworkSize.Height, GraphicsUnit.Pixel, attributes);
                        }
                        Fade(graphics, new Rectangle(target.Left, target.Top, Math.Max(2, (int)(target.Width * .36f)), target.Height), 0f);
                        Fade(graphics, new Rectangle(target.Left, target.Top, target.Width, Math.Max(2, (int)(target.Height * .18f))), 90f);
                        Fade(graphics, new Rectangle(target.Left, target.Bottom - Math.Max(2, (int)(target.Height * .20f)), target.Width, Math.Max(2, (int)(target.Height * .20f))), 270f);
                        Fade(graphics, new Rectangle(target.Right - Math.Max(2, (int)(target.Width * .06f)), target.Top, Math.Max(2, (int)(target.Width * .06f)), target.Height), 180f);
                    }
                    DrawAmbientLight(graphics, ClientSize, banner);
                }
                return surface;
            }
            catch { surface.Dispose(); throw; }
        }
        // Static light behind live text. It is deliberately independent of the
        // rail animation so transparent child controls never capture a different
        // lighting phase and leave a rectangular seam during partial repaints.
        internal static void DrawAmbientLight(Graphics graphics, Size size, bool bannerMode)
        {
            if (size.Width < 2 || size.Height < 2) return;
            float w = size.Width, h = size.Height;
            if (bannerMode)
            {
                float logoWidth = Math.Min(w * .43f, 430f);
                SoftLight(graphics, new RectangleF(2, h * .08f, logoWidth, h * .72f), Palette.Violet, 48);
                SoftLight(graphics, new RectangleF(12, h * .20f, logoWidth * .72f, h * .48f), Color.FromArgb(158, 218, 255), 26);
            }
            else
            {
                SoftLight(graphics, new RectangleF(w * .004f, h * .04f, w * .49f, h * .88f), Palette.Violet, 44);
                SoftLight(graphics, new RectangleF(w * .008f, h * .10f, w * .36f, h * .52f), Color.FromArgb(158, 218, 255), 22);
            }
        }
        private static void SoftLight(Graphics graphics, RectangleF bounds, Color color, int opacity)
        {
            if (!graphics.IsVisible(bounds)) return;
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath ellipse = new GraphicsPath())
                {
                    ellipse.AddEllipse(bounds);
                    using (PathGradientBrush light = new PathGradientBrush(ellipse))
                    {
                        light.CenterColor = Color.FromArgb(opacity, color);
                        light.SurroundColors = new[] { Color.FromArgb(0, color) };
                        graphics.FillPath(light, ellipse);
                    }
                }
            }
            finally { graphics.Restore(state); }
        }
        private void Fade(Graphics graphics, Rectangle bounds, float angle)
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(bounds, BackColor, Color.FromArgb(0, BackColor), angle))
            {
                brush.WrapMode = WrapMode.TileFlipXY;
                brush.Blend = new Blend
                {
                    Positions = new[] { 0f, .2f, .4f, .6f, .8f, 1f },
                    Factors = new[] { 0f, .104f, .352f, .648f, .896f, 1f }
                };
                graphics.FillRectangle(brush, bounds);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { glowTimer.Dispose(); if (surfaceCache != null) surfaceCache.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
