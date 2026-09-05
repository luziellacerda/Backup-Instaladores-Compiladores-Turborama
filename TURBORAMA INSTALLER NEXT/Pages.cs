using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TurboRama.Next
{
    internal sealed class OverviewPage : UserControl
    {
        public OverviewPage(Action<PageId> navigate, Action<bool> profile)
        {
            BackColor = Palette.Background; Dock = DockStyle.Fill;
            FlowLayoutPanel stack = Ui.Stack(); Controls.Add(stack); Ui.FillStackWidth(stack);
            TableLayoutPanel hero = new TableLayoutPanel { Name = "OverviewHero", ColumnCount = 2,
                Height = 292, BackColor = Palette.Surface, Margin = new Padding(0, 0, 0, 18), Padding = new Padding(24) };
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            TableLayoutPanel copy = Ui.Vertical(); copy.Dock = DockStyle.Fill;
            Ui.AddRow(copy, Ui.Label("TURBORAMA  /  NOVA EXPERIÊNCIA", 9, Palette.Accent, true));
            Ui.AddRow(copy, Ui.Label("Seu próximo jogo\ncomeça aqui.", 29, Palette.Text, true));
            Label description = Ui.Label("Conheça seu PC. Escolha os componentes.\nConfira cada etapa antes de instalar.", 11, Palette.Muted);
            Ui.AddRow(copy, description);
            ActionButton scan = Ui.Button("OverviewScan", "Analisar meu PC", true);
            scan.Icon = Glyph.Scan; scan.TrailingArrow = true; scan.Width = 248;
            scan.Click += delegate { navigate(PageId.Diagnostics); };
            scan.Dock = DockStyle.None; scan.Margin = new Padding(0, 8, 0, 0);
            copy.RowStyles.Add(new RowStyle(SizeType.AutoSize)); copy.Controls.Add(scan, 0, copy.RowCount++);
            hero.Controls.Add(copy, 0, 0);
            hero.Controls.Add(new CoreArtwork { Dock = DockStyle.Fill, Margin = new Padding(0) }, 1, 0);
            stack.Controls.Add(hero);
            Label caption = Ui.Label("UM PLANO QUE COMBINA COM VOCÊ", 9, Palette.Muted, true);
            stack.Controls.Add(caption);
            TableLayoutPanel profiles = new TableLayoutPanel { ColumnCount = 2, Height = 110,
                BackColor = Palette.Background, Margin = new Padding(0, 0, 0, 12) };
            profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            profiles.Controls.Add(ProfileCard("01", "PC moderno", "Essenciais para o seu novo setup.", delegate { profile(false); }), 0, 0);
            profiles.Controls.Add(ProfileCard("02", "Do clássico ao atual", "Inclui opções de compatibilidade.", delegate { profile(true); }), 1, 0);
            stack.Controls.Add(profiles);
        }
        private Control ProfileCard(string number, string title, string detail, Action selected)
        {
            ActionButton choose = Ui.Button("Profile" + number, title);
            choose.Appearance = ButtonAppearance.Compound; choose.Description = detail;
            choose.Icon = number == "01" ? Glyph.Monitor : Glyph.Gamepad;
            choose.Dock = DockStyle.Fill; choose.Font = new Font("Segoe UI Semibold", 11.5f);
            choose.Margin = new Padding(0, 0, number == "01" ? 12 : 0, 0);
            choose.AccessibleName = "Selecionar perfil " + title; choose.Click += delegate { selected(); };
            return choose;
        }
    }

    internal sealed class ComponentsPage : UserControl
    {
        private readonly SetupSession session;
        private readonly Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, Control> cards = new Dictionary<string, Control>();
        private readonly Label count;
        private readonly Dictionary<string, ActionButton> filters = new Dictionary<string, ActionButton>();
        private string activeFilter = "";
        private bool refreshing;
        public ComponentsPage(SetupSession state)
        {
            session = state; Dock = DockStyle.Fill; BackColor = Palette.Background;
            TableLayoutPanel page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            ActionButton essentials = Ui.Button("SelectEssentials", "Essenciais"); essentials.Width = 170; essentials.Icon = Glyph.Spark;
            essentials.Click += delegate { session.ApplyProfile(false); RefreshSelection(); };
            ActionButton legacy = Ui.Button("SelectCompatibility", "Compatibilidade"); legacy.Width = 222; legacy.Icon = Glyph.Gamepad;
            legacy.Click += delegate { session.ApplyProfile(true); RefreshSelection(); };
            ActionButton clear = Ui.Button("ClearSelection", "Limpar seleção"); clear.Width = 178; clear.Appearance = ButtonAppearance.Quiet; clear.Icon = Glyph.Close;
            clear.Click += delegate { session.ClearSelection(); RefreshSelection(); };
            toolbar.Controls.AddRange(new Control[] { essentials, legacy, clear }); page.Controls.Add(toolbar, 0, 0);
            FlowLayoutPanel filterBar = new FlowLayoutPanel { Name = "ComponentFilters", Dock = DockStyle.Top, AutoSize = true,
                WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            Label filterLabel = Ui.Label("EXIBIR", 9, Palette.Muted, true); filterLabel.Margin = new Padding(6, 15, 18, 0);
            filterBar.Controls.Add(filterLabel);
            AddFilter(filterBar, "FilterAll", "Todos", "", 120);
            AddFilter(filterBar, "FilterEssentials", "Essenciais", "ESSENCIAIS", 156);
            AddFilter(filterBar, "FilterCompatibility", "Compatibilidade", "COMPATIBILIDADE", 200);
            page.Controls.Add(filterBar, 0, 1);
            count = Ui.Label("", 10, Palette.Muted); page.Controls.Add(count, 0, 2);
            FlowLayoutPanel rows = Ui.Stack(); Ui.FillStackWidth(rows); page.Controls.Add(rows, 0, 3);
            foreach (ComponentOption item in ComponentCatalog.All)
            {
                TableLayoutPanel row = new TableLayoutPanel { Name = "ComponentRow_" + item.Id, ColumnCount = 1,
                    Height = 106, Padding = new Padding(20, 10, 20, 10), BackColor = Palette.Surface, Margin = new Padding(0, 0, 0, 10) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                CheckBox check = new CheckBox { Name = "Select_" + item.Id, AccessibleName = item.Title,
                    Text = item.Title + (item.Recommended ? "   /   ESSENCIAL" : "   /   OPCIONAL"),
                    AutoSize = true, Dock = DockStyle.Top, ForeColor = Palette.Text, Font = Ui.Font(11, true),
                    Padding = new Padding(0, 2, 0, 3), Margin = new Padding(0, 0, 0, 4) };
                string captured = item.Id;
                check.CheckedChanged += delegate { if (!refreshing) { session.Select(captured, check.Checked); RefreshSelection(); } };
                Label detail = Ui.Label(item.Detail, 10, Palette.Muted); detail.Dock = DockStyle.Top;
                row.Controls.Add(check, 0, 0); row.Controls.Add(detail, 0, 1);
                row.RowStyles.Add(new RowStyle(SizeType.AutoSize)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                checks.Add(item.Id, check); cards.Add(item.Id, row); rows.Controls.Add(row);
            }
            Controls.Add(page); RefreshSelection();
        }
        public void RefreshSelection()
        {
            refreshing = true;
            try { foreach (var item in checks) item.Value.Checked = session.IsSelected(item.Key); }
            finally { refreshing = false; }
            count.Text = session.SelectionCount + " de " + ComponentCatalog.All.Count + " grupos selecionados. Nesta prévia, nenhum pacote é baixado ou instalado.";
        }
        private void AddFilter(FlowLayoutPanel host, string name, string title, string group, int width)
        {
            ActionButton button = Ui.Button(name, title); button.Appearance = ButtonAppearance.Navigation;
            button.Size = new Size(width, 44); button.Selected = group == activeFilter;
            button.AccessibleName = "Exibir " + title.ToLowerInvariant();
            button.AccessibleDescription = "Filtra a lista sem alterar os componentes selecionados.";
            button.Click += delegate { activeFilter = group; ApplyFilter(); };
            filters.Add(group, button); host.Controls.Add(button);
        }
        private void ApplyFilter()
        {
            foreach (var filter in filters) filter.Value.Selected = filter.Key == activeFilter;
            foreach (ComponentOption item in ComponentCatalog.All)
                cards[item.Id].Visible = activeFilter.Length == 0 || item.Group == activeFilter;
        }
    }

    internal sealed class ReviewPage : UserControl
    {
        private readonly SetupSession session;
        private readonly Label items;
        private readonly CheckBox agree;
        private bool refreshing;
        public ReviewPage(SetupSession state)
        {
            session = state; Dock = DockStyle.Fill; BackColor = Palette.Background;
            FlowLayoutPanel stack = Ui.Stack(); Ui.FillStackWidth(stack); Controls.Add(stack);
            TableLayoutPanel summary = Ui.Vertical(); summary.BackColor = Palette.Surface; summary.Padding = new Padding(24);
            Ui.AddRow(summary, Ui.Label("O QUE ESTÁ NO SEU PLANO", 9, Palette.Accent, true));
            items = Ui.Label("", 12, Palette.Text); items.Name = "PlanItems"; Ui.AddRow(summary, items);
            stack.Controls.Add(summary);
            TableLayoutPanel safety = Ui.Vertical(); safety.BackColor = Palette.Surface; safety.Padding = new Padding(24); safety.Margin = new Padding(0, 14, 0, 14);
            Ui.AddRow(safety, Ui.Label("Você continua no controle.", 16, Palette.Text, true));
            Ui.AddRow(safety, Ui.Label("Esta é uma avaliação da interface nova. A simulação percorre as etapas do plano sem instalar, baixar arquivos, alterar o Registro ou pedir acesso de administrador.", 11, Palette.Muted));
            Ui.AddRow(safety, Ui.Label("A entrega final depende de testes em Windows limpo e validação dos instaladores e alertas. Esta prévia não é uma versão aprovada para produção.", 10, Palette.Warning));
            stack.Controls.Add(safety);
            agree = new CheckBox { Name = "PreviewConsent", Text = "Entendi: vou testar uma simulação, não uma instalação real.",
                AutoSize = true, Font = Ui.Font(11), ForeColor = Palette.Text, Margin = new Padding(4, 8, 0, 14) };
            agree.CheckedChanged += delegate { if (!refreshing) session.SetConsent(agree.Checked); }; stack.Controls.Add(agree);
            RefreshPlan();
        }
        public void RefreshPlan()
        {
            SetupPlan plan = session.BuildPlan();
            items.Text = plan.Items.Count == 0 ? "Nenhum componente selecionado. Volte para montar seu plano." :
                string.Join(Environment.NewLine, plan.Items.Select((item, index) => (index + 1).ToString("00") + "    " + item.Title));
            refreshing = true; agree.Checked = session.Consent; refreshing = false;
        }
    }

    internal sealed class ExecutionPage : UserControl
    {
        private readonly Label current;
        private readonly Label percent;
        private readonly ProgressBar bar;
        private readonly ListBox history;
        public ExecutionPage()
        {
            Dock = DockStyle.Fill; BackColor = Palette.Background;
            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(24), BackColor = Palette.Surface };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            percent = Ui.Label("0%", 32, Palette.Accent, true); percent.Name = "SimulationPercent";
            current = Ui.Label("Preparando a simulação...", 14, Palette.Text, true);
            bar = new ProgressBar { Name = "SimulationProgress", Dock = DockStyle.Top, Height = 12, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 10, 0, 20) };
            content.Controls.Add(percent, 0, 0); content.Controls.Add(current, 0, 1); content.Controls.Add(bar, 0, 2);
            content.Controls.Add(Ui.Label("SIMULAÇÃO  /  Nenhuma alteração é feita no computador.", 10, Palette.Muted), 0, 3);
            history = new ListBox { Name = "SimulationHistory", Dock = DockStyle.Fill, Font = Ui.Font(11),
                BorderStyle = BorderStyle.None, BackColor = Palette.Surface, ForeColor = Palette.Muted, HorizontalScrollbar = true };
            content.Controls.Add(history, 0, 4); Controls.Add(content);
        }
        public void ResetProgress() { history.Items.Clear(); bar.Value = 0; percent.Text = "0%"; current.Text = "Preparando a simulação..."; }
        public void Report(PlanProgress progress)
        {
            int value = progress.Total == 0 ? 0 : Math.Max(0, Math.Min(100, progress.Completed * 100 / progress.Total));
            bar.Value = value; percent.Text = value + "%"; current.Text = progress.Message;
            history.Items.Add(progress.Message); history.TopIndex = Math.Max(0, history.Items.Count - 1);
        }
    }

    internal sealed class ResultPage : UserControl
    {
        private readonly Label title;
        private readonly Label detail;
        public ResultPage()
        {
            Dock = DockStyle.Fill; BackColor = Palette.Background;
            FlowLayoutPanel stack = Ui.Stack(); Ui.FillStackWidth(stack); Controls.Add(stack);
            TableLayoutPanel content = Ui.Vertical(); content.Padding = new Padding(30); content.BackColor = Palette.Surface;
            Ui.AddRow(content, Ui.Label("FIM DO TESTE DE FLUXO", 10, Palette.Violet, true));
            title = Ui.Label("Simulação concluída.", 28, Palette.Text, true); Ui.AddRow(content, title);
            detail = Ui.Label("", 12, Palette.Muted); Ui.AddRow(content, detail);
            Ui.AddRow(content, Ui.Label("Nenhum componente foi instalado.\nIsso não confirma que jogos ou emuladores funcionarão neste PC.", 11, Palette.Warning));
            stack.Controls.Add(content);
        }
        public void SetResult(bool success, string message)
        {
            title.Text = success ? "Simulação concluída." : "A simulação foi interrompida.";
            detail.Text = message;
        }
    }
}
