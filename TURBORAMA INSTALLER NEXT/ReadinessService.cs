using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace TurboRama.Next
{
    /// <summary>
    /// Collects local evidence only. It never starts processes, downloads files,
    /// opens browsers or writes to the registry. Detection is not certification
    /// that a particular game, emulator, driver or operating system is supported.
    /// </summary>
    public sealed class ReadinessService
    {
        public Task<ReadinessSnapshot> ScanAsync(CancellationToken token)
        {
            return Task.Run(delegate
            {
                token.ThrowIfCancellationRequested();
                ReadinessSnapshot result = new ReadinessSnapshot();
                Add(result, token, "Windows e arquitetura", DetectWindows);
                Add(result, token, "Processador", DetectCpu);
                Add(result, token, "Memória RAM", DetectMemory);
                Add(result, token, "Disco do Windows", DetectDisk);
                Add(result, token, "Adaptador de vídeo", DetectGraphics);
                Add(result, token, ".NET Framework", DetectFramework);
                Add(result, token, ".NET Desktop x64", delegate { return DetectDesktopRuntime(true, token); });
                Add(result, token, ".NET Desktop x86", delegate { return DetectDesktopRuntime(false, token); });
                Add(result, token, "Visual C++ x64", delegate { return DetectVisualCpp(true); });
                Add(result, token, "Visual C++ x86", delegate { return DetectVisualCpp(false); });
                Add(result, token, "Bibliotecas DirectX legadas", DetectLegacyDirectX);
                Add(result, token, "Microsoft Edge WebView2", DetectWebView);
                token.ThrowIfCancellationRequested();
                result.CapturedAtUtc = DateTime.UtcNow;
                return result;
            }, token);
        }

        private static void Add(ReadinessSnapshot result, CancellationToken token,
            string name, Func<ReadinessCheck> detect)
        {
            token.ThrowIfCancellationRequested();
            ReadinessCheck check;
            try
            {
                check = detect();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Permissions, unavailable registry views and native failures are
                // unknown evidence, never a claim that a prerequisite is absent.
                check = Evidence(CheckState.Unknown, "Não foi possível ler este item com segurança.",
                    "Confira este componente nas configurações do Windows.");
            }
            token.ThrowIfCancellationRequested();
            check.Name = name;
            result.Checks.Add(check);
        }

        private static ReadinessCheck DetectWindows()
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            string product = ReadMachineString(path, "ProductName", NativeView);
            string display = ReadMachineString(path, "DisplayVersion", NativeView);
            string build = ReadMachineString(path, "CurrentBuildNumber", NativeView);
            int buildNumber;
            if (int.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out buildNumber)
                && buildNumber >= 22000 && product.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                product = product.Replace("Windows 10", "Windows 11");
            }
            string arch = Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits";
            if (string.IsNullOrWhiteSpace(product) || string.IsNullOrWhiteSpace(build))
                return Evidence(CheckState.Unknown, "Windows " + arch + "; edição ou build não confirmadas.",
                    "Confira a versão em Configurações > Sistema > Sobre.");
            return Evidence(Environment.Is64BitOperatingSystem ? CheckState.Good : CheckState.Warning,
                product + (string.IsNullOrWhiteSpace(display) ? "" : " · " + display) + " · build " + build + " · " + arch,
                "Confira atualizações e os requisitos do jogo. Esta leitura não valida suporte do Windows.");
        }

        private static ReadinessCheck DetectCpu()
        {
            string name = ReadMachineString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", NativeView);
            if (string.IsNullOrWhiteSpace(name))
                return Evidence(CheckState.Unknown, Environment.ProcessorCount + " processadores lógicos; modelo não confirmado.",
                    "Veja o modelo no Gerenciador de Tarefas. Instruções como AVX2 não foram testadas.");
            return Evidence(CheckState.Good, name.Trim() + " · " + Environment.ProcessorCount + " processadores lógicos",
                "Compare o modelo com os requisitos do emulador. AVX2 e virtualização não foram testadas.");
        }

        private static ReadinessCheck DetectMemory()
        {
            MemoryStatus status = new MemoryStatus();
            status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            if (!GlobalMemoryStatusEx(ref status))
                return Evidence(CheckState.Unknown, "A quantidade de memória não foi confirmada.", "Confira a RAM no Gerenciador de Tarefas.");
            return Evidence(CheckState.Good, GiB(status.TotalPhysical) + " instalados · " + GiB(status.AvailablePhysical) + " disponíveis agora",
                "A memória disponível muda durante o uso. O mínimo necessário depende do jogo e do emulador.");
        }

        private static ReadinessCheck DetectDisk()
        {
            string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.IsNullOrWhiteSpace(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return Evidence(CheckState.Unknown, "A unidade local do Windows não foi confirmada.", "Confira o espaço da pasta que será usada para instalação.");
            DriveInfo drive = new DriveInfo(root);
            if (!drive.IsReady)
                return Evidence(CheckState.Unknown, "A unidade " + root + " não respondeu à consulta.", "Confira o armazenamento nas configurações do Windows.");
            return Evidence(CheckState.Good, root + " · " + GiB((ulong)Math.Max(0L, drive.AvailableFreeSpace)) + " livres",
                "Esta é a unidade do Windows, não uma reserva de espaço para instalar. O tamanho do produto ainda precisa ser conferido.");
        }

        private static ReadinessCheck DetectGraphics()
        {
            List<string> adapters = new List<string>();
            bool basicDriver = false;
            // Local API only; bounded enumeration avoids WMI service waits.
            for (uint index = 0; index < 16; index++)
            {
                DisplayDevice device = new DisplayDevice();
                device.Size = Marshal.SizeOf(typeof(DisplayDevice));
                if (!EnumDisplayDevices(null, index, ref device, 0)) break;
                if ((device.StateFlags & 8) != 0 || string.IsNullOrWhiteSpace(device.DeviceString)) continue;
                string description = device.DeviceString.Trim();
                if (description.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    description.IndexOf("Básic", StringComparison.OrdinalIgnoreCase) >= 0)
                    basicDriver = true;
                if (!adapters.Contains(description)) adapters.Add(description);
            }
            if (adapters.Count == 0)
                return Evidence(CheckState.Unknown, "Nenhum adaptador foi confirmado pela consulta local.",
                    "Confira a GPU e o driver no Gerenciador de Dispositivos.");
            return Evidence(basicDriver ? CheckState.Warning : CheckState.Good, string.Join("; ", adapters),
                "Modelo detectado; desempenho e suporte a Vulkan, OpenGL ou Direct3D não foram testados. Use o driver oficial da GPU.");
        }

        private static ReadinessCheck DetectFramework()
        {
            string releaseText = ReadMachineString(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", RegistryView.Registry32);
            int release;
            if (!int.TryParse(releaseText, out release))
                return Evidence(CheckState.Unknown, "A versão do .NET Framework 4.x não foi confirmada no Registro.",
                    "Confira as dependências do aplicativo. .NET Framework e .NET Desktop são produtos diferentes.");
            string version = release >= 533320 ? "4.8.1 ou posterior" : release >= 528040 ? "4.8" : release >= 461808 ? "4.7.2" : "4.x";
            return Evidence(CheckState.Good, ".NET Framework " + version + " · release " + release,
                "A versão detectada não substitui a verificação dos requisitos específicos do aplicativo.");
        }

        private static ReadinessCheck DetectDesktopRuntime(bool x64, CancellationToken token)
        {
            if (x64 && !Environment.Is64BitOperatingSystem)
                return Evidence(CheckState.Warning, "O Windows de 32 bits não executa runtimes x64.",
                    "Use aplicativos compatíveis com a arquitetura do Windows.");
            string architecture = x64 ? "x64" : "x86";
            SortedSet<string> versions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string registryPath = @"SOFTWARE\dotnet\Setup\InstalledVersions\" + architecture + @"\sharedfx\Microsoft.WindowsDesktop.App";
            foreach (RegistryView view in RegistryViews())
            {
                token.ThrowIfCancellationRequested();
                using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey key = machine.OpenSubKey(registryPath, false))
                {
                    if (key == null) continue;
                    foreach (string name in key.GetValueNames().Take(64))
                    {
                        Version parsed;
                        if (Version.TryParse(name, out parsed)) versions.Add(name);
                    }
                }
            }
            // A missing registry entry alone does not rule out a standard local installation.
            string programFiles = x64 ? ReadMachineString(@"SOFTWARE\Microsoft\Windows\CurrentVersion", "ProgramFilesDir", RegistryView.Registry64) :
                Environment.GetFolderPath(Environment.Is64BitOperatingSystem ? Environment.SpecialFolder.ProgramFilesX86 : Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles) && Path.IsPathRooted(programFiles) && !programFiles.StartsWith(@"\\", StringComparison.Ordinal))
            {
                string folder = Path.Combine(programFiles, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                if (Directory.Exists(folder))
                {
                    foreach (string item in Directory.EnumerateDirectories(folder).Take(64))
                    {
                        token.ThrowIfCancellationRequested();
                        string name = Path.GetFileName(item);
                        Version parsed;
                        if (Version.TryParse(name, out parsed)) versions.Add(name);
                    }
                }
            }
            if (versions.Count == 0)
                return Evidence(CheckState.Missing, "Nenhuma instalação " + architecture + " confirmada nos locais padrão.",
                    "Confira a versão exigida pelo aplicativo. Instalações portáteis ou em outra pasta não são detectadas aqui.");
            return Evidence(CheckState.Good, architecture + " · versões encontradas: " + string.Join(", ", versions),
                "Versões diferentes podem coexistir. A presença de uma versão não confirma a dependência de outro aplicativo.");
        }

        private static ReadinessCheck DetectVisualCpp(bool x64)
        {
            if (x64 && !Environment.Is64BitOperatingSystem)
                return Evidence(CheckState.Warning, "O Windows de 32 bits não executa componentes x64.", "Use a versão x86 quando exigida pelo aplicativo.");
            string arch = x64 ? "x64" : "x86";
            string path = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\" + arch;
            foreach (RegistryView view in RegistryViews())
            {
                if (ReadMachineString(path, "Installed", view) != "1") continue;
                string version = ReadMachineString(path, "Version", view);
                return Evidence(CheckState.Good, "Visual C++ 14.x " + arch + (string.IsNullOrWhiteSpace(version) ? " · registrado" : " · " + version),
                    "A detecção cobre a família 14.x. Jogos antigos podem exigir outras famílias de Visual C++.");
            }
            return Evidence(CheckState.Missing, "Visual C++ 14.x " + arch + " não confirmado no Registro.",
                "Confira os pré-requisitos do aplicativo antes de instalar o redistribuível oficial Microsoft.");
        }

        private static ReadinessCheck DetectLegacyDirectX()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] libraries = { "D3DX9_43.dll", "XInput1_3.dll", "XAudio2_7.dll" };
            // A 32-bit host must use Sysnative to inspect the actual x64 files;
            // otherwise Windows silently redirects System32 to SysWOW64.
            string nativeSystem = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32";
            string[] folders = Environment.Is64BitOperatingSystem ? new[] { nativeSystem, "SysWOW64" } : new[] { "System32" };
            List<string> missing = new List<string>();
            foreach (string folder in folders)
                foreach (string library in libraries)
                    if (!File.Exists(Path.Combine(windows, folder, library))) missing.Add(folder + "/" + library);
            if (missing.Count > 0)
                return Evidence(CheckState.Missing, missing.Count + " de " + (libraries.Length * folders.Length) + " bibliotecas consultadas não foram localizadas.",
                    "Só jogos que exigem estas bibliotecas precisam do pacote legado. Isso não mede a versão do DirectX da GPU.");
            return Evidence(CheckState.Good, "As " + (libraries.Length * folders.Length) + " bibliotecas legadas consultadas estão presentes.",
                "Leitura de presença, sem teste de execução ou autenticidade. DirectX 11/12 e recursos da GPU são outra verificação.");
        }

        private static ReadinessCheck DetectWebView()
        {
            const string path = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C72B079829}";
            foreach (RegistryView view in RegistryViews())
            {
                foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey key = root.OpenSubKey(path, false))
                    {
                        string version = key == null ? null : Convert.ToString(key.GetValue("pv", null, RegistryValueOptions.DoNotExpandEnvironmentNames), CultureInfo.InvariantCulture);
                        Version parsed;
                        if (Version.TryParse(version, out parsed) && parsed.Major > 0)
                            return Evidence(CheckState.Good, "WebView2 Runtime · " + version,
                                "Componente de interface web; não é requisito universal de jogos ou emuladores.");
                    }
                }
            }
            return Evidence(CheckState.Missing, "WebView2 Runtime não confirmado nos registros padrão.",
                "Instale somente se o aplicativo exigir. Distribuições fixas incluídas no próprio aplicativo não são verificadas.");
        }

        private static RegistryView NativeView
        {
            get { return Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32; }
        }

        private static IEnumerable<RegistryView> RegistryViews()
        {
            if (Environment.Is64BitOperatingSystem) yield return RegistryView.Registry64;
            yield return RegistryView.Registry32;
        }

        private static string ReadMachineString(string path, string name, RegistryView view)
        {
            using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (RegistryKey key = machine.OpenSubKey(path, false))
                return key == null ? string.Empty : Convert.ToString(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static ReadinessCheck Evidence(CheckState state, string detail, string action)
        {
            return new ReadinessCheck { State = state, Detail = detail, Action = action };
        }

        private static string GiB(ulong bytes)
        {
            return (bytes / 1073741824.0).ToString("0.0", CultureInfo.CurrentCulture) + " GiB";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(string device, uint number, ref DisplayDevice displayDevice, uint flags);
    }
}
