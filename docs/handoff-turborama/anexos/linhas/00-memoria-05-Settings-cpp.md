# 00-memoria: TurboramaEmulationStation/es-core/src/Settings.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Valores padrão e limites de configuração. Um padrão não sobrescreve necessariamente uma configuração já salva.

- Antes: `6f6b8b8372610fc2abe1e137d99a48c3ec52412e`.
- Depois: `0e02780b761cb488c591416d2986130efcc166dd`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/Settings.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 222, depois 222

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/Settings.cpp#L222) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/Settings.cpp#L222)

```text
ANTES | DEPOIS |   CÓDIGO
  222 |    222 |   	mIntMap["ScraperResizeHeight"] = 0;
  223 |    223 |   
  224 |    224 |   #if defined(_WIN64) || defined(X86_64)
  225 |        | - 	// LZ TURBO premium theme: balanced image cache + explicit RAM budget for stability.
      |    225 | + 	// 64-bit desktop: balanced image cache and a bounded decoder-buffer budget.
  226 |    226 |   	mIntMap["MaxVRAM"] = 3072;
  227 |    227 |   	mIntMap["MaxRAM"] = 2048;
  228 |    228 |   	mIntMap["MaxVideoRAM"] = 768;
  229 |    229 |   #elif defined(_WIN32) || defined(X86)
  230 |        | - 	// Safer default for 32-bit Windows builds.
      |    230 | + 	// 32-bit desktop: stay within the smaller address space.
  231 |    231 |   	mIntMap["MaxVRAM"] = 1024;
      |    232 | + 	mIntMap["MaxRAM"] = 768;
      |    233 | + 	mIntMap["MaxVideoRAM"] = 192;
  232 |    234 |   #elif defined(TINKERBOARD) || defined(ODROIDN2) || defined(ODROIDC2) || defined(ODROIDXU4) || defined(RPI4)
  233 |        | - 	// Boards > 1Gb RAM
      |    235 | + 	// Boards with more than 1 GB of RAM.
  234 |    236 |   	mIntMap["MaxVRAM"] = 512;
  235 |    237 |   	mIntMap["MaxRAM"] = 1024;
      |    238 | + 	mIntMap["MaxVideoRAM"] = 256;
  236 |    239 |   #elif defined(ODROIDGOA) || defined(GAMEFORCE) || defined(RK3326) || defined(RPIZERO2) || defined(RPI2) || defined(RPI3) || defined(ROCKPRO64)
  237 |        | - 	// Boards with 1Gb RAM
      |    240 | + 	// Boards with 1 GB of RAM.
  238 |    241 |   	mIntMap["MaxVRAM"] = 128;
  239 |    242 |   	mIntMap["MaxRAM"] = 384;
      |    243 | + 	mIntMap["MaxVideoRAM"] = 128;
  240 |    244 |   #elif defined(_RPI_)
  241 |        | - 	// Rpi 0, 1
      |    245 | + 	// Older Raspberry Pi 0/1 models.
  242 |    246 |   	mIntMap["MaxVRAM"] = 128;
  243 |    247 |   	mIntMap["MaxRAM"] = 256;
      |    248 | + 	mIntMap["MaxVideoRAM"] = 64;
  244 |    249 |   #else
  245 |        | - 	// Other boards
      |    250 | + 	// Conservative fallback for other boards.
  246 |    251 |   	mIntMap["MaxVRAM"] = 100;
  247 |    252 |   	mIntMap["MaxRAM"] = 256;
      |    253 | + 	mIntMap["MaxVideoRAM"] = 64;
  248 |    254 |   #endif
  249 |    255 |   
  250 |    256 |   	mIntMap["MaxAsyncQueue"] = 12;
  251 |    257 |   	mIntMap["MaxConcurrentVideos"] = 3;
      |    258 | + 	// 0 keeps the theme XML's maxLogoCount authoritative. Positive values allow
      |    259 | + 	// low-memory installations to add a separate carousel decoder cap.
      |    260 | + 	mIntMap["MaxConcurrentCarouselVideos"] = 0;
  252 |    261 |   	mBoolMap["EnforceVideoLimit"] = true;
  253 |    262 |   
  254 |    263 |   	mStringMap["TransitionStyle"] = "auto";
```

Conferência: 1 trechos, 15 linhas adicionadas e 6 removidas.

