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
				Check(NativeWindowTheme.ColorRef(Color.FromArgb(16, 18, 23)) == 0x171210,
					"Native title color uses Windows COLORREF channel order");
				assertions += RuntimeVersionPolicyTests.Run();
				assertions += JavaRuntimeDetectorTests.Run();
				assertions += PublisherPolicyTests.Run();
                assertions += PrerequisiteControl.RunSelectionRegressionTests();
                assertions += ArtworkTests.Run();
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
                            if (step == 2) { Find<FlowLayoutPanel>(form, "prerequisiteContent").AutoScrollPosition = Point.Empty; Pump(); }
                            Check(form.TestPage.Controls.Find("ConsumerLayout", true).Length == 1, "Exactly one page layout tree");
                            Check(form.TestPage.Controls.Find("OriginalSequence", true).Length == 1, "Original five-step indicator exists");
                            Control brand = Find<Control>(form.TestPage, "TurboRamaBrand");
                            Control sequence = Find<Control>(form.TestPage, "OriginalSequence");
                            Control heading = Find<TableLayoutPanel>(form.TestPage, "ConsumerLayout").GetControlFromPosition(0, 2);
                            Control body = Find<Control>(form.TestPage, "WizardBody");
                            Control footer = Find<Control>(form.TestPage, "WizardActions");
                            WizardSequenceBar sequenceBar = (WizardSequenceBar)sequence;
                            Check(sequenceBar.StepCount == 5 && sequenceBar.CurrentStep == step,
                                "Progress map preserves the original order and highlights the current page");
                            Check(!sequenceBar.TabStop && sequenceBar.Controls.Count == 0,
                                "Read-only progress map does not add focus traps or hidden menu controls");
                            Check(brand.Bottom <= sequence.Top && sequence.Bottom <= heading.Top && heading.Bottom <= body.Top && body.Bottom <= footer.Top,
                                "Artwork, original sequence, heading, page and actions never overlap");
                            if (step == 0)
                            {
                                Control hero = Find<Control>(form.TestPage, "TurboRamaHero");
                                Control description = Find<Control>(form.TestPage, "WelcomeDescription");
                                Check(description.Parent.Width <= hero.Width / 2 && description.Parent.Height <= hero.Height,
                                    "Welcome copy stays in its own column and leaves the F-15 visible");
                                Check(body.ClientRectangle.Contains(hero.Bounds), "Welcome artwork fits available height, with no vertical overflow");
                                Control copy = Find<Control>(form.TestPage, "WelcomeCopy");
                                Label headline = Find<Label>(form.TestPage, "WelcomeHeadline");
                                Check(hero.ClientRectangle.Contains(copy.Bounds), "Welcome copy is fully inside the artwork viewport");
                                Check(copy.BackColor == Color.Transparent, "Welcome has no opaque text rectangle cutting through the image");
                                Check(copy.ClientRectangle.Contains(description.Bounds) && copy.ClientRectangle.Contains(headline.Bounds) && headline.Bottom <= description.Top,
                                    "Welcome headline and description do not overlap or clip");
                                Check(headline.Height <= headline.Font.Height * 2 + 4, "Welcome headline occupies at most two lines");
                                Check(description.Height >= description.GetPreferredSize(new Size(description.Width, 0)).Height,
                                    "Full welcome description fits at the current width");
                                Check(!((ScrollableControl)hero).VerticalScroll.Visible && !((ScrollableControl)body).VerticalScroll.Visible,
                                    "Welcome has no vertical scrollbar");
                            }
                            if (step == 1)
                            {
                                TextBox license = Find<TextBox>(form.TestPage, "licenseTextBox");
                                Control card = Find<Control>(form.TestPage, "LicenseCard");
                                Check(license.Text == Texts.GetString("LicenseText"), "Original license content is unchanged");
                                Check(license.Left >= 20 && license.Top >= 20 && card.Width - license.Right >= 20 && card.Height - license.Bottom >= 20,
                                    "License text is inset from every card edge");
                                Check(license.Height >= 140 && body.ClientRectangle.Contains(card.Parent.Bounds), "License has useful reading height without page overflow");
                                Check(license.Height % license.Font.Height == 0, "License reading area ends on a complete text line");
                                Check(Find<CheckBox>(form.TestPage, "chkAgree").Bottom <= card.Parent.ClientSize.Height,
                                    "License agreement stays visible below the reading area");
                            }
                            ValidateLayout(form.TestPage);
                            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, "Step" + step + "-" + size.Width + ".png")); }
                            using (StreamWriter log = new StreamWriter(Path.Combine(directory, "Step" + step + "-" + size.Width + ".layout.txt"))) Dump(form.TestPage, log, 0);
                            if (step == 2)
                            {
                                FlowLayoutPanel content = Find<FlowLayoutPanel>(form, "prerequisiteContent");
                                CheckBox dokany = Find<CheckBox>(form, "chkDokany");
                                CheckBox winfsp = Find<CheckBox>(form, "chkwinFSP");
                                Point location = content.PointToClient(dokany.PointToScreen(Point.Empty));
                                content.AutoScrollPosition = new Point(0, Math.Max(0, location.Y - content.AutoScrollPosition.Y - 16)); Pump();
                                Check(dokany.Enabled && winfsp.Enabled && !dokany.Checked && !winfsp.Checked,
                                    "New driver options remain available but unchecked in the original Prerequisites step");
                                Check(winfsp.Text.IndexOf("Beta", StringComparison.OrdinalIgnoreCase) >= 0,
                                    "WinFsp checkbox explicitly identifies the prerelease");
                                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                                { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, "Drivers-" + size.Width + ".png")); }
                                CheckBox compatibility = Find<CheckBox>(form, "chkOptionalCompatibility");
                                location = content.PointToClient(compatibility.PointToScreen(Point.Empty));
                                content.AutoScrollPosition = new Point(0, Math.Max(0, location.Y - content.AutoScrollPosition.Y - 16)); Pump();
                                location = content.PointToClient(compatibility.PointToScreen(Point.Empty));
                                Check(compatibility.Enabled && !compatibility.Checked && location.Y >= 0 &&
                                    location.Y + compatibility.Height <= content.ClientSize.Height,
                                    "Additional compatibility is available, opt-in and fully reachable in the original scrolling prerequisite step");
                                Check(compatibility.Text.Contains("Java 8/17/21/25") && compatibility.Text.Contains("XNA") &&
                                    compatibility.Text.Contains(".NET x86"),
                                    "Additional compatibility clearly names only its bundled dependency families");
                                ValidateLayout(form.TestPage);
                                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                                { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, "Compatibility-" + size.Width + ".png")); }
                            }
                        }
                    }
                    Check(form.TestPage is FinishControl && form.TestPage.Controls.Find("btnBack", true).Length == 0,
                        "Conclusion has no Back, as in the original");
                    form.Close();
                }
                Console.WriteLine("CONSUMER UI PASS assertions=" + assertions + "; original five-step and optional-component captures; no real scanner, installer or extraction invoked.");
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
