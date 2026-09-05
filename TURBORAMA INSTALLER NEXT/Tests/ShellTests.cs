using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TurboRama.Next;

// Tests fresh source controls in this process with invisible owned forms and
// fake services. No old EXE, reflection, SendKeys, real scanner or installer.
internal static class ShellTests
{
    private static int cases;
    private static int failures;
    private static int assertions;

    [STAThread]
    public static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (Form loopHost = new Form { ShowInTaskbar = false, Opacity = 0 })
        {
            loopHost.Shown += delegate
            {
                loopHost.BeginInvoke(new Action(delegate { RunAll(); loopHost.Close(); }));
            };
            Application.Run(loopHost);
        }
        return failures == 0 ? 0 : 1;
    }

    private static void RunAll()
    {
        Run("Active-page navigation and consent command binding", NavigationAndConsent);
        Run("Component buttons and filters preserve selected plan", ComponentsAndFilters);
        Run("Holding runner blocks navigation, duplicate begin and close", BusyShell);
        Run("Failure displays result and supports explicit retry", FailureAndRetry);
        Run("AcceptButton runs only reviewed consented plan", AcceptButtonAction);
        Run("Success then edit invalidates consent and success", SuccessEdit);
        Run("Session updates refresh visible review details", VisibleReviewRefresh);
        Run("Late progress from old run cannot contaminate new run", StaleProgress);
        Run("Disposal cancels operation and eventually releases session", Disposal);
        Console.WriteLine("RESULT cases={0} assertions={1} passedCases={2} failures={3}", cases, assertions, cases - failures, failures);
    }

    private static void Run(string name, Action action)
    {
        cases++;
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception error)
        { failures++; Console.WriteLine("FAIL " + name + ": " + error.GetType().Name + ": " + error.Message); }
        Pump();
    }

    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }

    private static void Pump()
    { for (int index = 0; index < 4; index++) { Application.DoEvents(); Thread.Sleep(1); } }

    private static void Wait(Task task)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.ElapsedMilliseconds < 3000) Pump();
        Check(task.IsCompleted, "Fake operation did not settle within three seconds.");
        task.GetAwaiter().GetResult();
        Pump();
    }

    private static T Find<T>(Control root, string name) where T : Control
    {
        if (root.Name == name) return (T)root;
        foreach (Control child in root.Controls)
        {
            T found = FindOrNull<T>(child, name);
            if (found != null) return found;
        }
        throw new InvalidOperationException("Control not found: " + name);
    }

    private static T FindOrNull<T>(Control root, string name) where T : Control
    {
        if (root.Name == name) return root as T;
        foreach (Control child in root.Controls)
        {
            T found = FindOrNull<T>(child, name); if (found != null) return found;
        }
        return null;
    }

    private static ShellForm Open(SetupSession session, IPlanRunner runner)
    {
        ShellForm shell = new ShellForm(session, FakeScan, runner);
        shell.ShowInTaskbar = false;
        shell.Opacity = 0;
        shell.Show(); Pump();
        return shell;
    }

    private static Task<ReadinessSnapshot> FakeScan(CancellationToken token)
    {
        ReadinessSnapshot snapshot = new ReadinessSnapshot { CapturedAtUtc = DateTime.UtcNow };
        snapshot.Checks.Add(new ReadinessCheck { Name = "Fake hardware", Detail = "Synthetic evidence only",
            Action = "No action", State = TurboRama.Next.CheckState.Good });
        return Task.FromResult(snapshot);
    }

    private static void Review(ShellForm shell)
    {
        shell.NavigateTo(PageId.Review); Pump();
        Find<CheckBox>(shell, "PreviewConsent").Checked = true; Pump();
    }

    private static void NavigationAndConsent()
    {
        SetupSession session = new SetupSession();
        using (ShellForm shell = Open(session, new QueueRunner()))
        {
            Button primary = Find<Button>(shell, "PrimaryAction");
            Check(object.ReferenceEquals(shell.AcceptButton, primary), "Overview default command must be shell primary.");
            foreach (PageId page in new[] { PageId.Diagnostics, PageId.Components, PageId.Review, PageId.Overview })
            {
                Find<Button>(shell, "Navigate" + page).PerformClick(); Pump();
                Check(session.Page == page, "Navigation button went to wrong page: " + page);
                Check(shell.CancelButton == null, "Shell retained a page-owned Escape button.");
                Check(page == PageId.Review ? shell.AcceptButton == null : object.ReferenceEquals(shell.AcceptButton, primary), "AcceptButton not rebound correctly on " + page);
            }
            shell.NavigateTo(PageId.Review);
            Check(!primary.Enabled && shell.AcceptButton == null, "Review without consent must have no default command.");
            CheckBox agree = Find<CheckBox>(shell, "PreviewConsent"); agree.Checked = true;
            Check(primary.Enabled && object.ReferenceEquals(shell.AcceptButton, primary), "Consent did not activate correct command.");
            agree.Checked = false;
            Check(!primary.Enabled && shell.AcceptButton == null, "Removing consent left actionable command.");
            Check(Find<Panel>(shell, "PageHost").Controls.Cast<Control>().Count(item => item.Visible) == 1, "More than one page is visible.");
        }
    }

    private static void ComponentsAndFilters()
    {
        SetupSession session = new SetupSession();
        using (ShellForm shell = Open(session, new QueueRunner()))
        {
            shell.NavigateTo(PageId.Components);
            Find<Button>(shell, "ClearSelection").PerformClick();
            Check(session.SelectionCount == 0, "Clear button did not clear state.");
            Find<CheckBox>(shell, "Select_xna").Checked = true;
            Check(session.SelectionCount == 1 && session.IsSelected("xna"), "Checkbox did not update state.");
            ComboBox filter = Find<ComboBox>(shell, "ComponentFilter"); filter.SelectedIndex = 1;
            Check(!Find<Control>(shell, "ComponentRow_xna").Visible, "Essentials filter did not hide compatibility row.");
            Check(session.IsSelected("xna"), "Filtering silently altered selection.");
            filter.SelectedIndex = 0;
            Check(Find<Control>(shell, "ComponentRow_xna").Visible, "All filter failed to restore row.");
            Find<Button>(shell, "SelectCompatibility").PerformClick();
            Check(session.SelectionCount == ComponentCatalog.All.Count, "Compatibility profile button mismatch.");
            Find<Button>(shell, "SelectEssentials").PerformClick();
            Check(session.SelectionCount == ComponentCatalog.All.Count(item => item.Recommended), "Essentials profile button mismatch.");
        }
    }

    private static void BusyShell()
    {
        SetupSession session = new SetupSession(); QueueRunner runner = new QueueRunner();
        using (ShellForm shell = Open(session, runner))
        {
            Review(shell); Task operation = shell.PrimaryActionAsync();
            Check(session.IsBusy && runner.Calls.Count == 1, "Run was not started once.");
            Check(shell.AcceptButton == null && !Find<Button>(shell, "PrimaryAction").Enabled, "Busy primary is active.");
            foreach (PageId page in new[] { PageId.Overview, PageId.Diagnostics, PageId.Components, PageId.Review })
            {
                Button button = Find<Button>(shell, "Navigate" + page);
                Check(!button.Enabled, "Navigation button enabled during run.");
                button.PerformClick(); shell.NavigateTo(page);
                Check(session.Page == PageId.Simulation, "Busy navigation changed page.");
            }
            shell.PrimaryActionAsync().GetAwaiter().GetResult();
            Check(runner.Calls.Count == 1, "Duplicate primary spawned another runner.");
            int revision = session.Revision;
            session.ClearSelection(); session.SetConsent(false);
            Check(session.Revision == revision && session.Consent, "Busy model mutation was accepted.");
            shell.Close(); Pump();
            Check(!shell.IsDisposed && session.IsBusy, "Close bypassed busy guard.");
            runner.Calls[0].Progress.Report(new PlanProgress { Completed = 1, Total = 2, Message = "Fresh midpoint" }); Pump();
            Check(Find<Label>(shell, "SimulationPercent").Text == "50%", "Progress did not update UI.");
            runner.Calls[0].Completion.SetResult(true); Wait(operation);
            Check(!session.IsBusy && session.Page == PageId.Result && session.LastRunSucceeded, "Success did not transition correctly.");
            Check(object.ReferenceEquals(shell.AcceptButton, Find<Button>(shell, "PrimaryAction")), "Result has stale default command.");
        }
    }

    private static void FailureAndRetry()
    {
        SetupSession session = new SetupSession(); QueueRunner runner = new QueueRunner();
        using (ShellForm shell = Open(session, runner))
        {
            Review(shell); Task first = shell.PrimaryActionAsync();
            runner.Calls[0].Progress.Report(new PlanProgress { Completed = 1, Total = 2, Message = "Before failure" }); Pump();
            runner.Calls[0].Completion.SetException(new InvalidOperationException("Synthetic runner failure")); Wait(first);
            Check(!session.LastRunSucceeded && !session.IsBusy && session.Page == PageId.Result, "Runner failure produced success or left busy.");
            Check(AllLabels(shell).Any(label => label.Text.Contains("Synthetic runner failure")), "Actionable failure detail missing.");
            Find<Button>(shell, "PreviousPage").PerformClick();
            Check(session.Page == PageId.Review && Find<CheckBox>(shell, "PreviewConsent").Checked, "Unchanged failed plan lost reviewed state.");
            Task second = shell.PrimaryActionAsync();
            Check(runner.Calls.Count == 2 && Find<ListBox>(shell, "SimulationHistory").Items.Count == 0, "Retry did not reset old history.");
            Check(Find<Label>(shell, "SimulationPercent").Text == "0%", "Retry retained prior percentage.");
            runner.Calls[1].Completion.SetResult(true); Wait(second);
            Check(session.LastRunSucceeded, "Explicit retry failed to complete.");
        }
    }

    private static IEnumerable<Label> AllLabels(Control control)
    {
        Label label = control as Label; if (label != null) yield return label;
        foreach (Control child in control.Controls)
            foreach (Label descendant in AllLabels(child)) yield return descendant;
    }

    private static void AcceptButtonAction()
    {
        SetupSession session = new SetupSession(); QueueRunner runner = new QueueRunner();
        using (ShellForm shell = Open(session, runner))
        {
            Review(shell); shell.AcceptButton.PerformClick(); Pump();
            Check(runner.Calls.Count == 1 && session.IsBusy && shell.AcceptButton == null, "Default command did not launch exactly the active plan.");
            runner.Calls[0].Completion.SetResult(true);
            Stopwatch watch = Stopwatch.StartNew(); while (session.IsBusy && watch.ElapsedMilliseconds < 2000) Pump();
            Check(!session.IsBusy && session.Page == PageId.Result, "Async click did not settle.");
        }
    }

    private static void SuccessEdit()
    {
        SetupSession session = new SetupSession(); QueueRunner runner = new QueueRunner();
        using (ShellForm shell = Open(session, runner))
        {
            Review(shell); Task first = shell.PrimaryActionAsync(); runner.Calls[0].Completion.SetResult(true); Wait(first);
            Wait(shell.PrimaryActionAsync());
            Check(session.Page == PageId.Components, "Result primary did not return to selection.");
            Find<CheckBox>(shell, "Select_xna").Checked = true;
            Check(!session.Consent && !session.LastRunSucceeded, "Edit retained old result or consent.");
            shell.NavigateTo(PageId.Review);
            Check(!Find<CheckBox>(shell, "PreviewConsent").Checked && shell.AcceptButton == null, "Review shows obsolete checked consent.");
            Check(Find<Label>(shell, "PlanItems").Text.Contains("XNA"), "Updated review omitted selected item.");
        }
    }

    private static void VisibleReviewRefresh()
    {
        SetupSession session = new SetupSession();
        using (ShellForm shell = Open(session, new QueueRunner()))
        {
            Review(shell); session.Select("xna", true); Pump();
            Check(!Find<CheckBox>(shell, "PreviewConsent").Checked, "Visible review still shows consent after model invalidation.");
            Check(Find<Label>(shell, "PlanItems").Text.Contains("XNA"), "Visible review did not respond to model Changed event.");
        }
    }

    private static void StaleProgress()
    {
        SetupSession session = new SetupSession(); QueueRunner runner = new QueueRunner();
        using (ShellForm shell = Open(session, runner))
        {
            Review(shell); Task first = shell.PrimaryActionAsync(); runner.Calls[0].Completion.SetResult(true); Wait(first);
            shell.NavigateTo(PageId.Review); Task second = shell.PrimaryActionAsync();
            runner.Calls[0].Progress.Report(new PlanProgress { Completed = 1, Total = 1, Message = "STALE PRIOR RUN" }); Pump();
            bool contaminated = Find<ListBox>(shell, "SimulationHistory").Items.Cast<object>().Any(item => item.ToString().Contains("STALE"));
            runner.Calls[1].Completion.SetResult(true); Wait(second);
            Check(!contaminated, "Late progress from prior run contaminated current run.");
        }
    }

    private static void Disposal()
    {
        SetupSession session = new SetupSession(); QueueRunner runner = new QueueRunner();
        ShellForm shell = Open(session, runner);
        try
        {
            Review(shell); Task pending = shell.PrimaryActionAsync();
            shell.Dispose();
            Check(runner.Calls[0].Token.IsCancellationRequested, "Disposal did not signal runner cancellation.");
            runner.Calls[0].Completion.SetCanceled(); Wait(pending);
            Check(!session.IsBusy && !session.LastRunSucceeded, "Disposed run left externally supplied session permanently busy.");
        }
        finally { shell.Dispose(); }
    }

    private sealed class Call
    {
        public IProgress<PlanProgress> Progress;
        public CancellationToken Token;
        public TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>();
    }
    private sealed class QueueRunner : IPlanRunner
    {
        public readonly List<Call> Calls = new List<Call>();
        public Task RunAsync(SetupPlan plan, IProgress<PlanProgress> progress, CancellationToken token)
        {
            Call call = new Call { Progress = progress, Token = token }; Calls.Add(call); return call.Completion.Task;
        }
    }
}
