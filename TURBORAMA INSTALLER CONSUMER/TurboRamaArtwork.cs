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
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // The brand strip stays quiet; the full photograph belongs in
                // the welcome body, not repeated as a cropped header thumbnail.
                if (!banner)
                {
                    Rectangle target = FitArtwork(ClientSize);
                    using (System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(Artwork.Value, target, 0, 0, ArtworkSize.Width, ArtworkSize.Height,
                            GraphicsUnit.Pixel, attributes);
                    }
                    // Feather all image edges into the page; the copy is transparent
                    // over this continuous gradient, never on an opaque rectangle.
                    Fade(g, new Rectangle(target.Left, target.Top, Math.Max(2, (int)(target.Width * .36f)), target.Height), 0f);
                    Fade(g, new Rectangle(target.Left, target.Top, target.Width, Math.Max(2, (int)(target.Height * .18f))), 90f);
                    Fade(g, new Rectangle(target.Left, target.Bottom - Math.Max(2, (int)(target.Height * .20f)), target.Width, Math.Max(2, (int)(target.Height * .20f))), 270f);
                    Fade(g, new Rectangle(target.Right - Math.Max(2, (int)(target.Width * .06f)), target.Top, Math.Max(2, (int)(target.Width * .06f)), target.Height), 180f);
                }
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
        { if (disposing) glowTimer.Dispose(); base.Dispose(disposing); }
    }
}
