using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TurboramaRomLinker
{
    internal static class Program
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

        [STAThread]
        private static void Main()
        {
            try
            {
                // Ajuda o Windows a usar o ícone correto na barra de tarefas e no Menu Iniciar.
                SetCurrentProcessExplicitAppUserModelID("LZGames.Turborama.RomLinker");
            }
            catch
            {
                // Se a API não estiver disponível, o programa continua abrindo normalmente.
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.ToString(), "Erro inesperado", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                MessageBox.Show(ex != null ? ex.ToString() : "Erro fatal desconhecido.", "Erro fatal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new MainForm());
        }
    }
}
