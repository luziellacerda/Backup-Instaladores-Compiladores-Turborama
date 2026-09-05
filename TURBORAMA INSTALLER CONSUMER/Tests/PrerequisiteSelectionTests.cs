// Compile only into the test executable, with /define:CONSUMER_UI_TESTS.
// This partial factory bypasses the production constructor and all diagnostics.
// It never creates a worker, starts an installer, shows a form or accesses the PC.
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace InstallerHost
{
    public partial class PrerequisiteControl
    {
        private bool SuppressReadinessForUiTest { get; set; }
        private enum PrerequisiteTestConstruction { SyntheticProfile }

        private PrerequisiteControl(MainForm main, PrerequisiteTestConstruction testConstruction)
        {
            mainForm = main;
            SuppressReadinessForUiTest = true;
            gamingReadinessProfile = CreateSelectionTestProfile(GamingReadinessState.Unknown);
            gamingReadinessCapturedAtUtc = DateTime.UtcNow;
            selectionInitialized = true;
            InitializeComponent();
            wizardHeader.Text = ConsumerText.GetString("PrerequisiteIntro", Array.Empty<object>());
            btnCancel.Text = ConsumerText.GetString("Cancel", Array.Empty<object>());
            btnNext.Text = ConsumerText.GetString("Next >", Array.Empty<object>());
            btnBack.Text = ConsumerText.GetString("< Back", Array.Empty<object>());
            chkVCpp.Checked = false;
            chkDirectX.Checked = false;
            chkNvidiaApp.Checked = false;
            chkNvidiaApp.Enabled = false;
            UpdateProgressMaximumFromSelection();
            ApplyGamingReadinessProfileToUi();
            readinessLabel.Text = "Diagnóstico sintético — somente teste de interface";
        }

        private static GamingReadinessProfile CreateSelectionTestProfile(GamingReadinessState state)
        {
            GamingReadinessProfile profile = new GamingReadinessProfile
            {
                OsCaption = "Windows (perfil sintético)",
                Is64BitOperatingSystem = true,
                SystemDrive = "C:\\",
                SystemDriveFreeBytes = 400L * 1024L * 1024L,
                OverallState = GamingReadinessState.Attention
            };
            foreach (GamingRuntimeComponent component in GamingRuntimeManifest.GetComponents())
            {
                profile.MutableRuntimeStatuses.Add(new RuntimeComponentStatus
                {
                    Component = component,
                    State = state,
                    Detail = "Estado sintético para testar navegação sem executar instalações.",
                    BundleAvailable = false
                });
            }
            return profile;
        }

        internal static PrerequisiteControl CreateForUiTest(MainForm main)
        {
            return new PrerequisiteControl(main, PrerequisiteTestConstruction.SyntheticProfile);
        }

        internal static int RunSelectionRegressionTests()
        {
            int passed = 0;
            Action<bool, string> verify = delegate(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException("FAIL: " + name);
                passed++;
                Console.WriteLine("PASS: " + name);
            };
            using (PrerequisiteControl page = CreateForUiTest(null))
            {
                verify(page.gamingReadinessWorker == null && page.installerWorker == null,
                    "Test constructor starts neither diagnostics nor installer workers");
                verify(page.GetSelectedStepCount(page.GetPrerequisiteSelection()) == 0,
                    "All-unchecked selection keeps the original zero-step path");
                verify(page.observedSelectionMask == 0, "All-unchecked selection is tracked without phantom actions");

                page.installationComplete = true;
                page.UpdateProgressMaximumFromSelection();
                verify(page.installationComplete, "Refreshing the same selection preserves completion");
                page.chkVCpp.Checked = true;
                verify(!page.installationComplete, "Changing the Microsoft selection invalidates prior completion");
                verify(page.GetPrerequisiteSelection().RuntimeSelection.InstallMicrosoftRuntimeStack,
                    "The new Microsoft choice is the effective selection");

                page.installationComplete = true;
                page.chkVCpp.Checked = true;
                page.UpdateProgressMaximumFromSelection();
                verify(page.installationComplete, "Assigning the same checkbox value does not invalidate completion");
                page.chkDirectX.Checked = true;
                verify(!page.installationComplete, "Changing the DirectX choice invalidates prior completion");

                page.chkNvidiaApp.Enabled = true;
                page.UpdateProgressMaximumFromSelection();
                page.installationComplete = true;
                page.chkNvidiaApp.Checked = true;
                verify(!page.installationComplete, "Selecting the NVIDIA link invalidates prior completion");

                page.chkNvidiaApp.Checked = false;
                page.chkNvidiaApp.Enabled = false;
                page.UpdateProgressMaximumFromSelection();
                page.installationComplete = true;
                page.chkNvidiaApp.Checked = true;
                verify(page.installationComplete, "An unavailable checkbox does not change the effective selection");
                page.chkNvidiaApp.Checked = false;

                page.SetButtonsInstallingState(true);
                verify(page.IsInstallationRunning(), "Busy state begins before the worker can start");
                verify(!page.chkVCpp.Enabled && !page.chkDirectX.Enabled && !page.chkNvidiaApp.Enabled,
                    "All actionable checkboxes are disabled while busy");
                page.chkVCpp.Checked = false;
                page.chkDirectX.Checked = false;
                page.chkNvidiaApp.Checked = true;
                verify(page.chkVCpp.Checked && page.chkDirectX.Checked && !page.chkNvidiaApp.Checked,
                    "Even programmatic checkbox changes are restored while busy");
                verify(page.installationComplete, "Rejected busy changes do not corrupt prior completion");

                // mainForm is null deliberately: any attempted navigation would
                // throw instead of opening another page or starting an installer.
                page.BtnNext_Click(page, EventArgs.Empty);
                page.BtnBack_Click(page, EventArgs.Empty);
                page.BtnCancel_Click(page, EventArgs.Empty);
                verify(page.installerWorker == null, "Next, Back and Cancel do nothing while busy");

                page.SetButtonsInstallingState(false);
                verify(!page.IsInstallationRunning(), "Unlock clears busy state without creating a worker");
                verify(page.chkVCpp.Enabled && page.chkDirectX.Enabled && !page.chkNvidiaApp.Enabled,
                    "Unlock restores the original availability of each checkbox");
                page.UpdateProgressMaximumFromSelection();
                verify(page.installationComplete, "Lock and unlock alone preserve the effective selection");

                page.chkVCpp.Checked = false;
                page.chkDirectX.Checked = false;
                verify(!page.installationComplete, "Deselecting after completion invalidates the previous result");
                verify(page.GetSelectedStepCount(page.GetPrerequisiteSelection()) == 0,
                    "Deselect-all still produces zero steps and never invokes the worker");
                verify(page.gamingReadinessWorker == null && page.installerWorker == null,
                    "All regression assertions finish without any scanner or installer execution");

                verify(!page.SkipIfAllInstalled(), "Unknown fixture does not skip the Prerequisites page");
                page.gamingReadinessCapturedAtUtc = DateTime.UtcNow;
                page.chkVCpp.Checked = true;
                page.BtnNext_Click(page, EventArgs.Empty);
                verify(page.installerWorker == null && !page.IsInstallationRunning() &&
                    page.progressTitleText == "Espaço insuficiente para preparar componentes",
                    "Low system-disk space blocks runtime execution before creating a worker");
                page.chkVCpp.Checked = false;
                GamingReadinessProfile ready = CreateSelectionTestProfile(GamingReadinessState.Ready);
                page.gamingReadinessProfile = ready;
                page.gamingReadinessCapturedAtUtc = DateTime.UtcNow;
                verify(page.SkipIfAllInstalled(), "Complete fresh Ready evidence allows the original skip path");
                RuntimeComponentStatus actionable = ready.MutableRuntimeStatuses.First(item =>
                    item.Component.CanInstallOffline && item.Component.Tier != GamingRuntimeTier.Optional &&
                    GamingRuntimeManifest.IsApplicableToCurrentOs(item.Component));
                actionable.State = GamingReadinessState.NotApplicable;
                verify(page.SkipIfAllInstalled(), "Verified NotApplicable also allows skip");
                actionable.State = GamingReadinessState.Unknown;
                verify(!page.SkipIfAllInstalled(), "One unknown actionable component prevents skip");
                actionable.State = GamingReadinessState.Attention;
                verify(!page.SkipIfAllInstalled(), "One missing actionable component prevents skip");
                actionable.State = GamingReadinessState.Ready;
                foreach (RuntimeComponentStatus item in ready.MutableRuntimeStatuses.Where(item =>
                    item.Component.Tier == GamingRuntimeTier.Optional || !item.Component.CanInstallOffline))
                    item.State = GamingReadinessState.Unknown;
                verify(page.SkipIfAllInstalled(), "Missing optional or manual-only components do not prevent skip");
                ready.MutableRuntimeStatuses.Remove(actionable);
                verify(!page.SkipIfAllInstalled(), "Incomplete evidence cannot skip");
                ready.MutableRuntimeStatuses.Add(actionable);
                ready.MutableRuntimeStatuses.Add(actionable);
                verify(!page.SkipIfAllInstalled(), "Duplicate evidence cannot skip");
                ready.MutableRuntimeStatuses.Remove(actionable);
                page.gamingReadinessCapturedAtUtc = DateTime.UtcNow.AddMinutes(-6);
                verify(!page.SkipIfAllInstalled(), "Expired evidence cannot skip");
                page.gamingReadinessCapturedAtUtc = DateTime.UtcNow;
                page.gamingReadinessScanPending = true;
                verify(!page.SkipIfAllInstalled(), "Pending rescan prevents skip using prior evidence");
                page.gamingReadinessScanPending = false;
                page.gamingReadinessProfile = null;
                verify(!page.SkipIfAllInstalled(), "Absent profile cannot skip or trigger synchronous detection");
                verify(page.GetSelectedStepCount(page.GetPrerequisiteSelection()) == 0,
                    "All unchecked needs no synchronous profile to retain zero-step behavior");
                page.chkVCpp.Checked = true;
                page.installationComplete = false;
                page.BtnNext_Click(page, EventArgs.Empty);
                verify(page.installerWorker == null && !page.installationComplete,
                    "Selected runtimes with no fresh evidence wait instead of installing or skipping");
                page.chkVCpp.Checked = false;

                GamingReadinessProfile beforeInstall = CreateSelectionTestProfile(GamingReadinessState.Unknown);
                page.gamingReadinessProfile = beforeInstall;
                page.gamingReadinessCapturedAtUtc = DateTime.UtcNow;
                int oldRevision = page.gamingReadinessRevision;
                page.SetButtonsInstallingState(true);
                page.CompleteGamingReadinessScan(oldRevision, ready, null);
                verify(ReferenceEquals(page.gamingReadinessProfile, beforeInstall),
                    "Scan completed during installation cannot overwrite the current profile");
                GamingReadinessProfile installed = CreateSelectionTestProfile(GamingReadinessState.Ready);
                PrerequisiteSelection completedSelection = new PrerequisiteSelection
                {
                    RuntimeSelection = new GamingRuntimeInstallSelection(),
                    OpenNvidiaOfficialSource = false
                };
                page.InstallerWorker_RunWorkerCompleted(page,
                    new RunWorkerCompletedEventArgs(new PrerequisiteInstallationResult(completedSelection, installed), null, false));
                verify(page.installationComplete && !page.IsInstallationRunning() && page.btnNext.Enabled,
                    "Successful completion stays on the page with Next enabled (null MainForm is never called)");
                verify(ReferenceEquals(page.gamingReadinessProfile, installed),
                    "Successful completion retains its verified post-install profile");
                page.CompleteGamingReadinessScan(oldRevision, beforeInstall, null);
                verify(ReferenceEquals(page.gamingReadinessProfile, installed),
                    "Late pre-install scan cannot overwrite the successful installation result");
                int currentRevision = ++page.gamingReadinessRevision;
                page.gamingReadinessScanPending = true;
                page.CompleteGamingReadinessScan(currentRevision, beforeInstall, null);
                verify(ReferenceEquals(page.gamingReadinessProfile, beforeInstall) && !page.gamingReadinessScanPending,
                    "Only a current complete scan may update the diagnostic profile");
                currentRevision = ++page.gamingReadinessRevision;
                page.gamingReadinessScanPending = true;
                page.CompleteGamingReadinessScan(currentRevision, new GamingReadinessProfile(), null);
                verify(!page.HasCurrentReadinessProfile() && !page.SkipIfAllInstalled(),
                    "Incomplete current scan is not trusted as a fresh result");
                verify(page.gamingReadinessWorker == null && page.installerWorker == null,
                    "Flow and stale-result tests never execute diagnostics or installers");
            }
            return passed;
        }
    }

    internal static class PrerequisiteSelectionTests
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                int passed = PrerequisiteControl.RunSelectionRegressionTests();
                Console.WriteLine("PASS " + passed + "/" + passed + " prerequisite selection assertions.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
