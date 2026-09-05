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
            chkDokany.Checked = false;
            chkwinFSP.Checked = false;
            chkOptionalCompatibility.Checked = false;
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
				page.ApplyDiagnosticRepairSelection(new GamingRuntimeInstallSelection
				{
					InstallMicrosoftRuntimeStack = true,
					InstallDirectXLegacy = true,
					InstallDokany = true,
					InstallWinFsp = true,
					InstallOptionalCompatibility = true,
					OpenNvidiaOfficialSource = true,
					AllowedComponentIds = new[] { "vc-modern-x64", "directx-june-2010" }
				});
				GamingRuntimeInstallSelection repairSelection = page.GetPrerequisiteSelection().RuntimeSelection;
				verify(repairSelection.InstallMicrosoftRuntimeStack && repairSelection.InstallDirectXLegacy &&
					!repairSelection.InstallDokany && !repairSelection.InstallWinFsp && !repairSelection.InstallOptionalCompatibility &&
					!repairSelection.OpenNvidiaOfficialSource &&
					repairSelection.AllowedComponentIds.OrderBy(id => id).SequenceEqual(
						new[] { "directx-june-2010", "vc-modern-x64" }),
					"Diagnostic repair applies only supported runtime groups and rejects optional drivers and external sources");
				verify(!page.installationComplete && page.progressTitleText == "Reparo de compatibilidade selecionado",
					"Diagnostic repair invalidates prior completion and explains the preflight validation");
				page.SetButtonsInstallingState(true);
				page.UpdateProgressVisualsSafe();
				verify(page.progressBar.Style == ProgressBarStyle.Marquee && page.progressBar.MarqueeAnimationSpeed > 0 &&
					page.btnNext.Text == "Reparando…" && page.progressPercentLabel.Text == "Em andamento",
					"Repair shows animated loading and a repair label before any worker completes a component");
				page.chkVCpp.Checked = false;
				verify(page.restrictedRepairComponentIds != null && page.chkVCpp.Checked,
					"Rejected busy selection changes preserve the exact repair restriction");
				page.SetButtonsInstallingState(false);
				verify(page.progressBar.Style == ProgressBarStyle.Continuous && page.progressBar.MarqueeAnimationSpeed == 0,
					"Completion or failure stops the loading animation");
				page.SetButtonsInstallingState(true);
				page.ShowInstallationFailure("Falha simulada ao criar pasta temporária");
				verify(!page.IsInstallationRunning() && page.btnNext.Enabled && page.progressBar.MarqueeAnimationSpeed == 0 &&
					page.progressPercentLabel.Text == "Falha" && page.progressDetailText.Contains("pasta temporária"),
					"A staging failure replaces in-progress text, stops animation and permits retry");
				page.chkVCpp.Checked = false;
				verify(page.restrictedRepairComponentIds == null,
					"A later manual checkbox change leaves repair mode and restores normal explicit group selection");
				page.chkDirectX.Checked = false;
				page.UpdateProgressMaximumFromSelection();

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
					page.progressTitleText == "Não foi possível preparar os componentes",
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
            RunProgressCountRegressionTests(verify);
            RunOptionalCompatibilityRegressionTests(verify);
            RunOptionalDriverRegressionTests(verify);
            return passed;
        }

        private static void RunProgressCountRegressionTests(Action<bool, string> verify)
        {
            using (PrerequisiteControl page = CreateForUiTest(null))
            {
                verify(page.plannedStepCount == 0 && page.progressBar.Maximum == 1 && page.progressBar.Value == 0 &&
                    page.progressCountLabel.Text == "Processadas: 0 de 0" && page.progressPercentLabel.Text == "0%",
                    "Zero selected work displays 0 of 0, independently of the progress widget minimum range");
                page.UpdateProgressBarSafe();
                verify(page.progressBar.Value == 0 && page.progressCountLabel.Text == "Processadas: 0 de 0" &&
                    !page.progressHintLabel.Text.Contains("foram processadas"),
                    "A completion callback cannot invent a processed step or success for an empty plan");

                page.SetProgressMaximumSafe(3);
                verify(page.plannedStepCount == 3 && page.progressBar.Maximum == 3 &&
                    page.progressCountLabel.Text == "Processadas: 0 de 3",
                    "Worker-reported planned totals replace the true work count");
                page.SetButtonsInstallingState(true);
                page.UpdateProgressBarSafe();
                verify(page.plannedStepCount == 3 && page.progressBar.Value == 1 &&
                    page.progressCountLabel.Text == "Processadas: 1 de 3" &&
                    page.progressPercentLabel.Text == "Em andamento" && page.progressBar.Style == ProgressBarStyle.Marquee,
                    "Busy progress retains the actual step total and animated in-progress state");
                page.SetProgressMaximumSafe(5);
                verify(page.progressBar.Value == 1 && page.progressCountLabel.Text == "Processadas: 1 de 5",
                    "Increasing a worker plan preserves completed steps without resetting their count");
                page.SetProgressMaximumSafe(2);
                page.UpdateProgressBarSafe();
                page.UpdateProgressBarSafe();
                verify(page.progressBar.Value == 2 && page.progressCountLabel.Text == "Processadas: 2 de 2",
                    "Processed count never exceeds the revised true plan total");
                page.SetButtonsInstallingState(false);
                page.UpdateProgressVisualsSafe();
                verify(page.progressBar.Style == ProgressBarStyle.Continuous && page.progressPercentLabel.Text == "100%",
                    "Two completed steps out of two retain their real processed percentage after unlocking");
                page.SetProgressMaximumSafe(0);
                page.UpdateProgressBarSafe();
                verify(page.plannedStepCount == 0 && page.progressBar.Value == 0 && page.progressBar.Maximum == 1 &&
                    page.progressCountLabel.Text == "Processadas: 0 de 0" && page.progressPercentLabel.Text == "0%" &&
                    !page.progressHintLabel.Text.Contains("foram processadas"),
                    "A new empty plan clears previous full progress and never reports a false 100 percent");
                page.SetProgressMaximumSafe(-1);
                verify(page.plannedStepCount == 0 && page.progressCountLabel.Text == "Processadas: 0 de 0",
                    "An invalid negative plan total cannot leak into the progress display");

                page.SetProgressMaximumSafe(4);
                page.SetButtonsInstallingState(true);
                page.UpdateProgressBarSafe();
                page.ShowInstallationFailure("Falha simulada de validação");
                verify(page.plannedStepCount == 4 && page.progressBar.Value == 1 &&
                    page.progressCountLabel.Text == "Processadas: 1 de 4" && page.progressPercentLabel.Text == "Falha" &&
                    !page.IsInstallationRunning() && !page.installationComplete && page.progressBar.MarqueeAnimationSpeed == 0,
                    "Failure preserves truthful partial work, stops loading and never turns it into success");
                page.gamingReadinessCapturedAtUtc = DateTime.UtcNow;
                page.chkOptionalCompatibility.Checked = true;
                int selected = page.GetSelectedStepCount(page.GetPrerequisiteSelection());
                verify(selected > 0 && page.plannedStepCount == selected &&
                    page.progressCountLabel.Text == "Processadas: 0 de " + selected,
                    "Changing checkbox selection replaces the failed plan with its new exact pending total");
                page.chkOptionalCompatibility.Checked = false;
                verify(page.plannedStepCount == 0 && page.progressCountLabel.Text == "Processadas: 0 de 0" &&
                    page.progressPercentLabel.Text == "0%",
                    "Deselect-all clears a previous nonempty plan without a phantom step");
                verify(page.gamingReadinessWorker == null && page.installerWorker == null,
                    "Progress counter regressions run without scanners, workers or installers");
            }
        }

        private static void RunOptionalCompatibilityRegressionTests(Action<bool, string> verify)
        {
            verify(!GamingRuntimeInstallSelection.RecommendedDefaults().InstallOptionalCompatibility,
                "Recommended defaults never opt into additional Java/XNA/.NET x86 compatibility");
            verify(HasSelectedRuntimeGroup(new GamingRuntimeInstallSelection { InstallOptionalCompatibility = true }),
                "Additional compatibility alone is a real runtime group and must pass normal preflight checks");
            using (PrerequisiteControl page = CreateForUiTest(null))
            {
                verify(!page.chkOptionalCompatibility.Checked && page.chkOptionalCompatibility.Parent != null &&
                    page.chkOptionalCompatibility.Text.Contains("Java 8/17/21/25 / XNA / .NET x86"),
                    "Additional compatibility is an explicit visible unchecked option on the existing prerequisite page");
                page.installationComplete = true;
                page.chkOptionalCompatibility.Checked = true;
                GamingRuntimeInstallSelection selected = page.GetPrerequisiteSelection().RuntimeSelection;
                verify(!page.installationComplete && selected.InstallOptionalCompatibility && page.observedSelectionMask == 32,
                    "Selecting additional compatibility changes the effective selection and invalidates previous completion");
                verify(!selected.InstallMicrosoftRuntimeStack && !selected.InstallDirectXLegacy &&
                    !selected.InstallDokany && !selected.InstallWinFsp && !selected.OpenNvidiaOfficialSource,
                    "Additional compatibility never silently selects another group or driver");
                page.UpdatePrerequisiteOptions();
                verify(page.chkOptionalCompatibility.Checked && page.chkOptionalCompatibility.Enabled,
                    "Returning to prerequisites preserves the explicit compatibility choice when offline options apply");
                page.SetButtonsInstallingState(true);
                verify(!page.chkOptionalCompatibility.Enabled, "Additional compatibility is locked before any worker starts");
                page.chkOptionalCompatibility.Checked = false;
                verify(page.chkOptionalCompatibility.Checked, "Programmatic compatibility changes are restored while busy");
                page.SetButtonsInstallingState(false);
                verify(page.chkOptionalCompatibility.Enabled && page.chkOptionalCompatibility.Checked,
                    "Unlock restores the checked compatibility choice and original availability");

                page.gamingReadinessCapturedAtUtc = DateTime.UtcNow;
                page.BtnNext_Click(page, EventArgs.Empty);
                verify(page.installerWorker == null && page.progressTitleText == "Não foi possível preparar os componentes",
                    "Additional compatibility alone cannot bypass the low-space preflight or start an installer");
                page.gamingReadinessProfile = null;
                page.BtnNext_Click(page, EventArgs.Empty);
                verify(page.installerWorker == null && page.progressTitleText == "Diagnóstico necessário",
                    "Additional compatibility waits for fresh evidence without a synchronous scan");

                page.ApplyDiagnosticRepairSelection(new GamingRuntimeInstallSelection
                {
                    InstallMicrosoftRuntimeStack = true,
                    InstallOptionalCompatibility = true,
                    AllowedComponentIds = new[] { "vc-modern-x64" }
                });
                verify(!page.chkOptionalCompatibility.Checked &&
                    !page.GetPrerequisiteSelection().RuntimeSelection.InstallOptionalCompatibility &&
                    page.restrictedRepairComponentIds.SequenceEqual(new[] { "vc-modern-x64" }),
                    "Diagnostic repair explicitly clears optional compatibility and retains its exact repair selection");
                page.chkOptionalCompatibility.Checked = true;
                verify(page.restrictedRepairComponentIds == null,
                    "A later manual compatibility choice exits restricted repair mode");
                page.chkVCpp.Checked = false;
                page.chkOptionalCompatibility.Checked = false;
                verify(!HasSelectedRuntimeGroup(page.GetPrerequisiteSelection().RuntimeSelection) &&
                    page.GetSelectedStepCount(page.GetPrerequisiteSelection()) == 0,
                    "Deselecting optional compatibility restores the zero-work path");
                verify(page.gamingReadinessWorker == null && page.installerWorker == null,
                    "All optional compatibility tests finish without diagnostics, processes or extraction");
            }
        }

        private static void RunOptionalDriverRegressionTests(Action<bool, string> verify)
        {
            GamingRuntimeInstallSelection defaults = GamingRuntimeInstallSelection.RecommendedDefaults();
            verify(!defaults.InstallDokany && !defaults.InstallWinFsp,
                "Recommended defaults never opt into either filesystem driver");
            GamingRuntimeComponent dokany = GamingRuntimeManifest.GetComponents().Single(item => item.Id == "dokany");
            GamingRuntimeComponent winfsp = GamingRuntimeManifest.GetComponents().Single(item => item.Id == "winfsp");
            verify(dokany.CanInstallOffline && winfsp.CanInstallOffline &&
                dokany.Tier == GamingRuntimeTier.Optional && winfsp.Tier == GamingRuntimeTier.Optional &&
                !dokany.IncludedByDefault && !winfsp.IncludedByDefault,
                "Both driver packages remain optional and excluded by default in the manifest");
            GamingReadinessProfile missing = CreateSelectionTestProfile(GamingReadinessState.Unknown);
            foreach (RuntimeComponentStatus item in missing.MutableRuntimeStatuses) item.BundleAvailable = true;
            for (int mask = 0; mask < 4; mask++)
            {
                GamingRuntimeInstallSelection selection = new GamingRuntimeInstallSelection
                {
                    InstallDokany = (mask & 1) != 0,
                    InstallWinFsp = (mask & 2) != 0
                };
                RuntimeInstallPlanItem[] planned = RuntimeInstallerHelper.BuildInstallationPlan(missing, selection).ToArray();
                verify((planned.Single(item => item.Component.Id == "dokany").Disposition == RuntimeInstallDisposition.InstallFromVerifiedBundle) == selection.InstallDokany &&
                    (planned.Single(item => item.Component.Id == "winfsp").Disposition == RuntimeInstallDisposition.InstallFromVerifiedBundle) == selection.InstallWinFsp,
                    "Driver choices are independent, explicit and honored for mask " + mask);
                verify(!planned.Any(item => item.Disposition == RuntimeInstallDisposition.InstallFromVerifiedBundle &&
                    item.Component.Id != "dokany" && item.Component.Id != "winfsp"),
                    "Selecting drivers never silently selects another runtime group (mask " + mask + ")");
            }
            verify(!RuntimeInstallerHelper.BuildInstallationPlan(missing, defaults).Any(item =>
                (item.Component.Id == "dokany" || item.Component.Id == "winfsp") &&
                item.Disposition == RuntimeInstallDisposition.InstallFromVerifiedBundle),
                "The recommended stack never schedules optional drivers");

			GamingReadinessProfile outdatedWinFsp = CreateSelectionTestProfile(GamingReadinessState.Ready);
			outdatedWinFsp.SystemDriveFreeBytes = RuntimeInstallerHelper.MinimumSystemDriveFreeBytes;
			RuntimeComponentStatus outdatedWinFspStatus = outdatedWinFsp.MutableRuntimeStatuses.Single(item =>
				item.Component.Id == "winfsp");
			outdatedWinFspStatus.State = GamingReadinessState.Attention;
			outdatedWinFspStatus.DetectedVersion = "2.1.25156";
			outdatedWinFspStatus.BundleAvailable = true;
			GamingRuntimeInstallSelection updateWinFsp = new GamingRuntimeInstallSelection { InstallWinFsp = true };
			verify(RuntimeInstallerHelper.BuildInstallationPlan(outdatedWinFsp, updateWinFsp)
				.Single(item => item.Component.Id == "winfsp").Disposition == RuntimeInstallDisposition.InstallFromVerifiedBundle &&
				RuntimeInstallerHelper.GetInstallationPreflightBlockReason(outdatedWinFsp, updateWinFsp, true) == null,
				"A confirmed outdated WinFsp remains eligible for its explicitly selected verified update");
            missing.MutableRuntimeStatuses.Single(item => item.Component.Id == "dokany").BundleAvailable = false;
            verify(RuntimeInstallerHelper.BuildInstallationPlan(missing, new GamingRuntimeInstallSelection { InstallDokany = true })
                .Single(item => item.Component.Id == "dokany").Disposition == RuntimeInstallDisposition.MissingBundle,
                "A selected driver with a missing payload is not silently skipped");
            verify(RuntimeInstallerHelper.GetOptionalDriverArguments("dokany") == "/quiet /norestart",
                "Dokany uses the approved quiet and no-restart arguments");
            verify(RuntimeInstallerHelper.GetOptionalDriverArguments("winfsp") == "/qn /norestart REBOOT=ReallySuppress INSTALLLEVEL=1",
                "WinFsp suppresses reboot and installs Core only, not developer/kernel tools");
            bool rejectedDriver = false;
            try { RuntimeInstallerHelper.GetOptionalDriverArguments("unapproved-driver"); }
            catch (InvalidOperationException) { rejectedDriver = true; }
            verify(rejectedDriver, "No implicit executor strategy exists for an unknown driver");

            GamingReadinessProfile space = CreateSelectionTestProfile(GamingReadinessState.Unknown);
            verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(space, true) != null,
                "Worker preflight rejects a synthetic 400 MiB system drive");
            space.SystemDriveFreeBytes = 0;
            verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(space, true) != null,
                "Unknown or zero available disk space fails closed");
            space.SystemDriveFreeBytes = RuntimeInstallerHelper.MinimumSystemDriveFreeBytes;
            verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(space, true) == null,
                "Initial reserve passes only when the complete threshold is available");
            space.PendingRestart = true;
            space.OsBuild = 19045;
            RuntimeComponentStatus updateWarning = PrerequisiteDetector.DetectRuntimeComponent(space,
                GamingRuntimeManifest.GetComponents().Single(component => component.DetectionKey == "windows-update"));
            verify(updateWarning.State == GamingReadinessState.Attention && updateWarning.Detail.Contains("permite continuar"),
                "Pending Windows Update is displayed as an advisory, never as a blocking component");
            verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(space, true) == null,
                "Windows Update pending restart is advisory and permits offline runtime installation");
            space.RuntimeRestartRequired = true;
            verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(space, true) != null,
                "Worker preflight still respects an actual component restart requirement");
			verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(null, true) != null &&
				RuntimeInstallerHelper.GetInstallationPreflightBlockReason(null, false) == null,
				"Missing evidence blocks real installation but preserves a zero-work path");
			space.PendingRestart = false;
			space.RuntimeRestartRequired = false;
			space.SystemDriveFreeBytes = RuntimeInstallerHelper.MinimumSystemDriveFreeBytes;
			GamingRuntimeInstallSelection dokanyOnly = new GamingRuntimeInstallSelection { InstallDokany = true };
			verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(space, dokanyOnly, true).Contains("Não foi possível consultar"),
				"A selected driver with unreadable evidence cannot start an installer");
			foreach (RuntimeComponentStatus missingStatus in space.MutableRuntimeStatuses)
				missingStatus.State = GamingReadinessState.Attention;
			verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(space, dokanyOnly, true) != null,
				"Dokany-only selection fails closed when its Visual C++ dependency is not ready");
			dokanyOnly.InstallMicrosoftRuntimeStack = true;
			verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(space, dokanyOnly, true) == null,
				"Explicit Microsoft runtime selection satisfies the Dokany dependency preflight without implicit selection");
			dokanyOnly.InstallMicrosoftRuntimeStack = false;
			foreach (RuntimeComponentStatus vcStatus in space.MutableRuntimeStatuses.Where(item =>
				item.Component.Id == "vc-modern-x64" || item.Component.Id == "vc-modern-x86"))
			{
				vcStatus.State = GamingReadinessState.Ready;
			}
			verify(RuntimeInstallerHelper.GetInstallationPreflightBlockReason(space, dokanyOnly, true) == null,
				"Already-current Visual C++ evidence permits explicit Dokany installation by itself");
			verify(RuntimeInstallerHelper.GetRequiredWorkingSpaceBytes(new long[] { 100, 200 }) ==
                RuntimeInstallerHelper.MinimumSystemDriveFreeBytes + 600,
                "Working-space reserve accounts for both staging and installer expansion");
            bool rejectedLength = false;
            try { RuntimeInstallerHelper.GetRequiredWorkingSpaceBytes(new long[] { 0 }); }
            catch (System.IO.InvalidDataException) { rejectedLength = true; }
            verify(rejectedLength, "Invalid payload lengths cannot reduce the disk reserve");
            bool rejectedOverflow = false;
            try { RuntimeInstallerHelper.GetRequiredWorkingSpaceBytes(new long[] { long.MaxValue }); }
            catch (OverflowException) { rejectedOverflow = true; }
            verify(rejectedOverflow, "Overflow in payload space calculations fails closed");
            verify(RuntimeInstallerHelper.IsRestartExitCode(3010) && RuntimeInstallerHelper.IsRestartExitCode(1641) &&
                !RuntimeInstallerHelper.IsRestartExitCode(0) && !RuntimeInstallerHelper.IsRestartExitCode(1638) &&
                !RuntimeInstallerHelper.IsRestartExitCode(1603),
                "Restart codes are distinguished from success, version conflict and failure");

            using (PrerequisiteControl page = CreateForUiTest(null))
            {
                verify(!page.chkDokany.Checked && !page.chkwinFSP.Checked,
                    "Both driver checkboxes are visibly unchecked on a new page");
                page.installationComplete = true;
                page.chkDokany.Checked = true;
                verify(!page.installationComplete && page.GetPrerequisiteSelection().RuntimeSelection.InstallDokany,
                    "Explicit Dokany selection invalidates an old completed result");
                page.installationComplete = true;
                page.chkwinFSP.Checked = true;
                verify(!page.installationComplete && page.GetPrerequisiteSelection().RuntimeSelection.InstallWinFsp,
                    "Explicit WinFsp selection invalidates an old completed result");
                verify(page.GetSelectedStepCount(page.GetPrerequisiteSelection()) == 2,
                    "Only the two selected missing driver payloads count as work");
                page.UpdatePrerequisiteOptions();
                verify(page.chkDokany.Checked && page.chkwinFSP.Checked,
                    "Returning to the page preserves both explicit driver choices");
                page.SetButtonsInstallingState(true);
                verify(!page.chkDokany.Enabled && !page.chkwinFSP.Enabled,
                    "Both driver choices are disabled before any worker starts");
                page.chkDokany.Checked = false;
                page.chkwinFSP.Checked = false;
                verify(page.chkDokany.Checked && page.chkwinFSP.Checked,
                    "Programmatic changes to either driver are also rejected while busy");
                page.SetButtonsInstallingState(false);
                verify(page.chkDokany.Enabled && page.chkwinFSP.Enabled,
                    "Unlock restores both explicit driver options");
                page.gamingReadinessCapturedAtUtc = DateTime.UtcNow;
                page.BtnNext_Click(page, EventArgs.Empty);
                verify(page.installerWorker == null && page.progressTitleText == "Não foi possível preparar os componentes",
                    "Selecting only drivers cannot bypass the UI low-disk gate");
                page.gamingReadinessProfile.SystemDriveFreeBytes = RuntimeInstallerHelper.MinimumSystemDriveFreeBytes;
                page.gamingReadinessProfile.PendingRestart = true;
                page.gamingReadinessProfile.RuntimeRestartRequired = true;
                page.BtnNext_Click(page, EventArgs.Empty);
                verify(page.installerWorker == null && page.progressTitleText == "Reinicialização pendente",
                    "Selecting only drivers cannot bypass the UI restart gate");
                page.gamingReadinessProfile = null;
                page.BtnNext_Click(page, EventArgs.Empty);
                verify(page.installerWorker == null && page.progressTitleText == "Diagnóstico necessário",
                    "Driver-only choices wait for fresh evidence without synchronous detection");

                GamingReadinessProfile ready = CreateSelectionTestProfile(GamingReadinessState.Ready);
                page.gamingReadinessProfile = ready;
                page.gamingReadinessCapturedAtUtc = DateTime.UtcNow;
                ready.MutableRuntimeStatuses.Single(item => item.Component.Id == "dokany").State = GamingReadinessState.Unknown;
                verify(!page.SkipIfAllInstalled(), "A selected driver with unknown state prevents automatic page skip");
                page.chkDokany.Checked = false;
                verify(page.SkipIfAllInstalled(), "An unselected optional driver does not prevent the original skip behavior");
                ready.PendingRestart = true;
                verify(page.SkipIfAllInstalled(), "Ready components permit continuation despite a Windows Update restart warning");
                page.SetButtonsInstallingState(true);
                page.InstallerWorker_RunWorkerCompleted(page, new RunWorkerCompletedEventArgs(
                    new PrerequisiteInstallationResult(new PrerequisiteSelection
                    { RuntimeSelection = new GamingRuntimeInstallSelection() }, ready), null, false));
                verify(page.installationComplete && !page.prerequisiteRestartRequired,
                    "Windows Update advisory cannot convert completed component work into a paused repair");
                ready.PendingRestart = false;
                RuntimeInstallerHelper.MarkRestartRequired(ready, winfsp, 3010);
                verify(ready.PendingRestart && ready.RuntimeStatuses.Single(item => item.Component.Id == "winfsp").State == GamingReadinessState.Attention,
                    "A 3010 result is pending verification, never falsely Ready");
                verify(ready.Findings.Any(item => item.Code == "installer-restart-winfsp"),
                    "Restart-pending result is included in the diagnostic report");
                page.SetButtonsInstallingState(true);
                page.InstallerWorker_RunWorkerCompleted(page, new RunWorkerCompletedEventArgs(
                    new PrerequisiteInstallationResult(new PrerequisiteSelection
                    {
                        RuntimeSelection = new GamingRuntimeInstallSelection { InstallWinFsp = true }
                    }, ready), null, false));
                verify(!page.installationComplete && page.prerequisiteRestartRequired && !page.IsInstallationRunning() && page.btnNext.Enabled,
                    "Restart outcome pauses on Prerequisites, unlocks the page and does not navigate");
                GamingReadinessProfile later = CreateSelectionTestProfile(GamingReadinessState.Ready);
                later.SystemDriveFreeBytes = RuntimeInstallerHelper.MinimumSystemDriveFreeBytes;
                page.CompleteGamingReadinessScan(++page.gamingReadinessRevision, later, null);
                page.BtnNext_Click(page, EventArgs.Empty);
                verify(page.prerequisiteRestartRequired && !page.SkipIfAllInstalled() && page.installerWorker == null &&
                    page.progressTitleText == "Reinicialização pendente",
                    "A later scanner cannot erase a reboot requested by the installer in this session");
                page.chkDokany.Checked = false;
                page.chkwinFSP.Checked = false;
                verify(page.GetSelectedStepCount(page.GetPrerequisiteSelection()) == 0,
                    "Deselect-all preserves zero-work semantics even after a restart warning");
                RuntimeInstallerHelper.MarkRestartRequired(later, dokany, 1641);
                verify(later.PendingRestart && later.RuntimeStatuses.Single(item => item.Component.Id == "dokany").Detail.Contains("1641"),
                    "Unexpected 1641 is reported as initiated reboot, not hidden as ordinary success");
                verify(page.gamingReadinessWorker == null && page.installerWorker == null,
                    "All optional-driver tests finish without diagnostics, processes or payload extraction");
            }
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
