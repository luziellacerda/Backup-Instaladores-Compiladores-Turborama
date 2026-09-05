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
            foreach (bool banner in new[] { true, false })
            foreach (Size size in new[] { new Size(760, banner ? 78 : 385), new Size(1064, banner ? 78 : 385), new Size(1, 1) })
            using (PaintProbe control = new PaintProbe(banner))
            using (Bitmap clean = new Bitmap(size.Width, size.Height))
            using (Bitmap dirty = new Bitmap(size.Width, size.Height))
            {
                control.Size = size;
                using (Graphics graphics = Graphics.FromImage(clean)) control.Render(graphics, control.ClientRectangle);
                using (Graphics graphics = Graphics.FromImage(dirty))
                { graphics.Clear(Color.Magenta); control.Render(graphics, control.ClientRectangle); }
                for (int y = 0; y < size.Height; y += 7)
                for (int x = 0; x < size.Width; x += 13)
                    if (clean.GetPixel(x, y) != dirty.GetPixel(x, y)) throw new Exception("Artwork retained a previous dirty buffer.");
                count++;
                Rectangle clip = new Rectangle(0, 0, Math.Min(40, size.Width), Math.Min(24, size.Height));
                using (Graphics graphics = Graphics.FromImage(dirty))
                { graphics.ResetClip(); graphics.DrawImageUnscaled(clean, 0, 0); graphics.SetClip(clip); using (Brush brush = new SolidBrush(Color.Magenta)) graphics.FillRectangle(brush, clip); control.Render(graphics, clip); }
                for (int y = 0; y < size.Height; y += 7)
                for (int x = 0; x < size.Width; x += 13)
                    if (clean.GetPixel(x, y) != dirty.GetPixel(x, y)) throw new Exception("Artwork clip differs from complete repaint.");
                count++;
                control.Visible = false;
                if (control.IsGlowRunning) throw new Exception("Hidden artwork kept an animation timer running.");
                count++;
            }
            Console.WriteLine("ARTWORK PASS assertions=" + count + "; embedded image, dirty buffers, clips and hidden motion.");
            return count;
        }
    }
}
