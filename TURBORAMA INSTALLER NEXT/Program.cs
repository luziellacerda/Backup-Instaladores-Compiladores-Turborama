using System;
using System.Reflection;
using System.Windows.Forms;
[assembly: AssemblyTitle("TurboRama Next — prévia de interface")]
[assembly: AssemblyDescription("Projeto novo. Diagnóstico somente leitura e simulação; não instala componentes.")]
[assembly: AssemblyCompany("LZ Games e Informática")]
[assembly: AssemblyProduct("TurboRama Next Preview")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
namespace TurboRama.Next
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            { MessageBox.Show("A prévia encontrou um erro: " + e.Exception.Message + "\r\nNenhuma instalação real é executada nesta versão.", "TurboRama Next", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            Application.Run(new ShellForm(new SetupSession(), new ReadinessService().ScanAsync, new SimulationRunner()));
        }
    }
}
