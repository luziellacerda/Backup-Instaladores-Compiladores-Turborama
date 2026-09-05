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
		private const uint TokenAdjustDefault = 0x0080U;
		private const uint DisableMaxPrivilege = 0x00000001U;
		private const uint LuaToken = 0x00000004U;
		private const uint SeGroupIntegrity = 0x00000020U;
		private const int ErrorNoToken = 1008;
		private const int SecurityImpersonation = 2;
		private const int SecurityIdentification = 1;
		private const int TokenImpersonation = 2;
		private const int TokenLinkedToken = 19;
		private const int TokenElevation = 20;
		private const int TokenIntegrityLevel = 25;
		private const int TokenType = 8;
		private const int TokenImpersonationLevel = 9;
		private const int TokenPrimary = 1;
		private const int MediumIntegrityRid = 0x2000;

		internal static void Run(Action action)
		{
			if (action == null)
			{
				throw new ArgumentNullException("action");
			}

			bool effectiveTokenCameFromThread;
			bool threadTokenWasReverted = false;
			IntPtr effectiveToken = OpenEffectiveToken(out effectiveTokenCameFromThread);
			try
			{
				if (IsSafelyLimited(effectiveToken))
				{
					action();
					return;
				}

				// A worker thread can inherit an identification-only impersonation
				// token from its execution context. TokenLinkedToken obtained from that
				// thread token cannot be promoted to SecurityImpersonation and Windows
				// returns ERROR_BAD_IMPERSONATION_LEVEL (1346). Always obtain the UAC
				// linked token from this process' primary token instead.
				if (effectiveTokenCameFromThread)
				{
					if (!RevertToSelf()) throw NewSecurityException("Não foi possível suspender o token temporário da thread.");
					threadTokenWasReverted = true;
				}
				IntPtr primaryProcessToken = OpenPrimaryProcessToken();
				try
				{
					TokenLinkedTokenInformation linkedInformation = new TokenLinkedTokenInformation();
					int returnedLength;
					bool hasLinkedToken = GetTokenInformation(
						primaryProcessToken,
						TokenLinkedToken,
						out linkedInformation,
						Marshal.SizeOf(typeof(TokenLinkedTokenInformation)),
						out returnedLength);

					IntPtr fallbackToken = IntPtr.Zero;
					try
					{
						// TokenLinkedToken already returns the primary token from the
						// user's split UAC pair. ImpersonateLoggedOnUser (used by
						// WindowsIdentity) accepts a primary token directly. Duplicating
						// it as an impersonation token is unnecessary and can fail with
						// ERROR_BAD_IMPERSONATION_LEVEL on valid elevated launches.
						IntPtr limitedToken = hasLinkedToken ? linkedInformation.LinkedToken : IntPtr.Zero;
						// UAC-disabled and built-in Administrator sessions may have no
						// linked standard token. A validated LUA token from the same
						// primary identity is the fail-safe local alternative.
						if (limitedToken == IntPtr.Zero || !IsSafelyLimited(limitedToken))
						{
							fallbackToken = CreateValidatedLuaToken(primaryProcessToken);
							limitedToken = fallbackToken;
						}
						EnsureTokenIsSafelyLimited(limitedToken);
						using (WindowsImpersonationContext context = WindowsIdentity.Impersonate(limitedToken))
						{
							EnsureCurrentTokenIsLimited();
							action();
						}
					}
					finally
					{
						if (fallbackToken != IntPtr.Zero) CloseHandle(fallbackToken);
						if (linkedInformation.LinkedToken != IntPtr.Zero)
						{
							CloseHandle(linkedInformation.LinkedToken);
						}
					}
				}
				finally
				{
					CloseHandle(primaryProcessToken);
				}
			}
			finally
			{
				int restoreError = 0;
				if (threadTokenWasReverted && !SetThreadToken(IntPtr.Zero, effectiveToken))
					restoreError = Marshal.GetLastWin32Error();
				CloseHandle(effectiveToken);
				if (restoreError != 0)
					throw new Win32Exception(restoreError, "Não foi possível restaurar o token original da thread.");
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

#if PRODUCT_PACKAGE_SECURITY_TESTS
		internal static void RunWithIdentificationThreadTokenForSecurityTest(Action action)
		{
			IntPtr processToken = OpenPrimaryProcessToken();
			IntPtr identificationToken = IntPtr.Zero;
			IntPtr originalThreadToken = IntPtr.Zero;
			bool hadOriginalThreadToken = OpenThreadToken(GetCurrentThread(),
				TokenQuery | TokenDuplicate | TokenImpersonate, true, out originalThreadToken);
			if (!hadOriginalThreadToken && Marshal.GetLastWin32Error() != ErrorNoToken)
				throw NewSecurityException("O teste não conseguiu preservar o token original da thread.");
			try
			{
				if (!DuplicateTokenEx(processToken, TokenQuery | TokenImpersonate | TokenDuplicate,
					IntPtr.Zero, SecurityIdentification, TokenImpersonation, out identificationToken))
					throw NewSecurityException("O teste não conseguiu criar o token de identificação.");
				if (!SetThreadToken(IntPtr.Zero, identificationToken))
					throw NewSecurityException("O teste não conseguiu aplicar o token de identificação.");
				Run(action);
			}
			finally
			{
				if (identificationToken != IntPtr.Zero)
				{
					if (!SetThreadToken(IntPtr.Zero, hadOriginalThreadToken ? originalThreadToken : IntPtr.Zero))
						throw NewSecurityException("O teste não conseguiu reverter o token da thread.");
					CloseHandle(identificationToken);
				}
				if (originalThreadToken != IntPtr.Zero) CloseHandle(originalThreadToken);
				CloseHandle(processToken);
			}
		}
#endif

		private static IntPtr OpenEffectiveToken(out bool cameFromThread)
		{
			IntPtr token;
			if (OpenThreadToken(GetCurrentThread(), TokenQuery | TokenDuplicate | TokenImpersonate, true, out token))
			{
				cameFromThread = true;
				return token;
			}

			int threadError = Marshal.GetLastWin32Error();
			if (threadError != ErrorNoToken)
			{
				throw new Win32Exception(threadError, "Não foi possível consultar o token efetivo da thread.");
			}

			cameFromThread = false;
			return OpenPrimaryProcessToken();
		}

		private static IntPtr OpenEffectiveToken()
		{
			bool cameFromThread;
			return OpenEffectiveToken(out cameFromThread);
		}

		private static IntPtr OpenPrimaryProcessToken()
		{
			IntPtr token;
			if (!OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenDuplicate | TokenAdjustDefault, out token))
				throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível consultar o token primário do processo.");
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

		private static IntPtr CreateValidatedLuaToken(IntPtr primaryProcessToken)
		{
			IntPtr restrictedToken;
			if (!CreateRestrictedToken(primaryProcessToken, DisableMaxPrivilege | LuaToken,
				0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, out restrictedToken))
				throw NewSecurityException("O Windows não conseguiu criar o token LUA restrito para extração.");

			try
			{
				SetMediumIntegrity(restrictedToken);
				EnsureTokenIsSafelyLimited(restrictedToken);
				return restrictedToken;
			}
			catch
			{
				CloseHandle(restrictedToken);
				throw;
			}
		}

		private static void SetMediumIntegrity(IntPtr token)
		{
			IntPtr mediumSid;
			if (!ConvertStringSidToSid("S-1-16-8192", out mediumSid))
				throw NewSecurityException("Não foi possível criar o SID de integridade média.");
			try
			{
				TokenMandatoryLabel label = new TokenMandatoryLabel();
				label.Label.Sid = mediumSid;
				label.Label.Attributes = SeGroupIntegrity;
				int length = Marshal.SizeOf(typeof(TokenMandatoryLabel)) + checked((int)GetLengthSid(mediumSid));
				if (!SetTokenInformation(token, TokenIntegrityLevel, ref label, length))
					throw NewSecurityException("Não foi possível limitar a integridade do token de extração.");
			}
			finally
			{
				LocalFree(mediumSid);
			}
		}

		private static void EnsureTokenIsSafelyLimited(IntPtr token)
		{
			int tokenType;
			int returnedLength;
			if (!GetTokenInformation(token, TokenType, out tokenType, sizeof(int), out returnedLength))
				throw NewSecurityException("Não foi possível verificar o tipo do token de extração.");
			if (tokenType != TokenPrimary)
			{
				int impersonationLevel;
				if (!GetTokenInformation(token, TokenImpersonationLevel, out impersonationLevel, sizeof(int), out returnedLength))
					throw NewSecurityException("Não foi possível verificar o nível de representação do token de extração.");
				if (impersonationLevel < SecurityImpersonation)
					throw new UnauthorizedAccessException("O token de extração não possui nível de representação suficiente.");
			}

			TokenElevationInformation elevation;
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

		[StructLayout(LayoutKind.Sequential)]
		private struct SidAndAttributes
		{
			internal IntPtr Sid;
			internal uint Attributes;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct TokenMandatoryLabel
		{
			internal SidAndAttributes Label;
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
		private static extern bool SetThreadToken(
			IntPtr threadHandlePointer,
			IntPtr tokenHandle);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool RevertToSelf();

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
		private static extern bool CreateRestrictedToken(
			IntPtr existingToken,
			uint flags,
			uint disableSidCount,
			IntPtr sidsToDisable,
			uint deletePrivilegeCount,
			IntPtr privilegesToDelete,
			uint restrictedSidCount,
			IntPtr sidsToRestrict,
			out IntPtr newToken);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetTokenInformation(
			IntPtr tokenHandle,
			int tokenInformationClass,
			ref TokenMandatoryLabel tokenInformation,
			int tokenInformationLength);

		[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool ConvertStringSidToSid(
			string stringSid,
			out IntPtr sid);

		[DllImport("advapi32.dll")]
		private static extern uint GetLengthSid(IntPtr sid);

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
			out int tokenInformation,
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

		[DllImport("kernel32.dll")]
		private static extern IntPtr LocalFree(IntPtr memory);
	}
}
