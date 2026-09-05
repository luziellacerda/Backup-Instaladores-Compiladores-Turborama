#Requires -Version 5.1
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Write-Utf8([string]$Path, [string]$Text) {
    $normalized = $Text.Replace("`r`n", "`n")
    if (-not $normalized.EndsWith("`n")) { $normalized += "`n" }
    [IO.File]::WriteAllText($Path, $normalized, [Text.UTF8Encoding]::new($false))
}

function Snapshot([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '<missing>' }
    return [Convert]::ToBase64String([IO.File]::ReadAllBytes($Path))
}

function File-Digest([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '<missing>' }
    $item = Get-Item -LiteralPath $Path
    return ('{0}:{1}' -f $item.Length, (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash)
}

function Set-FileLength([string]$Path, [long]$Length) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.SetLength($Length) } finally { $stream.Dispose() }
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('turborama-credit-failclosed-' + [Guid]::NewGuid().ToString('N'))
$buildRoot = Join-Path $testRoot 'build'
$harness = Join-Path $buildRoot 'credit-failclosed.exe'
$creditSource = Join-Path $projectRoot 'es-app\src\CreditManager.cpp'
$md5Source = Join-Path $projectRoot 'es-core\src\utils\md5.cpp'
$appInclude = Join-Path $projectRoot 'es-app\src'
$coreInclude = Join-Path $projectRoot 'es-core\src'

$config = @"
# TurboRama test fixture
schemaVersion=5
enabled=1
blockWithoutCredit=1
showHud=1
minutesPerCoin=30
debounceMs=350
maxRemainingSeconds=28800
priceCentsPerMinute=0
adminPasswordHash=pbkdf2-sha256`$210000`$00112233445566778899aabbccddeeff`$0000000000000000000000000000000000000000000000000000000000000000
"@

$validCredit = @"
schemaVersion=5
walletSchema=1
remainingSeconds=120
guestId=wallet-0123456789abcdef
guestRemainingSeconds=120
totalCoinsAccepted=1
totalMinutesSold=2
totalSecondsPlayed=3
currentPlayer=
"@

$validMirror = @"
# TurboRama Locadora - jogadores
schemaVersion=5
currentPlayer=
"@

$legacyCredit = @"
schemaVersion=4
remainingSeconds=1,20
totalCoinsAccepted=1
totalMinutesSold=2
totalSecondsPlayed=3
currentPlayer=Ana
"@

$legacyMirror = @"
# TurboRama legacy fixture
schemaVersion=4
currentPlayer=Ana
player=Ana;playedSeconds=5;remainingSeconds=0;totalMinutesPurchased=2
"@

$overCap = '315360001'
$legacyWalletCap = '604800'
$legacyWalletOverCap = '604801'
$schema5PlayerOverCap = @"
schemaVersion=5
walletSchema=1
remainingSeconds=120
guestId=wallet-0123456789abcdef
guestRemainingSeconds=120
totalCoinsAccepted=1
totalMinutesSold=2
totalSecondsPlayed=3
currentPlayer=
player=Ana;id=wallet-player-0123456789abcdef;playedSeconds=5;remainingSeconds=$overCap;totalMinutesPurchased=2;archived=0;tombstonedAt=0
"@

$schema5RetiredOverCap = $validCredit + "`nretiredGuest=id=wallet-retired-0123456789abcdef;remainingSeconds=$overCap;retiredAt=0`n"

$schema5MirrorOverCap = @"
# TurboRama Locadora - jogadores
schemaVersion=5
currentPlayer=Ana
player=Ana;id=wallet-player-0123456789abcdef;playedSeconds=5;remainingSeconds=$overCap;totalMinutesPurchased=2;archived=0;tombstonedAt=0
"@

$validWalletGraph = @"
schemaVersion=5
walletSchema=1
remainingSeconds=120
guestId=wallet-guest-0123456789abcdef
guestRemainingSeconds=30
totalCoinsAccepted=1
totalMinutesSold=2
totalSecondsPlayed=3
currentPlayer=Ana
player=Ana;id=wallet-alias-0123456789abcdef;playedSeconds=5;remainingSeconds=120;totalMinutesPurchased=2;archived=0;tombstonedAt=0
retiredGuest=id=wallet-retired-0123456789abcdef;remainingSeconds=60;retiredAt=1
retiredGuestAlias=wallet-alias-0123456789abcdef
pixTransaction=tx-valid-ledger
"@

$validSchema5PlayerMirror = @"
# TurboRama derived schema 5 mirror
schemaVersion=5
currentPlayer=Ana
player=Ana;id=wallet-player-0123456789abcdef;playedSeconds=5;remainingSeconds=120;totalMinutesPurchased=2;archived=0;tombstonedAt=0
"@

# Here-strings inherit the checkout's CRLF on Windows. Normalize fixtures before
# regex mutations, otherwise invalid-config cases accidentally test valid input.
foreach ($fixtureName in @('config', 'validCredit', 'validMirror', 'legacyCredit',
    'legacyMirror', 'validWalletGraph', 'validSchema5PlayerMirror')) {
    $fixture = Get-Variable -Name $fixtureName -ValueOnly
    Set-Variable -Name $fixtureName -Value $fixture.Replace("`r`n", "`n")
}

$largeSchema5GuestWallet = ($validCredit -replace '(?m)^remainingSeconds=120$', 'remainingSeconds=36000') `
    -replace 'guestRemainingSeconds=120', 'guestRemainingSeconds=36000'

$largeSchema5PlayerWallet = @"
schemaVersion=5
walletSchema=1
remainingSeconds=36000
guestId=wallet-guest-0123456789abcdef
guestRemainingSeconds=30
totalCoinsAccepted=1
totalMinutesSold=600
totalSecondsPlayed=3
currentPlayer=Ana
player=Ana;id=wallet-player-0123456789abcdef;playedSeconds=5;remainingSeconds=36000;totalMinutesPurchased=600;archived=0;tombstonedAt=0
pixTransaction=tx-large-wallet
"@

try {
    [IO.Directory]::CreateDirectory($buildRoot) | Out-Null
    $creditSourceText = [IO.File]::ReadAllText($creditSource)
    Assert ($creditSourceText -match 'O_CLOEXEC') 'lock POSIX nao solicita O_CLOEXEC.'
    Assert ($creditSourceText -match 'FD_CLOEXEC') 'lock POSIX nao valida close-on-exec via fcntl.'

    Write-Utf8 (Join-Path $buildRoot 'stubs.cpp') @'
#include "Log.h"
#include "Paths.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"
#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <filesystem>
#include <windows.h>

LogLevel Log::mReportingLevel = LogError;
bool Log::mEnabled = false;
Log::Log(LogLevel level) : mLevel(level) {}
Log::~Log() {}
std::ostringstream& Log::stream() { static std::ostringstream sink; return sink; }
void Log::init() {}
void Log::flush() {}
void Log::close() {}

Paths* Paths::_instance = nullptr;
Paths::Paths()
{
    const char* root = std::getenv("TURBORAMA_CREDIT_TEST_ROOT");
    mUserEmulationStationPath = root ? root : ".";
}

namespace Utils { namespace String {
std::string trim(const std::string& value)
{
    const size_t first = value.find_first_not_of(" \t\r\n");
    if (first == std::string::npos) return std::string();
    const size_t last = value.find_last_not_of(" \t\r\n");
    return value.substr(first, last - first + 1);
}
std::string toLower(const std::string& value)
{
    std::string result(value);
    std::transform(result.begin(), result.end(), result.begin(),
        [](unsigned char ch) { return (char)std::tolower(ch); });
    return result;
}
int toInteger(const std::string& value) { try { return std::stoi(value); } catch (...) { return 0; } }
const std::wstring convertToWideString(const std::string& value)
{
    if (value.empty()) return std::wstring();
    const int count = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
    std::wstring result((size_t)count, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, &result[0], count);
    result.pop_back();
    return result;
}
} }

namespace Utils { namespace FileSystem {
std::string combine(const std::string& left, const std::string& right)
{ return (std::filesystem::u8path(left) / std::filesystem::u8path(right)).u8string(); }
std::string getParent(const std::string& path)
{ return std::filesystem::u8path(path).parent_path().u8string(); }
bool createDirectory(const std::string& path)
{ std::error_code error; return std::filesystem::create_directories(std::filesystem::u8path(path), error) || !error; }
bool exists(const std::string& path, bool)
{ std::error_code error; return std::filesystem::exists(std::filesystem::u8path(path), error); }
bool removeFile(const std::string& path)
{ std::error_code error; return std::filesystem::remove(std::filesystem::u8path(path), error); }
bool renameFile(const std::string& source, const std::string& target, bool overwrite)
{
    const DWORD flags = overwrite ? MOVEFILE_REPLACE_EXISTING : 0;
    return MoveFileExW(Utils::String::convertToWideString(source).c_str(),
        Utils::String::convertToWideString(target).c_str(), flags) != FALSE;
}
} }
'@

    Write-Utf8 (Join-Path $buildRoot 'harness.cpp') @'
#include "CreditManager.h"
#include <cstdlib>
#include <string>
#include <windows.h>

static std::string testPath(const char* fileName)
{
    const char* root = std::getenv("TURBORAMA_CREDIT_TEST_ROOT");
    return std::string(root ? root : ".") + "\\" + fileName;
}

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    const std::string mode = argv[1];
    if (mode == "authority-no-mirror-read"
        && _putenv_s("TURBORAMA_CREDIT_TEST_ABORT_ON_MIRROR_READ", "1") != 0)
        return 4;
    if (mode == "config-load-write-fail"
        && _putenv_s("TURBORAMA_CREDIT_TEST_FAIL_ATOMIC_PATH", "arcade_credit.cfg") != 0)
        return 38;
    CreditManager& credits = CreditManager::getInstance();
    _putenv_s("TURBORAMA_CREDIT_TEST_ABORT_ON_MIRROR_READ", "");
    if (mode == "config-load-write-fail")
        _putenv_s("TURBORAMA_CREDIT_TEST_FAIL_ATOMIC_PATH", "");
    if (mode == "concurrent-a" || mode == "concurrent-b")
    {
        const bool active = credits.hasCredit() && credits.getRemainingSeconds() == 120;
        const char suffix = mode == "concurrent-a" ? 'a' : 'b';
        const std::string stateName = std::string(active ? "active-" : "blocked-") + suffix;
        HANDLE state = CreateFileA(testPath(stateName.c_str()).c_str(), GENERIC_WRITE, 0,
            nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (state == INVALID_HANDLE_VALUE) return 5;
        CloseHandle(state);
        const char* ownName = mode == "concurrent-a" ? "ready-a" : "ready-b";
        const char* peerName = mode == "concurrent-a" ? "ready-b" : "ready-a";
        HANDLE marker = CreateFileA(testPath(ownName).c_str(), GENERIC_WRITE, 0,
            nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (marker == INVALID_HANDLE_VALUE) return 6;
        CloseHandle(marker);
        bool peerReady = false;
        for (int attempt = 0; attempt < 1000; ++attempt)
        {
            if (GetFileAttributesA(testPath(peerName).c_str()) != INVALID_FILE_ATTRIBUTES)
            {
                peerReady = true;
                break;
            }
            Sleep(10);
        }
        if (!peerReady) return 7;
        if (active)
        {
            if (!credits.addMinutes(1)) return 8;
            Sleep(250);
            return 0;
        }
        if (credits.getRemainingSeconds() != 0 || credits.hasCredit()
            || credits.addMinutes(1)) return 9;
        return 0;
    }
    if (mode == "fresh")
    {
        if (credits.hasCredit() || credits.getRemainingSeconds() != 0) return 13;
        return credits.addMinutes(1) ? 0 : 14;
    }
    if (mode.rfind("capacity-", 0) == 0)
    {
        const bool guestBalance = mode == "capacity-guest-noop"
            || mode == "capacity-rotate" || mode == "capacity-new";
        if (credits.getRemainingSeconds() != (guestBalance ? 60 : 0)
            || credits.hasCredit() != guestBalance) return 15;
        if (mode == "capacity-noop" || mode == "capacity-guest-noop") return 0;
        if (mode == "capacity-register")
        {
            if (credits.registerPlayer("Archived")) return 16;
        }
        else if (mode == "capacity-archived-pix")
        {
            if (credits.applyPixCredit("tx-capacity-archived", 1, "player",
                "wallet-archived-capacity") != PixCreditResult::Rejected) return 17;
        }
        else if (mode == "capacity-retired-pix")
        {
            if (credits.applyPixCredit("tx-capacity-retired", 1, "guest",
                "wallet-retired-capacity") != PixCreditResult::Rejected) return 18;
        }
        else if (mode == "capacity-rotate")
        {
            if (credits.switchToPlayer("P000")) return 19;
            if (credits.getRemainingSeconds() != 60 || !credits.hasCredit()) return 27;
        }
        else if (mode == "capacity-new")
        {
            if (credits.registerPlayer("Novo")) return 28;
            if (credits.getRemainingSeconds() != 60 || !credits.hasCredit()) return 29;
        }
        else return 33;
        // A refusal is operational, not a persistence latch.
        if (!credits.setPriceCentsPerMinute(123)) return 34;
        return 0;
    }
    if (mode == "size-near-limit" || mode == "size-cross-limit")
    {
        if (credits.getRemainingSeconds() != 120 || !credits.hasCredit()) return 35;
        if (mode == "size-near-limit") return 0;
        const std::string transactionId = std::string("tx-size-cross-") + std::string(50, 'z');
        if (credits.applyPixCredit(transactionId, 1) != PixCreditResult::Rejected) return 36;
        if (credits.hasCredit() || credits.getRemainingSeconds() != 0) return 37;
        return 0;
    }
    if (mode == "retired-limit-noop" || mode == "retired-limit-rotate")
    {
        if (credits.getRemainingSeconds() != 120 || !credits.hasCredit()) return 44;
        if (mode == "retired-limit-noop") return 0;
        credits.clearGuestCredit();
        if (credits.getRemainingSeconds() != 120 || !credits.hasCredit()) return 45;
        if (!credits.setPriceCentsPerMinute(123)) return 46;
        return 0;
    }
    if (mode == "ledger-limit-noop" || mode == "ledger-limit-pix")
    {
        if (credits.getRemainingSeconds() != 120 || !credits.hasCredit()) return 47;
        if (mode == "ledger-limit-noop") return 0;
        if (credits.applyPixCredit("tx-ledger-cap-new", 1) != PixCreditResult::Rejected) return 48;
        if (credits.getRemainingSeconds() != 120 || !credits.hasCredit()) return 49;
        if (!credits.setPriceCentsPerMinute(123)) return 50;
        return 0;
    }
    if (mode == "invalid" || mode == "config-load-write-fail")
    {
        if (credits.hasCredit()) return 10;
        if (credits.addMinutes(1)) return 11;
        if (credits.setPriceCentsPerMinute(123)) return 12;
        credits.save();
        credits.savePlayers();
        credits.flushNow();
        return 0;
    }
    const long expectedSeconds = mode == "legacy60" ? 60
        : (mode == "legacy-cap" ? 604800
        : (mode == "large-wallet" ? 36000
        : (mode == "recover-lock" ? 180 : 120)));
    if (credits.getRemainingSeconds() != expectedSeconds || !credits.hasCredit()) return 20;
    if (mode == "valid") return credits.addMinutes(1) ? 0 : 21;
    if (mode == "recover-lock") return credits.addMinutes(1) ? 0 : 25;
    if (mode == "authority-no-mirror-read") return 0;
    if (mode == "large-wallet")
    {
        std::string beneficiaryType;
        std::string beneficiaryId;
        if (!credits.getPixBeneficiary(beneficiaryType, beneficiaryId)
            || !credits.canAcceptPixMinutes(beneficiaryType, beneficiaryId, 1)) return 26;
        return 0;
    }
    if (mode == "legacy" || mode == "legacy60" || mode == "legacy-cap") return 0;

    const bool writeFailure = mode == "write-fail-add" || mode == "write-fail-coin"
        || mode == "write-fail-debit";
    const bool replaceFailure = mode == "replace-fail-add" || mode == "replace-fail-coin"
        || mode == "replace-fail-debit";
    const bool mirrorFailure = mode == "mirror-best-effort";
    HANDLE lockedAuthority = INVALID_HANDLE_VALUE;
    if (writeFailure)
    {
        if (_putenv_s("TURBORAMA_CREDIT_TEST_FAIL_ATOMIC_PATH",
            "arcade_credit.dat") != 0) return 30;
    }
    else if (replaceFailure)
    {
        lockedAuthority = CreateFileA(testPath("arcade_credit.dat").c_str(),
            GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (lockedAuthority == INVALID_HANDLE_VALUE) return 31;
    }
    else if (mirrorFailure)
    {
        if (_putenv_s("TURBORAMA_CREDIT_TEST_FAIL_ATOMIC_PATH",
            "arcade_players.dat") != 0) return 32;
    }
    else return 3;

    bool operationResult = false;
    if (mode.find("add") != std::string::npos || mirrorFailure)
        operationResult = credits.addMinutes(1);
    else if (mode.find("coin") != std::string::npos)
        operationResult = credits.addCoin();
    else
    {
        credits.beginGameSession();
        operationResult = credits.updateGameSession(5);
    }
    if (lockedAuthority != INVALID_HANDLE_VALUE) CloseHandle(lockedAuthority);
    _putenv_s("TURBORAMA_CREDIT_TEST_FAIL_ATOMIC_PATH", "");

    if (mirrorFailure)
    {
        if (!operationResult || !credits.hasCredit() || credits.getRemainingSeconds() != 180) return 40;
        return 0;
    }
    if (operationResult) return 41;
    if (credits.hasCredit() || credits.getRemainingSeconds() != 0) return 42;
    if (credits.addMinutes(1) || credits.updateGameSession(6)) return 43;
    return 0;
}
'@

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    Assert (Test-Path -LiteralPath $vswhere -PathType Leaf) 'vswhere.exe nao encontrado.'
    $vsPath = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    Assert (-not [string]::IsNullOrWhiteSpace($vsPath)) 'MSVC nao encontrado.'
    $vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
    $compile = '"{0}" >nul && cl.exe /nologo /DNOMINMAX /DCREDIT_MANAGER_TEST_HOOKS /std:c++17 /EHsc "{1}" "{2}" "{3}" "{4}" /I"{5}" /I"{6}" /Fe:"{7}" bcrypt.lib' -f `
        $vcvars, $creditSource, $md5Source, (Join-Path $buildRoot 'stubs.cpp'), (Join-Path $buildRoot 'harness.cpp'), $appInclude, $coreInclude, $harness
    & cmd.exe /d /s /c $compile
    if ($LASTEXITCODE -ne 0) { throw "Compilacao do harness falhou: $LASTEXITCODE" }

    function New-Fixture([string]$Name, [string]$CreditText, [string]$PlayersText = $validMirror) {
        $root = Join-Path $testRoot $Name
        [IO.Directory]::CreateDirectory($root) | Out-Null
        Write-Utf8 (Join-Path $root 'arcade_credit.cfg') $config
        Write-Utf8 (Join-Path $root 'arcade_credit.dat') $CreditText
        Write-Utf8 (Join-Path $root 'arcade_players.dat') $PlayersText
        return $root
    }

    function New-CapacityCredit([int]$ActiveCount, [int]$GuestSeconds,
        [bool]$IncludeArchived, [bool]$IncludeRetired) {
        $builder = [Text.StringBuilder]::new()
        [void]$builder.AppendLine('schemaVersion=5')
        [void]$builder.AppendLine('walletSchema=1')
        [void]$builder.AppendLine("remainingSeconds=$GuestSeconds")
        [void]$builder.AppendLine('guestId=wallet-guest-capacity')
        [void]$builder.AppendLine("guestRemainingSeconds=$GuestSeconds")
        [void]$builder.AppendLine('totalCoinsAccepted=0')
        [void]$builder.AppendLine('totalMinutesSold=0')
        [void]$builder.AppendLine('totalSecondsPlayed=0')
        [void]$builder.AppendLine('currentPlayer=')
        for ($index = 0; $index -lt $ActiveCount; $index++) {
            $name = 'P{0:D3}' -f $index
            $id = 'wallet-player-capacity-{0:D4}' -f $index
            [void]$builder.AppendLine("player=$name;id=$id;playedSeconds=0;remainingSeconds=0;totalMinutesPurchased=0;archived=0;tombstonedAt=0")
        }
        if ($IncludeArchived) {
            [void]$builder.AppendLine('player=Archived;id=wallet-archived-capacity;playedSeconds=0;remainingSeconds=0;totalMinutesPurchased=0;archived=1;tombstonedAt=1')
        }
        if ($IncludeRetired) {
            [void]$builder.AppendLine('retiredGuest=id=wallet-retired-capacity;remainingSeconds=60;retiredAt=1')
        }
        return $builder.ToString()
    }

    function Open-Utf8Writer([string]$Path) {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Create,
            [IO.FileAccess]::Write, [IO.FileShare]::None)
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        $writer.NewLine = "`n"
        return $writer
    }

    function Write-RetiredLimitAuthority([string]$Path) {
        $writer = Open-Utf8Writer $Path
        try {
            $base = $validCredit.Replace("`r`n", "`n")
            if (-not $base.EndsWith("`n")) { $base += "`n" }
            $writer.Write($base)
            for ($index = 0; $index -lt 100000; $index++) {
                $id = 'r{0:D15}' -f $index
                $writer.WriteLine("retiredGuest=id=$id;remainingSeconds=0;retiredAt=0")
            }
        }
        finally { $writer.Dispose() }
    }

    function Write-LedgerLimitAuthority([string]$Path) {
        $writer = Open-Utf8Writer $Path
        try {
            $base = $validCredit.Replace("`r`n", "`n")
            if (-not $base.EndsWith("`n")) { $base += "`n" }
            $writer.Write($base)
            for ($index = 0; $index -lt 100000; $index++) {
                $writer.WriteLine(('pixTransaction=t{0:D5}' -f $index))
            }
        }
        finally { $writer.Dispose() }
    }

    function Write-NearLimitAuthority([string]$Path) {
        $limit = 8MB
        $base = $validCredit.Replace("`r`n", "`n")
        if (-not $base.EndsWith("`n")) { $base += "`n" }
        $encoding = [Text.UTF8Encoding]::new($false)
        $fullId = 'r000000-' + ('x' * 120)
        $fullLine = "retiredGuest=id=$fullId;remainingSeconds=0;retiredAt=0`n"
        $baseBytes = $encoding.GetByteCount($base)
        $fullLineBytes = $encoding.GetByteCount($fullLine)
        $fullCount = [Math]::Floor(($limit - $baseBytes) / $fullLineBytes)
        $writer = Open-Utf8Writer $Path
        try {
            $writer.Write($base)
            for ($index = 0; $index -lt $fullCount; $index++) {
                $prefix = 'r{0:D6}-' -f $index
                $id = $prefix + ('x' * (128 - $prefix.Length))
                $writer.Write("retiredGuest=id=$id;remainingSeconds=0;retiredAt=0`n")
            }
            $used = $baseBytes + ($fullCount * $fullLineBytes)
            $remaining = $limit - $used
            $fixedBytes = $encoding.GetByteCount("retiredGuest=id=;remainingSeconds=0;retiredAt=0`n")
            $lastIdLength = $remaining - $fixedBytes
            if ($lastIdLength -ge 16 -and $lastIdLength -le 128) {
                $lastId = 'last-' + ('q' * ($lastIdLength - 5))
                $writer.Write("retiredGuest=id=$lastId;remainingSeconds=0;retiredAt=0`n")
            }
        }
        finally { $writer.Dispose() }
        $length = (Get-Item -LiteralPath $Path).Length
        $newPixLineBytes = $encoding.GetByteCount(('pixTransaction=' + 'tx-size-cross-' + ('z' * 50) + "`n"))
        Assert ($length -le $limit) 'near-limit: fixture excedeu limite de leitura.'
        Assert (($limit - $length) -lt $newPixLineBytes) 'near-limit: fixture nao ficou proxima o suficiente do limite.'
    }

    function Invoke-Case([string]$Root, [string]$Mode) {
        $old = $env:TURBORAMA_CREDIT_TEST_ROOT
        try {
            $env:TURBORAMA_CREDIT_TEST_ROOT = $Root
            & $harness $Mode
            if ($LASTEXITCODE -ne 0) { throw "Harness $Mode em $Root falhou: $LASTEXITCODE" }
        }
        finally { $env:TURBORAMA_CREDIT_TEST_ROOT = $old }
    }

    $freshRoot = Join-Path $testRoot 'fresh-install'
    [IO.Directory]::CreateDirectory($freshRoot) | Out-Null
    Invoke-Case $freshRoot 'fresh'
    Assert (Test-Path -LiteralPath (Join-Path $freshRoot 'arcade_credit.lock') -PathType Leaf) 'fresh: lock regular nao foi criado.'
    Assert (Test-Path -LiteralPath (Join-Path $freshRoot 'arcade_credit.cfg') -PathType Leaf) 'fresh: configuracao nao foi criada.'
    $freshStored = [IO.File]::ReadAllText((Join-Path $freshRoot 'arcade_credit.dat'))
    Assert ($freshStored -match '(?m)^remainingSeconds=60\r?$') 'fresh: primeira operacao financeira falhou.'

    foreach ($configCase in @(
        @{ Name='config-schema6'; Text=($config -replace 'schemaVersion=5', 'schemaVersion=6') },
        @{ Name='config-schema3'; Text=($config -replace 'schemaVersion=5', 'schemaVersion=3') },
        @{ Name='config-missing-schema'; Text=($config -replace "schemaVersion=5`n", '') },
        @{ Name='config-duplicate'; Text=($config + "`nenabled=1`n") },
        @{ Name='config-unknown-key'; Text=($config + "`nfutureSetting=1`n") },
        @{ Name='config-line-without-equals'; Text=($config + "`nbroken-line`n") },
        @{ Name='config-invalid-bool'; Text=($config -replace 'enabled=1', 'enabled=yes') },
        @{ Name='config-invalid-number'; Text=($config -replace 'minutesPerCoin=30', 'minutesPerCoin=0') },
        @{ Name='config-v5-locale-number'; Text=($config -replace 'minutesPerCoin=30', 'minutesPerCoin=3,0') },
        @{ Name='config-both-credentials'; Text=($config + "`nadminPassword=senha123`n") },
        @{ Name='config-v5-plaintext'; Text=($config -replace 'adminPasswordHash=.*', 'adminPassword=senha123') }
    )) {
        $configRoot = New-Fixture $configCase.Name $validCredit
        $configPath = Join-Path $configRoot 'arcade_credit.cfg'
        $configCredit = Join-Path $configRoot 'arcade_credit.dat'
        $configMirror = Join-Path $configRoot 'arcade_players.dat'
        Write-Utf8 $configPath $configCase.Text
        $beforeBadConfig = Snapshot $configPath
        $beforeBadConfigCredit = Snapshot $configCredit
        $beforeBadConfigMirror = Snapshot $configMirror
        Invoke-Case $configRoot 'invalid'
        Assert ((Snapshot $configPath) -eq $beforeBadConfig) "$($configCase.Name): config invalida foi regravada."
        Assert ((Snapshot $configCredit) -eq $beforeBadConfigCredit) "$($configCase.Name): authority foi alterada."
        Assert ((Snapshot $configMirror) -eq $beforeBadConfigMirror) "$($configCase.Name): espelho foi alterado."
    }

    $legacyConfigBase = @"
schemaVersion=4
enabled=true
blockWithoutCredit=1
showHud=false
minutesPerCoin=30
debounceMs=350
maxRemainingSeconds=28,800
priceCentsPerMinute=0
"@
    foreach ($legacyConfigCase in @(
        @{ Name='config-v4-hash-valid'; Credential='adminPasswordHash=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' },
        @{ Name='config-v4-plaintext-valid'; Credential='adminPassword=senha123' }
    )) {
        $legacyConfigRoot = New-Fixture $legacyConfigCase.Name $validCredit
        Write-Utf8 (Join-Path $legacyConfigRoot 'arcade_credit.cfg') ($legacyConfigBase + "`n" + $legacyConfigCase.Credential)
        Invoke-Case $legacyConfigRoot 'legacy'
        $migratedConfig = [IO.File]::ReadAllText((Join-Path $legacyConfigRoot 'arcade_credit.cfg'))
        Assert ($migratedConfig -match '(?m)^schemaVersion=5\r?$') "$($legacyConfigCase.Name): schema 4 nao foi migrado."
        Assert ($migratedConfig -match '(?m)^maxRemainingSeconds=28800\r?$') "$($legacyConfigCase.Name): numero legado valido foi perdido."
        Assert ($migratedConfig -match '(?m)^adminPasswordHash=') "$($legacyConfigCase.Name): credencial nao foi migrada."
        Assert ($migratedConfig -notmatch '(?m)^adminPassword=') "$($legacyConfigCase.Name): plaintext permaneceu na config."
    }

    $configRewriteFailureRoot = New-Fixture 'config-v4-rewrite-failure' $validCredit
    $configRewriteFailurePath = Join-Path $configRewriteFailureRoot 'arcade_credit.cfg'
    $configRewriteFailureCredit = Join-Path $configRewriteFailureRoot 'arcade_credit.dat'
    $configRewriteFailureMirror = Join-Path $configRewriteFailureRoot 'arcade_players.dat'
    Write-Utf8 $configRewriteFailurePath ($legacyConfigBase + "`nadminPasswordHash=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa`n")
    $beforeConfigRewriteFailure = Snapshot $configRewriteFailurePath
    $beforeConfigRewriteFailureCredit = Snapshot $configRewriteFailureCredit
    $beforeConfigRewriteFailureMirror = Snapshot $configRewriteFailureMirror
    Invoke-Case $configRewriteFailureRoot 'config-load-write-fail'
    Assert ((Snapshot $configRewriteFailurePath) -eq $beforeConfigRewriteFailure) 'config rewrite failure: config foi alterada.'
    Assert ((Snapshot $configRewriteFailureCredit) -eq $beforeConfigRewriteFailureCredit) 'config rewrite failure: authority foi alterada.'
    Assert ((Snapshot $configRewriteFailureMirror) -eq $beforeConfigRewriteFailureMirror) 'config rewrite failure: mirror foi alterado.'

    $configCreateFailureRoot = New-Fixture 'config-bootstrap-write-failure' $validCredit
    $configCreateFailurePath = Join-Path $configCreateFailureRoot 'arcade_credit.cfg'
    $configCreateFailureCredit = Join-Path $configCreateFailureRoot 'arcade_credit.dat'
    $configCreateFailureMirror = Join-Path $configCreateFailureRoot 'arcade_players.dat'
    [IO.File]::Delete($configCreateFailurePath)
    $beforeConfigCreateFailureCredit = Snapshot $configCreateFailureCredit
    $beforeConfigCreateFailureMirror = Snapshot $configCreateFailureMirror
    Invoke-Case $configCreateFailureRoot 'config-load-write-fail'
    Assert ((Snapshot $configCreateFailurePath) -eq '<missing>') 'config bootstrap failure: config parcial foi criada.'
    Assert ((Snapshot $configCreateFailureCredit) -eq $beforeConfigCreateFailureCredit) 'config bootstrap failure: authority foi alterada.'
    Assert ((Snapshot $configCreateFailureMirror) -eq $beforeConfigCreateFailureMirror) 'config bootstrap failure: mirror foi alterado.'

    $capacityArchived = New-CapacityCredit 500 0 $true $false
    foreach ($capacityCase in @(
        @{ Name='capacity-register-archived'; Text=$capacityArchived; Pre='capacity-noop'; Mode='capacity-register' },
        @{ Name='capacity-pix-archived'; Text=$capacityArchived; Pre='capacity-noop'; Mode='capacity-archived-pix' },
        @{ Name='capacity-pix-retired'; Text=(New-CapacityCredit 500 0 $false $true); Pre='capacity-noop'; Mode='capacity-retired-pix' },
        @{ Name='capacity-guest-rotate'; Text=(New-CapacityCredit 500 60 $false $false); Pre='capacity-guest-noop'; Mode='capacity-rotate' },
        @{ Name='capacity-new-with-recovery'; Text=(New-CapacityCredit 499 60 $false $false); Pre='capacity-guest-noop'; Mode='capacity-new' }
    )) {
        $capacityRoot = New-Fixture $capacityCase.Name $capacityCase.Text
        $capacityAuthority = Join-Path $capacityRoot 'arcade_credit.dat'
        Invoke-Case $capacityRoot $capacityCase.Pre
        $beforeCapacityRefusal = Snapshot $capacityAuthority
        Invoke-Case $capacityRoot $capacityCase.Mode
        Assert ((Snapshot $capacityAuthority) -eq $beforeCapacityRefusal) "$($capacityCase.Name): recusa alterou authority."
        Invoke-Case $capacityRoot $capacityCase.Pre
        Assert ((Snapshot $capacityAuthority) -eq $beforeCapacityRefusal) "$($capacityCase.Name): estado nao recarregou identico."
    }

    $retiredLimitRoot = New-Fixture 'retired-limit-transactional' $validCredit
    $retiredLimitAuthority = Join-Path $retiredLimitRoot 'arcade_credit.dat'
    Write-RetiredLimitAuthority $retiredLimitAuthority
    Invoke-Case $retiredLimitRoot 'retired-limit-noop'
    $beforeRetiredLimit = File-Digest $retiredLimitAuthority
    Invoke-Case $retiredLimitRoot 'retired-limit-rotate'
    Assert ((File-Digest $retiredLimitAuthority) -eq $beforeRetiredLimit) 'retired limit: rotacao descartou/substituiu tombstone.'
    Invoke-Case $retiredLimitRoot 'retired-limit-noop'

    $ledgerLimitRoot = New-Fixture 'ledger-limit-transactional' $validCredit
    $ledgerLimitAuthority = Join-Path $ledgerLimitRoot 'arcade_credit.dat'
    Write-LedgerLimitAuthority $ledgerLimitAuthority
    Invoke-Case $ledgerLimitRoot 'ledger-limit-noop'
    $beforeLedgerLimit = File-Digest $ledgerLimitAuthority
    Invoke-Case $ledgerLimitRoot 'ledger-limit-pix'
    Assert ((File-Digest $ledgerLimitAuthority) -eq $beforeLedgerLimit) 'ledger limit: PIX apagou idempotencia antiga ou alterou saldo.'
    Invoke-Case $ledgerLimitRoot 'ledger-limit-noop'

    $nearLimitRoot = New-Fixture 'writer-near-read-limit' $validCredit
    $nearLimitAuthority = Join-Path $nearLimitRoot 'arcade_credit.dat'
    $nearLimitMirror = Join-Path $nearLimitRoot 'arcade_players.dat'
    Write-NearLimitAuthority $nearLimitAuthority
    Invoke-Case $nearLimitRoot 'size-near-limit'
    $beforeNearLimitAuthority = File-Digest $nearLimitAuthority
    $beforeNearLimitMirror = Snapshot $nearLimitMirror
    Invoke-Case $nearLimitRoot 'size-cross-limit'
    Assert ((File-Digest $nearLimitAuthority) -eq $beforeNearLimitAuthority) 'writer limit: authority ilegivel foi commitada.'
    Assert ((Snapshot $nearLimitMirror) -eq $beforeNearLimitMirror) 'writer limit: mirror mudou apos falha da authority.'
    Invoke-Case $nearLimitRoot 'size-near-limit'

    foreach ($case in @(
        @{ Name='truncated'; Text="schemaVersion=5`nwalletSchema=1`nremainingSeconds=120`n" },
        @{ Name='corrupt'; Text=($validCredit -replace 'guestRemainingSeconds=120', 'guestRemainingSeconds=-120') },
        @{ Name='legacy-negative'; Text=($legacyCredit -replace 'remainingSeconds=1,20', 'remainingSeconds=-120') },
        @{ Name='legacy-noise'; Text=($legacyCredit -replace 'remainingSeconds=1,20', 'remainingSeconds=1x20') },
        @{ Name='legacy-overflow'; Text=($legacyCredit -replace 'remainingSeconds=1,20', 'remainingSeconds=999999999120') },
        @{ Name='legacy-over-cap'; Text=($legacyCredit -replace 'remainingSeconds=1,20', "remainingSeconds=$overCap") },
        @{ Name='legacy-over-historical-cap'; Text=($legacyCredit -replace 'remainingSeconds=1,20', "remainingSeconds=$legacyWalletOverCap") },
        @{ Name='legacy-100m-over-historical-cap'; Text=($legacyCredit -replace 'remainingSeconds=1,20', 'remainingSeconds=100000000') },
        @{ Name='legacy-counter-negative'; Text=($legacyCredit -replace 'totalCoinsAccepted=1', 'totalCoinsAccepted=-1') },
        @{ Name='schema5-active-over-cap'; Text=($validCredit -replace '(?m)^remainingSeconds=120$', "remainingSeconds=$overCap") },
        @{ Name='schema5-guest-over-cap'; Text=($validCredit -replace 'guestRemainingSeconds=120', "guestRemainingSeconds=$overCap") },
        @{ Name='schema5-player-over-cap'; Text=$schema5PlayerOverCap },
        @{ Name='schema5-retired-over-cap'; Text=$schema5RetiredOverCap },
        @{ Name='schema6-forward'; Text=($validCredit -replace 'schemaVersion=5', 'schemaVersion=6') },
        @{ Name='schema6-forward-no-marker'; Text=(($validCredit -replace 'schemaVersion=5', 'schemaVersion=6') -replace "walletSchema=1`n", '') },
        @{ Name='legacy-schema3'; Text=($legacyCredit -replace 'schemaVersion=4', 'schemaVersion=3') },
        @{ Name='legacy-missing-schema'; Text=($legacyCredit -replace "schemaVersion=4`n", '') },
        @{ Name='legacy-duplicate-schema'; Text=($legacyCredit + "`nschemaVersion=4`n") },
        @{ Name='legacy-duplicate-remaining'; Text=($legacyCredit + "`nremainingSeconds=120`n") },
        @{ Name='legacy-duplicate-coins'; Text=($legacyCredit + "`ntotalCoinsAccepted=1`n") },
        @{ Name='legacy-duplicate-minutes-sold'; Text=($legacyCredit + "`ntotalMinutesSold=2`n") },
        @{ Name='legacy-duplicate-seconds-played'; Text=($legacyCredit + "`ntotalSecondsPlayed=3`n") },
        @{ Name='legacy-duplicate-current'; Text=($legacyCredit + "`ncurrentPlayer=Ana`n") },
        @{ Name='legacy-invalid-current'; Text=($legacyCredit -replace 'currentPlayer=Ana', 'currentPlayer=Ana;invalida') },
        @{ Name='legacy-unknown-key'; Text=($legacyCredit + "`nunknownFinancialField=120`n") },
        @{ Name='legacy-wallet-marker'; Text=($legacyCredit + "`nwalletSchema=1`n") },
        @{ Name='legacy-wallet-guest-id'; Text=($legacyCredit + "`nguestId=wallet-guest-0123456789abcdef`n") },
        @{ Name='legacy-wallet-guest-balance'; Text=($legacyCredit + "`nguestRemainingSeconds=120`n") },
        @{ Name='legacy-wallet-player'; Text=($legacyCredit + "`nplayer=Ana;id=wallet-player-0123456789abcdef;playedSeconds=0;remainingSeconds=120;totalMinutesPurchased=0;archived=0;tombstonedAt=0`n") },
        @{ Name='legacy-wallet-retired'; Text=($legacyCredit + "`nretiredGuest=id=wallet-retired-0123456789abcdef;remainingSeconds=120;retiredAt=1`n") },
        @{ Name='legacy-wallet-alias'; Text=($legacyCredit + "`nretiredGuestAlias=wallet-alias-0123456789abcdef`n") },
        @{ Name='legacy-invalid-ledger'; Text=($legacyCredit + "`npixTransaction=tx/invalida`n") },
        @{ Name='legacy-duplicate-ledger'; Text=($legacyCredit + "`npixTransaction=tx-legacy`npixTransaction=tx-legacy`n") },
        @{ Name='schema5-duplicate-current'; Text=($validCredit + "`ncurrentPlayer=`n") },
        @{ Name='schema5-invalid-current'; Text=($validCredit -replace 'currentPlayer=', 'currentPlayer=Ana;invalida') },
        @{ Name='schema5-invalid-ledger'; Text=($validCredit + "`npixTransaction=tx/invalida`n") },
        @{ Name='schema5-duplicate-ledger'; Text=($validCredit + "`npixTransaction=tx-ledger`npixTransaction=tx-ledger`n") },
        @{ Name='schema5-guest-snapshot-low'; Text=($validCredit -replace '(?m)^remainingSeconds=120$', 'remainingSeconds=60') },
        @{ Name='schema5-guest-snapshot-high'; Text=($validCredit -replace '(?m)^remainingSeconds=120$', 'remainingSeconds=180') },
        @{ Name='schema5-player-snapshot-low'; Text=($validWalletGraph -replace '(?m)^remainingSeconds=120$', 'remainingSeconds=60') },
        @{ Name='schema5-player-snapshot-high'; Text=($validWalletGraph -replace '(?m)^remainingSeconds=120$', 'remainingSeconds=180') },
        @{ Name='schema5-duplicate-player-id'; Text=($validWalletGraph + "`nplayer=Bia;id=wallet-alias-0123456789abcdef;playedSeconds=0;remainingSeconds=0;totalMinutesPurchased=0;archived=0;tombstonedAt=0`n") },
        @{ Name='schema5-duplicate-retired-id'; Text=($validWalletGraph + "`nretiredGuest=id=wallet-retired-0123456789abcdef;remainingSeconds=0;retiredAt=2`n") },
        @{ Name='schema5-retired-player-collision'; Text=($validWalletGraph -replace 'retiredGuest=id=wallet-retired-0123456789abcdef', 'retiredGuest=id=wallet-alias-0123456789abcdef') },
        @{ Name='schema5-retired-guest-collision'; Text=($validWalletGraph -replace 'retiredGuest=id=wallet-retired-0123456789abcdef', 'retiredGuest=id=wallet-guest-0123456789abcdef') },
        @{ Name='schema5-orphan-alias'; Text=($validWalletGraph -replace 'retiredGuestAlias=wallet-alias-0123456789abcdef', 'retiredGuestAlias=wallet-orphan-0123456789abcdef') },
        @{ Name='schema5-duplicate-alias'; Text=($validWalletGraph + "`nretiredGuestAlias=wallet-alias-0123456789abcdef`n") },
        @{ Name='schema5-guest-player-collision'; Text=($validWalletGraph -replace 'guestId=wallet-guest-0123456789abcdef', 'guestId=wallet-alias-0123456789abcdef') }
    )) {
        $root = New-Fixture $case.Name $case.Text
        $creditPath = Join-Path $root 'arcade_credit.dat'
        $mirrorPath = Join-Path $root 'arcade_players.dat'
        $beforeCredit = Snapshot $creditPath
        $beforeMirror = Snapshot $mirrorPath
        Invoke-Case $root 'invalid'
        Assert ((Snapshot $creditPath) -eq $beforeCredit) "$($case.Name): autoridade foi alterada."
        Assert ((Snapshot $mirrorPath) -eq $beforeMirror) "$($case.Name): espelho foi alterado."
    }

    $oversizedCreditRoot = New-Fixture 'credit-over-size-limit' $validCredit
    $oversizedCreditPath = Join-Path $oversizedCreditRoot 'arcade_credit.dat'
    $oversizedCreditMirror = Join-Path $oversizedCreditRoot 'arcade_players.dat'
    Set-FileLength $oversizedCreditPath (8MB + 1)
    $beforeOversizedCredit = File-Digest $oversizedCreditPath
    $beforeOversizedCreditMirror = Snapshot $oversizedCreditMirror
    Invoke-Case $oversizedCreditRoot 'invalid'
    Assert ((File-Digest $oversizedCreditPath) -eq $beforeOversizedCredit) 'credit-over-size-limit: autoridade foi alterada.'
    Assert ((Snapshot $oversizedCreditMirror) -eq $beforeOversizedCreditMirror) 'credit-over-size-limit: espelho foi alterado.'

    $longCreditRoot = New-Fixture 'credit-over-line-limit' $validCredit
    $longCreditPath = Join-Path $longCreditRoot 'arcade_credit.dat'
    $longCreditMirror = Join-Path $longCreditRoot 'arcade_players.dat'
    Write-Utf8 $longCreditPath ('#' + ('x' * 4096))
    $beforeLongCredit = Snapshot $longCreditPath
    $beforeLongCreditMirror = Snapshot $longCreditMirror
    Invoke-Case $longCreditRoot 'invalid'
    Assert ((Snapshot $longCreditPath) -eq $beforeLongCredit) 'credit-over-line-limit: autoridade foi alterada.'
    Assert ((Snapshot $longCreditMirror) -eq $beforeLongCreditMirror) 'credit-over-line-limit: espelho foi alterado.'

    $oversizedConfigRoot = New-Fixture 'config-over-size-limit' $validCredit
    $oversizedConfigPath = Join-Path $oversizedConfigRoot 'arcade_credit.cfg'
    $oversizedConfigCredit = Join-Path $oversizedConfigRoot 'arcade_credit.dat'
    Set-FileLength $oversizedConfigPath (64KB + 1)
    $beforeOversizedConfig = File-Digest $oversizedConfigPath
    $beforeOversizedConfigCredit = Snapshot $oversizedConfigCredit
    Invoke-Case $oversizedConfigRoot 'invalid'
    Assert ((File-Digest $oversizedConfigPath) -eq $beforeOversizedConfig) 'config-over-size-limit: configuracao foi alterada.'
    Assert ((Snapshot $oversizedConfigCredit) -eq $beforeOversizedConfigCredit) 'config-over-size-limit: autoridade foi alterada.'

    $manyConfigLinesRoot = New-Fixture 'config-over-line-count' $validCredit
    $manyConfigLinesPath = Join-Path $manyConfigLinesRoot 'arcade_credit.cfg'
    $manyConfigLinesCredit = Join-Path $manyConfigLinesRoot 'arcade_credit.dat'
    Write-Utf8 $manyConfigLinesPath (('# test' + "`n") * 513)
    $beforeManyConfigLines = Snapshot $manyConfigLinesPath
    $beforeManyConfigLinesCredit = Snapshot $manyConfigLinesCredit
    Invoke-Case $manyConfigLinesRoot 'invalid'
    Assert ((Snapshot $manyConfigLinesPath) -eq $beforeManyConfigLines) 'config-over-line-count: configuracao foi alterada.'
    Assert ((Snapshot $manyConfigLinesCredit) -eq $beforeManyConfigLinesCredit) 'config-over-line-count: autoridade foi alterada.'

    $lockedRoot = New-Fixture 'locked' $validCredit
    $lockedCredit = Join-Path $lockedRoot 'arcade_credit.dat'
    $lockedMirror = Join-Path $lockedRoot 'arcade_players.dat'
    $beforeLocked = Snapshot $lockedCredit
    $beforeLockedMirror = Snapshot $lockedMirror
    $lock = [IO.File]::Open($lockedCredit, 'Open', 'ReadWrite', 'None')
    try { Invoke-Case $lockedRoot 'invalid' } finally { $lock.Dispose() }
    Assert ((Snapshot $lockedCredit) -eq $beforeLocked) 'locked: autoridade foi alterada.'
    Assert ((Snapshot $lockedMirror) -eq $beforeLockedMirror) 'locked: espelho foi alterado.'

    $creditDirectoryRoot = New-Fixture 'credit-is-directory' $validCredit
    $creditDirectoryPath = Join-Path $creditDirectoryRoot 'arcade_credit.dat'
    $creditDirectoryMirror = Join-Path $creditDirectoryRoot 'arcade_players.dat'
    [IO.File]::Delete($creditDirectoryPath)
    [IO.Directory]::CreateDirectory($creditDirectoryPath) | Out-Null
    $beforeCreditDirectoryMirror = Snapshot $creditDirectoryMirror
    Invoke-Case $creditDirectoryRoot 'invalid'
    Assert (Test-Path -LiteralPath $creditDirectoryPath -PathType Container) 'credit-directory: alvo inseguro foi substituido.'
    Assert ((Snapshot $creditDirectoryMirror) -eq $beforeCreditDirectoryMirror) 'credit-directory: espelho foi alterado.'

    $mirrorDirectoryRoot = New-Fixture 'mirror-is-directory' $validCredit
    $mirrorDirectoryCredit = Join-Path $mirrorDirectoryRoot 'arcade_credit.dat'
    $mirrorDirectoryPath = Join-Path $mirrorDirectoryRoot 'arcade_players.dat'
    [IO.File]::Delete($mirrorDirectoryCredit)
    [IO.File]::Delete($mirrorDirectoryPath)
    [IO.Directory]::CreateDirectory($mirrorDirectoryPath) | Out-Null
    Invoke-Case $mirrorDirectoryRoot 'invalid'
    Assert ((Snapshot $mirrorDirectoryCredit) -eq '<missing>') 'mirror-directory: autoridade ausente foi criada.'
    Assert (Test-Path -LiteralPath $mirrorDirectoryPath -PathType Container) 'mirror-directory: alvo inseguro foi substituido.'

    $configDirectoryRoot = New-Fixture 'config-is-directory' $validCredit
    $configDirectoryPath = Join-Path $configDirectoryRoot 'arcade_credit.cfg'
    $configDirectoryCredit = Join-Path $configDirectoryRoot 'arcade_credit.dat'
    $beforeConfigDirectoryCredit = Snapshot $configDirectoryCredit
    [IO.File]::Delete($configDirectoryPath)
    [IO.Directory]::CreateDirectory($configDirectoryPath) | Out-Null
    Invoke-Case $configDirectoryRoot 'invalid'
    Assert (Test-Path -LiteralPath $configDirectoryPath -PathType Container) 'config-directory: alvo inseguro foi substituido.'
    Assert ((Snapshot $configDirectoryCredit) -eq $beforeConfigDirectoryCredit) 'config-directory: autoridade foi alterada.'

    $lockDirectoryRoot = New-Fixture 'lock-is-directory' $validCredit
    $lockDirectoryPath = Join-Path $lockDirectoryRoot 'arcade_credit.lock'
    $lockDirectoryCredit = Join-Path $lockDirectoryRoot 'arcade_credit.dat'
    $lockDirectoryMirror = Join-Path $lockDirectoryRoot 'arcade_players.dat'
    [IO.Directory]::CreateDirectory($lockDirectoryPath) | Out-Null
    $beforeLockDirectoryCredit = Snapshot $lockDirectoryCredit
    $beforeLockDirectoryMirror = Snapshot $lockDirectoryMirror
    Invoke-Case $lockDirectoryRoot 'invalid'
    Assert (Test-Path -LiteralPath $lockDirectoryPath -PathType Container) 'lock-directory: alvo inseguro foi substituido.'
    Assert ((Snapshot $lockDirectoryCredit) -eq $beforeLockDirectoryCredit) 'lock-directory: autoridade foi alterada.'
    Assert ((Snapshot $lockDirectoryMirror) -eq $beforeLockDirectoryMirror) 'lock-directory: espelho foi alterado.'

    $junctionRoot = New-Fixture 'credit-is-junction' $validCredit
    $junctionCredit = Join-Path $junctionRoot 'arcade_credit.dat'
    $junctionMirror = Join-Path $junctionRoot 'arcade_players.dat'
    $junctionTarget = Join-Path $junctionRoot 'junction-target'
    $junctionMarker = Join-Path $junctionTarget 'marker.bin'
    [IO.File]::Delete($junctionCredit)
    [IO.Directory]::CreateDirectory($junctionTarget) | Out-Null
    [IO.File]::WriteAllBytes($junctionMarker, [byte[]](1, 2, 3, 4))
    New-Item -ItemType Junction -Path $junctionCredit -Target $junctionTarget | Out-Null
    $junctionAttributes = [IO.File]::GetAttributes($junctionCredit)
    Assert (($junctionAttributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) 'junction: fixture nao criou reparse point.'
    $beforeJunctionMarker = Snapshot $junctionMarker
    $beforeJunctionMirror = Snapshot $junctionMirror
    Invoke-Case $junctionRoot 'invalid'
    Assert ((Snapshot $junctionMarker) -eq $beforeJunctionMarker) 'junction: destino do reparse foi alterado.'
    Assert ((Snapshot $junctionMirror) -eq $beforeJunctionMirror) 'junction: espelho foi alterado.'
    [IO.Directory]::Delete($junctionCredit, $false)

    $validRoot = New-Fixture 'valid' $validCredit
    Invoke-Case $validRoot 'valid'
    $validStored = [IO.File]::ReadAllText((Join-Path $validRoot 'arcade_credit.dat'))
    Assert ($validStored -match 'guestRemainingSeconds=180') 'Fluxo valido deixou de persistir normalmente.'

    $validGraphRoot = New-Fixture 'valid-wallet-graph' $validWalletGraph
    Invoke-Case $validGraphRoot 'valid'
    $validGraphStored = [IO.File]::ReadAllText((Join-Path $validGraphRoot 'arcade_credit.dat'))
    Assert ($validGraphStored -match 'retiredGuestAlias=wallet-alias-0123456789abcdef') 'Alias valido foi perdido.'
    Assert ($validGraphStored -match 'retiredGuest=id=wallet-retired-0123456789abcdef') 'Carteira aposentada valida foi perdida.'
    Assert ($validGraphStored -match 'pixTransaction=tx-valid-ledger') 'Ledger valido foi perdido.'

    foreach ($largeWalletCase in @(
        @{ Name='schema5-large-guest-roundtrip'; Text=$largeSchema5GuestWallet; Pattern='(?m)^guestRemainingSeconds=36000\r?$' },
        @{ Name='schema5-large-player-roundtrip'; Text=$largeSchema5PlayerWallet; Pattern='(?m)^player=Ana;.*remainingSeconds=36000;.*$' }
    )) {
        $largeWalletRoot = New-Fixture $largeWalletCase.Name $largeWalletCase.Text
        $largeWalletPath = Join-Path $largeWalletRoot 'arcade_credit.dat'
        Invoke-Case $largeWalletRoot 'large-wallet'
        $afterFirstLargeWalletLoad = Snapshot $largeWalletPath
        Invoke-Case $largeWalletRoot 'large-wallet'
        Assert ((Snapshot $largeWalletPath) -eq $afterFirstLargeWalletLoad) "$($largeWalletCase.Name): reload alterou bytes da carteira."
        $largeWalletStored = [IO.File]::ReadAllText($largeWalletPath)
        Assert ($largeWalletStored -match '(?m)^remainingSeconds=36000\r?$') "$($largeWalletCase.Name): snapshot ativo foi truncado pelo teto manual."
        Assert ($largeWalletStored -match $largeWalletCase.Pattern) "$($largeWalletCase.Name): saldo da carteira foi truncado no round-trip."
    }

    $skipMirrorRoot = New-Fixture 'schema5-skips-mirror-read' $validCredit
    $skipMirrorPath = Join-Path $skipMirrorRoot 'arcade_players.dat'
    Set-FileLength $skipMirrorPath (8MB + 1)
    Invoke-Case $skipMirrorRoot 'authority-no-mirror-read'
    $skipMirrorCredit = [IO.File]::ReadAllText((Join-Path $skipMirrorRoot 'arcade_credit.dat'))
    $skipMirrorStored = [IO.File]::ReadAllText($skipMirrorPath)
    Assert ($skipMirrorCredit -match '(?m)^remainingSeconds=120\r?$') 'Authority schema 5 perdeu saldo ao ignorar espelho.'
    Assert ($skipMirrorStored -match '(?m)^schemaVersion=5\r?$') 'Espelho derivado enorme nao foi reconstruido best-effort.'
    Assert ((Get-Item -LiteralPath $skipMirrorPath).Length -lt 4096) 'Authority schema 5 parece ter preservado espelho patologico.'

    $missingMirrorRoot = New-Fixture 'schema5-missing-mirror' $validCredit
    $missingMirrorPath = Join-Path $missingMirrorRoot 'arcade_players.dat'
    [IO.File]::Delete($missingMirrorPath)
    Invoke-Case $missingMirrorRoot 'authority-no-mirror-read'
    Assert (Test-Path -LiteralPath $missingMirrorPath -PathType Leaf) 'Authority schema 5 nao reconstruiu espelho ausente.'

    foreach ($mode in @(
        'write-fail-add', 'replace-fail-add',
        'write-fail-coin', 'replace-fail-coin',
        'write-fail-debit', 'replace-fail-debit'
    )) {
        $runtimeRoot = New-Fixture ('runtime-' + $mode) $validCredit
        $runtimeCredit = Join-Path $runtimeRoot 'arcade_credit.dat'
        $runtimeMirror = Join-Path $runtimeRoot 'arcade_players.dat'
        $beforeRuntimeCredit = Snapshot $runtimeCredit
        $beforeRuntimeMirror = Snapshot $runtimeMirror
        Invoke-Case $runtimeRoot $mode
        Assert ((Snapshot $runtimeCredit) -eq $beforeRuntimeCredit) "$mode`: autoridade foi alterada apos falha."
        Assert ((Snapshot $runtimeMirror) -eq $beforeRuntimeMirror) "$mode`: espelho foi alterado apos falha da autoridade."
    }

    $mirrorBestEffortRoot = New-Fixture 'mirror-best-effort' $validCredit
    $mirrorBestEffortPath = Join-Path $mirrorBestEffortRoot 'arcade_players.dat'
    $beforeBestEffortMirror = Snapshot $mirrorBestEffortPath
    Invoke-Case $mirrorBestEffortRoot 'mirror-best-effort'
    $bestEffortStored = [IO.File]::ReadAllText((Join-Path $mirrorBestEffortRoot 'arcade_credit.dat'))
    Assert ($bestEffortStored -match 'guestRemainingSeconds=180') 'Falha do espelho impediu commit da autoridade.'
    Assert ((Snapshot $mirrorBestEffortPath) -eq $beforeBestEffortMirror) 'Falha best-effort alterou o espelho existente.'

    $hardlinkRoot = New-Fixture 'fixed-temp-hardlink' $validCredit
    $hardlinkAuthority = Join-Path $hardlinkRoot 'arcade_credit.dat'
    $fixedTempHardlink = Join-Path $hardlinkRoot 'arcade_credit.dat.tmp'
    $beforeHardlinkTarget = Snapshot $hardlinkAuthority
    New-Item -ItemType HardLink -Path $fixedTempHardlink -Target $hardlinkAuthority | Out-Null
    Assert (Test-Path -LiteralPath $fixedTempHardlink -PathType Leaf) 'hardlink: fixture nao foi criada.'
    Invoke-Case $hardlinkRoot 'valid'
    $hardlinkAuthorityStored = [IO.File]::ReadAllText($hardlinkAuthority)
    Assert ($hardlinkAuthorityStored -match 'guestRemainingSeconds=180') 'hardlink: commit valido falhou.'
    Assert ((Snapshot $fixedTempHardlink) -eq $beforeHardlinkTarget) 'hardlink: alvo do antigo .tmp foi truncado/alterado.'
    Assert (@(Get-ChildItem -LiteralPath $hardlinkRoot -Filter '*.tmp-*' -File).Count -eq 0) 'hardlink: temporario aleatorio ficou abandonado.'

    $concurrentRoot = New-Fixture 'cross-process-lock' $validCredit
    $oldConcurrentRoot = $env:TURBORAMA_CREDIT_TEST_ROOT
    try {
        $env:TURBORAMA_CREDIT_TEST_ROOT = $concurrentRoot
        $processA = Start-Process -FilePath $harness -ArgumentList 'concurrent-a' -PassThru
        $processB = Start-Process -FilePath $harness -ArgumentList 'concurrent-b' -PassThru
        $processA.WaitForExit()
        $processB.WaitForExit()
        Assert ($processA.ExitCode -eq 0) "concurrent-a falhou: $($processA.ExitCode)"
        Assert ($processB.ExitCode -eq 0) "concurrent-b falhou: $($processB.ExitCode)"
    }
    finally { $env:TURBORAMA_CREDIT_TEST_ROOT = $oldConcurrentRoot }
    Assert (@(Get-ChildItem -LiteralPath $concurrentRoot -Filter 'active-*' -File).Count -eq 1) 'lock: mais ou menos de uma instancia ficou ativa.'
    Assert (@(Get-ChildItem -LiteralPath $concurrentRoot -Filter 'blocked-*' -File).Count -eq 1) 'lock: instancia concorrente nao falhou fechada.'
    $concurrentStored = [IO.File]::ReadAllText((Join-Path $concurrentRoot 'arcade_credit.dat'))
    Assert ($concurrentStored -match '(?m)^remainingSeconds=180\r?$') 'lock: saldo final indica perda/corrupcao multi-writer.'
    Assert ($concurrentStored -match '(?m)^guestRemainingSeconds=180\r?$') 'lock: carteira guest final ficou inconsistente.'
    Assert (@(Get-ChildItem -LiteralPath $concurrentRoot -Filter '*.tmp-*' -File).Count -eq 0) 'lock: temporario concorrente ficou abandonado.'
    Invoke-Case $concurrentRoot 'recover-lock'
    $recoveredLockStored = [IO.File]::ReadAllText((Join-Path $concurrentRoot 'arcade_credit.dat'))
    Assert ($recoveredLockStored -match '(?m)^remainingSeconds=240\r?$') 'lock: nao foi recuperavel apos saida dos processos.'

    $badMirrors = @(
        @{ Name='mirror-truncated'; Text="schemaVersion=4`ncurrentPlayer=Ana`nplayer=Ana;playedSeconds=5`n" },
        @{ Name='mirror-schema3'; Text="schemaVersion=3`ncurrentPlayer=Ana`nplayer=Ana;playedSeconds=5;remainingSeconds=120;totalMinutesPurchased=2`n" },
        @{ Name='mirror-schema6'; Text=($validSchema5PlayerMirror -replace 'schemaVersion=5', 'schemaVersion=6') },
        @{ Name='mirror-missing-schema'; Text="currentPlayer=Ana`nplayer=Ana;playedSeconds=5;remainingSeconds=120;totalMinutesPurchased=2`n" },
        @{ Name='mirror-schema4-wallet-field'; Text="schemaVersion=4`ncurrentPlayer=Ana`nplayer=Ana;id=wallet-player-0123456789abcdef;playedSeconds=5;remainingSeconds=120;totalMinutesPurchased=2`n" },
        @{ Name='mirror-corrupt'; Text="schemaVersion=4`ncurrentPlayer=Ana`nplayer=Ana;playedSeconds=5;remainingSeconds=-10;totalMinutesPurchased=2`n" },
        @{ Name='mirror-legacy-noise'; Text="schemaVersion=4`ncurrentPlayer=Ana`nplayer=Ana;playedSeconds=5;remainingSeconds=1x20;totalMinutesPurchased=2`n" },
        @{ Name='mirror-legacy-overflow'; Text="schemaVersion=4`ncurrentPlayer=Ana`nplayer=Ana;playedSeconds=5;remainingSeconds=999999999120;totalMinutesPurchased=2`n" },
        @{ Name='mirror-legacy-over-cap'; Text="schemaVersion=4`ncurrentPlayer=Ana`nplayer=Ana;playedSeconds=5;remainingSeconds=$overCap;totalMinutesPurchased=2`n" },
        @{ Name='mirror-legacy-over-historical-cap'; Text="schemaVersion=4`ncurrentPlayer=Ana`nplayer=Ana;playedSeconds=5;remainingSeconds=$legacyWalletOverCap;totalMinutesPurchased=2`n" },
        @{ Name='mirror-legacy-100m-over-historical-cap'; Text="schemaVersion=4`ncurrentPlayer=Ana`nplayer=Ana;playedSeconds=5;remainingSeconds=100000000;totalMinutesPurchased=2`n" },
        @{ Name='mirror-schema5-over-cap'; Text=$schema5MirrorOverCap }
    )
    foreach ($case in $badMirrors) {
        $mirrorRoot = New-Fixture $case.Name $validCredit $case.Text
        $mirrorCredit = Join-Path $mirrorRoot 'arcade_credit.dat'
        $mirrorPath = Join-Path $mirrorRoot 'arcade_players.dat'
        [IO.File]::Delete($mirrorCredit)
        $beforeBadMirror = Snapshot $mirrorPath
        Invoke-Case $mirrorRoot 'invalid'
        Assert ((Snapshot $mirrorCredit) -eq '<missing>') "$($case.Name): autoridade ausente foi criada."
        Assert ((Snapshot $mirrorPath) -eq $beforeBadMirror) "$($case.Name): espelho invalido foi alterado."
    }

    $oversizedMirrorRoot = New-Fixture 'mirror-over-size-limit' $validCredit
    $oversizedMirrorCredit = Join-Path $oversizedMirrorRoot 'arcade_credit.dat'
    $oversizedMirrorPath = Join-Path $oversizedMirrorRoot 'arcade_players.dat'
    [IO.File]::Delete($oversizedMirrorCredit)
    Set-FileLength $oversizedMirrorPath (8MB + 1)
    $beforeOversizedMirror = File-Digest $oversizedMirrorPath
    Invoke-Case $oversizedMirrorRoot 'invalid'
    Assert ((Snapshot $oversizedMirrorCredit) -eq '<missing>') 'mirror-over-size-limit: autoridade ausente foi criada.'
    Assert ((File-Digest $oversizedMirrorPath) -eq $beforeOversizedMirror) 'mirror-over-size-limit: espelho foi alterado.'

    $longMirrorRoot = New-Fixture 'mirror-over-line-limit' $validCredit
    $longMirrorCredit = Join-Path $longMirrorRoot 'arcade_credit.dat'
    $longMirrorPath = Join-Path $longMirrorRoot 'arcade_players.dat'
    [IO.File]::Delete($longMirrorCredit)
    Write-Utf8 $longMirrorPath ('#' + ('x' * 4096))
    $beforeLongMirror = Snapshot $longMirrorPath
    Invoke-Case $longMirrorRoot 'invalid'
    Assert ((Snapshot $longMirrorCredit) -eq '<missing>') 'mirror-over-line-limit: autoridade ausente foi criada.'
    Assert ((Snapshot $longMirrorPath) -eq $beforeLongMirror) 'mirror-over-line-limit: espelho foi alterado.'

    $lockedMirrorRoot = New-Fixture 'mirror-locked' $validCredit $legacyMirror
    $lockedMirrorCredit = Join-Path $lockedMirrorRoot 'arcade_credit.dat'
    $lockedMirrorPath = Join-Path $lockedMirrorRoot 'arcade_players.dat'
    [IO.File]::Delete($lockedMirrorCredit)
    $beforeLockedMirrorOnly = Snapshot $lockedMirrorPath
    $mirrorLock = [IO.File]::Open($lockedMirrorPath, 'Open', 'ReadWrite', 'None')
    try { Invoke-Case $lockedMirrorRoot 'invalid' } finally { $mirrorLock.Dispose() }
    Assert ((Snapshot $lockedMirrorCredit) -eq '<missing>') 'mirror-locked: autoridade ausente foi criada.'
    Assert ((Snapshot $lockedMirrorPath) -eq $beforeLockedMirrorOnly) 'mirror-locked: espelho foi alterado.'

    $mirror5OnlyRoot = New-Fixture 'mirror5-without-authority' $validCredit $validSchema5PlayerMirror
    $mirror5OnlyCredit = Join-Path $mirror5OnlyRoot 'arcade_credit.dat'
    $mirror5OnlyPath = Join-Path $mirror5OnlyRoot 'arcade_players.dat'
    [IO.File]::Delete($mirror5OnlyCredit)
    $beforeMirror5Only = Snapshot $mirror5OnlyPath
    Invoke-Case $mirror5OnlyRoot 'invalid'
    Assert ((Snapshot $mirror5OnlyCredit) -eq '<missing>') 'mirror5-without-authority: authority foi criada indevidamente.'
    Assert ((Snapshot $mirror5OnlyPath) -eq $beforeMirror5Only) 'mirror5-without-authority: espelho derivado foi alterado.'

    $legacyWithMirror5Root = New-Fixture 'legacy-authority-with-mirror5' $legacyCredit $validSchema5PlayerMirror
    $legacyWithMirror5Credit = Join-Path $legacyWithMirror5Root 'arcade_credit.dat'
    $legacyWithMirror5Path = Join-Path $legacyWithMirror5Root 'arcade_players.dat'
    $beforeLegacyWithMirror5Credit = Snapshot $legacyWithMirror5Credit
    $beforeLegacyWithMirror5 = Snapshot $legacyWithMirror5Path
    Invoke-Case $legacyWithMirror5Root 'invalid'
    Assert ((Snapshot $legacyWithMirror5Credit) -eq $beforeLegacyWithMirror5Credit) 'legacy-authority-with-mirror5: authority foi migrada.'
    Assert ((Snapshot $legacyWithMirror5Path) -eq $beforeLegacyWithMirror5) 'legacy-authority-with-mirror5: espelho foi alterado.'

    $legacyPlayerNoMirrorRoot = New-Fixture 'legacy-player-without-mirror' $legacyCredit
    $legacyPlayerNoMirrorCredit = Join-Path $legacyPlayerNoMirrorRoot 'arcade_credit.dat'
    $legacyPlayerNoMirrorPath = Join-Path $legacyPlayerNoMirrorRoot 'arcade_players.dat'
    [IO.File]::Delete($legacyPlayerNoMirrorPath)
    $beforeLegacyPlayerNoMirror = Snapshot $legacyPlayerNoMirrorCredit
    Invoke-Case $legacyPlayerNoMirrorRoot 'invalid'
    Assert ((Snapshot $legacyPlayerNoMirrorCredit) -eq $beforeLegacyPlayerNoMirror) 'legacy-player-without-mirror: authority foi migrada sem carteira ativa.'
    Assert ((Snapshot $legacyPlayerNoMirrorPath) -eq '<missing>') 'legacy-player-without-mirror: espelho ausente foi criado.'

    $corruptLegacyMirror = ($badMirrors | Where-Object { $_.Name -eq 'mirror-corrupt' }).Text
    $legacyBadRoot = New-Fixture 'legacy-with-corrupt-mirror' $legacyCredit $corruptLegacyMirror
    $legacyBadCredit = Join-Path $legacyBadRoot 'arcade_credit.dat'
    $legacyBadMirror = Join-Path $legacyBadRoot 'arcade_players.dat'
    $beforeLegacyBadCredit = Snapshot $legacyBadCredit
    $beforeLegacyBadMirror = Snapshot $legacyBadMirror
    Invoke-Case $legacyBadRoot 'invalid'
    Assert ((Snapshot $legacyBadCredit) -eq $beforeLegacyBadCredit) 'legacy-corrupt: autoridade foi migrada apesar do espelho invalido.'
    Assert ((Snapshot $legacyBadMirror) -eq $beforeLegacyBadMirror) 'legacy-corrupt: espelho invalido foi alterado.'

    $mismatchMirror = @"
schemaVersion=4
currentPlayer=Bia
player=Bia;playedSeconds=0;remainingSeconds=0;totalMinutesPurchased=0
"@
    $legacyMismatchRoot = New-Fixture 'legacy-current-mismatch' $legacyCredit $mismatchMirror
    $legacyMismatchCredit = Join-Path $legacyMismatchRoot 'arcade_credit.dat'
    $legacyMismatchMirror = Join-Path $legacyMismatchRoot 'arcade_players.dat'
    $beforeLegacyMismatchCredit = Snapshot $legacyMismatchCredit
    $beforeLegacyMismatchMirror = Snapshot $legacyMismatchMirror
    Invoke-Case $legacyMismatchRoot 'invalid'
    Assert ((Snapshot $legacyMismatchCredit) -eq $beforeLegacyMismatchCredit) 'legacy-current-mismatch: autoridade foi alterada.'
    Assert ((Snapshot $legacyMismatchMirror) -eq $beforeLegacyMismatchMirror) 'legacy-current-mismatch: espelho foi alterado.'

    $legacySnapshotMirror = @"
schemaVersion=4
currentPlayer=Ana
player=Ana;playedSeconds=5;remainingSeconds=0;totalMinutesPurchased=2
player=Bob;playedSeconds=7;remainingSeconds=60;totalMinutesPurchased=1
"@
    $legacySnapshotCredit = $legacyCredit + "`npixTransaction=tx-authority-120`n"
    $legacyRoot = New-Fixture 'legacy-authority-snapshot-120' $legacySnapshotCredit $legacySnapshotMirror
    Invoke-Case $legacyRoot 'legacy'
    $legacyStored = [IO.File]::ReadAllText((Join-Path $legacyRoot 'arcade_credit.dat'))
    Assert ($legacyStored -match 'walletSchema=1') 'Migracao valida do schema 4 deixou de funcionar.'
    Assert ($legacyStored -match '(?m)^remainingSeconds=120\r?$') 'Authority schema 4 perdeu o snapshot ativo de 120 segundos.'
    Assert ($legacyStored -match '(?m)^player=Ana;.*remainingSeconds=120;.*$') 'Authority schema 4 nao sobrescreveu snapshot obsoleto de Ana.'
    Assert ($legacyStored -match '(?m)^player=Bob;.*remainingSeconds=60;.*$') 'Migracao schema 4 alterou saldo historico de Bob.'
    Assert ($legacyStored -match '(?m)^pixTransaction=tx-authority-120\r?$') 'Ledger schema 4 foi perdido na migracao.'

    $legacy60Credit = ($legacyCredit -replace 'remainingSeconds=1,20', 'remainingSeconds=60') + "`npixTransaction=tx-authority-60`n"
    $legacy60Mirror = @"
schemaVersion=4
currentPlayer=Ana
player=Ana;playedSeconds=5;remainingSeconds=120;totalMinutesPurchased=2
player=Bob;playedSeconds=7;remainingSeconds=60;totalMinutesPurchased=1
"@
    $legacy60Root = New-Fixture 'legacy-authority-snapshot-60' $legacy60Credit $legacy60Mirror
    Invoke-Case $legacy60Root 'legacy60'
    $legacy60Stored = [IO.File]::ReadAllText((Join-Path $legacy60Root 'arcade_credit.dat'))
    Assert ($legacy60Stored -match '(?m)^remainingSeconds=60\r?$') 'Authority schema 4 nao reduziu snapshot ativo para 60 segundos.'
    Assert ($legacy60Stored -match '(?m)^player=Ana;.*remainingSeconds=60;.*$') 'Snapshot stale de Ana prevaleceu sobre authority schema 4.'
    Assert ($legacy60Stored -match '(?m)^player=Bob;.*remainingSeconds=60;.*$') 'Migracao de Ana alterou carteira de Bob.'
    Assert ($legacy60Stored -match '(?m)^pixTransaction=tx-authority-60\r?$') 'Ledger da migracao de 60 segundos foi perdido.'

    $legacyGuestCredit = (($legacyCredit -replace 'remainingSeconds=1,20', 'remainingSeconds=60') -replace 'currentPlayer=Ana', 'currentPlayer=') + "`npixTransaction=tx-guest-60`n"
    $legacyGuestMirror = @"
schemaVersion=4
currentPlayer=
player=Bob;playedSeconds=7;remainingSeconds=60;totalMinutesPurchased=1
"@
    $legacyGuestRoot = New-Fixture 'legacy-guest-valid' $legacyGuestCredit $legacyGuestMirror
    Invoke-Case $legacyGuestRoot 'legacy60'
    $legacyGuestStored = [IO.File]::ReadAllText((Join-Path $legacyGuestRoot 'arcade_credit.dat'))
    Assert ($legacyGuestStored -match '(?m)^guestRemainingSeconds=60\r?$') 'Migracao guest schema 4 perdeu o saldo authority.'
    Assert ($legacyGuestStored -match '(?m)^player=Bob;.*remainingSeconds=60;.*$') 'Migracao guest alterou carteira de Bob.'
    Assert ($legacyGuestStored -match '(?m)^pixTransaction=tx-guest-60\r?$') 'Ledger guest schema 4 foi perdido.'

    $legacyGuestNoMirrorRoot = New-Fixture 'legacy-guest-without-mirror' $legacyGuestCredit
    $legacyGuestNoMirrorPath = Join-Path $legacyGuestNoMirrorRoot 'arcade_players.dat'
    [IO.File]::Delete($legacyGuestNoMirrorPath)
    Invoke-Case $legacyGuestNoMirrorRoot 'legacy60'
    $legacyGuestNoMirrorStored = [IO.File]::ReadAllText((Join-Path $legacyGuestNoMirrorRoot 'arcade_credit.dat'))
    Assert ($legacyGuestNoMirrorStored -match '(?m)^guestRemainingSeconds=60\r?$') 'Guest schema 4 sem espelho nao foi migrado.'

    $legacyCapCredit = $legacyCredit -replace 'remainingSeconds=1,20', "remainingSeconds=$legacyWalletCap"
    $legacyCapRoot = New-Fixture 'legacy-at-historical-cap' $legacyCapCredit $legacyMirror
    Invoke-Case $legacyCapRoot 'legacy-cap'
    $legacyCapStored = [IO.File]::ReadAllText((Join-Path $legacyCapRoot 'arcade_credit.dat'))
    Assert ($legacyCapStored -match '(?m)^remainingSeconds=604800\r?$') 'Limite historico schema 4 de sete dias foi rejeitado.'

    $mirrorOnlyLegacy = $legacyMirror -replace 'remainingSeconds=0', 'remainingSeconds=120'
    $mirrorOnlyRoot = New-Fixture 'mirror-only-valid' $validCredit $mirrorOnlyLegacy
    $mirrorOnlyCredit = Join-Path $mirrorOnlyRoot 'arcade_credit.dat'
    [IO.File]::Delete($mirrorOnlyCredit)
    Invoke-Case $mirrorOnlyRoot 'legacy'
    $mirrorOnlyStored = [IO.File]::ReadAllText($mirrorOnlyCredit)
    Assert ($mirrorOnlyStored -match 'walletSchema=1') 'Espelho legado valido sem autoridade nao foi migrado.'
    Assert ($mirrorOnlyStored -match '(?m)^remainingSeconds=120\r?$') 'Migracao do espelho valido perdeu o saldo.'

    Write-Host 'OK: config/authority estritas, limites transacionais, lock, atomicidade e round-trips validados.'
}
finally {
    if (Test-Path -LiteralPath $testRoot -PathType Container) {
        [IO.Directory]::Delete($testRoot, $true)
    }
}
