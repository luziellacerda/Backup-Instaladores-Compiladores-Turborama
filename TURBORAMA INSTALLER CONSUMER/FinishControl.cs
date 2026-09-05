using System;
using System.Windows.Forms;
namespace InstallerHost
{
    public partial class FinishControl : UserControl
    {
        public FinishControl(MainForm main, string path) : this(main, path, false) { }

        internal FinishControl(MainForm main, string path, bool dependenciesOnly)
        {
            InitializeComponent(); lblMessage.Text = ConsumerText.GetString("InstallComplete");
            lblWelcomeDesc.Text = ConsumerText.GetString("InstallCompleteDescription"); lblInstallPath.Text = path;
            if (dependenciesOnly)
            {
                lblMessage.Text = "Sessão de preparação encerrada.";
                lblComplete.Text = "Arquivos do TurboRama preservados.";
                lblWelcomeDesc.Text = "Esta sessão terminou sem instalar os arquivos do TurboRama. " +
                    "Somente as opções escolhidas na etapa Pré-requisitos foram processadas; se nenhuma foi selecionada, nada foi instalado. " +
                    "Isso não significa que todos os problemas do PC foram corrigidos nem garante compatibilidade com todos os jogos.";
                lblPathTitle.Visible = false;
                lblInstallPath.Visible = false;
            }
        }
        private void BtnFinish_Click(object sender, EventArgs e)
        {
            // Preserve the audited original: never launch the product elevated.
            Application.Exit();
        }
    }
}
