using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TurboRama.Next
{
    public sealed class ReadinessPage : UserControl
    {
        private static readonly Color Background = Color.FromArgb(16, 18, 23);
        private static readonly Color Surface = Color.FromArgb(24, 27, 34);
        private static readonly Color Raised = Color.FromArgb(34, 38, 49);
        private static readonly Color TextColor = Color.FromArgb(242, 244, 248);
        private static readonly Color Muted = Color.FromArgb(165, 173, 185);
        private static readonly Color Accent = Color.FromArgb(185, 247, 99);
        private static readonly Color Violet = Color.FromArgb(182, 160, 255);
        private readonly Func<CancellationToken, Task<ReadinessSnapshot>> scan;
        private readonly FlowLayoutPanel rows;
        private readonly Label status;
        private readonly Button refresh;
        private readonly Button cancel;
        private readonly Font strongFont;
        private CancellationTokenSource activeScan;
        private int scanGeneration;
        private bool activateRequested;
        private bool disposingPage;

        public event EventHandler SnapshotChanged;
        public ReadinessSnapshot Snapshot { get; private set; }
        public bool IsScanning { get; private set; }

        public ReadinessPage(Func<CancellationToken, Task<ReadinessSnapshot>> scan)
        {
            if (scan == null) throw new ArgumentNullException("scan");
            this.scan = scan;
            Name = "ReadinessPage";
            AccessibleName = "Diagnóstico somente leitura do computador";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Background;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            strongFont = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            Size = new Size(1000, 380);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Name = "ReadinessLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            TableLayoutPanel tools = new TableLayoutPanel
            {
                Name = "ReadinessToolbar", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 14), Padding = Padding.Empty
            };
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status = NewLabel("A análise ainda não foi iniciada.", "ReadinessStatus", TextColor);
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Margin = new Padding(0, 0, 16, 0);
            tools.Controls.Add(status, 0, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Name = "ReadinessActions", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
                Anchor = AnchorStyles.Right, Margin = Padding.Empty, Padding = Padding.Empty
            };
            refresh = NewButton("Atualizar análise", "ReadinessRefresh", true);
            refresh.Click += delegate { StartScan(); };
            cancel = NewButton("Cancelar análise", "ReadinessCancel", false);
            cancel.Enabled = false;
            cancel.Visible = false;
            cancel.Click += delegate { CancelScan(); };
            actions.Controls.Add(refresh);
            actions.Controls.Add(cancel);
            tools.Controls.Add(actions, 1, 0);
            layout.Controls.Add(tools, 0, 0);

            rows = new FlowLayoutPanel
            {
                Name = "ReadinessRows", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, Margin = Padding.Empty,
                Padding = Padding.Empty, BackColor = Background, TabStop = false
            };
            rows.SizeChanged += delegate { StretchRows(); };
            layout.Controls.Add(rows, 0, 1);
            AddNotice("O que será verificado", "Windows, CPU, RAM, armazenamento, GPU e registros de componentes locais.",
                "Nenhum programa será instalado, nenhum ajuste será aplicado e nenhum arquivo será baixado.");

            Label note = NewLabel("Somente leitura · “Detectado” significa evidência local, não compatibilidade garantida com um jogo ou emulador.",
                "ReadinessDisclaimer", Muted);
            note.Dock = DockStyle.Fill;
            note.Margin = new Padding(0, 12, 0, 0);
            layout.Controls.Add(note, 0, 2);
        }

        public void Activate()
        {
            if (disposingPage || IsDisposed) return;
            activateRequested = true;
            if (!IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(Activate));
                return;
            }
            if (Snapshot == null && !IsScanning) StartScan();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (activateRequested && !disposingPage)
                BeginInvoke(new MethodInvoker(Activate));
        }

        private async void StartScan()
        {
            if (disposingPage || IsDisposed || !IsHandleCreated || IsScanning) return;
            CancellationTokenSource source = new CancellationTokenSource();
            activeScan = source;
            int generation = ++scanGeneration;
            SetScanning(true);
            status.Text = Snapshot == null ? "Analisando o PC, somente leitura…" : "Atualizando… os resultados anteriores continuam abaixo.";
            status.ForeColor = Violet;
            try
            {
                ReadinessSnapshot result = await scan(source.Token);
                if (!CanApply(generation, source)) return;
                if (result == null || result.Checks.Count == 0 || result.Checks.Any(item => item == null))
                    throw new InvalidOperationException("O diagnóstico não retornou itens verificáveis.");
                Snapshot = result;
                ShowSnapshot(result);
                EventHandler changed = SnapshotChanged;
                if (changed != null) changed(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                if (CanApply(generation, source))
                {
                    status.Text = "Análise cancelada. Nenhuma alteração foi feita no computador.";
                    status.ForeColor = Muted;
                }
            }
            catch (Exception)
            {
                if (CanApply(generation, source))
                {
                    status.Text = "Não foi possível concluir a análise. Você pode tentar novamente.";
                    status.ForeColor = Color.FromArgb(255, 202, 128);
                    if (Snapshot == null)
                    {
                        ClearRows();
                        AddNotice("Análise indisponível", "A consulta não retornou resultados confirmados.",
                            "Nenhum componente foi instalado. Use Atualizar análise para tentar novamente.");
                    }
                }
            }
            finally
            {
                if (ReferenceEquals(activeScan, source))
                {
                    activeScan = null;
                    if (!disposingPage && !IsDisposed) SetScanning(false);
                }
                source.Dispose();
            }
        }

        private bool CanApply(int generation, CancellationTokenSource source)
        {
            return !disposingPage && !IsDisposed && IsHandleCreated &&
                generation == scanGeneration && !source.IsCancellationRequested;
        }

        private void CancelScan()
        {
            CancellationTokenSource source = activeScan;
            if (source == null) return;
            ++scanGeneration;
            activeScan = null;
            source.Cancel();
            SetScanning(false);
            status.Text = Snapshot == null ? "Análise cancelada. Nenhuma alteração foi feita." : "Atualização cancelada. Resultados anteriores mantidos.";
            status.ForeColor = Muted;
        }

        private void SetScanning(bool scanning)
        {
            IsScanning = scanning;
            refresh.Enabled = !scanning;
            cancel.Enabled = scanning;
            cancel.Visible = scanning;
            refresh.Text = scanning ? "Analisando…" : "Atualizar análise";
        }

        private void ShowSnapshot(ReadinessSnapshot snapshot)
        {
            rows.SuspendLayout();
            try
            {
                ClearRows();
                int index = 0;
                foreach (ReadinessCheck check in snapshot.Checks)
                {
                    TableLayoutPanel row = CreateRow(check.Name, check.Detail, check.Action, StateText(check.State), StateColor(check.State));
                    row.Name = "ReadinessRow_" + index.ToString("00");
                    row.AccessibleName = check.Name + ": " + StateText(check.State);
                    rows.Controls.Add(row);
                    index++;
                }
                StretchRows();
                int detected = snapshot.Checks.Count(item => item.State == CheckState.Good);
                status.Text = snapshot.Checks.Count + (snapshot.Checks.Count == 1 ? " item consultado · " : " itens consultados · ") +
                    detected + (detected == 1 ? " detectado · " : " detectados · ") +
                    (snapshot.Checks.Count - detected) + " para conferir\r\n" +
                    "Atualizado às " + snapshot.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss");
                status.ForeColor = TextColor;
                rows.AutoScrollPosition = Point.Empty;
            }
            finally { rows.ResumeLayout(true); }
        }

        private void AddNotice(string title, string detail, string action)
        {
            TableLayoutPanel row = CreateRow(title, detail, action, "SEM ALTERAÇÕES", Violet);
            row.Name = "ReadinessNotice";
            rows.Controls.Add(row);
            StretchRows();
        }

        private TableLayoutPanel CreateRow(string title, string detail, string action, string stateText, Color stateColor)
        {
            TableLayoutPanel row = new TableLayoutPanel
            {
                ColumnCount = 1, RowCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Surface, ForeColor = TextColor, Padding = new Padding(16),
                Margin = new Padding(0, 0, 0, 10), TabStop = false
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < 3; index++) row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            TableLayoutPanel heading = new TableLayoutPanel
            {
                Name = "ReadinessRowHeader", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty
            };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label name = NewLabel(title, "ReadinessCheckName", TextColor);
            name.Font = strongFont;
            name.Dock = DockStyle.Fill;
            name.TextAlign = ContentAlignment.MiddleLeft;
            name.Margin = new Padding(0, 0, 12, 0);
            heading.Controls.Add(name, 0, 0);
            Label state = NewLabel(stateText, "ReadinessCheckState", stateColor);
            state.BackColor = Raised;
            state.Padding = new Padding(8, 4, 8, 4);
            state.Anchor = AnchorStyles.Right;
            heading.Controls.Add(state, 1, 0);
            row.Controls.Add(heading, 0, 0);
            Label evidence = NewLabel(detail, "ReadinessCheckDetail", TextColor);
            evidence.Dock = DockStyle.Fill;
            evidence.Margin = new Padding(0, 9, 0, 0);
            row.Controls.Add(evidence, 0, 1);
            Label next = NewLabel(action, "ReadinessCheckAction", Muted);
            next.Dock = DockStyle.Fill;
            next.Margin = new Padding(0, 5, 0, 0);
            row.Controls.Add(next, 0, 2);
            return row;
        }

        private void StretchRows()
        {
            if (rows == null || rows.IsDisposed) return;
            // Reserve the scrollbar consistently; content width never depends on
            // whether a previous row happened to trigger the scrollbar.
            int width = Math.Max(1, rows.ClientSize.Width - rows.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
            foreach (Control row in rows.Controls)
            {
                row.MinimumSize = new Size(width, 0);
                row.MaximumSize = new Size(width, 0);
                row.Width = width;
            }
        }

        private void ClearRows()
        {
            foreach (Control row in rows.Controls.Cast<Control>().ToArray())
            {
                rows.Controls.Remove(row);
                row.Dispose();
            }
        }

        private static Label NewLabel(string text, string name, Color color)
        {
            return new Label
            {
                Name = name, Text = text ?? string.Empty, AutoSize = true,
                ForeColor = color, BackColor = Color.Transparent, Margin = Padding.Empty,
                UseMnemonic = false
            };
        }

        private Button NewButton(string text, string name, bool primary)
        {
            ActionButton button = Ui.Button(name, text);
            button.Width = 214; button.Margin = new Padding(6, 0, 0, 0);
            button.Icon = primary ? Glyph.Refresh : Glyph.Close;
            button.Appearance = primary ? ButtonAppearance.Secondary : ButtonAppearance.Quiet;
            return button;
        }

        private static string StateText(CheckState state)
        {
            switch (state)
            {
                case CheckState.Good: return "DETECTADO";
                case CheckState.Warning: return "VERIFICAR";
                case CheckState.Missing: return "NÃO DETECTADO";
                default: return "SEM LEITURA";
            }
        }

        private static Color StateColor(CheckState state)
        {
            switch (state)
            {
                case CheckState.Good: return Accent;
                case CheckState.Warning: return Color.FromArgb(255, 202, 128);
                case CheckState.Missing: return Violet;
                default: return Muted;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposingPage)
            {
                disposingPage = true;
                IsScanning = false;
                ++scanGeneration;
                CancellationTokenSource source = activeScan;
                activeScan = null;
                if (source != null) source.Cancel();
                // A running operation owns its source until its finally block;
                // disposal cannot invalidate the token while the service uses it.
                strongFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
