using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace InstallerHost
{
    public partial class PrerequisiteControl
    {
        internal void SetUiTestEvidence(GamingReadinessState state)
        {
            gamingReadinessProfile = CreateSelectionTestProfile(state);
            gamingReadinessCapturedAtUtc = DateTime.UtcNow;
            gamingReadinessScanPending = false;
        }
    }
    public partial class MainForm
    {
        internal void PrepareSyntheticPrerequisites() { _prerequisite = PrerequisiteControl.CreateForUiTest(this); }
        internal void SetPrerequisiteEvidence(GamingReadinessState state) { _prerequisite.SetUiTestEvidence(state); }
        internal UserControl TestPage { get { return currentControl; } }
    }
    internal static class ConsumerUiTests
    {
        private static int assertions;
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
				assertions += RuntimeVersionPolicyTests.Run();
                assertions += PrerequisiteControl.RunSelectionRegressionTests();
                string directory = args.Length == 0 ? "TestResults\\consumer" : args[0]; Directory.CreateDirectory(directory);
                using (MainForm form = new MainForm())
                {
                    form.PrepareSyntheticPrerequisites(); form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Pump();
                    Check(form.TestPage is WelcomeControl, "Starts with the original welcome page");
                    Find<Button>(form, "btnNext").PerformClick(); Pump();
                    Check(form.TestPage is LicenseControl, "Welcome advances to license");
                    Button next = Find<Button>(form, "btnNext");
                    Check(!next.Enabled && form.AcceptButton == null, "License prevents advance and Enter until agreement");
                    Find<CheckBox>(form, "chkAgree").Checked = true;
                    Check(next.Enabled && ReferenceEquals(form.AcceptButton, next), "License agreement enables its sole default action");
                    next.PerformClick(); Pump();
                    Check(form.TestPage is PrerequisiteControl, "License advances to prerequisites, no invented step");
                    Check(!Find<CheckBox>(form, "chkVCpp").Checked && !Find<CheckBox>(form, "chkDirectX").Checked,
                        "Synthetic fixture contains no requested installations");
                    Find<Button>(form, "btnNext").PerformClick(); Pump();
                    Check(form.TestPage is InstallControl, "Empty prerequisite selection follows original route to installation");
                    Find<TextBox>(form, "txtFolder").Text = "D:\\Destino-escolhido-para-teste";
                    Find<Button>(form, "btnBack").PerformClick(); Pump();
                    Check(form.TestPage is PrerequisiteControl, "Installation Back returns to prerequisites");
                    form.ShowInstall(); Pump();
                    Check(Find<TextBox>(form, "txtFolder").Text == "D:\\Destino-escolhido-para-teste", "Returning preserves the selected destination");
                    form.ShowPrerequisites(false); Pump();
                    Find<Button>(form, "btnBack").PerformClick(); Pump();
                    Check(form.TestPage is LicenseControl, "Prerequisites Back returns to license");
                    Find<Button>(form, "btnBack").PerformClick(); Pump();
                    Check(form.TestPage is WelcomeControl, "License Back returns to welcome");

                    form.SetPrerequisiteEvidence(GamingReadinessState.Ready);
                    form.ShowLicense(); Pump();
                    Find<Button>(form, "btnNext").PerformClick(); Pump();
                    Check(form.TestPage is InstallControl, "Verified-ready prerequisites are skipped when advancing");
                    Find<Button>(form, "btnBack").PerformClick(); Pump();
                    Check(form.TestPage is LicenseControl, "Back skips verified-ready prerequisites and returns to license");
                    form.SetPrerequisiteEvidence(GamingReadinessState.Unknown);

                    foreach (Size size in new[] { new Size(1120, 680), new Size(820, 570) })
                    {
                        form.ClientSize = size;
                        for (int step = 0; step < 5; step++)
                        {
                            if (step == 0) form.ShowWelcome(); else if (step == 1) form.ShowLicense();
                            else if (step == 2) form.ShowPrerequisites(true); else if (step == 3) form.ShowInstall();
                            else form.ShowFinish("D:\\Exemplo-visual-nao-instalado");
                            Pump();
                            Check(form.TestPage.Controls.Find("ConsumerLayout", true).Length == 1, "Exactly one page layout tree");
                            Check(form.TestPage.Controls.Find("OriginalSequence", true).Length == 1, "Original five-step indicator exists");
                            ValidateLayout(form.TestPage);
                            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, "Step" + step + "-" + size.Width + ".png")); }
                            using (StreamWriter log = new StreamWriter(Path.Combine(directory, "Step" + step + "-" + size.Width + ".layout.txt"))) Dump(form.TestPage, log, 0);
                        }
                    }
                    Check(form.TestPage is FinishControl && form.TestPage.Controls.Find("btnBack", true).Length == 0,
                        "Conclusion has no Back, as in the original");
                    form.Close();
                }
                Console.WriteLine("CONSUMER UI PASS assertions=" + assertions + "; ten captures; no real scanner, installer or extraction invoked.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        private static T Find<T>(Control parent, string name) where T : Control
        { return parent.Controls.Find(name, true).OfType<T>().Single(); }
        private static void Pump() { Application.DoEvents(); }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); assertions++; Console.WriteLine("PASS " + message); }
        private static void ValidateLayout(Control control)
        {
            if (!control.Visible) return;
            FlowLayoutPanel flow = control as FlowLayoutPanel;
            if (flow != null) Check(!flow.HorizontalScroll.Visible, "No horizontal overflow: " + control.Name);
            foreach (Control child in control.Controls) ValidateLayout(child);
        }
        private static void Dump(Control control, TextWriter writer, int depth)
        {
            if (!control.Visible) return;
            writer.WriteLine(new string(' ', depth * 2) + control.GetType().Name + " " + control.Name + " " + control.Bounds);
            foreach (Control child in control.Controls) Dump(child, writer, depth + 1);
        }
    }
}
