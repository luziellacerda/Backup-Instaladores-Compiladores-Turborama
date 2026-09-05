using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TurboRama.Next;
namespace InstallerHost
{
    // Read-only progress map for the original five pages. It replaces boxed
    // labels with connected neon nodes without changing navigation or order.
    internal class WizardSequenceBar : Control
    {
        private readonly int currentStep;
        internal int CurrentStep { get { return currentStep; } }
        internal int StepCount { get { return ConsumerLayout.Steps.Length; } }
        internal WizardSequenceBar(int step)
        {
            currentStep = step; Name = "OriginalSequence"; Height = 48; Dock = DockStyle.Top;
            Margin = new Padding(0, 0, 0, 18); BackColor = Palette.Background; TabStop = false;
            AccessibleRole = AccessibleRole.StaticText; AccessibleName = "Etapas da instalação";
            AccessibleDescription = "Etapa " + (step + 1) + " de 5: " + ConsumerLayout.Steps[step];
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color color = SystemInformation.HighContrast ? SystemColors.Control : BackColor;
            using (Brush brush = new SolidBrush(color)) e.Graphics.FillRectangle(brush, Rectangle.Intersect(ClientRectangle, e.ClipRectangle));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            OnPaintBackground(e);
            if (Width < 10 || Height < 10) return;
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float dpi = graphics.DpiX / 96f;
            float segment = Width / (float)StepCount;
            float nodeY = 11f * dpi;
            float radius = Math.Max(7f, 8.5f * dpi);
            Color line = SystemInformation.HighContrast ? SystemColors.ControlDark : Palette.Line;
            Color complete = SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(170, Palette.Violet);
            Color active = SystemInformation.HighContrast ? SystemColors.Highlight : Palette.Accent;
            using (Pen baseLine = new Pen(line, Math.Max(1f, dpi)))
            using (Pen progress = new Pen(complete, Math.Max(1.5f, 2f * dpi)))
            {
                baseLine.StartCap = baseLine.EndCap = LineCap.Round;
                progress.StartCap = progress.EndCap = LineCap.Round;
                float first = segment * .5f, last = segment * (StepCount - .5f);
                graphics.DrawLine(baseLine, first, nodeY, last, nodeY);
                if (currentStep > 0) graphics.DrawLine(progress, first, nodeY, segment * (currentStep + .5f), nodeY);
            }
            for (int index = 0; index < StepCount; index++)
            {
                float centerX = segment * (index + .5f);
                RectangleF node = new RectangleF(centerX - radius, nodeY - radius, radius * 2, radius * 2);
                Color nodeColor = index < currentStep ? complete : index == currentStep ? active : line;
                if (!SystemInformation.HighContrast && index == currentStep)
                {
                    using (Brush glow = new SolidBrush(Color.FromArgb(28, active)))
                        graphics.FillEllipse(glow, RectangleF.Inflate(node, 6f * dpi, 6f * dpi));
                }
                using (Brush fill = new SolidBrush(index <= currentStep ? nodeColor : BackColor)) graphics.FillEllipse(fill, node);
                using (Pen edge = new Pen(nodeColor, Math.Max(1.2f, 1.6f * dpi))) graphics.DrawEllipse(edge, node);
                using (Font number = new Font("Segoe UI Semibold", 7.25f, FontStyle.Bold))
                {
                    string value = (index + 1).ToString("00");
                    Color numberColor = index <= currentStep ? Palette.Background : Palette.Muted;
                    TextRenderer.DrawText(graphics, value, number, Rectangle.Round(node),
                        SystemInformation.HighContrast ? SystemColors.ControlText : numberColor,
                        TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                }
                Rectangle labelBounds = new Rectangle((int)(segment * index), (int)(24 * dpi),
                    Math.Max(1, (int)segment), Math.Max(1, Height - (int)(24 * dpi)));
                using (Font label = new Font("Segoe UI Semibold", 9f, index == currentStep ? FontStyle.Bold : FontStyle.Regular))
                    TextRenderer.DrawText(graphics, ConsumerLayout.Steps[index], label, labelBounds,
                        index == currentStep ? active : Palette.Muted,
                        TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.Top |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
            }
            base.OnPaint(e);
        }
    }

    // One layout tree per original wizard page; no overlays or hidden old UI.
    internal static class ConsumerLayout
    {
        internal static readonly string[] Steps = { "Boas-vindas", "Licença", "Pré-requisitos", "Instalação", "Conclusão" };
        public static Panel Build(UserControl owner, int step, out Label heading, out FlowLayoutPanel actions)
        {
            owner.BackColor = Palette.Background; owner.ForeColor = Palette.Text; owner.Font = Ui.Font(10); owner.Dock = DockStyle.Fill;
            TableLayoutPanel root = new TableLayoutPanel { Name = "ConsumerLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                BackColor = Palette.Background, Padding = new Padding(28, 18, 28, 12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            TurboRamaArtwork brand = new TurboRamaArtwork(true) { Name = "TurboRamaBrand", Dock = DockStyle.Top,
                Height = 66, Margin = new Padding(0, 0, 0, 10) };
            root.Controls.Add(brand, 0, 0);
            WizardSequenceBar steps = new WizardSequenceBar(step);
            root.Controls.Add(steps, 0, 1);
            heading = Label(Steps[step], 25, true); heading.Name = "WizardHeading"; heading.Dock = DockStyle.Top;
            heading.Margin = new Padding(0, 0, 0, 18); root.Controls.Add(heading, 0, 2);
            Panel body = new Panel { Name = "WizardBody", Dock = DockStyle.Fill, BackColor = Palette.Background, Margin = Padding.Empty }; root.Controls.Add(body, 0, 3);
            TableLayoutPanel footer = new TableLayoutPanel { Name = "WizardActions", Dock = DockStyle.Top, AutoSize = true,
                ColumnCount = 2, Padding = new Padding(0, 18, 0, 0), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label publisher = Label("LZ Games e Informática", 9); publisher.Dock = DockStyle.Fill; publisher.ForeColor = Palette.Muted; publisher.TextAlign = ContentAlignment.MiddleLeft;
            actions = new FlowLayoutPanel { Name = "WizardButtons", AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill, Margin = Padding.Empty };
            footer.Controls.Add(publisher, 0, 0); footer.Controls.Add(actions, 1, 0); root.Controls.Add(footer, 0, 4); owner.Controls.Add(root); return body;
        }
        public static Button Action(string name, string text, bool primary = false)
        {
            ActionButton button = Ui.Button(name, text, primary); button.Width = primary ? 180 : 134;
            if (primary) button.TrailingArrow = true;
            else if (name.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0) { button.Appearance = ButtonAppearance.Quiet; button.Icon = Glyph.Close; }
            else if (name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0) button.Icon = Glyph.ArrowLeft;
            return button;
        }
        public static Label Label(string text, float size = 10, bool bold = false) { return Ui.Label(text, size, Palette.Text, bold); }
        public static void BindDefault(UserControl page, Button primary)
        {
            Action refresh = delegate { Form form = page.FindForm(); if (form != null && page.Visible) form.AcceptButton = primary.Enabled && primary.Visible ? primary : null; };
            page.VisibleChanged += delegate { refresh(); }; page.Load += delegate { refresh(); };
            primary.EnabledChanged += delegate { refresh(); }; primary.VisibleChanged += delegate { refresh(); };
        }
    }
    internal static class ConsumerText
    {
        internal static string GetString(string key, params object[] arguments)
        {
            switch (key)
            {
                case "Next >": return "Avançar";
                case "< Back": return "Voltar";
                case "Cancel": return "Cancelar";
                case "CancelSure": return "Tem certeza que deseja cancelar a instalação?";
                case "CancelButtonTitle": return "Cancelar instalação";
                case "Finish": return "Concluir";
                case "Welcome": return "Bem-vindo ao TurboRama.";
                case "WelcomeText": return "Prepare seu PC e instale o TurboRama.\r\n\r\nFeche outros programas e clique em Avançar.";
                case "LicenseIntro": return "Termos de licença";
                case "AgreeText": return "Li e aceito os termos da licença.";
                case "PrerequisiteIntro": return "Prepare seu computador.";
                case "All prerequisites installed": return "Os componentes necessários já foram detectados.";
                case "InstallTitle": return "Onde vamos instalar?";
                case "InstallInfo": return "Escolha a pasta de destino. A instalação começa somente quando você clicar em Instalar.";
                case "SelectFolder": return "PASTA DE INSTALAÇÃO";
                case "Browse...": return "Procurar";
                case "InstallFolderHint": return "Use uma pasta local vazia. O pacote será validado antes da extração; arquivos existentes não serão substituídos.";
                case "Install": return "Instalar";
                case "InstallComplete": return "Instalação concluída.";
                case "InstallCompleteDescription": return "O TurboRama foi instalado. Clique em Concluir para fechar este assistente e abra o programa pela pasta de instalação.";
                default: return Texts.GetString(key, arguments);
            }
        }
    }
}
