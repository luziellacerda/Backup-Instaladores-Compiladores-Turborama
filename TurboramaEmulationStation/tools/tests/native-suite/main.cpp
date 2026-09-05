#include <Windows.h>
#include "SuiteAccessGate.h"
#include <cstdio>
#include <cstring>
#include <string>

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
