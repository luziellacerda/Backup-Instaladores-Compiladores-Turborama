# 03-pix: TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Contrato e estado do player, incluindo estruturas usadas por callbacks e o método público de espera na PIX.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 99, depois 99

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L99) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L99)

```text
ANTES | DEPOIS |   CÓDIGO
   99 |     99 |   
  100 |    100 |   public:
  101 |    101 |   	static void init();
      |    102 | + 	static bool waitForAudioRelease(unsigned timeoutMs);
  102 |    103 |   	static void releaseContext(VideoContext* ctx);
  103 |    104 |   	static void clearBufferPool();
  104 |    105 |   
```

Conferência: 1 trechos, 1 linhas adicionadas e 0 removidas.

