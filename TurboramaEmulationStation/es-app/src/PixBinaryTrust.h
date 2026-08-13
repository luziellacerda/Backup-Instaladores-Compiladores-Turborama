#pragma once

#include <algorithm>
#include <array>
#include <cctype>
#include <cwctype>
#include <functional>
#include <string>
#include <utility>
#include <vector>

#ifdef _WIN32
#include <windows.h>
#include <softpub.h>
#include <wincrypt.h>
#include <wintrust.h>
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "wintrust.lib")
#endif

#define TURBORAMA_PIX_STRINGIFY_INNER(value) #value
#define TURBORAMA_PIX_STRINGIFY(value) TURBORAMA_PIX_STRINGIFY_INNER(value)

// A verificacao fica desativada somente em builds locais de desenvolvimento.
// O orquestrador comercial define TURBORAMA_REQUIRE_SIGNED_PIX e grava no
// binario o thumbprint do certificado fornecido pelo proprietario. A chave
// privada nunca entra no codigo, no Git ou no instalador.
namespace PixBinaryTrust
{
	inline bool required()
	{
#if defined(_WIN32) && defined(TURBORAMA_REQUIRE_SIGNED_PIX)
		return true;
#else
		return false;
#endif
	}

	inline std::string expectedPublisherThumbprint()
	{
#ifdef TURBORAMA_PIX_SIGNER_THUMBPRINT
		std::string value = TURBORAMA_PIX_STRINGIFY(TURBORAMA_PIX_SIGNER_THUMBPRINT);
		std::transform(value.begin(), value.end(), value.begin(), [](unsigned char character) {
			return static_cast<char>(std::toupper(character));
		});
		return value;
#else
		return {};
#endif
	}

#ifdef _WIN32
	inline std::string certificateSha1(PCCERT_CONTEXT certificate)
	{
		if (certificate == nullptr) return {};
		DWORD size = 0;
		if (!CertGetCertificateContextProperty(certificate, CERT_SHA1_HASH_PROP_ID, nullptr, &size)
			|| size == 0 || size > 128) return {};
		std::string result;
		result.resize(static_cast<size_t>(size) * 2);
		std::vector<unsigned char> digest(size);
		if (!CertGetCertificateContextProperty(certificate, CERT_SHA1_HASH_PROP_ID,
			digest.data(), &size)) return {};
		static const char hexadecimal[] = "0123456789ABCDEF";
		for (DWORD index = 0; index < size; ++index)
		{
			result[index * 2] = hexadecimal[(digest[index] >> 4) & 0x0F];
			result[index * 2 + 1] = hexadecimal[digest[index] & 0x0F];
		}
		SecureZeroMemory(digest.data(), digest.size());
		return result;
	}

	inline bool verifyWindowsSignature(const std::wstring& path, bool requireTurboRamaPublisher,
		std::string& error)
	{
		WINTRUST_FILE_INFO file{};
		file.cbStruct = sizeof(file);
		file.pcwszFilePath = path.c_str();

		WINTRUST_DATA trust{};
		trust.cbStruct = sizeof(trust);
		trust.dwUIChoice = WTD_UI_NONE;
	#if defined(TURBORAMA_REQUIRE_SIGNED_PIX) && TURBORAMA_REQUIRE_SIGNED_PIX
		// O perfil comercial falha fechado quando a cadeia do assinante foi
		// revogada ou quando o Windows nao consegue comprovar seu estado. PIX ja
		// exige conectividade; nao aceite um cache antigo como autoridade final.
		trust.fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN;
	#else
		trust.fdwRevocationChecks = WTD_REVOKE_NONE;
	#endif
		trust.dwUnionChoice = WTD_CHOICE_FILE;
		trust.pFile = &file;
		trust.dwStateAction = WTD_STATEACTION_VERIFY;
	#if defined(TURBORAMA_REQUIRE_SIGNED_PIX) && TURBORAMA_REQUIRE_SIGNED_PIX
		trust.dwProvFlags = WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT | WTD_DISABLE_MD2_MD4;
	#else
		trust.dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE;
	#endif
		trust.dwUIContext = WTD_UICONTEXT_EXECUTE;

		GUID policy = WINTRUST_ACTION_GENERIC_VERIFY_V2;
		const LONG status = WinVerifyTrust(nullptr, &policy, &trust);
		bool accepted = status == ERROR_SUCCESS;
		if (accepted && requireTurboRamaPublisher)
		{
			const std::string expected = expectedPublisherThumbprint();
			CRYPT_PROVIDER_DATA* provider = WTHelperProvDataFromStateData(trust.hWVTStateData);
			CRYPT_PROVIDER_SGNR* signer = provider == nullptr
				? nullptr : WTHelperGetProvSignerFromChain(provider, 0, FALSE, 0);
			PCCERT_CONTEXT certificate = signer == nullptr || signer->csCertChain == 0
				? nullptr : signer->pasCertChain[0].pCert;
			accepted = expected.size() == 40 && certificateSha1(certificate) == expected;
		}

		trust.dwStateAction = WTD_STATEACTION_CLOSE;
		WinVerifyTrust(nullptr, &policy, &trust);
		if (!accepted)
		{
			error = requireTurboRamaPublisher
				? "Arquivo PIX recusado: assinatura ou editor comercial diferente do autorizado."
				: "Componente do Windows recusado: assinatura digital invalida.";
		}
		return accepted;
	}

	inline bool buildSanitizedDotnetEnvironment(const std::wstring& runtimeRoot,
		const std::vector<std::pair<std::wstring, std::wstring>>& extraEntries,
		std::vector<wchar_t>& environment, std::string& error)
	{
		environment.clear();
		if (runtimeRoot.empty() || runtimeRoot.find(L'\0') != std::wstring::npos)
		{
			error = "O caminho do runtime privado PIX e invalido.";
			return false;
		}

		std::vector<std::pair<std::wstring, std::wstring>> values;
		auto add = [&](const std::wstring& name, const std::wstring& value) {
			if (name.empty() || value.find(L'\0') != std::wstring::npos
				|| value.find(L'\r') != std::wstring::npos || value.find(L'\n') != std::wstring::npos)
				return false;
			for (const auto& current : values)
				if (_wcsicmp(current.first.c_str(), name.c_str()) == 0) return false;
			values.emplace_back(name, value);
			return true;
		};
		auto inherit = [&](const wchar_t* name) {
			const DWORD needed = GetEnvironmentVariableW(name, nullptr, 0);
			if (needed == 0 || needed > 32768) return true;
			std::vector<wchar_t> buffer(needed);
			const DWORD copied = GetEnvironmentVariableW(name, buffer.data(), needed);
			return copied > 0 && copied < needed ? add(name, std::wstring(buffer.data(), copied)) : false;
		};

		for (const wchar_t* name : { L"SystemRoot", L"WINDIR", L"USERPROFILE", L"LOCALAPPDATA",
			L"APPDATA", L"PROGRAMDATA", L"TEMP", L"TMP", L"COMPUTERNAME", L"USERNAME",
			L"USERDOMAIN", L"PROCESSOR_ARCHITECTURE", L"NUMBER_OF_PROCESSORS" })
		{
			if (!inherit(name))
			{
				error = "O Windows nao forneceu um ambiente basico seguro para o agente PIX.";
				return false;
			}
		}

		wchar_t windowsDirectory[MAX_PATH + 1]{};
		const UINT windowsLength = GetWindowsDirectoryW(windowsDirectory, MAX_PATH);
		if (windowsLength == 0 || windowsLength >= MAX_PATH)
		{
			error = "O diretorio seguro do Windows nao pode ser determinado.";
			return false;
		}
		if (!add(L"Path", std::wstring(windowsDirectory, windowsLength) + L"\\System32")
			|| !add(L"DOTNET_ROOT", runtimeRoot)
			|| !add(L"DOTNET_MULTILEVEL_LOOKUP", L"0")
			|| !add(L"DOTNET_EnableDiagnostics", L"0")
			|| !add(L"COMPlus_EnableDiagnostics", L"0")
			|| !add(L"DOTNET_CLI_TELEMETRY_OPTOUT", L"1")
			|| !add(L"DOTNET_NOLOGO", L"1"))
		{
			error = "O ambiente protegido do runtime PIX possui variaveis duplicadas.";
			return false;
		}
		for (const auto& entry : extraEntries)
		{
			if (entry.first.empty() || entry.first.find(L'=') != std::wstring::npos
				|| !std::all_of(entry.first.begin(), entry.first.end(), [](wchar_t character) {
					return std::iswalnum(character) != 0 || character == L'_';
				}) || !add(entry.first, entry.second))
			{
				error = "O ambiente protegido do agente PIX recebeu uma variavel invalida.";
				return false;
			}
		}
		std::sort(values.begin(), values.end(), [](const auto& left, const auto& right) {
			return _wcsicmp(left.first.c_str(), right.first.c_str()) < 0;
		});
		for (const auto& entry : values)
		{
			environment.insert(environment.end(), entry.first.begin(), entry.first.end());
			environment.push_back(L'=');
			environment.insert(environment.end(), entry.second.begin(), entry.second.end());
			environment.push_back(L'\0');
		}
		environment.push_back(L'\0');
		return true;
	}

	inline bool sha256RegularFile(const std::wstring& path, std::array<unsigned char, 32>& digest,
		std::string& error)
	{
		digest.fill(0);
		HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
		if (file == INVALID_HANDLE_VALUE)
		{
			error = "O manifesto PIX nao conseguiu abrir um arquivo protegido.";
			return false;
		}
		BY_HANDLE_FILE_INFORMATION information{};
		if (!GetFileInformationByHandle(file, &information)
			|| (information.dwFileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0
			|| information.nNumberOfLinks != 1)
		{
			CloseHandle(file);
			error = "O manifesto PIX recusou arquivo especial, redirecionado ou com hardlink.";
			return false;
		}

		BCRYPT_ALG_HANDLE algorithm = nullptr;
		BCRYPT_HASH_HANDLE hash = nullptr;
		DWORD objectLength = 0;
		DWORD received = 0;
		std::vector<unsigned char> object;
		bool accepted = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0
			&& BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
				reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength), &received, 0) >= 0
			&& objectLength > 0 && objectLength <= 1024 * 1024;
		if (accepted)
		{
			object.resize(objectLength);
			accepted = BCryptCreateHash(algorithm, &hash, object.data(), objectLength, nullptr, 0, 0) >= 0;
		}
		std::array<unsigned char, 64 * 1024> buffer{};
		while (accepted)
		{
			DWORD count = 0;
			if (!ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &count, nullptr))
			{
				accepted = false;
				break;
			}
			if (count == 0) break;
			accepted = BCryptHashData(hash, buffer.data(), count, 0) >= 0;
		}
		if (accepted) accepted = BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) >= 0;
		SecureZeroMemory(buffer.data(), buffer.size());
		if (!object.empty()) SecureZeroMemory(object.data(), object.size());
		if (hash) BCryptDestroyHash(hash);
		if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
		CloseHandle(file);
		if (!accepted) error = "O manifesto PIX nao conseguiu calcular SHA-256 de um arquivo protegido.";
		return accepted;
	}

	inline bool verifyDirectoryTreeSha256(const std::wstring& root, const std::string& expectedHash,
		std::string& error)
	{
		if (expectedHash.size() != 64 || !std::all_of(expectedHash.begin(), expectedHash.end(), [](unsigned char value) {
			return std::isxdigit(value) != 0;
		}))
		{
			error = "O hash incorporado do bundle PIX e invalido.";
			return false;
		}
		const DWORD rootAttributes = GetFileAttributesW(root.c_str());
		if (rootAttributes == INVALID_FILE_ATTRIBUTES
			|| (rootAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0
			|| (rootAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
		{
			error = "A pasta protegida do agente PIX esta ausente ou redirecionada.";
			return false;
		}

		std::vector<std::wstring> files;
		std::function<bool(const std::wstring&)> collect = [&](const std::wstring& relative) {
			const std::wstring directory = relative.empty() ? root : root + L"\\" + relative;
			WIN32_FIND_DATAW data{};
			HANDLE search = FindFirstFileW((directory + L"\\*").c_str(), &data);
			if (search == INVALID_HANDLE_VALUE) return false;
			bool valid = true;
			do
			{
				const std::wstring name = data.cFileName;
				if (name == L"." || name == L"..") continue;
				if ((data.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
				{
					valid = false;
					break;
				}
				const std::wstring child = relative.empty() ? name : relative + L"\\" + name;
				if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
				{
					if (!collect(child)) { valid = false; break; }
				}
				else files.push_back(child);
			}
			while (FindNextFileW(search, &data));
			const DWORD findError = GetLastError();
			FindClose(search);
			return valid && findError == ERROR_NO_MORE_FILES;
		};
		if (!collect(L"") || files.empty())
		{
			error = "O bundle protegido do agente PIX nao pode ser enumerado com seguranca.";
			return false;
		}
		for (auto& relative : files)
		{
			std::replace(relative.begin(), relative.end(), L'\\', L'/');
			std::transform(relative.begin(), relative.end(), relative.begin(), [](wchar_t character) {
				return static_cast<wchar_t>(std::towlower(character));
			});
		}
		std::sort(files.begin(), files.end());
		if (std::adjacent_find(files.begin(), files.end()) != files.end())
		{
			error = "O bundle PIX contem caminhos duplicados sem distincao de caixa.";
			return false;
		}

		BCRYPT_ALG_HANDLE algorithm = nullptr;
		BCRYPT_HASH_HANDLE treeHash = nullptr;
		DWORD objectLength = 0;
		DWORD received = 0;
		std::vector<unsigned char> object;
		bool accepted = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0
			&& BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
				reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength), &received, 0) >= 0
			&& objectLength > 0 && objectLength <= 1024 * 1024;
		if (accepted)
		{
			object.resize(objectLength);
			accepted = BCryptCreateHash(algorithm, &treeHash, object.data(), objectLength, nullptr, 0, 0) >= 0;
		}
		const unsigned char zero = 0;
		const unsigned char newline = '\n';
		for (const std::wstring& relative : files)
		{
			if (!accepted) break;
			const int utf8Length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS,
				relative.c_str(), static_cast<int>(relative.size()), nullptr, 0, nullptr, nullptr);
			if (utf8Length <= 0) { accepted = false; break; }
			std::vector<unsigned char> utf8(static_cast<size_t>(utf8Length));
			if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, relative.c_str(),
				static_cast<int>(relative.size()), reinterpret_cast<char*>(utf8.data()), utf8Length,
				nullptr, nullptr) != utf8Length) { accepted = false; break; }
			std::wstring diskRelative = relative;
			std::replace(diskRelative.begin(), diskRelative.end(), L'/', L'\\');
			std::array<unsigned char, 32> fileDigest{};
			accepted = sha256RegularFile(root + L"\\" + diskRelative, fileDigest, error)
				&& BCryptHashData(treeHash, utf8.data(), static_cast<ULONG>(utf8.size()), 0) >= 0
				&& BCryptHashData(treeHash, const_cast<PUCHAR>(&zero), 1, 0) >= 0
				&& BCryptHashData(treeHash, fileDigest.data(), static_cast<ULONG>(fileDigest.size()), 0) >= 0
				&& BCryptHashData(treeHash, const_cast<PUCHAR>(&newline), 1, 0) >= 0;
			SecureZeroMemory(fileDigest.data(), fileDigest.size());
			if (!utf8.empty()) SecureZeroMemory(utf8.data(), utf8.size());
		}
		std::array<unsigned char, 32> digest{};
		if (accepted) accepted = BCryptFinishHash(treeHash, digest.data(), static_cast<ULONG>(digest.size()), 0) >= 0;
		if (!object.empty()) SecureZeroMemory(object.data(), object.size());
		if (treeHash) BCryptDestroyHash(treeHash);
		if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
		if (!accepted)
		{
			if (error.empty()) error = "O manifesto completo do bundle PIX nao pode ser calculado.";
			return false;
		}
		static const char hexadecimal[] = "0123456789ABCDEF";
		std::string actual(64, '0');
		for (size_t index = 0; index < digest.size(); ++index)
		{
			actual[index * 2] = hexadecimal[(digest[index] >> 4) & 0x0F];
			actual[index * 2 + 1] = hexadecimal[digest[index] & 0x0F];
		}
		SecureZeroMemory(digest.data(), digest.size());
		std::string normalizedExpected = expectedHash;
		std::transform(normalizedExpected.begin(), normalizedExpected.end(), normalizedExpected.begin(),
			[](unsigned char value) { return static_cast<char>(std::toupper(value)); });
		if (actual != normalizedExpected)
		{
			error = "O bundle do agente PIX foi alterado, recebeu arquivo extra ou esta incompleto.";
			return false;
		}
		return true;
	}
#endif

	inline bool verifyVendorBinary(const std::wstring& path, std::string& error)
	{
		if (!required()) return true;
#ifdef _WIN32
		return verifyWindowsSignature(path, true, error);
#else
		(void)path;
		error = "A verificacao comercial de assinatura exige Windows.";
		return false;
#endif
	}

	inline bool verifyTrustedRuntime(const std::wstring& path, std::string& error)
	{
		if (!required()) return true;
#ifdef _WIN32
		return verifyWindowsSignature(path, false, error);
#else
		(void)path;
		error = "A verificacao do runtime exige Windows.";
		return false;
#endif
	}

	inline bool verifyCommercialAgentBundle(const std::wstring& agentRoot, std::string& error)
	{
		if (!required()) return true;
#if defined(_WIN32) && defined(TURBORAMA_PIX_BUNDLE_SHA256)
		return verifyDirectoryTreeSha256(agentRoot,
			TURBORAMA_PIX_STRINGIFY(TURBORAMA_PIX_BUNDLE_SHA256), error);
#else
		(void)agentRoot;
		error = "O build comercial nao incorporou o manifesto SHA-256 completo do agente PIX.";
		return false;
#endif
	}
}
