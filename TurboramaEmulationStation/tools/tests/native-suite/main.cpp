#include <Windows.h>
#include <TlHelp32.h>
#include "SuiteAccessGate.h"
#include <cstdio>
#include <cstring>
#include <string>

namespace
{
    bool ownHelperRunning()
    {
        const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE) return true;
        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        bool running = false;
        if (Process32FirstW(snapshot, &entry))
        {
            do
            {
                if (entry.th32ParentProcessID == GetCurrentProcessId() &&
                    _wcsicmp(entry.szExeFile, L"TurboRama.Suite.Access.exe") == 0)
                {
                    // Read/wait only, never termination rights to arbitrary PIDs.
                    const HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE,
                        FALSE, entry.th32ProcessID);
                    if (process)
                    {
                        running = WaitForSingleObject(process, 0) != WAIT_OBJECT_0;
                        CloseHandle(process);
                        if (running) break;
                    }
                    else if (GetLastError() != ERROR_INVALID_PARAMETER) { running = true; break; }
                }
            } while (Process32NextW(snapshot, &entry));
        }
        CloseHandle(snapshot);
        return running;
    }

    int lifecycle(const char* scenario)
    {
        // A completion event avoids retaining a dead process/image section
        // handle while production code removes its private extraction tree.
        const std::wstring eventName = L"Local\\TurboRama.Native.EofCompleted." +
            std::to_wstring(GetCurrentProcessId());
        const HANDLE completed = CreateEventW(nullptr, TRUE, FALSE, eventName.c_str());
        if (!completed) return 19;
        if (GetLastError() == ERROR_ALREADY_EXISTS) { CloseHandle(completed); return 19; }
        const ULONGLONG begin = GetTickCount64();
        bool unwound = false;
        int status = 0;
        try
        {
            auto run = [&]() -> int {
                std::string error;
                if (!SuiteAccessGate::instance().start(error)) return 20;
                SuiteAccessLifetime lifetime(SuiteAccessGate::instance());
                if (!ownHelperRunning() || !SuiteAccessGate::instance().authorized()) return 21;
                if (std::strcmp(scenario, "--lifecycle-early-return") == 0) return 0;
                if (std::strcmp(scenario, "--lifecycle-unwind") == 0) throw 73;
                SuiteAccessGate::instance().stop();
                return 0; // The scope's second stop must be harmless.
            };
            status = run();
        }
        catch (int value) { unwound = value == 73; }
        const ULONGLONG elapsed = GetTickCount64() - begin;
        const bool cleanupCompleted = WaitForSingleObject(completed, 0) == WAIT_OBJECT_0;
        CloseHandle(completed);
        if (status != 0 || ownHelperRunning() || SuiteAccessGate::instance().authorized()) return 22;
        if (std::strcmp(scenario, "--lifecycle-unwind") == 0 && !unwound) return 23;
        if (std::strcmp(scenario, "--lifecycle-stuck") == 0)
        {
            if (cleanupCompleted || elapsed < 5800 || elapsed > 11000) return 24;
        }
        else if (!cleanupCompleted || elapsed < 250 || elapsed > 5500) return 25;
        std::printf("SUITE_NATIVE_LIFECYCLE=OK scenario=%s elapsed_ms=%llu no_orphan=1\n",
            scenario, static_cast<unsigned long long>(elapsed));
        return 0;
    }
}

int main(int argc, char** argv)
{
    SetEnvironmentVariableW(L"DOTNET_STARTUP_HOOKS", L"untrusted-startup-hook");
    SetEnvironmentVariableW(L"CORECLR_ENABLE_PROFILING", L"1");
    SetEnvironmentVariableW(L"COR_ENABLE_PROFILING", L"1");
    SetEnvironmentVariableW(L"DOTNET_ADDITIONAL_DEPS", L"untrusted-dependencies");
    SetEnvironmentVariableW(L"DOTNET_SHARED_STORE", L"untrusted-store");
    SetEnvironmentVariableW(L"DOTNET_BUNDLE_EXTRACT_BASE_DIR", L"C:\\untrusted-cache");
    if (argc == 2 && std::strcmp(argv[1], "--suite-access-integrity-self-test") == 0)
        return SuiteAccessGate::verifyHelperIntegrity() ? 0 : 44;
    if (argc == 2 && std::strcmp(argv[1], "--suite-access-probe-identity") == 0)
        return SuiteAccessGate::probeIdentity();
    if (argc == 2 && (std::strcmp(argv[1], "--lifecycle-graceful") == 0 ||
        std::strcmp(argv[1], "--lifecycle-stuck") == 0 ||
        std::strcmp(argv[1], "--lifecycle-early-return") == 0 ||
        std::strcmp(argv[1], "--lifecycle-unwind") == 0)) return lifecycle(argv[1]);
    if (argc == 2 && (std::strcmp(argv[1], "--expect-cancelled") == 0 ||
        std::strcmp(argv[1], "--expect-initial-failure") == 0))
    {
        const bool cancelled = std::strcmp(argv[1], "--expect-cancelled") == 0;
        std::string error;
        if (SuiteAccessGate::instance().start(error) || SuiteAccessGate::instance().authorized()) return 30;
        if (cancelled != error.empty() || ownHelperRunning()) return 31;
        std::printf("SUITE_NATIVE_INITIAL_REPLY=OK cancelled=%d no_authorization=1 no_orphan=1\n", cancelled ? 1 : 0);
        return 0;
    }
    if (!SuiteAccessGate::runSelfTest()) return 1;
    if (argc == 2 && std::strcmp(argv[1], "--expect-integrity-failure") == 0)
    {
        if (SuiteAccessGate::verifyHelperIntegrity() || SuiteAccessGate::probeIdentity() != 44) return 2;
        std::string error;
        if (SuiteAccessGate::instance().start(error) || SuiteAccessGate::instance().authorized()) return 3;
        std::puts("SUITE_NATIVE_INVALID_RESOURCE_REJECTED=OK");
        return 0;
    }
    if (!SuiteAccessGate::verifyHelperIntegrity()) return 4;
    if (SuiteAccessGate::probeIdentity() != 21) return 5;
    std::string error;
    if (!SuiteAccessGate::instance().start(error)) { std::puts(error.c_str()); return 6; }
    if (!SuiteAccessGate::instance().authorized()) return 7;
    for (int elapsed = 0; elapsed < 6000 && SuiteAccessGate::instance().authorized(); elapsed += 50)
        Sleep(50);
    if (SuiteAccessGate::instance().authorized()) return 8;
    SuiteAccessGate::instance().stop();
    if (SuiteAccessGate::instance().authorized()) return 9;
    std::puts("SUITE_NATIVE_EMBED_PIPE_JOB_PIN_ACL_LOCK_ENV_REVOCATION_TEST=OK");
    return 0;
}
