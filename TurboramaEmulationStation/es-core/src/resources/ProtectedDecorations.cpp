#include "resources/ProtectedDecorations.h"

#include <algorithm>
#include <array>
#include <cctype>
#include <cstring>
#include <vector>

#ifdef WIN32
#include <Windows.h>
#include <bcrypt.h>
#endif

namespace
{
	const char* const kResourcePrefix =
		":/__turborama_protected/decorations/default_unglazed/systems/";

	struct DecorationEntry
	{
		const char* name;
		int resourceId;
	};

	const DecorationEntry kEntries[] = {
		{ "pc", 201 },
		{ "ps3", 202 },
		{ "ps4", 203 },
		{ "ps5", 204 },
		{ "switch", 205 },
		{ "windows", 206 },
		{ "xboxone", 207 }
	};

	std::string lowerAscii(std::string value)
	{
		std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
			return static_cast<char>(std::tolower(ch));
		});
		return value;
	}

	const DecorationEntry* findBySystem(const std::string& systemName)
	{
		std::string normalized = lowerAscii(systemName);
		if (normalized.size() > 4 && normalized.substr(normalized.size() - 4) == ".png")
			normalized.resize(normalized.size() - 4);

		for (const DecorationEntry& entry : kEntries)
			if (normalized == entry.name)
				return &entry;
		return nullptr;
	}

	const DecorationEntry* findByPath(const std::string& path)
	{
		const std::string normalized = lowerAscii(path);
		const size_t prefixLength = std::strlen(kResourcePrefix);
		if (normalized.size() <= prefixLength ||
			normalized.compare(0, prefixLength, kResourcePrefix) != 0)
			return nullptr;

		const std::string filename = normalized.substr(prefixLength);
		if (filename.find('/') != std::string::npos || filename.find('\\') != std::string::npos)
			return nullptr;
		return findBySystem(filename);
	}

#ifdef WIN32
	const unsigned char kKeyPartA[32] = {
		0x26, 0x8F, 0x97, 0x11, 0xE2, 0x64, 0x4A, 0xC3,
		0x0D, 0xF1, 0x5B, 0x72, 0xA0, 0xCD, 0x18, 0x3E,
		0x93, 0x27, 0xCC, 0x8A, 0x41, 0xBE, 0x09, 0x65,
		0xFA, 0xD4, 0x32, 0x80, 0x17, 0xED, 0x69, 0xB5
	};

	const unsigned char kKeyPartB[32] = {
		0x5B, 0x2E, 0xAB, 0xF9, 0xBB, 0x6B, 0xF8, 0x85,
		0xCA, 0x65, 0x7A, 0xAF, 0xCB, 0xF5, 0xE8, 0xBB,
		0x89, 0xE9, 0xBF, 0xC7, 0xD3, 0xBB, 0xE8, 0xD2,
		0x9E, 0xFB, 0xEA, 0xB9, 0xBC, 0xBB, 0xE9, 0xA9
	};

	const unsigned char kPayloadMagic[8] = {
		'T', 'R', 'D', 'E', 'C', 'O', '1', 0
	};
	const size_t kNonceLength = 12;
	const size_t kTagLength = 16;
	const size_t kHeaderLength = sizeof(kPayloadMagic) + kNonceLength + kTagLength;

	bool decryptEntry(const DecorationEntry& entry,
		std::shared_ptr<unsigned char>& output, size_t& outputLength)
	{
		output.reset();
		outputLength = 0;

		HMODULE module = GetModuleHandleW(nullptr);
		HRSRC resource = FindResourceW(module, MAKEINTRESOURCEW(entry.resourceId), MAKEINTRESOURCEW(10));
		if (resource == nullptr)
			return false;
		HGLOBAL loaded = LoadResource(module, resource);
		if (loaded == nullptr)
			return false;

		const DWORD resourceLength = SizeofResource(module, resource);
		const unsigned char* payload = static_cast<const unsigned char*>(LockResource(loaded));
		if (payload == nullptr || resourceLength <= kHeaderLength ||
			std::memcmp(payload, kPayloadMagic, sizeof(kPayloadMagic)) != 0)
			return false;

		const unsigned char* nonce = payload + sizeof(kPayloadMagic);
		const unsigned char* tag = nonce + kNonceLength;
		const unsigned char* cipherText = tag + kTagLength;
		const ULONG cipherLength = resourceLength - static_cast<DWORD>(kHeaderLength);

		std::array<unsigned char, 32> key = {};
		for (size_t index = 0; index < key.size(); ++index)
			key[index] = kKeyPartA[index] ^ kKeyPartB[index];

		BCRYPT_ALG_HANDLE algorithm = nullptr;
		BCRYPT_KEY_HANDLE keyHandle = nullptr;
		std::vector<unsigned char> keyObject;
		bool success = false;

		do
		{
			if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_AES_ALGORITHM, nullptr, 0) < 0)
				break;
			if (BCryptSetProperty(algorithm, BCRYPT_CHAINING_MODE,
				reinterpret_cast<PUCHAR>(const_cast<wchar_t*>(BCRYPT_CHAIN_MODE_GCM)),
				static_cast<ULONG>(sizeof(BCRYPT_CHAIN_MODE_GCM)), 0) < 0)
				break;

			DWORD objectLength = 0;
			DWORD copied = 0;
			if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
				reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength), &copied, 0) < 0 ||
				objectLength == 0)
				break;
			keyObject.resize(objectLength);
			if (BCryptGenerateSymmetricKey(algorithm, &keyHandle, keyObject.data(), objectLength,
				key.data(), static_cast<ULONG>(key.size()), 0) < 0)
				break;

			const std::string aad = std::string("TurboRamaProtectedDecoration:v1:") + entry.name + ".png";
			BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO authInfo;
			BCRYPT_INIT_AUTH_MODE_INFO(authInfo);
			authInfo.pbNonce = const_cast<PUCHAR>(nonce);
			authInfo.cbNonce = static_cast<ULONG>(kNonceLength);
			authInfo.pbTag = const_cast<PUCHAR>(tag);
			authInfo.cbTag = static_cast<ULONG>(kTagLength);
			authInfo.pbAuthData = reinterpret_cast<PUCHAR>(const_cast<char*>(aad.data()));
			authInfo.cbAuthData = static_cast<ULONG>(aad.size());

			std::shared_ptr<unsigned char> plaintext(
				new unsigned char[cipherLength],
				[cipherLength](unsigned char* bytes) {
					if (bytes != nullptr)
					{
						SecureZeroMemory(bytes, cipherLength);
						delete[] bytes;
					}
				});

			ULONG decryptedLength = 0;
			if (BCryptDecrypt(keyHandle, const_cast<PUCHAR>(cipherText), cipherLength,
				&authInfo, nullptr, 0, plaintext.get(), cipherLength,
				&decryptedLength, 0) < 0 || decryptedLength != cipherLength)
				break;

			output = plaintext;
			outputLength = decryptedLength;
			success = true;
		} while (false);

		if (keyHandle != nullptr)
			BCryptDestroyKey(keyHandle);
		if (algorithm != nullptr)
			BCryptCloseAlgorithmProvider(algorithm, 0);
		if (!keyObject.empty())
			SecureZeroMemory(keyObject.data(), keyObject.size());
		SecureZeroMemory(key.data(), key.size());
		return success;
	}
#endif
}

namespace ProtectedDecorations
{
	bool isResourcePath(const std::string& path)
	{
		return findByPath(path) != nullptr;
	}

	bool hasSystem(const std::string& systemName)
	{
		return findBySystem(systemName) != nullptr;
	}

	std::string resourcePathForSystem(const std::string& systemName)
	{
		const DecorationEntry* entry = findBySystem(systemName);
		return entry == nullptr ? std::string() :
			std::string(kResourcePrefix) + entry->name + ".png";
	}

	bool loadResource(const std::string& path,
		std::shared_ptr<unsigned char>& data, size_t& length)
	{
		const DecorationEntry* entry = findByPath(path);
		if (entry == nullptr)
			return false;
#ifdef WIN32
		return decryptEntry(*entry, data, length);
#else
		(void)data;
		(void)length;
		return false;
#endif
	}
}
