using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using TurboRama.Next;

// Deterministic in-process paint regression. It exercises the real protected
// background + foreground callbacks against intentionally dirty paint buffers.
// This is not an external-window screenshot or a substitute for real desktop QA.
internal static class PaintRegressionTests
{
    private static int cases, assertions, failures;

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        foreach (Color background in new[] { Palette.Background, Palette.Surface, Palette.Raised })
            foreach (bool nested in new[] { false, true })
                foreach (ButtonAppearance appearance in Enum.GetValues(typeof(ButtonAppearance)))
                {
                    Color localColor = background; bool localNested = nested; ButtonAppearance localAppearance = appearance;
                    Run("Dirty buffer: " + appearance + " / " + background.ToArgb() + " / nested=" + nested,
                        delegate { DirtyBuffer(localColor, localNested, localAppearance); });
                }
        foreach (int dimension in new[] { 1, 5, 11, 12, 20 })
        {
            int localDimension = dimension;
            Run("Tiny paint still clears: " + dimension, delegate { TinyBackground(localDimension); });
        }
        Console.WriteLine("PAINT RESULT cases={0} assertions={1} passedCases={2} failures={3}", cases, assertions, cases - failures, failures);
        Console.WriteLine("Synthetic callback/bitmap checks only; live window painting and native monitor DPI still require desktop QA.");
        return failures == 0 ? 0 : 1;
    }

    private sealed class Probe : ActionButton
    {
        protected override bool ShowFocusCues { get { return false; } }
        internal void PaintFrame(Bitmap bitmap, Rectangle clip, bool backgroundCallback = true)
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SetClip(clip);
                using (PaintEventArgs args = new PaintEventArgs(graphics, clip))
                {
                    if (backgroundCallback) OnPaintBackground(args);
                    OnPaint(args);
                }
            }
        }
    }

    private static void DirtyBuffer(Color background, bool nested, ButtonAppearance appearance)
    {
        using (Panel parent = new Panel { BackColor = background, Size = new Size(500, 260) })
        using (Panel transparent = new Panel { BackColor = Color.Transparent, Location = new Point(15, 17), Size = new Size(450, 220) })
        using (Probe probe = new Probe { Appearance = appearance, Text = "Diagnóstico", Description = "Análise do computador", Icon = Glyph.Scan,
            Size = new Size(272, appearance == ButtonAppearance.Compound ? 120 : 56), Location = new Point(30, 20), Font = new Font("Segoe UI", 10.5f) })
        {
            parent.Controls.Add(transparent);
            (nested ? transparent : parent).Controls.Add(probe);
            Rectangle full = new Rectangle(Point.Empty, probe.Size);
            using (Bitmap clean = NewBitmap(probe.Size, background))
            using (Bitmap dirty = NewBitmap(probe.Size, background))
            {
                Poison(dirty, full);
                probe.PaintFrame(clean, full);
                probe.PaintFrame(dirty, full);
                Compare(clean, dirty, full, "Full paint retains stale buffer pixels");
                Check(dirty.GetPixel(0, 0).ToArgb() == background.ToArgb(), "Corner does not match the effective parent background.");

                // ButtonBase marks its surface Opaque. WM_PAINT may therefore
                // skip the background callback entirely; OnPaint must initialize
                // its complete update region rather than rely on that callback.
                Poison(dirty, full);
                probe.PaintFrame(dirty, full, false);
                Compare(clean, dirty, full, "Foreground-only paint retains stale pixels");

                Rectangle part = new Rectangle(0, 0, probe.Width / 2, probe.Height);
                Poison(dirty, part);
                probe.PaintFrame(dirty, part);
                Compare(clean, dirty, full, "Partial paint retains stale pixels or corrupts pixels outside its clip");

                // A dirty buffer outside the update region must be left intact.
                // This catches renderers that call Graphics.Clear or GDI text
                // without preserving the Graphics clipping region.
                using (Bitmap outside = NewBitmap(probe.Size, background))
                using (Bitmap expected = NewBitmap(probe.Size, background))
                {
                    Poison(outside, full); Poison(expected, full);
                    for (int y = part.Top; y < part.Bottom; y++)
                        for (int x = part.Left; x < part.Right; x++) expected.SetPixel(x, y, clean.GetPixel(x, y));
                    probe.PaintFrame(outside, part);
                    Compare(expected, outside, full, "Partial paint writes outside its clipping region");
                }

                probe.Selected = true;
                using (Bitmap selected = NewBitmap(probe.Size, background))
                {
                    probe.PaintFrame(selected, full);
                    probe.PaintFrame(dirty, full);
                    Compare(selected, dirty, full, "Selected transition depends on the previous frame");
                }
                probe.Selected = false;
                probe.PaintFrame(dirty, full);
                Compare(clean, dirty, full, "Deselect does not erase the selected surface/underline");

                for (int repaint = 0; repaint < 5; repaint++) probe.PaintFrame(dirty, full);
                Compare(clean, dirty, full, "Repeated paints accumulate shadows or antialiasing");

                probe.Enabled = false;
                using (Bitmap disabled = NewBitmap(probe.Size, background))
                {
                    probe.PaintFrame(disabled, full);
                    probe.PaintFrame(dirty, full);
                    Compare(disabled, dirty, full, "Disabled transition depends on the previous frame");
                }
                probe.Enabled = true;
                probe.PaintFrame(dirty, full);
                Compare(clean, dirty, full, "Re-enable retains the disabled frame");

                probe.Text = "Voltar"; probe.Description = "Outro perfil"; probe.Icon = Glyph.ArrowLeft;
                using (Bitmap changedText = NewBitmap(probe.Size, background))
                {
                    probe.PaintFrame(changedText, full);
                    probe.PaintFrame(dirty, full);
                    Compare(changedText, dirty, full, "Changed label or icon retains previous glyphs");
                }

                Color changed = Color.FromArgb(background.B, background.R, Math.Min(255, background.G + 25));
                parent.BackColor = changed;
                using (Bitmap changedParent = NewBitmap(probe.Size, changed))
                {
                    probe.PaintFrame(changedParent, full);
                    probe.PaintFrame(dirty, full);
                    Compare(changedParent, dirty, full, "Changing parent background retains previous background pixels");
                    Check(dirty.GetPixel(0, 0).ToArgb() == changed.ToArgb(), "Changed effective parent background not used.");
                }
            }
        }
    }

    private static void TinyBackground(int dimension)
    {
        using (Panel parent = new Panel { BackColor = Palette.Surface })
        using (Probe probe = new Probe { Size = new Size(dimension, dimension) })
        using (Bitmap clean = NewBitmap(probe.Size, Palette.Surface))
        using (Bitmap dirty = NewBitmap(probe.Size, Palette.Surface))
        {
            parent.Controls.Add(probe);
            Rectangle full = new Rectangle(Point.Empty, probe.Size);
            Poison(dirty, full);
            probe.PaintFrame(clean, full); probe.PaintFrame(dirty, full);
            Compare(clean, dirty, full, "Tiny paint retains stale buffer pixels");
            Poison(dirty, full); probe.PaintFrame(dirty, full, false);
            Compare(clean, dirty, full, "Tiny foreground-only paint retains stale buffer pixels");
        }
    }

    private static Bitmap NewBitmap(Size size, Color background)
    {
        Bitmap bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
        using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(background);
        return bitmap;
    }

    private static void Poison(Bitmap bitmap, Rectangle clip)
    {
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Brush red = new SolidBrush(Color.FromArgb(253, 2, 93)))
        using (Brush green = new SolidBrush(Color.FromArgb(12, 253, 84)))
        {
            graphics.SetClip(clip);
            graphics.FillRectangle(red, clip);
            for (int x = 0; x < bitmap.Width; x += 9) graphics.FillRectangle(green, x, 0, 4, bitmap.Height);
        }
    }

    private static void Compare(Bitmap expected, Bitmap actual, Rectangle area, string message)
    {
        int mismatches = 0; Point first = Point.Empty;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (expected.GetPixel(x, y).ToArgb() != actual.GetPixel(x, y).ToArgb())
                { if (mismatches == 0) first = new Point(x, y); mismatches++; }
        Check(mismatches == 0, message + ": " + mismatches + " mismatches; first " + first + ".");
    }

    private static void Run(string name, Action action)
    {
        cases++;
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failures++; Console.WriteLine("FAIL " + name + ": " + exception.Message); }
    }
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }
}
