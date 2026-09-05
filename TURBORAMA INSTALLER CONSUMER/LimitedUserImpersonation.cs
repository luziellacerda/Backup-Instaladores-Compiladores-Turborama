using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace InstallerHost
{
	/// <summary>
	/// Runs product-package work with the non-elevated linked token of the current
	/// UAC session. Prerequisite installers intentionally remain outside this scope.
	/// </summary>
	internal static class LimitedUserImpersonation
	{
		private const uint TokenQuery = 0x0008U;
		private const uint TokenDuplicate = 0x0002U;
		private const uint TokenImpersonate = 0x0004U;
		private const int ErrorNoToken = 1008;
		private const int SecurityImpersonation = 2;
		private const int TokenImpersonation = 2;
		private const int TokenLinkedToken = 19;
		private const int TokenElevation = 20;
		private const int TokenIntegrityLevel = 25;
		private const int MediumIntegrityRid = 0x2000;

		internal static void Run(Action action)
		{
			if (action == null)
			{
				throw new ArgumentNullException("action");
			}

			IntPtr effectiveToken = OpenEffectiveToken();
			try
			{
				if (IsSafelyLimited(effectiveToken))
				{
					action();
					return;
				}

				TokenLinkedTokenInformation linkedInformation;
				int returnedLength;
				if (!GetTokenInformation(
					effectiveToken,
					TokenLinkedToken,
					out linkedInformation,
					Marshal.SizeOf(typeof(TokenLinkedTokenInformation)),
					out returnedLength))
				{
					throw NewSecurityException(
						"O Windows não forneceu um token padrão vinculado; a extração do produto foi recusada.");
				}

				try
				{
					IntPtr impersonationToken;
					if (!DuplicateTokenEx(
						linkedInformation.LinkedToken,
						TokenQuery | TokenImpersonate | TokenDuplicate,
						IntPtr.Zero,
						SecurityImpersonation,
						TokenImpersonation,
						out impersonationToken))
					{
						throw NewSecurityException("Não foi possível duplicar o token padrão para extração.");
					}

					try
					{
						EnsureTokenIsSafelyLimited(impersonationToken);
						using (WindowsImpersonationContext context = WindowsIdentity.Impersonate(impersonationToken))
						{
							EnsureCurrentTokenIsLimited();
							action();
						}
					}
					finally
					{
						CloseHandle(impersonationToken);
					}
				}
				finally
				{
					if (linkedInformation.LinkedToken != IntPtr.Zero)
					{
						CloseHandle(linkedInformation.LinkedToken);
					}
				}
			}
			finally
			{
				CloseHandle(effectiveToken);
			}
		}

		internal static void EnsureCurrentTokenIsLimited()
		{
			IntPtr token = OpenEffectiveToken();
			try
			{
				EnsureTokenIsSafelyLimited(token);
			}
			finally
			{
				CloseHandle(token);
			}
		}

		private static IntPtr OpenEffectiveToken()
		{
			IntPtr token;
			if (OpenThreadToken(GetCurrentThread(), TokenQuery | TokenDuplicate, true, out token))
			{
				return token;
			}

			int threadError = Marshal.GetLastWin32Error();
			if (threadError != ErrorNoToken)
			{
				throw new Win32Exception(threadError, "Não foi possível consultar o token efetivo da thread.");
			}

			if (!OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenDuplicate, out token))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível consultar o token do processo.");
			}
			return token;
		}

		private static bool IsSafelyLimited(IntPtr token)
		{
			try
			{
				EnsureTokenIsSafelyLimited(token);
				return true;
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
		}

		private static void EnsureTokenIsSafelyLimited(IntPtr token)
		{
			TokenElevationInformation elevation;
			int returnedLength;
			if (!GetTokenInformation(
				token,
				TokenElevation,
				out elevation,
				Marshal.SizeOf(typeof(TokenElevationInformation)),
				out returnedLength))
			{
				throw NewSecurityException("Não foi possível verificar a elevação do token de extração.");
			}
			if (elevation.TokenIsElevated != 0)
			{
				throw new UnauthorizedAccessException("A extração do produto não pode executar com token elevado.");
			}

			int integrityRid = GetIntegrityRid(token);
			if (integrityRid > MediumIntegrityRid)
			{
				throw new UnauthorizedAccessException("A extração do produto exige integridade Medium ou inferior.");
			}

			using (WindowsIdentity identity = new WindowsIdentity(token))
			{
				WindowsPrincipal principal = new WindowsPrincipal(identity);
				if (principal.IsInRole(WindowsBuiltInRole.Administrator))
				{
					throw new UnauthorizedAccessException("O token de extração ainda possui Administradores habilitado.");
				}
			}
		}

		private static int GetIntegrityRid(IntPtr token)
		{
			int requiredLength;
			GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out requiredLength);
			int firstError = Marshal.GetLastWin32Error();
			if (requiredLength <= 0)
			{
				throw new Win32Exception(firstError, "Não foi possível dimensionar o nível de integridade do token.");
			}

			IntPtr buffer = Marshal.AllocHGlobal(requiredLength);
			try
			{
				if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, requiredLength, out requiredLength))
				{
					throw NewSecurityException("Não foi possível ler o nível de integridade do token.");
				}

				IntPtr sid = Marshal.ReadIntPtr(buffer);
				if (sid == IntPtr.Zero || !IsValidSid(sid))
				{
					throw new UnauthorizedAccessException("SID de integridade inválido no token de extração.");
				}
				IntPtr countPointer = GetSidSubAuthorityCount(sid);
				if (countPointer == IntPtr.Zero)
				{
					throw NewSecurityException("Não foi possível ler o SID de integridade.");
				}
				byte count = Marshal.ReadByte(countPointer);
				if (count == 0)
				{
					throw new UnauthorizedAccessException("SID de integridade vazio no token de extração.");
				}
				IntPtr ridPointer = GetSidSubAuthority(sid, (uint)(count - 1));
				if (ridPointer == IntPtr.Zero)
				{
					throw NewSecurityException("Não foi possível ler o RID de integridade.");
				}
				return Marshal.ReadInt32(ridPointer);
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}

		private static UnauthorizedAccessException NewSecurityException(string message)
		{
			return new UnauthorizedAccessException(
				message + " Win32=" + Marshal.GetLastWin32Error() + ".");
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct TokenLinkedTokenInformation
		{
			internal IntPtr LinkedToken;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct TokenElevationInformation
		{
			internal int TokenIsElevated;
		}

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool OpenProcessToken(
			IntPtr processHandle,
			uint desiredAccess,
			out IntPtr tokenHandle);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool OpenThreadToken(
			IntPtr threadHandle,
			uint desiredAccess,
			[MarshalAs(UnmanagedType.Bool)] bool openAsSelf,
			out IntPtr tokenHandle);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool DuplicateTokenEx(
			IntPtr existingToken,
			uint desiredAccess,
			IntPtr tokenAttributes,
			int impersonationLevel,
			int tokenType,
			out IntPtr newToken);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetTokenInformation(
			IntPtr tokenHandle,
			int tokenInformationClass,
			out TokenLinkedTokenInformation tokenInformation,
			int tokenInformationLength,
			out int returnLength);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetTokenInformation(
			IntPtr tokenHandle,
			int tokenInformationClass,
			out TokenElevationInformation tokenInformation,
			int tokenInformationLength,
			out int returnLength);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetTokenInformation(
			IntPtr tokenHandle,
			int tokenInformationClass,
			IntPtr tokenInformation,
			int tokenInformationLength,
			out int returnLength);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsValidSid(IntPtr sid);

		[DllImport("advapi32.dll", SetLastError = true)]
		private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

		[DllImport("advapi32.dll", SetLastError = true)]
		private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentProcess();

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentThread();

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CloseHandle(IntPtr handle);
	}
}
