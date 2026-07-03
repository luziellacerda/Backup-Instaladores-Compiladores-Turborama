#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <algorithm>

namespace
{
    const wchar_t k = 0x005A;

    std::wstring Decode(const unsigned short* data)
    {
        std::wstring result;
        for (size_t i = 0; data[i] != 0x0000; ++i)
        {
            result.push_back(static_cast<wchar_t>(data[i] ^ k));
        }
        return result;
    }

    std::wstring GetModulePath()
    {
        std::vector<wchar_t> buffer(MAX_PATH);
        DWORD size = 0;

        while (true)
        {
            size = GetModuleFileNameW(NULL, buffer.data(), static_cast<DWORD>(buffer.size()));
            if (size == 0)
            {
                return L"";
            }

            if (size < buffer.size() - 1)
            {
                return std::wstring(buffer.data(), size);
            }

            buffer.resize(buffer.size() * 2);
        }
    }

    std::wstring GetDirectoryName(const std::wstring& fullPath)
    {
        size_t pos = fullPath.find_last_of(L"\\/");
        if (pos == std::wstring::npos)
        {
            return L".";
        }

        return fullPath.substr(0, pos);
    }

    std::wstring CombinePath(const std::wstring& a, const std::wstring& b)
    {
        if (a.empty())
        {
            return b;
        }

        wchar_t last = a[a.size() - 1];
        if (last == L'\\' || last == L'/')
        {
            return a + b;
        }

        return a + L"\\" + b;
    }

    std::wstring GetFullPathSafe(const std::wstring& path)
    {
        DWORD required = GetFullPathNameW(path.c_str(), 0, NULL, NULL);
        if (required == 0)
        {
            return path;
        }

        std::vector<wchar_t> buffer(required + 2);
        DWORD written = GetFullPathNameW(path.c_str(), static_cast<DWORD>(buffer.size()), buffer.data(), NULL);
        if (written == 0)
        {
            return path;
        }

        return std::wstring(buffer.data(), written);
    }

    bool SamePath(const std::wstring& a, const std::wstring& b)
    {
        std::wstring fa = GetFullPathSafe(a);
        std::wstring fb = GetFullPathSafe(b);
        return _wcsicmp(fa.c_str(), fb.c_str()) == 0;
    }

    bool ExistsFile(const std::wstring& path)
    {
        DWORD attrs = GetFileAttributesW(path.c_str());
        return attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) == 0;
    }

    std::wstring Quote(const std::wstring& s)
    {
        return L"\"" + s + L"\"";
    }

    std::vector<std::wstring> GetTargetCandidates()
    {
        // Caminhos codificados por XOR para dificultar busca simples em editor hexadecimal.
        static const unsigned short target1[] = { 0x0029, 0x0033, 0x0029, 0x002E, 0x003F, 0x0037, 0x003B, 0x0006, 0x000E, 0x002F, 0x0028, 0x0038, 0x0035, 0x0008, 0x003B, 0x0037, 0x003B, 0x0074, 0x003F, 0x0022, 0x003F, 0x0000 };
        static const unsigned short target2[] = { 0x0029, 0x0033, 0x0029, 0x002E, 0x003F, 0x0037, 0x003B, 0x0006, 0x002E, 0x002F, 0x0028, 0x0038, 0x0035, 0x0028, 0x003B, 0x0037, 0x003B, 0x0074, 0x003F, 0x0022, 0x003F, 0x0000 };
        static const unsigned short target3[] = { 0x0029, 0x0033, 0x0029, 0x002E, 0x003F, 0x0037, 0x003B, 0x0006, 0x000E, 0x002F, 0x0028, 0x0038, 0x0035, 0x0028, 0x003B, 0x0037, 0x003B, 0x0074, 0x003F, 0x0022, 0x003F, 0x0000 };

        std::vector<std::wstring> targets;
        targets.push_back(Decode(target1)); // sistema\TurboRama.exe
        targets.push_back(Decode(target2)); // sistema\turborama.exe
        targets.push_back(Decode(target3)); // sistema\Turborama.exe
        return targets;
    }

    std::wstring FindTarget(const std::wstring& launcherPath, const std::wstring& launcherDir)
    {
        std::vector<std::wstring> candidates = GetTargetCandidates();

        for (size_t i = 0; i < candidates.size(); ++i)
        {
            std::wstring candidate = CombinePath(launcherDir, candidates[i]);

            if (ExistsFile(candidate) && !SamePath(candidate, launcherPath))
            {
                return GetFullPathSafe(candidate);
            }
        }

        return L"";
    }

    void ShowError(const wchar_t* message)
    {
        MessageBoxW(NULL, message, L"TurboRama", MB_ICONERROR | MB_OK);
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR lpCmdLine, int nCmdShow)
{
    std::wstring launcherPath = GetModulePath();
    if (launcherPath.empty())
    {
        ShowError(L"Nao foi possivel localizar o executavel do launcher.");
        return 1;
    }

    std::wstring launcherDir = GetDirectoryName(launcherPath);
    std::wstring target = FindTarget(launcherPath, launcherDir);

    if (target.empty())
    {
        ShowError(L"Arquivo principal nao encontrado dentro da pasta sistema.\n\nVerifique se existe:\n.\\sistema\\TurboRama.exe");
        return 2;
    }

    std::wstring workingDir = GetDirectoryName(target);
    std::wstring commandLine = Quote(target);

    if (lpCmdLine != NULL && wcslen(lpCmdLine) > 0)
    {
        commandLine += L" ";
        commandLine += lpCmdLine;
    }

    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    ZeroMemory(&pi, sizeof(pi));
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = static_cast<WORD>(nCmdShow);

    std::vector<wchar_t> mutableCommandLine(commandLine.begin(), commandLine.end());
    mutableCommandLine.push_back(L'\0');

    BOOL ok = CreateProcessW(
        target.c_str(),
        mutableCommandLine.data(),
        NULL,
        NULL,
        FALSE,
        0,
        NULL,
        workingDir.c_str(),
        &si,
        &pi
    );

    if (!ok)
    {
        ShowError(L"Nao foi possivel iniciar o Sistema TurboRama.");
        return 3;
    }

    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    return 0;
}
