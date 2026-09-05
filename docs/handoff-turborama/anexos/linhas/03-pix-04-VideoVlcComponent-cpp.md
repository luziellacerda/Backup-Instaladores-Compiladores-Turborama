# 03-pix: TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Player VLC, callbacks concorrentes, buffers e pools. Na PIX, inclui a espera limitada pela fila de liberação antes do emulador.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 18, depois 18

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L18) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L18)

```text
ANTES | DEPOIS |   CÓDIGO
   18 |     18 |   #include "AudioManager.h"
   19 |     19 |   #include "Log.h"
   20 |     20 |   #include <condition_variable>
      |     21 | + #include <chrono>
   21 |     22 |   #include <deque>
   22 |     23 |   #include <new>
   23 |     24 |   #include <thread>
```

## Trecho 2: antes 69, depois 70

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L69) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L70)

```text
ANTES | DEPOIS |   CÓDIGO
   69 |     70 |   				// measure. Under pathological rapid scrolling, apply backpressure by
   70 |     71 |   				// completing this one release on the caller instead of growing forever.
   71 |     72 |   				if (mJobs.size() + mInFlight >= MAX_RELEASE_JOBS)
      |     73 | + 				{
   72 |     74 |   					releaseSynchronously = true;
      |     75 | + 					++mInFlight;
      |     76 | + 				}
   73 |     77 |   				else
   74 |     78 |   					mJobs.push_back({ player, context });
   75 |     79 |   			}
```

## Trecho 3: antes 78, depois 82

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L78) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L82)

```text
ANTES | DEPOIS |   CÓDIGO
   78 |     82 |   				if (player != nullptr)
   79 |     83 |   					libvlc_media_player_release(player);
   80 |     84 |   				VideoVlcComponent::releaseContext(context);
      |     85 | + 				{
      |     86 | + 					std::lock_guard<std::mutex> lock(mMutex);
      |     87 | + 					--mInFlight;
      |     88 | + 				}
      |     89 | + 				mDrained.notify_all();
   81 |     90 |   				return;
   82 |     91 |   			}
   83 |     92 |   			mCondition.notify_one();
   84 |     93 |   		}
   85 |     94 |   
      |     95 | + 		bool waitUntilReleased(unsigned timeoutMs)
      |     96 | + 		{
      |     97 | + 			std::unique_lock<std::mutex> lock(mMutex);
      |     98 | + 			return mDrained.wait_for(lock, std::chrono::milliseconds(timeoutMs),
      |     99 | + 				[this]() { return mJobs.empty() && mInFlight == 0; });
      |    100 | + 		}
      |    101 | + 
   86 |    102 |   	private:
   87 |    103 |   		MediaPlayerReleaseQueue() : mStopping(false), mInFlight(0), mWorker([this]() { run(); })
   88 |    104 |   		{
```

## Trecho 4: antes 122, depois 138

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L122) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L138)

```text
ANTES | DEPOIS |   CÓDIGO
  122 |    138 |   					std::lock_guard<std::mutex> lock(mMutex);
  123 |    139 |   					mInFlight--;
  124 |    140 |   				}
      |    141 | + 				mDrained.notify_all();
  125 |    142 |   			}
  126 |    143 |   		}
  127 |    144 |   
  128 |    145 |   		static const size_t MAX_RELEASE_JOBS = 16;
  129 |    146 |   		std::mutex mMutex;
  130 |    147 |   		std::condition_variable mCondition;
      |    148 | + 		std::condition_variable mDrained;
  131 |    149 |   		std::deque<MediaPlayerReleaseJob> mJobs;
  132 |    150 |   		bool mStopping;
  133 |    151 |   		size_t mInFlight;
```

## Trecho 5: antes 135, depois 153

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L135) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L153)

```text
ANTES | DEPOIS |   CÓDIGO
  135 |    153 |   	};
  136 |    154 |   }
  137 |    155 |   
      |    156 | + bool VideoVlcComponent::waitForAudioRelease(unsigned timeoutMs)
      |    157 | + {
      |    158 | + 	// Called on the UI thread after Window::deinit has hidden the videos.
      |    159 | + 	// Normal carousel navigation remains asynchronous and keeps its pools.
      |    160 | + 	return MediaPlayerReleaseQueue::instance().waitUntilReleased(timeoutMs);
      |    161 | + }
      |    162 | + 
  138 |    163 |   // VLC prepares to render a video frame.
  139 |    164 |   static void *lock(void *data, void **p_pixels) 
  140 |    165 |   {
```

Conferência: 5 trechos, 25 linhas adicionadas e 0 removidas.

