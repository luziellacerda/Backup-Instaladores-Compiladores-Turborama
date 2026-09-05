#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include <aclapi.h>
#include <cstdio>
#include <cstring>
#include <fcntl.h>
#include <io.h>
#include <string>
#include <vector>

static bool privateAcl(const wchar_t* directory)
{
    PACL acl = nullptr;
    PSECURITY_DESCRIPTOR descriptor = nullptr;
    if (GetNamedSecurityInfoW(directory, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
        nullptr, nullptr, &acl, nullptr, &descriptor) != ERROR_SUCCESS) return false;
    SECURITY_DESCRIPTOR_CONTROL control{};
    DWORD revision = 0;
    bool good = GetSecurityDescriptorControl(descriptor, &control, &revision) &&
        (control & SE_DACL_PROTECTED) != 0 && acl != nullptr && acl->AceCount == 2;
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) good = false;
    DWORD size = 0;
    GetTokenInformation(token, TokenUser, nullptr, 0, &size);
    std::vector<unsigned char> user(size);
    if (!GetTokenInformation(token, TokenUser, user.data(), size, &size)) good = false;
    if (token) CloseHandle(token);
    bool currentUser = false, system = false;
    for (DWORD i = 0; good && i < acl->AceCount; ++i)
    {
        void* raw = nullptr;
        if (!GetAce(acl, i, &raw)) { good = false; break; }
        const auto* ace = static_cast<ACCESS_ALLOWED_ACE*>(raw);
        PSID sid = const_cast<DWORD*>(&ace->SidStart);
        if (ace->Header.AceType != ACCESS_ALLOWED_ACE_TYPE) { good = false; break; }
        if (IsWellKnownSid(sid, WinLocalSystemSid)) system = true;
        else if (EqualSid(sid, reinterpret_cast<TOKEN_USER*>(user.data())->User.Sid)) currentUser = true;
        else good = false;
    }
    LocalFree(descriptor);
    return good && currentUser && system;
}

int main(int argc, char** argv)
{
    if (argc != 2) return 3;
    wchar_t value[32768]{};
    const wchar_t* forbidden[] = { L"DOTNET_STARTUP_HOOKS", L"CORECLR_ENABLE_PROFILING",
        L"COR_ENABLE_PROFILING", L"DOTNET_ADDITIONAL_DEPS", L"DOTNET_SHARED_STORE" };
    for (const auto* name : forbidden)
        if (GetEnvironmentVariableW(name, value, 32768) != 0) return 4;
    if (GetEnvironmentVariableW(L"DOTNET_BUNDLE_EXTRACT_BASE_DIR", value, 32768) == 0) return 5;
    const std::wstring directory(value);
    if (!privateAcl(directory.c_str())) return 6;
    wchar_t executable[32768]{};
    if (!GetModuleFileNameW(nullptr, executable, 32768)) return 7;
    const std::wstring path(executable);
    if (path.substr(0, path.find_last_of(L'\\')) != directory) return 8;
    HANDLE writer = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    if (writer != INVALID_HANDLE_VALUE) { CloseHandle(writer); return 9; }
    HANDLE deleter = CreateFileW(path.c_str(), DELETE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    if (deleter != INVALID_HANDLE_VALUE) { CloseHandle(deleter); return 10; }
    // A locked root must still allow the .NET bundle to create its private tree.
    const std::wstring child = directory + L"\\runtime-test";
    if (!CreateDirectoryW(child.c_str(), nullptr)) return 11;
    HANDLE file = CreateFileW((child + L"\\native-library-test.bin").c_str(), GENERIC_WRITE, 0,
        nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return 12;
    CloseHandle(file);
    _setmode(_fileno(stdout), _O_BINARY);
    if (std::strcmp(argv[1], "--probe-identity") == 0)
    {
        std::fputs("EXISTING_IDENTITY_UNAVAILABLE\n", stdout);
        std::fflush(stdout);
        return 21;
    }
    if (std::strcmp(argv[1], "--bridge") != 0) return 13;
    std::fputs("READY\n", stdout);
    std::fflush(stdout);
    char line[20]{};
    for (int round = 0; round < 2; ++round)
    {
        if (!std::fgets(line, sizeof(line), stdin) || std::strcmp(line, "CHECK\n") != 0) return 14;
        std::fputs("OK\n", stdout);
        std::fflush(stdout);
    }
    if (!std::fgets(line, sizeof(line), stdin)) return 15;
    std::fputs("DENIED\n", stdout);
    std::fflush(stdout);
    Sleep(10000);
    return 0;
}
