using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using TurboRama.Next;

// In-process probes against fresh source. No reflection, native input injection,
// real scanner, installer, external UI automation, or Windows setting changes.
internal static class ButtonTests
{
    private static int cases, assertions, failures;
    private static Form host;
    private static Button alternate;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (host = new Form { ShowInTaskbar = false, Opacity = 0, ClientSize = new Size(700, 420) })
        {
            alternate = new Button { Text = "Test focus sink", Location = new Point(400, 300) };
            host.Controls.Add(alternate);
            host.Shown += delegate
            {
                host.BeginInvoke(new Action(delegate
                {
                    Run("Mouse primary press/release/leave/re-enter", MouseStates);
                    Run("Right mouse does not press", RightMouse);
                    Run("Lost mouse capture clears press", CaptureLoss);
                    Run("Disable clears transient states and prevents default action", Disable);
                    Run("Space keyboard press and release", Space);
                    Run("Losing focus clears keyboard and mouse press", FocusLoss);
                    Run("Hover transition settles and leaves", HoverSettles);
                    Run("Hidden buttons stop animation and clear state", Hide);
                    Run("Disposed buttons stop their timer", Disposal);
                    Run("Selected property invalidates only on change", SelectionInvalidates);
                    Run("Accessible role, name, selection and single default action", Accessibility);
                    Run("Native default-button command clicks exactly once", DefaultButton);
                    Run("All button renderers survive tiny bounds and DPI", TinyRendering);
                    Run("Vector glyphs preserve graphics transform and clip", IconRendering);
                    Run("Actual application labels fit content slots at 96/144/192 DPI", LabelWidths);
                    if (args.Length > 0) Run("Render real control state sheets", delegate { RenderSheets(args[0]); });
                    Console.WriteLine("RESULT cases={0} assertions={1} passedCases={2} failures={3}", cases, assertions, cases - failures, failures);
                    host.Close();
                }));
            };
            Application.Run(host);
        }
        return failures == 0 ? 0 : 1;
    }

    private sealed class Probe : ActionButton
    {
        protected override bool ShowFocusCues { get { return true; } }
        internal void EnterPointer() { OnMouseEnter(EventArgs.Empty); }
        internal void LeavePointer() { OnMouseLeave(EventArgs.Empty); }
        internal void Down(MouseButtons button) { OnMouseDown(new MouseEventArgs(button, 1, Width / 2, Height / 2, 0)); }
        internal void Up(MouseButtons button) { OnMouseUp(new MouseEventArgs(button, 1, Width / 2, Height / 2, 0)); }
        internal void PressSpace() { OnKeyDown(new KeyEventArgs(Keys.Space)); }
        internal void ReleaseSpace() { OnKeyUp(new KeyEventArgs(Keys.Space)); }
        internal void LoseFocus() { OnLostFocus(EventArgs.Empty); }
        internal void LoseCapture() { Capture = false; OnMouseCaptureChanged(EventArgs.Empty); }
        // Synthetic paint/geometry probe only. Graphics.FromImage does not use
        // the native control WM_PRINT render path (particularly GDI text).
        internal Bitmap Render(float dpi)
        {
            Bitmap bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppRgb);
            bitmap.SetResolution(dpi, dpi);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Palette.Background);
                OnPaint(new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)));
            }
            return bitmap;
        }
        internal Bitmap RenderNative()
        {
            Bitmap bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Palette.Background);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            return bitmap;
        }
    }

    private static Probe NewProbe()
    {
        Probe probe = new Probe { Text = "Analisar meu PC", AccessibleName = "Analisar meu PC", Size = new Size(272, 56),
            Font = new Font("Segoe UI Semibold", 10.5f), Location = new Point(20, 20), Primary = true, Icon = Glyph.Scan, TrailingArrow = true };
        host.Controls.Add(probe); probe.CreateControl(); Pump(); return probe;
    }
    private static void Run(string name, Action body)
    {
        cases++;
        try { body(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.GetType().Name + ": " + error.Message); }
        Pump();
    }
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }
    private static void Pump() { Application.DoEvents(); Thread.Sleep(1); }
    private static void Settle(Probe probe)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (probe.IsAnimating && watch.ElapsedMilliseconds < 1200) Pump();
        Check(!probe.IsAnimating, "Animation did not settle in 1200ms.");
    }
    private static void MouseStates()
    {
        using (Probe probe = NewProbe())
        {
            Check(!probe.IsPressed, "Initially pressed."); probe.EnterPointer(); probe.Down(MouseButtons.Left);
            Check(probe.IsPressed, "Left press has no visual state."); probe.LeavePointer();
            Check(!probe.IsPressed, "Dragged outside remains pressed."); probe.EnterPointer();
            Check(probe.IsPressed, "Dragging back inside loses original press."); probe.Up(MouseButtons.Left);
            Check(!probe.IsPressed, "Mouse release remains pressed.");
        }
    }
    private static void RightMouse()
    {
        using (Probe probe = NewProbe()) { probe.EnterPointer(); probe.Down(MouseButtons.Right); Check(!probe.IsPressed, "Right mouse presses button."); probe.Up(MouseButtons.Right); }
    }
    private static void CaptureLoss()
    {
        using (Probe probe = NewProbe()) { probe.EnterPointer(); probe.Down(MouseButtons.Left); Check(probe.IsPressed, "Precondition press missing."); probe.LoseCapture(); Check(!probe.IsPressed, "Lost capture remains pressed."); }
    }
    private static void Disable()
    {
        using (Probe probe = NewProbe())
        {
            int clicks = 0; probe.Click += delegate { clicks++; };
            probe.EnterPointer(); probe.Down(MouseButtons.Left); probe.PressSpace(); probe.Enabled = false;
            Check(!probe.IsPressed && !probe.IsAnimating && probe.HoverLevel == 0, "Disabled transient state retained.");
            Check(probe.Cursor == Cursors.Default, "Disabled hand cursor retained.");
            probe.AccessibilityObject.DoDefaultAction(); Check(clicks == 0, "Disabled accessible action clicked.");
            probe.Enabled = true; Check(!probe.IsPressed && probe.HoverLevel == 0, "Re-enable revives old input state.");
        }
    }
    private static void Space()
    {
        using (Probe probe = NewProbe())
        {
            int clicks = 0; probe.Click += delegate { clicks++; }; probe.Focus();
            probe.PressSpace(); Check(probe.IsPressed, "Space down is not rendered pressed."); Check(clicks == 0, "Space down clicked too early.");
            probe.ReleaseSpace(); Check(!probe.IsPressed, "Space release remains pressed.");
            Check(clicks == 1, "Space press/release must click exactly once; got " + clicks + ".");
        }
    }
    private static void FocusLoss()
    {
        using (Probe probe = NewProbe()) { probe.EnterPointer(); probe.Down(MouseButtons.Left); probe.PressSpace(); probe.LoseFocus(); Check(!probe.IsPressed, "Lost focus retains press."); }
    }
    private static void HoverSettles()
    {
        using (Probe probe = NewProbe()) { probe.EnterPointer(); Settle(probe); Check(probe.HoverLevel == 1f, "Hover settles below target."); probe.LeavePointer(); Settle(probe); Check(probe.HoverLevel == 0f, "Leave settles above target."); }
    }
    private static void Hide()
    {
        using (Probe probe = NewProbe()) { probe.EnterPointer(); probe.Down(MouseButtons.Left); probe.PressSpace(); probe.Visible = false; Check(!probe.IsAnimating && !probe.IsPressed && probe.HoverLevel == 0, "Hidden transient state retained."); probe.Visible = true; Check(!probe.IsAnimating && !probe.IsPressed, "Show revives old state."); }
    }
    private static void Disposal()
    {
        Probe probe = NewProbe(); probe.EnterPointer(); probe.Dispose();
        Check(probe.IsDisposed, "Not disposed."); Check(!probe.IsAnimating, "Disposed timer remains enabled."); Pump();
    }
    private static void SelectionInvalidates()
    {
        using (Probe probe = NewProbe())
        {
            int invalidations = 0; probe.Invalidated += delegate { invalidations++; };
            probe.Selected = true; Check(invalidations > 0, "Selected change did not invalidate.");
            int before = invalidations; probe.Selected = true; Check(invalidations == before, "Same selected value invalidates.");
            probe.Selected = false; Check(invalidations > before, "Clearing selected did not invalidate.");
        }
    }
    private static void Accessibility()
    {
        using (Probe probe = NewProbe())
        {
            AccessibleObject accessible = probe.AccessibilityObject; int clicks = 0; probe.Click += delegate { clicks++; };
            Check(accessible.Role == AccessibleRole.PushButton, "Not exposed as push button.");
            Check(accessible.Name == "Analisar meu PC", "Accessible action name lost.");
            probe.Description = "Somente leitura"; Check(accessible.Description == "Somente leitura", "Description lost.");
            probe.Selected = true; Check((accessible.State & AccessibleStates.Selected) != 0, "Selected state inaccessible.");
            probe.Selected = false; Check((accessible.State & AccessibleStates.Selected) == 0, "Selected accessibility state stale.");
            accessible.DoDefaultAction(); Check(clicks == 1, "Accessible action clicked " + clicks + " times.");
            probe.Enabled = false; Check((accessible.State & AccessibleStates.Unavailable) != 0, "Disabled accessibility state missing.");
        }
    }
    private static void DefaultButton()
    {
        using (Probe probe = NewProbe())
        {
            int clicks = 0; probe.Click += delegate { clicks++; }; host.AcceptButton = probe;
            IButtonControl action = host.AcceptButton; action.NotifyDefault(true); action.PerformClick();
            Check(clicks == 1, "Default action clicked " + clicks + " times.");
            action.NotifyDefault(false); host.AcceptButton = null;
        }
    }
    private static void TinyRendering()
    {
        using (Probe probe = NewProbe())
        {
            foreach (float dpi in new[] { 96f, 144f, 192f })
                foreach (ButtonAppearance appearance in Enum.GetValues(typeof(ButtonAppearance)))
                    foreach (int size in new[] { 1, 5, 11, 12, 20, 30, 44, 80 })
                    {
                        probe.Appearance = appearance; probe.Size = new Size(size, size);
                        using (Bitmap bitmap = probe.Render(dpi)) Check(bitmap.Width == size, "Incorrect tiny render size.");
                    }
        }
    }
    private static void IconRendering()
    {
        using (Bitmap bitmap = new Bitmap(160, 160)) using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.TranslateTransform(3, 7); graphics.SetClip(new Rectangle(1, 2, 150, 148));
            float[] before = graphics.Transform.Elements; RectangleF clip = graphics.ClipBounds;
            foreach (Glyph glyph in Enum.GetValues(typeof(Glyph)))
                foreach (float size in new[] { -1f, 0f, .1f, 1f, 12f, 24f, 48f })
                {
                    VectorIcon.Draw(graphics, glyph, new RectangleF(10, 10, size, size), Palette.Text);
                    float[] after = graphics.Transform.Elements;
                    for (int index = 0; index < before.Length; index++) Check(before[index] == after[index], "Glyph leaks transform.");
                    Check(graphics.ClipBounds == clip, "Glyph leaks clip.");
                }
        }
    }
    private static void LabelWidths()
    {
        string[] titles = { "Analisar meu PC", "Escolher componentes", "Revisar meu plano", "Simular plano", "Simulando...", "Revisar seleção",
            "Analisar meu PC", "Voltar", "Visão geral", "Diagnóstico", "Componentes", "Revisar plano", "Essenciais", "Compatibilidade", "Limpar seleção", "Atualizar análise", "Cancelar análise",
            "Todos", "Essenciais", "Compatibilidade" };
        int[] widths = { 272, 272, 272, 272, 272, 272, 248, 120, 179, 179, 179, 179, 170, 222, 178, 214, 214, 120, 156, 200 };
        int[] glyphs = { 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0 };
        foreach (float dpi in new[] { 96f, 144f, 192f })
            using (Bitmap bitmap = new Bitmap(600, 150))
            {
                bitmap.SetResolution(dpi, dpi);
                // TextRenderer uses GDI device-font metrics; explicitly scale the
                // font as WinForms autoscaling does, not only bitmap resolution.
                using (Graphics graphics = Graphics.FromImage(bitmap)) using (Font font = new Font("Segoe UI Semibold", 10.5f * dpi / 96f))
                    for (int index = 0; index < titles.Length; index++)
                    {
                        string text = titles[index];
                        int available = (int)Math.Round(widths[index] * dpi / 96f) - 2 * (int)Math.Round(4 * dpi / 96f) - 1 - 2 * (int)Math.Round(14 * dpi / 96f) - glyphs[index] * ((int)Math.Round(19 * dpi / 96f) + (int)Math.Round(10 * dpi / 96f));
                        int measured = TextRenderer.MeasureText(graphics, text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
                        Check(measured <= available, text + " clips at " + dpi + " DPI: needs " + measured + ", has " + available + ".");
                    }
            }
    }
    private static void RenderSheets(string directory)
    {
        Directory.CreateDirectory(directory);
        RenderComparison(directory);
        foreach (float dpi in new[] { 96f, 144f, 192f })
        {
            float scale = dpi / 96f; int width = (int)(1656 * scale), height = (int)(635 * scale);
            using (Bitmap sheet = new Bitmap(width, height, PixelFormat.Format32bppRgb))
            {
                sheet.SetResolution(dpi, dpi);
                using (Graphics graphics = Graphics.FromImage(sheet)) using (Font labelFont = Ui.Font(10 * scale))
                {
                    graphics.Clear(Palette.Background);
                    string[] labels = { "NORMAL", "HOVER", "PRESSIONADO", "FOCO", "SELECIONADO", "DESABILITADO" };
                    for (int column = 0; column < labels.Length; column++)
                        TextRenderer.DrawText(graphics, labels[column], labelFont, new Point((int)((column * 272 + 14) * scale), (int)(12 * scale)), Palette.Text);
                    int row = 0;
                    foreach (ButtonAppearance appearance in Enum.GetValues(typeof(ButtonAppearance)))
                    {
                        for (int column = 0; column < labels.Length; column++)
                            using (Probe probe = NewProbe())
                            {
                                probe.Appearance = appearance; probe.Text = appearance == ButtonAppearance.Compound ? "PC moderno" : "Analisar meu PC";
                                probe.Description = "Essenciais para o setup"; probe.Icon = appearance == ButtonAppearance.Compound ? Glyph.Monitor : Glyph.Scan;
                                probe.Font = new Font("Segoe UI Semibold", (appearance == ButtonAppearance.Compound ? 11.5f : 10.5f) * scale);
                                probe.Size = new Size((int)(264 * scale), (int)((appearance == ButtonAppearance.Compound ? 120 : 64) * scale));
                                alternate.Focus();
                                if (column == 1 || column == 2) { probe.EnterPointer(); Settle(probe); }
                                if (column == 2) probe.Down(MouseButtons.Left);
                                if (column == 3) { probe.Focus(); Check(probe.Focused, "Focus render not actually focused."); }
                                if (column == 4) probe.Selected = true;
                                if (column == 5) probe.Enabled = false;
                                using (Bitmap control = dpi == 96 ? probe.RenderNative() : probe.Render(dpi)) graphics.DrawImageUnscaled(control, (int)((column * 272 + 8) * scale), (int)((42 + row * 112) * scale));
                            }
                        row++;
                    }
                }
                sheet.Save(Path.Combine(directory, (dpi == 96 ? "Buttons-states-" : "Buttons-paint-probe-") + (int)dpi + "dpi.png"), ImageFormat.Png);
            }
        }
        Console.WriteLine("96 DPI state sheet uses native DrawToBitmap. 144/192 DPI paint probes are synthetic, not native monitor-DPI certification.");
    }
    private static void RenderComparison(string directory)
    {
        using (Probe probe = NewProbe()) using (Bitmap comparison = new Bitmap(590, 210, PixelFormat.Format32bppRgb))
        using (Graphics graphics = Graphics.FromImage(comparison)) using (Font label = Ui.Font(10))
        {
            graphics.Clear(Palette.Background); alternate.Focus();
            TextRenderer.DrawText(graphics, "OnPaint / Graphics.FromImage", label, new Point(12, 12), Palette.Text);
            TextRenderer.DrawText(graphics, "DrawToBitmap / controle real", label, new Point(306, 12), Palette.Text);
            foreach (bool primary in new[] { true, false })
            {
                probe.Primary = primary;
                using (Bitmap direct = probe.Render(96)) using (Bitmap native = probe.RenderNative())
                {
                    int y = primary ? 44 : 120;
                    graphics.DrawImageUnscaled(direct, 8, y); graphics.DrawImageUnscaled(native, 302, y);
                    Check(direct.Size == native.Size, "Compared render dimensions differ.");
                }
            }
            comparison.Save(Path.Combine(directory, "Buttons-render-comparison-96dpi.png"), ImageFormat.Png);
        }
    }
}
