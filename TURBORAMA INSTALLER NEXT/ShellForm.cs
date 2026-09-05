using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TurboRama.Next
{
    public sealed class ShellForm : Form
    {
        private readonly SetupSession session;
        private readonly IPlanRunner runner;
        private readonly Dictionary<PageId, UserControl> pages = new Dictionary<PageId, UserControl>();
        private readonly List<ActionButton> navigation = new List<ActionButton>();
        private readonly Panel content;
        private readonly Label heading;
        private readonly Label subtitle;
        private readonly Label status;
        private readonly ActionButton primary;
        private readonly ActionButton back;
        private readonly ReadinessPage diagnostics;
        private readonly ComponentsPage components;
        private readonly ReviewPage review;
        private readonly ExecutionPage execution;
        private readonly ResultPage result;
        private CancellationTokenSource running;
        private int runGeneration;
        private PageId? renderedPage;

        public ShellForm(SetupSession state, Func<CancellationToken, Task<ReadinessSnapshot>> scan, IPlanRunner planRunner)
        {
            session = state; runner = planRunner;
            Name = "TurboRamaNextShell"; Text = "TurboRama Next — PRÉVIA / não instala componentes";
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = Ui.Font(10); BackColor = Palette.Background; ForeColor = Palette.Text;
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(1120, 720);
            MinimumSize = new Size(780, 580); KeyPreview = true; DoubleBuffered = true;
            TableLayoutPanel shell = new TableLayoutPanel { Name = "ApplicationLayout", Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = 4, BackColor = Palette.Background, Padding = new Padding(28, 12, 28, 8) };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(shell);
            TableLayoutPanel brand = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill,
                AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Label logo = Ui.Label("TURBORAMA  /  NEXT", 17, Palette.Text, true); logo.Margin = new Padding(0, 4, 0, 6);
            Label badge = Ui.Label("●  LAB 01     ·     SEM INSTALAÇÃO", 9, Palette.Accent, true);
            badge.Dock = DockStyle.Fill; badge.TextAlign = ContentAlignment.MiddleRight;
            brand.Controls.Add(logo, 0, 0); brand.Controls.Add(badge, 1, 0); shell.Controls.Add(brand, 0, 0);
            TableLayoutPanel menu = new TableLayoutPanel { Name = "MainNavigation", Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 18), Padding = Padding.Empty };
            for (int column = 0; column < 4; column++) menu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            menu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            string[] titles = { "01   Visão geral", "02   Diagnóstico", "03   Componentes", "04   Revisar plano" };
            for (int index = 0; index < titles.Length; index++)
            {
                PageId target = (PageId)index;
                ActionButton button = Ui.Button("Navigate" + target, titles[index]); button.Dock = DockStyle.Fill;
                button.Margin = new Padding(0, 0, index == 3 ? 0 : 12, 6);
                button.Click += delegate { NavigateTo(target); }; menu.Controls.Add(button, index, 0); navigation.Add(button);
            }
            shell.Controls.Add(menu, 0, 1);
            TableLayoutPanel stage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            stage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stage.RowStyles.Add(new RowStyle(SizeType.AutoSize)); stage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heading = Ui.Label("", 23, Palette.Text, true); heading.Name = "PageHeading"; heading.Dock = DockStyle.Top;
            subtitle = Ui.Label("", 10, Palette.Muted); subtitle.Dock = DockStyle.Top; subtitle.Margin = new Padding(0, 0, 0, 16);
            content = new Panel { Name = "PageHost", Dock = DockStyle.Fill, Margin = Padding.Empty };
            stage.Controls.Add(heading, 0, 0); stage.Controls.Add(subtitle, 0, 1); stage.Controls.Add(content, 0, 2); shell.Controls.Add(stage, 0, 2);
            TableLayoutPanel footer = new TableLayoutPanel { Name = "ActionBar", ColumnCount = 2, AutoSize = true,
                Dock = DockStyle.Fill, Padding = new Padding(0, 18, 0, 8), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status = Ui.Label("", 9, Palette.Muted); status.Name = "SessionStatus"; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
            back = Ui.Button("PreviousPage", "Voltar"); back.Width = 110; back.Click += delegate { PreviousPage(); };
            primary = Ui.Button("PrimaryAction", "Começar", true); primary.Width = 214; primary.Margin = Padding.Empty;
            primary.Click += async delegate { await PrimaryActionAsync(); };
            actions.Controls.Add(back); actions.Controls.Add(primary); footer.Controls.Add(status, 0, 0); footer.Controls.Add(actions, 1, 0); shell.Controls.Add(footer, 0, 3);
            diagnostics = new ReadinessPage(scan); components = new ComponentsPage(session); review = new ReviewPage(session);
            execution = new ExecutionPage(); result = new ResultPage();
            pages.Add(PageId.Overview, new OverviewPage(NavigateTo, delegate(bool classic) { session.ApplyProfile(classic); NavigateTo(PageId.Components); }));
            pages.Add(PageId.Diagnostics, diagnostics); pages.Add(PageId.Components, components); pages.Add(PageId.Review, review);
            pages.Add(PageId.Simulation, execution); pages.Add(PageId.Result, result);
            foreach (UserControl page in pages.Values) { page.Dock = DockStyle.Fill; page.Visible = false; content.Controls.Add(page); }
            session.Changed += SessionChanged;
            Shown += delegate { FitToScreen(); Render(); };
            Render();
        }
        public SetupSession Session { get { return session; } }
        public void NavigateTo(PageId target)
        {
            if (!session.Navigate(target)) return;
            if (target == PageId.Diagnostics) diagnostics.Activate();
        }
        private void SessionChanged(object sender, EventArgs e) { if (!IsDisposed) Render(); }
        private void Render()
        {
            bool changed = renderedPage != session.Page;
            if (changed)
            {
                foreach (var entry in pages) entry.Value.Visible = entry.Key == session.Page;
                pages[session.Page].BringToFront(); renderedPage = session.Page;
            }
            if (session.Page == PageId.Components) components.RefreshSelection();
            if (session.Page == PageId.Review) review.RefreshPlan();
            string[] headings = { "Seu setup, do seu jeito.", "Seu hardware, sem adivinhação.", "Escolha o que faz sentido.", "Revise antes de continuar.", "Cada etapa, à vista.", "Resultado do teste." };
            string[] descriptions = { "Uma nova central de preparação para jogos e emulação.", "Informações reais. O que não for confirmado aparece como não confirmado.",
                "A seleção é sua. Nenhuma instalação acontece ao marcar uma opção.", "Confira sua seleção e os limites desta prévia.", "Modo de teste: o computador não está sendo modificado.", "Simulação não equivale a instalação ou certificação do Windows." };
            heading.Text = headings[(int)session.Page]; subtitle.Text = descriptions[(int)session.Page];
            heading.Visible = session.Page != PageId.Overview;
            subtitle.Visible = session.Page != PageId.Overview;
            for (int index = 0; index < navigation.Count; index++)
            {
                navigation[index].Enabled = !session.IsBusy;
                navigation[index].Selected = index == (int)session.Page;
                navigation[index].Invalidate();
            }
            back.Visible = session.Page != PageId.Overview && session.Page != PageId.Simulation;
            back.Enabled = !session.IsBusy;
            string[] actions = { "Analisar meu PC  →", "Escolher componentes  →", "Revisar meu plano  →", "Simular plano  →", "Simulando...", "Revisar seleção  →" };
            primary.Text = actions[(int)session.Page]; primary.AccessibleName = primary.Text;
            primary.Enabled = !session.IsBusy && (session.Page != PageId.Review || (session.Consent && session.SelectionCount > 0));
            status.Text = session.IsBusy ? "SIMULAÇÃO EM ANDAMENTO\nAguarde a conclusão das etapas." :
                session.SelectionCount + " grupos no plano  ·  revisão " + session.Revision + "\nPrévia sem assinatura digital. Não é uma release final.";
            // The shell exclusively owns keyboard commands. No page may retain the old AcceptButton.
            AcceptButton = primary.Enabled ? primary : null;
            CancelButton = null;
            if (changed && !session.IsBusy && IsHandleCreated) pages[session.Page].SelectNextControl(null, true, true, true, false);
        }
        private void PreviousPage()
        {
            if (session.IsBusy) return;
            NavigateTo(session.Page == PageId.Result ? PageId.Review : (PageId)Math.Max(0, (int)session.Page - 1));
        }
        public async Task PrimaryActionAsync()
        {
            if (session.IsBusy) return;
            if (session.Page == PageId.Review) { await SimulateAsync(); return; }
            if (session.Page == PageId.Result) { NavigateTo(PageId.Components); return; }
            if ((int)session.Page < 3) NavigateTo((PageId)((int)session.Page + 1));
        }
        private async Task SimulateAsync()
        {
            SetupPlan plan = session.BeginSimulation(); if (plan == null) return;
            execution.ResetProgress();
            CancellationTokenSource source = new CancellationTokenSource();
            running = source;
            int generation = ++runGeneration;
            bool success = false; string message = "";
            try
            {
                Progress<PlanProgress> progress = new Progress<PlanProgress>(delegate(PlanProgress step)
                { if (!IsDisposed && session.IsBusy && generation == runGeneration && ReferenceEquals(running, source)) execution.Report(step); });
                await runner.RunAsync(plan, progress, source.Token);
                success = true; message = plan.Items.Count + " etapas percorridas na simulação. Você pode voltar, alterar a seleção e testar novamente.";
            }
            catch (OperationCanceledException) { message = "A simulação foi cancelada. Nenhuma instalação foi executada."; }
            catch (Exception error) { message = "Falha no teste: " + error.Message + "\nVolte ao plano para tentar novamente."; }
            finally { source.Dispose(); if (ReferenceEquals(running, source)) running = null; }
            if (!IsDisposed) { result.SetResult(success, message); session.EndSimulation(success); }
        }
        private void FitToScreen()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Fail closed for every close path: X, Alt+F4, menu, and programmatic close.
            if (session.IsBusy) { e.Cancel = true; status.Text = "Aguarde a simulação terminar para fechar."; return; }
            base.OnFormClosing(e);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (session.IsBusy && (keyData == Keys.Escape || keyData == Keys.Enter || keyData == (Keys.Alt | Keys.Left))) return true;
            if (keyData == (Keys.Alt | Keys.Left)) { PreviousPage(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                session.Changed -= SessionChanged;
                ++runGeneration;
                if (running != null) { running.Cancel(); session.EndSimulation(false); }
            }
            base.Dispose(disposing);
        }
    }
}
