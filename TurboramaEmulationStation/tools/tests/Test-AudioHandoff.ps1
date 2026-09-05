$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot '..\..\es-core\src\components\VideoVlcComponent.cpp'
$source = [IO.File]::ReadAllText($sourcePath)
$start = $source.IndexOf('namespace')
$end = $source.IndexOf('bool VideoVlcComponent::waitForAudioRelease', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Release queue missing' }
$queue = $source.Substring($start, $end - $start)
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('audio-handoff-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$prefix = @'
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <deque>
#include <mutex>
#include <thread>
#include <future>
#include <stdexcept>
#include <iostream>
struct libvlc_media_player_t {};
struct VideoContext {};
std::mutex gateMutex;
std::condition_variable gate;
bool allowRelease = false;
int entered = 0;
std::atomic<int> released{0};
void libvlc_media_player_release(libvlc_media_player_t*) {
    std::unique_lock<std::mutex> lock(gateMutex);
    ++entered; gate.notify_all();
    gate.wait(lock, [] { return allowRelease; });
    ++released;
}
struct VideoVlcComponent {
    static void releaseContext(VideoContext*) {}
    static void clearBufferPool() {}
};
'@
$test = @'
void check(bool ok) { if (!ok) throw std::runtime_error("audio handoff assertion"); }
int main() {
    auto& queue = MediaPlayerReleaseQueue::instance();
    check(queue.waitUntilReleased(0));
    libvlc_media_player_t player;
    queue.enqueue(&player, nullptr);
    {
        std::unique_lock<std::mutex> lock(gateMutex);
        check(gate.wait_for(lock, std::chrono::seconds(2), [] { return entered == 1; }));
    }
    // The job has left the queue but VLC is still releasing: must not pass.
    check(!queue.waitUntilReleased(20));
    // Fill the bound while worker is blocked, exercising synchronous overflow.
    for (int i = 0; i < 15; ++i) queue.enqueue(&player, nullptr);
    auto overflow = std::async(std::launch::async, [&] { queue.enqueue(&player, nullptr); });
    {
        std::unique_lock<std::mutex> lock(gateMutex);
        check(gate.wait_for(lock, std::chrono::seconds(2), [] { return entered == 2; }));
    }
    check(!queue.waitUntilReleased(20));
    { std::lock_guard<std::mutex> lock(gateMutex); allowRelease = true; }
    gate.notify_all();
    check(queue.waitUntilReleased(2000));
    overflow.get();
    check(released.load() == 17);
    check(queue.waitUntilReleased(0));
    std::cout << "AUDIO_HANDOFF_TEST=OK (in-flight, queued, overflow, timeout, completion)\n";
}
'@
$harness = Join-Path $testRoot 'harness.cpp'
[IO.File]::WriteAllText($harness, $prefix + "`n" + $queue + "`n" + $test)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
if (-not $vs) { throw 'MSVC missing' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
Push-Location $testRoot
try {
    & cmd.exe /d /s /c ('"{0}" >nul && cl.exe /nologo /std:c++17 /EHsc harness.cpp /Fe:harness.exe' -f $vcvars)
    if ($LASTEXITCODE -ne 0) { throw 'Harness compilation failed' }
    $process = Start-Process -FilePath (Join-Path $testRoot 'harness.exe') -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'Harness timeout' }
    if ($process.ExitCode -ne 0) { throw "Harness failed: $($process.ExitCode)" }
    Write-Host 'AUDIO_HANDOFF_TEST=OK'
}
finally { Pop-Location }
