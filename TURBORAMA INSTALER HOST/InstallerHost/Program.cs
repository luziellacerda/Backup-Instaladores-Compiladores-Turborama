using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace InstallerHost
{
	// Token: 0x0200000F RID: 15
	internal static class Program
	{
		// Token: 0x0600005D RID: 93
		[DllImport("user32.dll")]
		private static extern bool SetProcessDPIAware();

		// Token: 0x0600005E RID: 94 RVA: 0x000087E0 File Offset: 0x000069E0
		[STAThread]
		private static void Main(string[] args)
		{
			Program.SetProcessDPIAware();
			AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs resolveArgs)
			{
				if (new AssemblyName(resolveArgs.Name).Name == "ICSharpCode.SharpZipLib")
				{
					Assembly executingAssembly = Assembly.GetExecutingAssembly();
					string text = "InstallerHost.resources.ICSharpCode.SharpZipLib.dll";
					using (Stream manifestResourceStream = executingAssembly.GetManifestResourceStream(text))
					{
						if (manifestResourceStream == null)
						{
							return null;
						}
						byte[] array = new byte[manifestResourceStream.Length];
						manifestResourceStream.Read(array, 0, array.Length);
						return Assembly.Load(array);
					}
				}
				return null;
			};
			CultureInfo cultureInfo = CultureInfo.CurrentUICulture;
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (args[i].Equals("-lang", StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						cultureInfo = new CultureInfo(args[i + 1]);
						Thread.CurrentThread.CurrentCulture = cultureInfo;
						Thread.CurrentThread.CurrentUICulture = cultureInfo;
						Logger.Log("Langue forcée : " + cultureInfo.Name);
					}
					catch (CultureNotFoundException)
					{
						Logger.Log("Langue non reconnue : " + args[i + 1] + " — langue système conservée.");
					}
				}
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			try
			{
				Logger.Log("Running Turborama Installer.");
				Application.Run(new MainForm());
			}
			catch (Exception ex)
			{
				Logger.Log("Fatal startup error: " + ex.ToString());
				MessageBox.Show(Texts.GetString("StartupError", Array.Empty<object>()), Texts.GetString("Error", Array.Empty<object>()), MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
	}
}
