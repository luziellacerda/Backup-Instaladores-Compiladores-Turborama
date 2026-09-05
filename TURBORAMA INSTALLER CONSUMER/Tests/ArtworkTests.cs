using System;
using System.Drawing;
using System.Windows.Forms;

namespace InstallerHost
{
    internal static class ArtworkTests
    {
        private sealed class PaintProbe : TurboRamaArtwork
        {
            internal PaintProbe(bool banner) : base(banner) { }
            internal void Render(Graphics graphics, Rectangle clip)
            { using (PaintEventArgs args = new PaintEventArgs(graphics, clip)) OnPaint(args); }
        }
        internal static int Run()
        {
            int count = 0;
            if (TurboRamaArtwork.ArtworkSize.Width < 1600 || TurboRamaArtwork.ArtworkSize.Height < 900)
                throw new Exception("Embedded background resolution is too small.");
            count++;
            foreach (Size viewport in new[] { new Size(1064, 367), new Size(764, 257), new Size(1500, 200), new Size(400, 700) })
            {
                Rectangle fitted = TurboRamaArtwork.FitArtwork(viewport);
                if (!new Rectangle(Point.Empty, viewport).Contains(fitted))
                    throw new Exception("Full artwork must fit inside the available body without cropping.");
                double expectedWidth = fitted.Height * (double)TurboRamaArtwork.ArtworkSize.Width / TurboRamaArtwork.ArtworkSize.Height;
                if (Math.Abs(expectedWidth - fitted.Width) > 2)
                    throw new Exception("Artwork aspect ratio was distorted.");
                count += 2;
            }
            foreach (bool banner in new[] { true, false })
            foreach (Size size in new[] { new Size(760, banner ? 78 : 385), new Size(1064, banner ? 78 : 385), new Size(1, 1) })
            using (PaintProbe control = new PaintProbe(banner))
            using (Bitmap clean = new Bitmap(size.Width, size.Height))
            using (Bitmap dirty = new Bitmap(size.Width, size.Height))
            using (Bitmap ambient = new Bitmap(size.Width, size.Height))
            {
                control.Size = size;
                using (Graphics graphics = Graphics.FromImage(ambient))
                {
                    graphics.Clear(TurboRama.Next.Palette.Background);
                    TurboRamaArtwork.DrawAmbientLight(graphics, size, banner);
                }
                using (Graphics graphics = Graphics.FromImage(clean)) control.Render(graphics, control.ClientRectangle);
                if (!banner && size.Width > 1)
                {
                    Rectangle fitted = TurboRamaArtwork.FitArtwork(size);
                    for (int x = fitted.Left; x < fitted.Right; x += 5)
                    {
                        AssertNearBackground(clean, ambient, x, fitted.Top);
                        AssertNearBackground(clean, ambient, x, fitted.Bottom - 1);
                    }
                    for (int y = fitted.Top; y < fitted.Bottom; y += 5)
                    {
                        AssertNearBackground(clean, ambient, fitted.Left, y);
                        AssertNearBackground(clean, ambient, fitted.Right - 1, y);
                    }
                    count++;
                }
                using (Graphics graphics = Graphics.FromImage(dirty))
                { graphics.Clear(Color.Magenta); control.Render(graphics, control.ClientRectangle); }
                for (int y = 0; y < size.Height; y += 7)
                for (int x = 0; x < size.Width; x += 13)
                    if (clean.GetPixel(x, y) != dirty.GetPixel(x, y)) throw new Exception("Artwork retained a previous dirty buffer.");
                count++;
                foreach (Rectangle clip in new[] {
                    new Rectangle(0, 0, Math.Min(40, size.Width), Math.Min(24, size.Height)),
                    new Rectangle(size.Width / 8, size.Height / 4, Math.Max(1, size.Width / 3), Math.Max(1, size.Height / 2)),
                    new Rectangle(size.Width / 3, 0, Math.Max(1, size.Width / 3), size.Height) })
                {
                using (Graphics graphics = Graphics.FromImage(dirty))
                { graphics.ResetClip(); graphics.DrawImageUnscaled(clean, 0, 0); graphics.SetClip(clip); using (Brush brush = new SolidBrush(Color.Magenta)) graphics.FillRectangle(brush, clip); control.Render(graphics, clip); }
                for (int y = 0; y < size.Height; y++)
                for (int x = 0; x < size.Width; x++)
                    if (clean.GetPixel(x, y) != dirty.GetPixel(x, y)) throw new Exception("Artwork clip differs from complete repaint: banner=" + banner + "; size=" + size + "; clip=" + clip + "; pixel=" + x + "," + y + "; expected=" + clean.GetPixel(x, y) + "; actual=" + dirty.GetPixel(x, y));
                count++;
                }
                if (size.Width > 1)
                {
                    bool hasLight = false;
                    Color background = TurboRama.Next.Palette.Background;
                    for (int y = 0; y < size.Height; y++)
                    for (int x = 0; x < size.Width; x++)
                    {
                        Color pixel = ambient.GetPixel(x, y);
                        if (Luminance(pixel) > Luminance(background) + .005) hasLight = true;
                        if ((x == 0 || y == 0 || x == size.Width - 1 || y == size.Height - 1) && pixel.ToArgb() != background.ToArgb())
                            throw new Exception("Ambient light has a clipped outer edge.");
                        Color foreground = banner ? TurboRama.Next.Palette.Accent : TurboRama.Next.Palette.Muted;
                        if ((Luminance(foreground) + .05) / (Luminance(pixel) + .05) < 4.5)
                            throw new Exception("Ambient light reduces text contrast below 4.5:1.");
                        if (x > 0 && Math.Abs(Luminance(pixel) - Luminance(ambient.GetPixel(x - 1, y))) > .008)
                            throw new Exception("Ambient light contains an abrupt transition.");
                    }
                    if (!hasLight) throw new Exception("Ambient lighting is not visible.");
                    count += 4;
                }
                if (banner && size.Width > 1)
                {
                    Color background = TurboRama.Next.Palette.Background;
                    int logoPixels = 0, distantPixels = 0, distantArea = 0;
                    for (int y = 0; y < Math.Max(1, size.Height - 8); y++)
                    for (int x = 0; x < size.Width; x++)
                    {
                        Color pixel = clean.GetPixel(x, y);
                        int difference = Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B);
                        if (x < Math.Min(size.Width, 360) && difference > 80) logoPixels++;
                        if (x >= size.Width * 3 / 5) { distantArea++; if (difference > 18) distantPixels++; }
                    }
                    if (logoPixels < 250) throw new Exception("TurboRama brand mark is too weak or missing.");
                    if (distantPixels > distantArea / 100) throw new Exception("Brand lighting reads as a full-width rectangular box.");
                    count += 2;
                }
                control.Visible = false;
                if (control.IsGlowRunning) throw new Exception("Hidden artwork kept an animation timer running.");
                count++;
            }
            foreach (bool banner in new[] { true, false })
            using (PaintProbe reused = new PaintProbe(banner))
            {
                foreach (int pass in new[] { 0, 1, 2 })
                {
                    Size size = new Size(pass == 0 ? 1064 : 764, banner ? 78 : 257);
                    Color background = pass == 2 ? Color.FromArgb(20, 22, 28) : TurboRama.Next.Palette.Background;
                    reused.Size = size; reused.BackColor = background;
                    using (PaintProbe fresh = new PaintProbe(banner) { Size = size, BackColor = background })
                    using (Bitmap expected = new Bitmap(size.Width, size.Height))
                    using (Bitmap actual = new Bitmap(size.Width, size.Height))
                    {
                        using (Graphics graphics = Graphics.FromImage(expected)) fresh.Render(graphics, fresh.ClientRectangle);
                        using (Graphics graphics = Graphics.FromImage(actual)) reused.Render(graphics, reused.ClientRectangle);
                        for (int y = 0; y < size.Height; y++)
                        for (int x = 0; x < size.Width; x++)
                            if (expected.GetPixel(x, y) != actual.GetPixel(x, y)) throw new Exception("Artwork cache is stale after resize or background change.");
                    }
                    count++;
                }
            }
            Console.WriteLine("ARTWORK PASS assertions=" + count + "; full artwork, smooth light, text contrast, all-pixel dirty clips, cache resize and hidden motion.");
            return count;
        }
        private static void AssertNearBackground(Bitmap image, Bitmap ambient, int x, int y)
        {
            Color pixel = image.GetPixel(x, y);
            Color background = ambient.GetPixel(x, y);
            if (Math.Abs(pixel.R - background.R) > 4 || Math.Abs(pixel.G - background.G) > 4 || Math.Abs(pixel.B - background.B) > 4)
                throw new Exception("Image edge does not fade into the page background: " + pixel + " at " + x + "," + y);
        }
        private static double Luminance(Color color)
        { return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B); }
        private static double Linear(byte value)
        {
            double channel = value / 255.0;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
    }
}
