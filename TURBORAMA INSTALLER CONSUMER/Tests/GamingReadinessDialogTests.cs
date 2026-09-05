using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace InstallerHost
{
    internal static class GamingReadinessDialogTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int passed = Run(args.Length == 0 ? "TestResults\\diagnostic" : args[0]);
                Console.WriteLine("DIAGNOSTIC DIALOG PASS assertions=" + passed + "; synthetic data only; no scanner, clipboard, browser or installer action.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        internal static int Run(string outputDirectory)
        {
            int passed = 0;
            Action<bool, string> verify = delegate(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException("FAIL: " + message);
                passed++;
                Console.WriteLine("PASS: " + message);
            };
            GamingReadinessProfile profile = new GamingReadinessProfile
            {
                OsCaption = "Windows — perfil sintético para layout",
                OsVersion = "10.0", OsBuild = 26100, OsArchitecture = "64 bits",
                CpuName = "Processador sintético com nome longo para conferir a coluna de informações",
                SystemDrive = "C:\\", SystemDriveFreeBytes = 400L * 1024L * 1024L,
                OverallState = GamingReadinessState.Attention, Score = 12
            };
            foreach (GamingRuntimeComponent component in GamingRuntimeManifest.GetComponents())
                profile.MutableRuntimeStatuses.Add(new RuntimeComponentStatus
                {
                    Component = component, State = GamingReadinessState.Unknown,
                    Detail = "Texto sintético longo para conferir leitura, tooltip e redimensionamento sem sobrepor controles.",
                    BundleAvailable = false
                });
			RuntimeComponentStatus repairableVc = profile.MutableRuntimeStatuses.Single(item => item.Component.Id == "vc-modern-x64");
			repairableVc.State = GamingReadinessState.Attention;
			repairableVc.BundleAvailable = true;
			RuntimeComponentStatus repairableDirectX = profile.MutableRuntimeStatuses.Single(item => item.Component.Id == "directx-june-2010");
			repairableDirectX.State = GamingReadinessState.Attention;
			repairableDirectX.BundleAvailable = true;
            Directory.CreateDirectory(outputDirectory);
            using (GamingReadinessDialog dialog = new GamingReadinessDialog(profile))
            {
                dialog.ShowInTaskbar = false;
                dialog.Opacity = 0;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-20000, -20000);
                dialog.Show();
                Application.DoEvents();
                TabControl tabs = Find<TabControl>(dialog, "DiagnosticTabs");
                verify(tabs.TabCount == 4, "The original four tabs are preserved");
                verify(tabs.TabPages.Cast<TabPage>().Select(page => page.Text).SequenceEqual(new[]
                    { "Hardware e APIs", "Componentes", "Recomendações", "Relatório técnico" }), "No tab was renamed, added or removed");
                TextBox report = Find<TextBox>(dialog, "DiagnosticReport");
                verify(report.ReadOnly && report.Text == profile.BuildDetailedReport(), "Technical report content is unchanged and read-only");
                Button close = Find<Button>(dialog, "CloseDiagnostic");
                Button copy = Find<Button>(dialog, "CopyOfficialSource");
				Button repair = Find<Button>(dialog, "RepairReadiness");
				verify(repair.Enabled && repair.Text == "REPARAR 2 PROBLEMAS",
					"Repair action exposes the exact number of safely repairable bundled dependencies");
				verify(dialog.RepairSelection.InstallMicrosoftRuntimeStack && dialog.RepairSelection.InstallDirectXLegacy &&
					!dialog.RepairSelection.InstallDokany && !dialog.RepairSelection.InstallWinFsp &&
					!dialog.RepairSelection.OpenNvidiaOfficialSource &&
					dialog.RepairSelection.AllowedComponentIds.OrderBy(id => id).SequenceEqual(
						new[] { "directx-june-2010", "vc-modern-x64" }),
					"Repair selection includes supported runtimes only and never opts into drivers or external sources");
                verify(ReferenceEquals(dialog.AcceptButton, close) && ReferenceEquals(dialog.CancelButton, close),
                    "Enter and Escape preserve the original close action");

                foreach (Size size in new[] { new Size(940, 680), new Size(760, 540) })
                {
                    dialog.Size = size;
                    dialog.PerformLayout();
                    Application.DoEvents();
                    Control header = Find<Control>(dialog, "DiagnosticHeader");
                    Control footer = Find<Control>(dialog, "DiagnosticFooter");
                    verify(!header.Bounds.IntersectsWith(tabs.Bounds) && !footer.Bounds.IntersectsWith(tabs.Bounds),
                        "Header, tabs and footer occupy separate layout rows at " + size.Width);
                    verify(!ScreenBounds(Find<Label>(dialog, "DiagnosticTitle")).IntersectsWith(ScreenBounds(Find<Label>(dialog, "DiagnosticScore"))),
                        "Title never overlaps score at " + size.Width);
                    verify(!ScreenBounds(Find<Label>(dialog, "DiagnosticSummary")).IntersectsWith(ScreenBounds(Find<Label>(dialog, "DiagnosticScore"))),
                        "Summary never overlaps score at " + size.Width);
                    verify(!ScreenBounds(Find<Label>(dialog, "DiagnosticLegal")).IntersectsWith(ScreenBounds(copy)) &&
                        !ScreenBounds(Find<Label>(dialog, "DiagnosticLegal")).IntersectsWith(ScreenBounds(close)),
                        "Legal text never overlaps footer actions at " + size.Width);
                    verify(!copy.Bounds.IntersectsWith(close.Bounds), "Footer actions do not overlap at " + size.Width);
					verify(!copy.Bounds.IntersectsWith(repair.Bounds) && !repair.Bounds.IntersectsWith(close.Bounds),
						"Repair, official source and close actions do not overlap at " + size.Width);
                    for (int index = 0; index < tabs.TabCount; index++)
                    {
                        tabs.SelectedIndex = index;
                        Application.DoEvents();
                        ListView list = tabs.SelectedTab.Controls.OfType<ListView>().FirstOrDefault();
                        if (list != null)
                            verify(list.Columns.Cast<ColumnHeader>().Sum(column => column.Width) <= list.ClientSize.Width,
                                "Columns fit " + tabs.SelectedTab.Text + " at " + size.Width);
                    }
                    tabs.SelectedIndex = 1;
                    Application.DoEvents();
                    using (Bitmap image = new Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
                        image.Save(Path.Combine(outputDirectory, "diagnostic-components-" + size.Width + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                ListView components = Find<ListView>(dialog, "DiagnosticComponentsList");
                verify(components.Items.Count == profile.RuntimeStatuses.Count, "All original component rows remain present");
                verify(components.Items[0].Tag as string == profile.RuntimeStatuses[0].Component.OfficialUrl,
                    "Official-source URL is unchanged without copying or opening it");
                verify(!string.IsNullOrWhiteSpace(components.Items[0].ToolTipText), "Long rows retain their complete content in a tooltip");
				profile.PendingRestart = true;
				profile.SystemDriveFreeBytes = RuntimeInstallerHelper.MinimumSystemDriveFreeBytes;
				verify(dialog.CheckRepairPrerequisites(), "Windows Update pending restart allows repair confirmation");
				profile.RuntimeRestartRequired = true;
				repair.PerformClick();
				Application.DoEvents();
				Label repairStatus = Find<Label>(dialog, "RepairStatus");
				verify(dialog.Visible && !dialog.RepairRequested && repairStatus.Text.Contains("exige reinicialização"),
					"An actual component restart requirement remains visible when repair is clicked");
				profile.PendingRestart = false;
				profile.RuntimeRestartRequired = false;
				profile.SystemDriveFreeBytes = 400L * 1024L * 1024L;
				repair.PerformClick();
				Application.DoEvents();
				verify(dialog.Visible && !dialog.RepairRequested && repairStatus.Text.Contains("2 GB"),
					"Actual repair click with low storage explains the required space without installing");
				verify(ScreenBounds(repairStatus).Bottom <= ScreenBounds(repair).Top &&
					ScreenBounds(close).Bottom <= dialog.RectangleToScreen(dialog.ClientRectangle).Bottom,
					"Blocking explanation and close action remain visible at the minimum window size");
				using (Bitmap blocked = new Bitmap(dialog.Width, dialog.Height))
				{
					dialog.DrawToBitmap(blocked, new Rectangle(Point.Empty, dialog.Size));
					blocked.Save(Path.Combine(outputDirectory, "repair-blocked-760.png"), System.Drawing.Imaging.ImageFormat.Png);
				}
				profile.SystemDriveFreeBytes = RuntimeInstallerHelper.MinimumSystemDriveFreeBytes;
				verify(dialog.CheckRepairPrerequisites() && !dialog.RepairRequested,
					"Cleared blockers allow confirmation but never authorize installation by themselves");
                dialog.Close();
            }
            return passed;
        }

        private static T Find<T>(Control root, string name) where T : Control
        {
            return (T)root.Controls.Find(name, true).Single();
        }

        private static Rectangle ScreenBounds(Control control)
        {
            return control.RectangleToScreen(control.ClientRectangle);
        }
    }
}
