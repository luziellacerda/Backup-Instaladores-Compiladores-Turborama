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
                Image art = Artwork.Value;
                if (banner)
                {
                    // Wide cinematic crop: keep both turbines in the header.
                    int artWidth = Math.Min(Width, (int)(Height * 5.6f));
                    Rectangle target = new Rectangle(Width - artWidth, 0, artWidth, Height);
                    g.DrawImage(art, target, new Rectangle(560, 310, 1112, 230), GraphicsUnit.Pixel);
                    using (LinearGradientBrush fade = new LinearGradientBrush(target, BackColor, Color.FromArgb(36, BackColor), 0f))
                        g.FillRectangle(fade, target);
                }
                else
                {
                    float scale = Math.Max(Width / (float)art.Width, Height / (float)art.Height);
                    int w = (int)Math.Ceiling(art.Width * scale), h = (int)Math.Ceiling(art.Height * scale);
                    g.DrawImage(art, new Rectangle(Width - w, (Height - h) / 2, w, h));
                }
                using (Pen edge = new Pen(Color.FromArgb(70, Palette.Violet)))
                    g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
            }
            if (banner)
            {
                float dpi = g.DpiX / 96f;
                int left = (int)(12 * dpi);
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
                using (Pen halo = new Pen(Color.FromArgb(24, Palette.Accent), 7))
                using (Pen rail = new Pen(SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(glow, Palette.Accent), 2))
                {
                    g.DrawLine(halo, 0, Height - 2, Width * .35f, Height - 2);
                    g.DrawLine(rail, 0, Height - 2, Width * .35f, Height - 2);
                }
            }
            base.OnPaint(e);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) glowTimer.Dispose(); base.Dispose(disposing); }
    }
}
