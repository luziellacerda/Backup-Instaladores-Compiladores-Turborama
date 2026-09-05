using System;
using System.Drawing;
using System.Windows.Forms;
using TurboRama.Next;
namespace InstallerHost
{
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
                Height = 78, Margin = new Padding(0, 0, 0, 12) };
            root.Controls.Add(brand, 0, 0);
            TableLayoutPanel steps = new TableLayoutPanel { Name = "OriginalSequence", ColumnCount = 5, RowCount = 1,
                Dock = DockStyle.Top, AutoSize = true, BackColor = Palette.Background, Margin = new Padding(0, 0, 0, 20) };
            for (int index = 0; index < Steps.Length; index++)
            {
                steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
                Label indicator = Label((index + 1).ToString("00") + "  " + Steps[index], 10, index == step);
                indicator.Name = "Step" + index; indicator.ForeColor = index == step ? Palette.Accent : Palette.Muted;
                indicator.BackColor = index == step ? Palette.Raised : Palette.Background;
                indicator.Padding = new Padding(8, 10, 8, 10); indicator.Dock = DockStyle.Fill;
                indicator.Margin = new Padding(0, 0, index == 4 ? 0 : 8, 0); steps.Controls.Add(indicator, index, 0);
            }
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
