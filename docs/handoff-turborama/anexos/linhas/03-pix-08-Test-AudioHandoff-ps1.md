# 03-pix: TurboramaEmulationStation/tools/tests/Test-AudioHandoff.ps1

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Teste automatizado: preparação, execução e asserções com dados sintéticos.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-AudioHandoff.ps1).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 0, depois 1

Arquivo novo nesta comparação; não existe na revisão anterior. [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-AudioHandoff.ps1#L1)

```text
ANTES | DEPOIS |   CÓDIGO
      |      1 | + $ErrorActionPreference = 'Stop'
      |      2 | + $sourcePath = Join-Path $PSScriptRoot '..\..\es-core\src\components\VideoVlcComponent.cpp'
      |      3 | + $source = [IO.File]::ReadAllText($sourcePath)
      |      4 | + $start = $source.IndexOf('namespace')
      |      5 | + $end = $source.IndexOf('bool VideoVlcComponent::waitForAudioRelease', $start)
      |      6 | + if ($start -lt 0 -or $end -le $start) { throw 'Release queue missing' }
      |      7 | + $queue = $source.Substring($start, $end - $start)
      |      8 | + $testRoot = Join-Path ([IO.Path]::GetTempPath()) ('audio-handoff-test-' + [Guid]::NewGuid().ToString('N'))
      |      9 | + New-Item -ItemType Directory -Path $testRoot | Out-Null
      |     10 | + $prefix = @'
      |     11 | + #include <atomic>
      |     12 | + #include <chrono>
      |     13 | + #include <condition_variable>
      |     14 | + #include <deque>
      |     15 | + #include <mutex>
      |     16 | + #include <thread>
      |     17 | + #include <future>
      |     18 | + #include <stdexcept>
      |     19 | + #include <iostream>
      |     20 | + struct libvlc_media_player_t {};
      |     21 | + struct VideoContext {};
      |     22 | + std::mutex gateMutex;
      |     23 | + std::condition_variable gate;
      |     24 | + bool allowRelease = false;
      |     25 | + int entered = 0;
      |     26 | + std::atomic<int> released{0};
      |     27 | + void libvlc_media_player_release(libvlc_media_player_t*) {
      |     28 | +     std::unique_lock<std::mutex> lock(gateMutex);
      |     29 | +     ++entered; gate.notify_all();
      |     30 | +     gate.wait(lock, [] { return allowRelease; });
      |     31 | +     ++released;
      |     32 | + }
      |     33 | + struct VideoVlcComponent {
      |     34 | +     static void releaseContext(VideoContext*) {}
      |     35 | +     static void clearBufferPool() {}
      |     36 | + };
      |     37 | + '@
      |     38 | + $test = @'
      |     39 | + void check(bool ok) { if (!ok) throw std::runtime_error("audio handoff assertion"); }
      |     40 | + int main() {
      |     41 | +     auto& queue = MediaPlayerReleaseQueue::instance();
      |     42 | +     check(queue.waitUntilReleased(0));
      |     43 | +     libvlc_media_player_t player;
      |     44 | +     queue.enqueue(&player, nullptr);
      |     45 | +     {
      |     46 | +         std::unique_lock<std::mutex> lock(gateMutex);
      |     47 | +         check(gate.wait_for(lock, std::chrono::seconds(2), [] { return entered == 1; }));
      |     48 | +     }
      |     49 | +     // The job has left the queue but VLC is still releasing: must not pass.
      |     50 | +     check(!queue.waitUntilReleased(20));
      |     51 | +     // Fill the bound while worker is blocked, exercising synchronous overflow.
      |     52 | +     for (int i = 0; i < 15; ++i) queue.enqueue(&player, nullptr);
      |     53 | +     auto overflow = std::async(std::launch::async, [&] { queue.enqueue(&player, nullptr); });
      |     54 | +     {
      |     55 | +         std::unique_lock<std::mutex> lock(gateMutex);
      |     56 | +         check(gate.wait_for(lock, std::chrono::seconds(2), [] { return entered == 2; }));
      |     57 | +     }
      |     58 | +     check(!queue.waitUntilReleased(20));
      |     59 | +     { std::lock_guard<std::mutex> lock(gateMutex); allowRelease = true; }
      |     60 | +     gate.notify_all();
      |     61 | +     check(queue.waitUntilReleased(2000));
      |     62 | +     overflow.get();
      |     63 | +     check(released.load() == 17);
      |     64 | +     check(queue.waitUntilReleased(0));
      |     65 | +     std::cout << "AUDIO_HANDOFF_TEST=OK (in-flight, queued, overflow, timeout, completion)\n";
      |     66 | + }
      |     67 | + '@
      |     68 | + $harness = Join-Path $testRoot 'harness.cpp'
      |     69 | + [IO.File]::WriteAllText($harness, $prefix + "`n" + $queue + "`n" + $test)
      |     70 | + $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
      |     71 | + $vs = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
      |     72 | + if (-not $vs) { throw 'MSVC missing' }
      |     73 | + $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
      |     74 | + Push-Location $testRoot
      |     75 | + try {
      |     76 | +     & cmd.exe /d /s /c ('"{0}" >nul && cl.exe /nologo /std:c++17 /EHsc harness.cpp /Fe:harness.exe' -f $vcvars)
      |     77 | +     if ($LASTEXITCODE -ne 0) { throw 'Harness compilation failed' }
      |     78 | +     $process = Start-Process -FilePath (Join-Path $testRoot 'harness.exe') -WindowStyle Hidden -PassThru
      |     79 | +     if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'Harness timeout' }
      |     80 | +     if ($process.ExitCode -ne 0) { throw "Harness failed: $($process.ExitCode)" }
      |     81 | +     Write-Host 'AUDIO_HANDOFF_TEST=OK'
      |     82 | + }
      |     83 | + finally { Pop-Location }
```

Conferência: 1 trechos, 83 linhas adicionadas e 0 removidas.
