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
                float dpi = g.DpiX / 96f;
                int left = (int)(2 * dpi);
                using (Font title = new Font("Segoe UI", 25, FontStyle.Bold | FontStyle.Italic))
                using (Font subtitle = new Font("Segoe UI Semibold", 8.5f))
                {
                    TextRenderer.DrawText(g, "TURBORAMA", title, new Rectangle(left, 2, Width - left, Height - 25),
                        SystemInformation.HighContrast ? SystemColors.ControlText : Palette.Text,
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                    TextRenderer.DrawText(g, "LZ GAMES  /  ARCADE & PC", subtitle, new Rectangle(left + 3, Height - 28, Width - left - 3, 22),
                        SystemInformation.HighContrast ? SystemColors.ControlText : Palette.Accent,
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                }
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
                SoftLight(graphics, new RectangleF(w * .002f, h * .015f, w * .64f, h * .87f), Palette.Violet, 72);
                SoftLight(graphics, new RectangleF(w * .006f, h * .04f, w * .37f, h * .72f), Color.FromArgb(158, 218, 255), 40);
                SoftLight(graphics, new RectangleF(w * .02f, h * .72f, w * .55f, h * .24f), Palette.Accent, 36);
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
