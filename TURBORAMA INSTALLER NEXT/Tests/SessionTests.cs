using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TurboRama.Next;

// Compile only with the fresh SetupSession.cs. No UI, registry, reflection,
// legacy assembly, elevation, download or installer execution is involved.
internal static class SessionTests
{
    private static int cases;
    private static int failures;
    private static int assertions;

    public static int Main()
    {
        Run("Default selection and catalog", Defaults);
        Run("Selection revisions and no-op stability", SelectionRevisions);
        Run("Invalid component rejected without mutation", InvalidComponent);
        Run("Profile changes invalidate consent", Profiles);
        Run("Begin requires review, consent and non-empty plan", BeginGuards);
        Run("Busy state blocks all mutation and duplicate run", BusyGuards);
        Run("Plan snapshot stays immutable", ImmutablePlan);
        Run("Success followed by back/edit requires new consent", SuccessThenEdit);
        Run("Failure followed by retry executes same reviewed plan", FailureThenRetry);
        Run("Skip-all then back/select cannot reuse success", EmptyThenSelect);
        Run("Completion outside busy cannot manufacture success", CompletionGuards);
        Run("Normal navigation and reserved pages", Navigation);
        Run("Undefined navigation is rejected", UndefinedNavigation);
        Run("Changed notifications follow committed state", Notifications);
        Run("Simulation reports ordered progress without changing session", SimulationProgress);
        Run("Simulation honors cancellation before execution", PreCancelledSimulation);
        Run("Simulation honors cancellation in progress", CancelledSimulation);
        Console.WriteLine("RESULT cases={0} assertions={1} passedCases={2} failures={3}",
            cases, assertions, cases - failures, failures);
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        cases++;
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error)
        {
            failures++;
            Console.WriteLine("FAIL " + name + ": " + error.GetType().Name + ": " + error.Message);
        }
    }

    private static void Check(bool condition, string detail)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(detail);
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    private static SetupSession Reviewed()
    {
        SetupSession session = new SetupSession();
        session.Navigate(PageId.Review);
        session.SetConsent(true);
        return session;
    }

    private static void Defaults()
    {
        SetupSession session = new SetupSession();
        Check(session.Page == PageId.Overview, "Initial page must be overview.");
        Check(!session.IsBusy && !session.Consent && !session.LastRunSucceeded, "Initial state must not claim consent or completion.");
        Check(session.SelectionCount == ComponentCatalog.All.Count(item => item.Recommended), "Initial count differs from recommended profile.");
        Check(ComponentCatalog.All.Select(item => item.Id).Distinct().Count() == ComponentCatalog.All.Count, "Catalog IDs must be unique.");
        foreach (ComponentOption item in ComponentCatalog.All)
            Check(session.IsSelected(item.Id) == item.Recommended, "Default selection differs for " + item.Id);
        Check(session.BuildPlan().Revision == session.Revision, "Plan revision mismatch.");
    }

    private static void SelectionRevisions()
    {
        SetupSession session = Reviewed();
        int revision = session.Revision;
        session.Select("vc-modern", true);
        Check(session.Revision == revision && session.Consent, "No-op select must preserve reviewed revision.");
        session.Select("vc-modern", false);
        Check(session.Revision == revision + 1, "Actual edit must increment revision.");
        Check(!session.Consent && !session.LastRunSucceeded, "Actual edit must invalidate consent and prior completion.");
        session.SetConsent(true);
        session.Select("vc-modern", true);
        Check(!session.Consent, "Selecting an item again must require consent again.");
        Check(session.SelectionCount == ComponentCatalog.All.Count(item => item.Recommended), "Selection must not contain duplicates.");
    }

    private static void InvalidComponent()
    {
        SetupSession session = Reviewed();
        int revision = session.Revision;
        int count = session.SelectionCount;
        Check(Throws<ArgumentException>(delegate { session.Select("not-in-catalog", true); }), "Unknown ID must be rejected.");
        Check(Throws<ArgumentException>(delegate { session.Select(null, true); }), "Null ID must be rejected.");
        Check(session.Revision == revision && session.SelectionCount == count && session.Consent, "Rejected edit changed state.");
    }

    private static void Profiles()
    {
        SetupSession session = Reviewed();
        int revision = session.Revision;
        session.ApplyProfile(true);
        Check(session.SelectionCount == ComponentCatalog.All.Count, "Compatibility profile must select full catalog.");
        Check(session.Revision > revision && !session.Consent, "Profile application must invalidate review.");
        session.SetConsent(true);
        session.ApplyProfile(false);
        Check(session.SelectionCount == ComponentCatalog.All.Count(item => item.Recommended), "Recommended profile mismatch.");
        Check(!session.Consent, "Recommended profile must invalidate consent.");
        session.SetConsent(true);
        session.ClearSelection();
        Check(session.SelectionCount == 0 && !session.Consent, "Clear selection must invalidate review.");
    }

    private static void BeginGuards()
    {
        SetupSession session = new SetupSession();
        session.SetConsent(true);
        Check(session.BeginSimulation() == null && !session.IsBusy, "Overview must not begin a simulation.");
        session.Navigate(PageId.Review);
        session.SetConsent(false);
        Check(session.BeginSimulation() == null && !session.IsBusy, "Review without consent must not begin.");
        session.ClearSelection();
        session.SetConsent(true);
        Check(session.BeginSimulation() == null && !session.IsBusy, "Empty plan must not begin.");
        session.Select("xna", true);
        Check(session.BeginSimulation() == null, "Selection revision requires new consent.");
        session.SetConsent(true);
        SetupPlan plan = session.BeginSimulation();
        Check(plan != null && plan.Items.Count == 1 && plan.Items[0].Id == "xna", "Reviewed non-empty selection must begin exactly that plan.");
        Check(session.IsBusy && session.Page == PageId.Simulation && !session.LastRunSucceeded, "Begin state must be busy simulation, not success.");
    }

    private static void BusyGuards()
    {
        SetupSession session = Reviewed();
        SetupPlan plan = session.BeginSimulation();
        int revision = session.Revision;
        int count = session.SelectionCount;
        session.Select("vc-modern", false);
        session.ApplyProfile(true);
        session.ClearSelection();
        session.SetConsent(false);
        foreach (PageId page in Enum.GetValues(typeof(PageId)))
            Check(!session.Navigate(page), "Busy navigation accepted: " + page);
        Check(session.BeginSimulation() == null, "A second begin must not return another plan.");
        Check(session.Revision == revision && session.SelectionCount == count && session.Consent, "Busy edit mutated session.");
        Check(session.Page == PageId.Simulation && session.IsBusy, "Busy edits changed lifecycle.");
        Check(plan.Items.Select(item => item.Id).SequenceEqual(session.BuildPlan().Items.Select(item => item.Id)), "Busy session diverged from running snapshot.");
        session.EndSimulation(false);
    }

    private static void ImmutablePlan()
    {
        SetupSession session = new SetupSession();
        SetupPlan snapshot = session.BuildPlan();
        string[] ids = snapshot.Items.Select(item => item.Id).ToArray();
        int revision = snapshot.Revision;
        session.ClearSelection();
        session.Select("xna", true);
        Check(snapshot.Items.Select(item => item.Id).SequenceEqual(ids), "Session changes mutated an existing snapshot.");
        Check(snapshot.Revision == revision && snapshot.Revision != session.Revision, "Snapshot revision mutated.");
        IList<ComponentOption> items = snapshot.Items;
        Check(items.IsReadOnly, "Plan items must expose read-only collection.");
        Check(Throws<NotSupportedException>(delegate { items.Clear(); }), "Plan list accepted mutation.");
        List<ComponentOption> source = new List<ComponentOption> { ComponentCatalog.All[0] };
        SetupPlan copied = new SetupPlan(42, source);
        source.Clear();
        Check(copied.Items.Count == 1 && copied.Revision == 42, "Plan constructor must copy its input collection.");
    }

    private static void SuccessThenEdit()
    {
        SetupSession session = Reviewed();
        SetupPlan first = session.BeginSimulation();
        session.EndSimulation(true);
        Check(!session.IsBusy && session.Page == PageId.Result && session.LastRunSucceeded, "Successful run was not committed.");
        Check(session.Navigate(PageId.Components), "Back to components must work after completion.");
        session.Select("xna", true);
        Check(!session.LastRunSucceeded && !session.Consent, "Selection edit must invalidate old success and consent.");
        session.Navigate(PageId.Review);
        Check(session.BeginSimulation() == null, "Old consent must not authorize revised plan.");
        session.SetConsent(true);
        SetupPlan second = session.BeginSimulation();
        Check(second != null && second.Revision > first.Revision && second.Items.Any(item => item.Id == "xna"), "New run must use updated plan.");
        Check(!session.LastRunSucceeded, "Running a revised plan must not display old success.");
        session.EndSimulation(false);
    }

    private static void FailureThenRetry()
    {
        SetupSession session = Reviewed();
        SetupPlan first = session.BeginSimulation();
        session.EndSimulation(false);
        Check(!session.IsBusy && session.Page == PageId.Result && !session.LastRunSucceeded, "Failure must end busy state without success.");
        Check(session.BeginSimulation() == null, "Result page must not begin directly.");
        session.Navigate(PageId.Review);
        SetupPlan second = session.BeginSimulation();
        Check(second != null && second.Revision == first.Revision, "Retry of unchanged consented plan must be possible.");
        Check(second.Items.Select(item => item.Id).SequenceEqual(first.Items.Select(item => item.Id)), "Retry mutated reviewed selection.");
        session.EndSimulation(true);
        Check(session.LastRunSucceeded, "Verified retry success must be recorded.");
    }

    private static void EmptyThenSelect()
    {
        SetupSession session = Reviewed();
        session.ClearSelection(); session.SetConsent(true);
        Check(session.BeginSimulation() == null && !session.LastRunSucceeded, "Skipping every component must not manufacture success.");
        session.Navigate(PageId.Components);
        session.Select("vc-legacy", true);
        session.Navigate(PageId.Review);
        Check(!session.Consent && session.BeginSimulation() == null, "New selection after empty plan must require review consent.");
        session.SetConsent(true);
        SetupPlan plan = session.BeginSimulation();
        Check(plan != null && plan.Items.Count == 1 && plan.Items[0].Id == "vc-legacy", "Previously empty plan must not skip newly selected item.");
        session.EndSimulation(false);
    }

    private static void CompletionGuards()
    {
        SetupSession session = new SetupSession();
        int revision = session.Revision;
        session.EndSimulation(true);
        Check(session.Page == PageId.Overview && !session.LastRunSucceeded && session.Revision == revision, "Completion outside a run changed state.");
        session.Navigate(PageId.Review); session.SetConsent(true);
        session.BeginSimulation(); session.EndSimulation(false); session.EndSimulation(true);
        Check(!session.LastRunSucceeded, "Duplicate completion overwrote failure.");
    }

    private static void Navigation()
    {
        SetupSession session = new SetupSession();
        foreach (PageId page in new[] { PageId.Diagnostics, PageId.Components, PageId.Review, PageId.Overview })
            Check(session.Navigate(page) && session.Page == page, "Valid navigation rejected: " + page);
        Check(!session.Navigate(PageId.Simulation) && session.Page == PageId.Overview, "Simulation page cannot be reached outside begin.");
        Check(!session.Navigate(PageId.Result) && session.Page == PageId.Overview, "Result cannot be reached outside completion.");
    }

    private static void UndefinedNavigation()
    {
        SetupSession session = new SetupSession();
        Check(!session.Navigate((PageId)999) && session.Page == PageId.Overview, "Undefined page accepted into session.");
        Check(!session.Navigate((PageId)(-1)) && session.Page == PageId.Overview, "Negative page accepted into session.");
    }

    private static void Notifications()
    {
        SetupSession session = new SetupSession();
        int events = 0;
        session.Changed += delegate
        {
            events++;
            Check(!session.IsBusy || session.Page == PageId.Simulation, "Observer saw busy state outside simulation.");
        };
        session.Select("vc-modern", true);
        Check(events == 0, "No-op selection should not emit change.");
        session.Select("xna", true);
        session.Navigate(PageId.Review);
        session.SetConsent(true);
        Check(events == 3, "Expected one event for each committed edit.");
        session.BeginSimulation();
        int beforeBusyAttempts = events;
        session.ClearSelection(); session.ApplyProfile(true); session.SetConsent(false); session.Navigate(PageId.Overview);
        Check(events == beforeBusyAttempts, "Rejected busy actions emitted state changes.");
        session.EndSimulation(true);
        Check(events == beforeBusyAttempts + 1, "Completion should emit exactly once.");
    }

    private static void SimulationProgress()
    {
        SetupSession session = new SetupSession();
        session.ClearSelection(); session.Select("xna", true); session.Select("vc-legacy", true);
        SetupPlan plan = session.BuildPlan();
        List<PlanProgress> reports = new List<PlanProgress>();
        new SimulationRunner().RunAsync(plan, new ImmediateProgress(reports.Add), CancellationToken.None).GetAwaiter().GetResult();
        Check(reports.Count == plan.Items.Count + 1, "Expected progress per item plus explicit simulated completion.");
        for (int index = 0; index < reports.Count; index++)
        {
            Check(reports[index].Completed == index && reports[index].Total == plan.Items.Count, "Progress sequence/count mismatch.");
            Check(!string.IsNullOrWhiteSpace(reports[index].Message), "Progress detail missing.");
        }
        Check(reports.Last().Message.Contains("Nenhum componente foi instalado"), "Completion must disclose simulation.");
        Check(!session.LastRunSucceeded && !session.IsBusy, "Runner must not fabricate session state.");
    }

    private static void PreCancelledSimulation()
    {
        SetupPlan plan = new SetupPlan(1, new[] { ComponentCatalog.All[0] });
        List<PlanProgress> reports = new List<PlanProgress>();
        using (CancellationTokenSource cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            Check(Throws<OperationCanceledException>(delegate
            {
                new SimulationRunner().RunAsync(plan, new ImmediateProgress(reports.Add), cancel.Token).GetAwaiter().GetResult();
            }), "Pre-cancelled simulation completed instead of cancelling.");
        }
        Check(reports.Count == 0, "Pre-cancelled run emitted execution progress.");
    }

    private static void CancelledSimulation()
    {
        SetupPlan plan = new SetupPlan(1, new[] { ComponentCatalog.All[0], ComponentCatalog.All[1] });
        List<PlanProgress> reports = new List<PlanProgress>();
        using (CancellationTokenSource cancel = new CancellationTokenSource())
        {
            ImmediateProgress progress = new ImmediateProgress(delegate(PlanProgress item)
            {
                reports.Add(item); cancel.Cancel();
            });
            Check(Throws<OperationCanceledException>(delegate
            {
                new SimulationRunner().RunAsync(plan, progress, cancel.Token).GetAwaiter().GetResult();
            }), "Cancellation during first step was ignored.");
        }
        Check(reports.Count == 1 && reports[0].Completed == 0, "Cancellation allowed a later step or completion.");
    }

    private sealed class ImmediateProgress : IProgress<PlanProgress>
    {
        private readonly Action<PlanProgress> callback;
        public ImmediateProgress(Action<PlanProgress> callback) { this.callback = callback; }
        public void Report(PlanProgress value) { callback(value); }
    }
}
