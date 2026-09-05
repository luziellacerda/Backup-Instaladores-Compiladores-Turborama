using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TurboRama.Next
{
    public enum PageId { Overview, Diagnostics, Components, Review, Simulation, Result }

    public sealed class ComponentOption
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Detail { get; private set; }
        public string Group { get; private set; }
        public bool Recommended { get; private set; }
        public ComponentOption(string id, string title, string detail, string group, bool recommended)
        { Id = id; Title = title; Detail = detail; Group = group; Recommended = recommended; }
    }

    public static class ComponentCatalog
    {
        // These are planning options, not a claim that installers are bundled.
        public static readonly ReadOnlyCollection<ComponentOption> All = new List<ComponentOption>
        {
            new ComponentOption("vc-modern", "Visual C++", "Bibliotecas x64 e x86 usadas por jogos e emuladores modernos.", "ESSENCIAIS", true),
            new ComponentOption("dotnet-current", ".NET Desktop", "Dependências de aplicativos e launchers. A versão será validada por pacote.", "ESSENCIAIS", true),
            new ComponentOption("directx-legacy", "DirectX legado", "Bibliotecas adicionais de jogos antigos; não substitui o DirectX do Windows.", "COMPATIBILIDADE", true),
            new ComponentOption("webview", "WebView2", "Conteúdo web usado por interfaces e launchers compatíveis.", "ESSENCIAIS", true),
            new ComponentOption("vc-legacy", "Visual C++ clássico", "Versões 2005 a 2013 para títulos que dependem dessas bibliotecas.", "COMPATIBILIDADE", false),
            new ComponentOption("xna", "XNA Framework", "Componente opcional para jogos que exigem o XNA 4.0.", "COMPATIBILIDADE", false)
        }.AsReadOnly();
    }

    public sealed class SetupPlan
    {
        public int Revision { get; private set; }
        public ReadOnlyCollection<ComponentOption> Items { get; private set; }
        public SetupPlan(int revision, IEnumerable<ComponentOption> items)
        { Revision = revision; Items = items.ToList().AsReadOnly(); }
    }

    public sealed class SetupSession
    {
        private readonly HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
        public event EventHandler Changed;
        public int Revision { get; private set; }
        public bool IsBusy { get; private set; }
        public bool Consent { get; private set; }
        public bool LastRunSucceeded { get; private set; }
        public PageId Page { get; private set; }
        public SetupSession() { ApplyProfile(false); }
        public bool IsSelected(string id) { return selected.Contains(id); }
        public int SelectionCount { get { return selected.Count; } }
        public void Select(string id, bool value)
        {
            if (IsBusy) return;
            if (!ComponentCatalog.All.Any(item => item.Id == id)) throw new ArgumentException("Componente desconhecido.", "id");
            bool changed = value ? selected.Add(id) : selected.Remove(id);
            if (changed) InvalidatePlan();
        }
        public void ApplyProfile(bool compatibility)
        {
            if (IsBusy) return;
            selected.Clear();
            foreach (ComponentOption item in ComponentCatalog.All)
                if (item.Recommended || compatibility) selected.Add(item.Id);
            InvalidatePlan();
        }
        public void ClearSelection()
        {
            if (IsBusy) return;
            selected.Clear(); InvalidatePlan();
        }
        public void SetConsent(bool value)
        {
            if (IsBusy || Consent == value) return;
            Consent = value; RaiseChanged();
        }
        public bool Navigate(PageId page)
        {
            if (IsBusy || (page != PageId.Overview && page != PageId.Diagnostics &&
                page != PageId.Components && page != PageId.Review)) return false;
            Page = page; RaiseChanged(); return true;
        }
        public SetupPlan BuildPlan()
        { return new SetupPlan(Revision, ComponentCatalog.All.Where(item => selected.Contains(item.Id))); }
        public SetupPlan BeginSimulation()
        {
            if (IsBusy || Page != PageId.Review || !Consent || selected.Count == 0) return null;
            SetupPlan plan = BuildPlan();
            IsBusy = true; LastRunSucceeded = false; Page = PageId.Simulation; RaiseChanged();
            return plan;
        }
        public void EndSimulation(bool success)
        {
            if (!IsBusy) return;
            IsBusy = false; LastRunSucceeded = success; Page = PageId.Result; RaiseChanged();
        }
        private void InvalidatePlan()
        { Revision++; Consent = false; LastRunSucceeded = false; RaiseChanged(); }
        private void RaiseChanged() { var handler = Changed; if (handler != null) handler(this, EventArgs.Empty); }
    }

    public sealed class PlanProgress
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public string Message { get; set; }
    }
    public interface IPlanRunner
    {
        Task RunAsync(SetupPlan plan, IProgress<PlanProgress> progress, CancellationToken token);
    }
    public sealed class SimulationRunner : IPlanRunner
    {
        public async Task RunAsync(SetupPlan plan, IProgress<PlanProgress> progress, CancellationToken token)
        {
            for (int index = 0; index < plan.Items.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                progress.Report(new PlanProgress { Completed = index, Total = plan.Items.Count,
                    Message = "Simulando etapa: " + plan.Items[index].Title });
                await Task.Delay(650, token);
            }
            progress.Report(new PlanProgress { Completed = plan.Items.Count, Total = plan.Items.Count,
                Message = "Fluxo simulado. Nenhum componente foi instalado." });
        }
    }
}
