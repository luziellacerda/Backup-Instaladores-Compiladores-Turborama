using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace InstallerHost
{
	internal interface IInstallerProcessTermination
	{
		void Kill();
		bool WaitForExit(int milliseconds);
	}

	internal enum TimedOutProcessDisposition
	{
		ConfirmedExited,
		QuarantineRequired
	}

	/// <summary>
	/// Starts the approved command suspended, assigns it to a kill-on-close Job
	/// Object, and only then resumes its first thread. Descendants created normally
	/// therefore join the job before any vendor code can escape the process tree.
	/// </summary>
	internal sealed class InstallerProcessJob : IInstallerProcessTermination, IDisposable
	{
		private const uint CreateSuspended = 0x00000004;
		private const uint CreateNoWindow = 0x08000000;
		private const uint JobObjectLimitKillOnJobClose = 0x00002000;
		private const int JobObjectBasicAccountingInformationClass = 1;
		private const int JobObjectExtendedLimitInformationClass = 9;
		private const uint WaitObject0 = 0x00000000;
		private const uint WaitTimeout = 0x00000102;
		private const uint WaitFailed = 0xFFFFFFFF;
		private const uint ResumeThreadFailed = 0xFFFFFFFF;
		private const uint ForcedInstallerExitCode = 0xC0E10001;

		private readonly SafeKernelObjectHandle jobHandle;
		private readonly SafeKernelObjectHandle processHandle;
		private readonly List<FileStream> protectedFiles;
		private readonly bool processAssignedToJob;
		private bool disposed;

		private InstallerProcessJob(
			SafeKernelObjectHandle jobHandle,
			SafeKernelObjectHandle processHandle,
			List<FileStream> protectedFiles,
			bool processAssignedToJob)
		{
			this.jobHandle = jobHandle;
			this.processHandle = processHandle;
			this.protectedFiles = protectedFiles;
			this.processAssignedToJob = processAssignedToJob;
		}

		internal static InstallerProcessJob Start(
			string executablePath,
			string arguments,
			string workingDirectory,
			IEnumerable<string> filesToProtect)
		{
			if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathRooted(executablePath))
				throw new ArgumentException("Executável absoluto ausente.", "executablePath");
			if (string.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathRooted(workingDirectory))
				throw new ArgumentException("Diretório de trabalho absoluto ausente.", "workingDirectory");
			string fullExecutablePath = Path.GetFullPath(executablePath);
			string fullWorkingDirectory = Path.GetFullPath(workingDirectory);
			if (fullExecutablePath.IndexOf('"') >= 0 || fullWorkingDirectory.IndexOf('"') >= 0)
				throw new InvalidDataException("Caminho do processo contém aspas.");

			List<FileStream> leases = OpenProtectedFiles(filesToProtect);
			SafeKernelObjectHandle job = null;
			SafeKernelObjectHandle process = null;
			SafeKernelObjectHandle thread = null;
			bool processCreated = false;
			bool processAssigned = false;
			try
			{
				job = CreateJobObject(IntPtr.Zero, null);
				if (job == null || job.IsInvalid)
					throw NewWin32Exception("Não foi possível criar o controle de processos do instalador.");

				JobObjectExtendedLimitInformation limits = new JobObjectExtendedLimitInformation();
				limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
				int limitsSize = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation));
				IntPtr limitsPointer = Marshal.AllocHGlobal(limitsSize);
				try
				{
					Marshal.StructureToPtr(limits, limitsPointer, false);
					if (!SetInformationJobObject(
						job, JobObjectExtendedLimitInformationClass, limitsPointer, (uint)limitsSize))
						throw NewWin32Exception("Não foi possível ativar o encerramento seguro da árvore de processos.");
				}
				finally
				{
					Marshal.FreeHGlobal(limitsPointer);
				}

				StartupInfo startup = new StartupInfo();
				startup.Size = Marshal.SizeOf(typeof(StartupInfo));
				ProcessInformation information;
				StringBuilder commandLine = new StringBuilder(
					QuoteCommandLineArgument(fullExecutablePath) +
					(string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments));
				if (!CreateProcess(
					fullExecutablePath,
					commandLine,
					IntPtr.Zero,
					IntPtr.Zero,
					false,
					CreateSuspended | CreateNoWindow,
					IntPtr.Zero,
					fullWorkingDirectory,
					ref startup,
					out information))
					throw NewWin32Exception("O processo verificado não pôde ser criado suspenso.");
				processCreated = true;
				process = new SafeKernelObjectHandle(information.ProcessHandle, true);
				thread = new SafeKernelObjectHandle(information.ThreadHandle, true);

				if (!AssignProcessToJobObject(job, process))
					throw NewWin32Exception(
						"O processo suspenso não pôde ser vinculado ao controle de processos; nenhum código do instalador foi executado.");
				processAssigned = true;
				uint previousSuspendCount = ResumeThread(thread);
				if (previousSuspendCount == ResumeThreadFailed)
					throw NewWin32Exception("O processo protegido não pôde ser iniciado.");
				if (previousSuspendCount != 1)
					throw new InvalidOperationException(
						"A thread inicial permaneceu suspensa em um estado inesperado; nenhum instalador será aguardado como se estivesse em execução.");

				thread.Dispose();
				thread = null;
				InstallerProcessJob result = new InstallerProcessJob(job, process, leases, true);
				job = null;
				process = null;
				leases = null;
				return result;
			}
			catch (Exception startError)
			{
				if (processCreated)
				{
					bool exitConfirmed = false;
					try
					{
						if (processAssigned && job != null && !job.IsInvalid)
						{
							TerminateJobObject(job, ForcedInstallerExitCode);
							exitConfirmed = WaitForAssignedJobEmpty(job, process, 10000);
						}
						else if (process != null && !process.IsInvalid)
						{
							TerminateProcess(process, ForcedInstallerExitCode);
							exitConfirmed = WaitForSingleObject(process, 10000) == WaitObject0;
						}
					}
					catch
					{
						exitConfirmed = false;
					}

					if (!exitConfirmed && process != null && !process.IsInvalid)
					{
						// Ownership is transferred before throwing. An unassigned process is
						// still suspended here, so vendor code has not executed; the manager
						// retries termination and blocks every later installation.
						if (thread != null)
						{
							thread.Dispose();
							thread = null;
						}
						InstallerProcessJob quarantined =
							new InstallerProcessJob(job, process, leases, processAssigned);
						job = null;
						process = null;
						leases = null;
						InstallerProcessQuarantine.Register(
							quarantined,
							"inicialização protegida do instalador",
							false);
						throw new InvalidOperationException(
							"O processo suspenso não pôde ser encerrado com confirmação. Ele foi colocado em quarentena; " +
							"nenhum código do instalador foi executado e nenhuma nova instalação será iniciada nesta sessão.",
							startError);
					}
				}
				throw;
			}
			finally
			{
				if (thread != null) thread.Dispose();
				if (process != null) process.Dispose();
				if (job != null) job.Dispose();
				DisposeFiles(leases);
			}
		}

		public void Kill()
		{
			ThrowIfDisposed();
			try
			{
				if (WaitForExit(0)) return;
			}
			catch
			{
				// Query failure is not exit evidence; still attempt the appropriate
				// job/process termination primitive below.
			}
			bool terminated = processAssignedToJob
				? TerminateJobObject(jobHandle, ForcedInstallerExitCode)
				: TerminateProcess(processHandle, ForcedInstallerExitCode);
			if (!terminated)
			{
				int terminationError = Marshal.GetLastWin32Error();
				// Termination can race with a natural exit. Re-query before treating
				// the API error as an uncertain live process.
				try
				{
					if (WaitForExit(0)) return;
				}
				catch
				{
				}
				throw NewWin32Exception(
					terminationError,
					"O Windows recusou o encerramento da árvore do instalador.");
			}
		}

		public bool WaitForExit(int milliseconds)
		{
			ThrowIfDisposed();
			if (milliseconds < 0) throw new ArgumentOutOfRangeException("milliseconds");
			if (processAssignedToJob)
				return WaitForAssignedJobEmpty(jobHandle, processHandle, milliseconds);

			uint wait = WaitForSingleObject(processHandle, (uint)milliseconds);
			if (wait == WaitTimeout) return false;
			if (wait == WaitFailed)
				throw NewWin32Exception("Não foi possível aguardar a árvore de processos do instalador.");
			if (wait != WaitObject0)
				throw new InvalidOperationException("Resultado inesperado ao aguardar o controle de processos: " + wait + ".");
			return true;
		}

		internal int GetExitCode()
		{
			ThrowIfDisposed();
			uint exitCode;
			if (!GetExitCodeProcess(processHandle, out exitCode))
				throw NewWin32Exception("Não foi possível obter o resultado do instalador.");
			return unchecked((int)exitCode);
		}

		internal uint GetActiveProcessCount()
		{
			ThrowIfDisposed();
			if (!processAssignedToJob)
				return WaitForSingleObject(processHandle, 0) == WaitObject0 ? 0U : 1U;
			return QueryAccounting().ActiveProcesses;
		}

		internal static int[] GetNativeLayoutForTest()
		{
			return new[]
			{
				Marshal.SizeOf(typeof(StartupInfo)),
				Marshal.SizeOf(typeof(ProcessInformation)),
				Marshal.SizeOf(typeof(JobObjectBasicLimitInformation)),
				Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation)),
				Marshal.SizeOf(typeof(JobObjectBasicAccountingInformation))
			};
		}

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			// KILL_ON_JOB_CLOSE is a final backstop if the owner disposes early.
			jobHandle.Dispose();
			processHandle.Dispose();
			DisposeFiles(protectedFiles);
		}

		private JobObjectBasicAccountingInformation QueryAccounting()
		{
			JobObjectBasicAccountingInformation accounting;
			if (!TryQueryAccounting(jobHandle, out accounting))
				throw NewWin32Exception("Não foi possível confirmar os processos ativos do instalador.");
			return accounting;
		}

		private static bool TryQueryAccounting(
			SafeKernelObjectHandle job,
			out JobObjectBasicAccountingInformation accounting)
		{
			int size = Marshal.SizeOf(typeof(JobObjectBasicAccountingInformation));
			return QueryInformationJobObject(
				job,
				JobObjectBasicAccountingInformationClass,
				out accounting,
				(uint)size,
				IntPtr.Zero);
		}

		private static bool WaitForAssignedJobEmpty(
			SafeKernelObjectHandle job,
			SafeKernelObjectHandle primaryProcess,
			int milliseconds)
		{
			Stopwatch elapsed = Stopwatch.StartNew();
			for (;;)
			{
				JobObjectBasicAccountingInformation accounting;
				if (!TryQueryAccounting(job, out accounting))
					throw NewWin32Exception("Não foi possível confirmar os processos ativos do instalador.");
				if (accounting.ActiveProcesses == 0) return true;
				int remaining = milliseconds - (int)Math.Min(milliseconds, elapsed.ElapsedMilliseconds);
				if (remaining <= 0) return false;
				int slice = Math.Min(100, remaining);
				uint wait = WaitForSingleObject(primaryProcess, (uint)slice);
				if (wait == WaitFailed)
					throw NewWin32Exception("Não foi possível aguardar o processo principal do instalador.");
				if (wait != WaitObject0 && wait != WaitTimeout)
					throw new InvalidOperationException("Resultado inesperado ao aguardar o processo principal: " + wait + ".");
				// A signaled primary handle returns immediately while descendants can
				// remain active. Avoid a tight loop until job accounting reaches zero.
				if (wait == WaitObject0) Thread.Sleep(slice);
			}
		}

		private static List<FileStream> OpenProtectedFiles(IEnumerable<string> paths)
		{
			List<FileStream> leases = new List<FileStream>();
			try
			{
				foreach (string path in (paths ?? Enumerable.Empty<string>())
					.Where(item => !string.IsNullOrWhiteSpace(item))
					.Select(Path.GetFullPath)
					.Distinct(StringComparer.OrdinalIgnoreCase))
				{
					leases.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
				}
				return leases;
			}
			catch
			{
				DisposeFiles(leases);
				throw;
			}
		}

		private static void DisposeFiles(IEnumerable<FileStream> files)
		{
			if (files == null) return;
			foreach (FileStream file in files)
			{
				try { file.Dispose(); }
				catch { }
			}
		}

		private static string QuoteCommandLineArgument(string value)
		{
			// Executable paths were already rejected when they contain quotes and do
			// not end in a directory separator, so ordinary enclosing quotes preserve
			// argv[0] without rewriting valid Windows path separators.
			return "\"" + value + "\"";
		}

		private void ThrowIfDisposed()
		{
			if (disposed) throw new ObjectDisposedException("InstallerProcessJob");
		}

		private static Win32Exception NewWin32Exception(string message)
		{
			int error = Marshal.GetLastWin32Error();
			return NewWin32Exception(error, message);
		}

		private static Win32Exception NewWin32Exception(int error, string message)
		{
			return new Win32Exception(error, message + " Código " + error + ".");
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct StartupInfo
		{
			internal int Size;
			internal IntPtr Reserved;
			internal IntPtr Desktop;
			internal IntPtr Title;
			internal int X;
			internal int Y;
			internal int XSize;
			internal int YSize;
			internal int XCountChars;
			internal int YCountChars;
			internal int FillAttribute;
			internal int Flags;
			internal short ShowWindow;
			internal short Reserved2;
			internal IntPtr Reserved2Pointer;
			internal IntPtr StandardInput;
			internal IntPtr StandardOutput;
			internal IntPtr StandardError;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct ProcessInformation
		{
			internal IntPtr ProcessHandle;
			internal IntPtr ThreadHandle;
			internal uint ProcessId;
			internal uint ThreadId;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct IoCounters
		{
			internal ulong ReadOperationCount;
			internal ulong WriteOperationCount;
			internal ulong OtherOperationCount;
			internal ulong ReadTransferCount;
			internal ulong WriteTransferCount;
			internal ulong OtherTransferCount;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct JobObjectBasicLimitInformation
		{
			internal long PerProcessUserTimeLimit;
			internal long PerJobUserTimeLimit;
			internal uint LimitFlags;
			internal UIntPtr MinimumWorkingSetSize;
			internal UIntPtr MaximumWorkingSetSize;
			internal uint ActiveProcessLimit;
			internal UIntPtr Affinity;
			internal uint PriorityClass;
			internal uint SchedulingClass;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct JobObjectExtendedLimitInformation
		{
			internal JobObjectBasicLimitInformation BasicLimitInformation;
			internal IoCounters IoInfo;
			internal UIntPtr ProcessMemoryLimit;
			internal UIntPtr JobMemoryLimit;
			internal UIntPtr PeakProcessMemoryUsed;
			internal UIntPtr PeakJobMemoryUsed;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct JobObjectBasicAccountingInformation
		{
			internal long TotalUserTime;
			internal long TotalKernelTime;
			internal long ThisPeriodTotalUserTime;
			internal long ThisPeriodTotalKernelTime;
			internal uint TotalPageFaultCount;
			internal uint TotalProcesses;
			internal uint ActiveProcesses;
			internal uint TotalTerminatedProcesses;
		}

		private sealed class SafeKernelObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
		{
			private SafeKernelObjectHandle() : base(true) { }
			internal SafeKernelObjectHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle)
			{
				SetHandle(handle);
			}

			protected override bool ReleaseHandle()
			{
				return CloseHandle(handle);
			}
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern SafeKernelObjectHandle CreateJobObject(IntPtr jobAttributes, string name);

		[DllImport("kernel32.dll", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern bool SetInformationJobObject(
			SafeKernelObjectHandle job,
			int informationClass,
			IntPtr information,
			uint informationLength);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern bool CreateProcess(
			string applicationName,
			StringBuilder commandLine,
			IntPtr processAttributes,
			IntPtr threadAttributes,
			bool inheritHandles,
			uint creationFlags,
			IntPtr environment,
			string currentDirectory,
			ref StartupInfo startupInfo,
			out ProcessInformation processInformation);

		[DllImport("kernel32.dll", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern bool AssignProcessToJobObject(
			SafeKernelObjectHandle job,
			SafeKernelObjectHandle process);

		[DllImport("kernel32.dll", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern uint ResumeThread(SafeKernelObjectHandle thread);

		[DllImport("kernel32.dll", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern bool TerminateJobObject(SafeKernelObjectHandle job, uint exitCode);

		[DllImport("kernel32.dll", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern bool TerminateProcess(SafeKernelObjectHandle process, uint exitCode);

		[DllImport("kernel32.dll", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern uint WaitForSingleObject(SafeKernelObjectHandle handle, uint milliseconds);

		[DllImport("kernel32.dll", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern bool QueryInformationJobObject(
			SafeKernelObjectHandle job,
			int informationClass,
			out JobObjectBasicAccountingInformation information,
			uint informationLength,
			IntPtr returnLength);

		[DllImport("kernel32.dll", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern bool GetExitCodeProcess(SafeKernelObjectHandle process, out uint exitCode);

		[DllImport("kernel32.dll", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern bool CloseHandle(IntPtr handle);
	}

	/// <summary>
	/// Owns an installer job whose empty state could not be proven. The UI thread is
	/// released, but cleanup and every new installation remain blocked. A process
	/// that never ran vendor code may be released after its job reports zero active
	/// processes. Every installer timeout remains quarantined for the host lifetime,
	/// because an EXE or MSI can delegate work to a service outside this job.
	/// </summary>
	internal static class InstallerProcessQuarantine
	{
		private static readonly object Sync = new object();
		private static readonly List<QuarantineEntry> Entries = new List<QuarantineEntry>();
		private static readonly List<SecureInstallerStaging> DeferredStaging =
			new List<SecureInstallerStaging>();
		private static bool prerequisiteCleanupDeferred;

		internal static void ThrowIfInstallationBlocked()
		{
			string reason = GetBlockReason();
			if (reason != null) throw new InvalidOperationException(reason);
		}

		internal static string GetBlockReason()
		{
			lock (Sync)
			{
				if (Entries.Count == 0) return null;
				QuarantineEntry entry = Entries[0];
				return "Uma execução anterior de " + entry.Label +
					" terminou sem confirmação completa da árvore de processos. Os arquivos permanecem em quarentena e " +
					"nenhuma nova instalação será iniciada nesta sessão. " +
					(entry.PersistentForHost
						? "Aguarde qualquer operação do instalador terminar e reinicie o TurboRama antes de tentar novamente."
						: "Aguarde o encerramento em segundo plano ou reinicie o TurboRama.");
			}
		}

		internal static void Register(
			InstallerProcessJob process,
			string label,
			bool persistentForHost)
		{
			if (process == null) throw new ArgumentNullException("process");
			if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Rótulo ausente.", "label");
			QuarantineEntry entry = new QuarantineEntry(process, label, persistentForHost);
			lock (Sync)
			{
				Entries.Add(entry);
			}
			try
			{
				// Persistent entries are monitored too: repeated termination attempts
				// contain a still-running tracked tree. Once it reaches zero, the entry
				// remains retained to cover work delegated outside the Job Object.
				if (!ThreadPool.QueueUserWorkItem(delegate { Monitor(entry); }))
					Logger.Log("No background worker was available for installer quarantine; it will remain retained for this host session.");
			}
			catch (Exception error)
			{
				// The entry already owns the job and file handles. Keeping it for the
				// host lifetime is safer than undoing the transfer without exit proof.
				Logger.Log("Installer quarantine monitor could not start; retention remains active: " + error.Message);
			}
		}

		internal static bool TryDeferStagingCleanup(SecureInstallerStaging staging)
		{
			if (staging == null) throw new ArgumentNullException("staging");
			lock (Sync)
			{
				if (Entries.Count == 0) return false;
				if (!DeferredStaging.Contains(staging)) DeferredStaging.Add(staging);
				return true;
			}
		}

		internal static bool DeferPrerequisiteCleanupIfRequired()
		{
			lock (Sync)
			{
				if (Entries.Count == 0) return false;
				prerequisiteCleanupDeferred = true;
				return true;
			}
		}

		internal static int GetActiveCountForTest()
		{
			lock (Sync) return Entries.Count;
		}

		private static void Monitor(QuarantineEntry entry)
		{
			bool failureLogged = false;
			for (;;)
			{
				try
				{
					entry.Process.Kill();
					if (entry.Process.WaitForExit(1000))
					{
						if (entry.PersistentForHost)
						{
							Logger.Log(
								"Tracked installer tree stopped; quarantine remains retained for delegated work: " +
								entry.Label + ".");
						}
						else
						{
							Release(entry);
						}
						return;
					}
				}
				catch (Exception error)
				{
					if (!failureLogged)
					{
						Logger.Log("Quarantine could not yet confirm job completion for '" + entry.Label + "': " + error.Message);
						failureLogged = true;
					}
					Thread.Sleep(1000);
				}
			}
		}

		private static void Release(QuarantineEntry entry)
		{
			List<SecureInstallerStaging> staging = null;
			bool cleanupPrerequisites = false;
			lock (Sync)
			{
				if (!Entries.Remove(entry)) return;
				if (Entries.Count == 0)
				{
					staging = new List<SecureInstallerStaging>(DeferredStaging);
					DeferredStaging.Clear();
					cleanupPrerequisites = prerequisiteCleanupDeferred;
					prerequisiteCleanupDeferred = false;
				}
			}

			entry.Process.Dispose();
			if (staging == null) return;
			foreach (SecureInstallerStaging item in staging) item.Dispose();
			if (cleanupPrerequisites) PrerequisiteBundle.CleanupExtractedFiles();
		}

		private sealed class QuarantineEntry
		{
			internal QuarantineEntry(
				InstallerProcessJob process,
				string label,
				bool persistentForHost)
			{
				Process = process;
				Label = label;
				PersistentForHost = persistentForHost;
			}

			internal InstallerProcessJob Process { get; private set; }
			internal string Label { get; private set; }
			internal bool PersistentForHost { get; private set; }
		}
	}
}
