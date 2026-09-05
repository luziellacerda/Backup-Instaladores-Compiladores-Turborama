using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TurboRama.Next;
using CheckState = TurboRama.Next.CheckState;

internal static class RenderTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string directory = args[0]; Directory.CreateDirectory(directory);
        using (ShellForm form = new ShellForm(new SetupSession(), FakeScan, new SimulationRunner()))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            PageId[] pages = { PageId.Overview, PageId.Diagnostics, PageId.Components, PageId.Review };
            foreach (PageId page in pages)
            {
                form.NavigateTo(page); Application.DoEvents();
                Capture(form, Path.Combine(directory, page + "-100.png"));
            }
            form.ClientSize = new Size(820, 570);
            foreach (PageId page in pages)
            {
                form.NavigateTo(page); Application.DoEvents();
                Capture(form, Path.Combine(directory, page + "-compact.png"));
            }
            form.Close();
        }
        foreach (float scale in new[] { 1.25f, 1.5f, 2f })
        {
            using (ShellForm form = new ShellForm(new SetupSession(), FakeScan, new SimulationRunner()))
            {
                form.AutoScaleDimensions = new SizeF(96f / scale, 96f / scale);
                form.PerformAutoScale();
                form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                foreach (PageId page in new[] { PageId.Overview, PageId.Diagnostics, PageId.Components, PageId.Review })
                {
                    form.NavigateTo(page); Application.DoEvents();
                    Capture(form, Path.Combine(directory, page + "-scale" + (int)(scale * 100) + ".png"));
                }
                form.Close();
            }
        }
        Console.WriteLine("Rendered actual pages at normal/compact and simulated125/150/200% scaling; fake diagnostic data only. Not native multi-monitor DPI certification.");
        return 0;
    }
    private static Task<ReadinessSnapshot> FakeScan(CancellationToken token)
    {
        ReadinessSnapshot snapshot = new ReadinessSnapshot { CapturedAtUtc = DateTime.UtcNow };
        snapshot.Checks.Add(new ReadinessCheck { Name = "Windows e arquitetura", Detail = "Dados fictícios para teste visual: Windows 11 · 64 bits", Action = "A versão deve ser validada no diagnóstico real.", State = CheckState.Good });
        snapshot.Checks.Add(new ReadinessCheck { Name = "Adaptador de vídeo", Detail = "Dados fictícios para teste visual: GPU não confirmada", Action = "Consulte a origem oficial do fabricante.", State = CheckState.Unknown });
        return Task.FromResult(snapshot);
    }
    private static void Capture(Form form, string path)
    {
        form.PerformLayout(); Application.DoEvents();
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(path); }
        using (StreamWriter writer = new StreamWriter(path + ".layout.txt")) Dump(form, writer, 0);
    }
    private static void Dump(Control control, TextWriter writer, int depth)
    {
        if (!control.Visible) return;
        writer.WriteLine(new string(' ', depth * 2) + control.GetType().Name + " " + control.Name + " " + control.Bounds + " text=" + control.Text.Replace("\r", "").Replace("\n", " | "));
        foreach (Control child in control.Controls) Dump(child, writer, depth + 1);
    }
}
