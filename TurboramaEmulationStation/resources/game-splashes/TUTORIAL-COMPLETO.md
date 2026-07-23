# Tutorial Completo — Memória, Vídeos e Splash de Jogos no TurboRama

**Para quem não sabe programar**  
**Versão:** TurboRama EmulationStation — Julho/2026

---

## Índice

1. [O que este tutorial explica](#1-o-que-este-tutorial-explica)
2. [Conceitos básicos (sem termos difíceis)](#2-conceitos-básicos-sem-termos-difíceis)
3. [Qual era o problema antes?](#3-qual-era-o-problema-antes)
4. [O que foi melhorado? (resumo simples)](#4-o-que-foi-melhorado-resumo-simples)
5. [Como funcionam os vídeos de splash de jogos](#5-como-funcionam-os-vídeos-de-splash-de-jogos)
6. [Passo a passo: colocar seus vídeos e imagens](#6-passo-a-passo-colocar-seus-vídeos-e-imagens)
7. [Quando cada vídeo aparece na tela](#7-quando-cada-vídeo-aparece-na-tela)
8. [As melhorias de memória explicadas para todos](#8-as-melhorias-de-memória-explicadas-para-todos)
9. [Configurações que você pode ajustar](#9-configurações-que-você-pode-ajustar)
10. [Qual configuração usar no seu PC](#10-qual-configuração-usar-no-seu-pc)
11. [Como saber se a memória está ok](#11-como-saber-se-a-memória-está-ok)
12. [Dicas para criar bons vídeos de splash](#12-dicas-para-criar-bons-vídeos-de-splash)
13. [Perguntas frequentes](#13-perguntas-frequentes)
14. [Glossário (palavras que aparecem no tutorial)](#14-glossário-palavras-que-aparecem-no-tutorial)

---

## 1. O que este tutorial explica

O TurboRama EmulationStation foi melhorado em duas frentes importantes:

1. **Memória mais inteligente** — para o programa não travar quando o tema tem muitas imagens e vídeos.
2. **Vídeos de entrada e saída de jogos** — animações ou imagens que aparecem quando você **abre** ou **fecha** um jogo.

Você **não precisa saber programar** para usar nada disso. Basta colocar arquivos nas pastas certas e, se quiser, ajustar algumas opções no menu de desenvolvedor.

---

## 2. Conceitos básicos (sem termos difíceis)

Antes de entrar nos detalhes, três ideias simples:

### RAM (memória do computador)

É a memória que o Windows usa para guardar programas e arquivos **abertos agora**.

- Imagine uma mesa de trabalho: quanto mais coisas você coloca na mesa, menos espaço sobra.
- Se colocar coisas demais, a mesa "enche" e o computador fica lento ou trava.

### VRAM (memória da placa de vídeo)

É a memória da sua placa de vídeo (GPU). O EmulationStation usa ela para mostrar imagens, capas de jogos e efeitos na tela.

- Funciona parecido com a RAM, mas é da placa de vídeo.
- Temas bonitos com muitas imagens grandes consomem bastante VRAM.

### Vídeo no EmulationStation

Um vídeo MP4 não fica só "guardado" no disco. Quando ele **toca na tela**, o programa precisa:

1. **Ler** o arquivo do disco.
2. **Decodificar** (traduzir o vídeo para imagens).
3. **Mostrar** cada frame na tela.

Esse processo usa **muita RAM e VRAM** — muito mais do que uma imagem PNG ou JPG do mesmo tamanho.

> **Regra prática:** um vídeo de 5 segundos em 1080p pode usar dezenas ou centenas de megabytes de memória **enquanto está tocando**.

---

## 3. Qual era o problema antes?

O tema TURBORAMA original vinha com **18 vídeos MP4 embutidos** — um para cada console (SNES, PS1, PS2, Wii, etc.). Eles ficavam dentro do próprio programa.

### O que acontecia na prática

| Situação | Problema |
|----------|----------|
| Navegar entre consoles no menu | O programa tentava preparar vídeos de vários sistemas ao mesmo tempo |
| Tema com muitas imagens grandes | Centenas de capas iam para a memória |
| PC com 4 GB de RAM | Travamentos, tela preta, fechamento inesperado |
| Placa de vídeo modesta | Imagens sumiam ou o menu ficava lento |

### Por que isso era grave?

Pense assim: você está no corredor de um shopping olhando vitrines. O programa antigo tentava **ligar a TV de todas as lojas ao mesmo tempo**, mesmo das que você nem estava olhando. Isso gastava energia (memória) à toa.

---

## 4. O que foi melhorado? (resumo simples)

As melhorias podem ser entendidas como **cinco regras novas**:

| # | Regra | Em linguagem simples |
|---|-------|----------------------|
| 1 | **Vídeos sob demanda** | O vídeo só toca quando você abre ou fecha um jogo — não fica rodando o tempo todo no menu |
| 2 | **Vídeos fora do tema** | Os MP4 saíram de dentro do programa; você coloca na pasta `game-splashes` |
| 3 | **Limite de memória configurável** | O programa sabe quando "a mesa está cheia" e começa a guardar menos coisas |
| 4 | **Menos pré-carregamento** | Só prepara o sistema que você está vendo e os vizinhos imediatos — não mais 5 de uma vez |
| 5 | **Controle de vídeos simultâneos** | Se muitos vídeos quiserem tocar juntos, os menos importantes esperam ou param |

### Resultado para você

- Menu mais estável com temas pesados.
- Vídeos personalizados na hora de jogar.
- Opções para ajustar se o PC for fraco ou potente.

---

## 5. Como funcionam os vídeos de splash de jogos

**Splash** = aquela tela que aparece por alguns segundos com uma imagem, logo ou vídeo.

No TurboRama existem **dois momentos**:

| Momento | Nome do arquivo | Quando aparece |
|---------|-----------------|----------------|
| **Entrada** | `entrada.mp4` (ou `.png`, `.jpg`) | Ao apertar para **iniciar** o jogo |
| **Saída** | `saida.mp4` (ou `.png`, `.jpg`) | Ao **voltar** do emulador para o menu |

### Ordem de busca (o que o programa procura primeiro)

Quando você lança um jogo de SNES, o programa procura nesta ordem:

```
1º) game-splashes/snes/entrada.mp4
2º) game-splashes/snes/entrada.png  (ou .jpg, .svg)
3º) game-splashes/snes/launch.mp4   (nome alternativo)
4º) game-splashes/snes/entry.mp4    (nome alternativo)
5º) game-splashes/snes/snes.mp4     (nome do próprio sistema)
6º) game-splashes/default/snes.mp4  (pasta "default" = genérico)
7º) game-splashes/default/entrada.mp4
8º) Se nada for encontrado → usa a capa do jogo + tema padrão
```

> **Dica:** o programa usa o **primeiro arquivo que encontrar** nessa lista. Não precisa ter todos — um só já basta.

### Onde ficam as pastas

O programa procura em **três lugares** (nesta ordem):

| Prioridade | Caminho no Windows | Para que serve |
|------------|-------------------|----------------|
| 1ª | `C:\Users\SEU_USUARIO\.emulationstation\game-splashes\` | Sua pasta pessoal (recomendado) |
| 2ª | `Pasta do TurboRama\resources\game-splashes\` | Pasta da instalação |
| 3ª | Pasta home do usuário | Alternativa em alguns setups |

**Recomendação:** use a pasta em `.emulationstation` — suas personalizações não se perdem ao atualizar o programa.

### Nomes de pasta = nome do sistema

O nome da pasta deve ser **igual ao nome do sistema** no EmulationStation:

| Pasta | Sistema |
|-------|---------|
| `snes` | Super Nintendo |
| `psx` | PlayStation 1 |
| `ps2` | PlayStation 2 |
| `gba` | Game Boy Advance |
| `gc` | GameCube |
| `wii` | Nintendo Wii |
| `arcade` | Arcade / MAME |

> Se não souber o nome exato, abra o menu de sistemas e veja como o console aparece nas configurações, ou consulte a pasta `roms` do seu setup.

---

## 6. Passo a passo: colocar seus vídeos e imagens

### Exemplo completo: splash de vídeo para SNES

**Passo 1 — Criar a pasta**

No Windows Explorer, navegue até:

```
C:\Users\SEU_USUARIO\.emulationstation\game-splashes\
```

Se a pasta `.emulationstation` não existir, crie-a. Dentro dela, crie `game-splashes`, e dentro desta, crie `snes`:

```
.emulationstation\
  game-splashes\
    snes\
```

**Passo 2 — Colocar os arquivos**

Copie seus arquivos para dentro de `snes\`:

```
.emulationstation\
  game-splashes\
    snes\
      entrada.mp4    ← vídeo ao abrir jogo
      saida.mp4      ← vídeo ao fechar jogo
```

**Passo 3 — Testar**

1. Abra o TurboRama EmulationStation.
2. Vá até o sistema SNES.
3. Escolha qualquer jogo e aperte para iniciar.
4. O vídeo `entrada.mp4` deve aparecer em tela cheia por alguns segundos.
5. Jogue e saia do emulador (tecla de saída configurada).
6. O vídeo `saida.mp4` deve aparecer ao voltar ao menu.

**Passo 4 — Se não funcionar**

- Confira se o nome da pasta é exatamente `snes` (minúsculas).
- Confira se o arquivo se chama `entrada.mp4` (não `Entrada.mp4` em alguns casos sensíveis).
- Teste com uma imagem `entrada.png` primeiro — é mais leve e mais fácil de verificar.

### Exemplo: splash genérico para todos os sistemas

Se você não quiser criar pasta para cada console, use a pasta `default`:

```
.emulationstation\
  game-splashes\
    default\
      entrada.mp4     ← usado quando não há pasta específica
      saida.png       ← imagem de saída genérica
      snes.mp4        ← fallback: se não achar pasta snes/, usa este para SNES
```

### Exemplo: só imagem (sem vídeo)

Funciona perfeitamente. Coloque um PNG ou JPG:

```
game-splashes\
  psx\
    entrada.png
    saida.png
```

A imagem fica **2 segundos** na tela e some.

### Formatos aceitos

| Formato | Tipo | Observação |
|---------|------|------------|
| `.mp4` | Vídeo | Recomendado. Use H.264. |
| `.png` | Imagem | Boa qualidade, arquivo maior |
| `.jpg` / `.jpeg` | Imagem | Arquivo menor |
| `.svg` | Imagem vetorial | Escala bem em qualquer resolução |

---

## 7. Quando cada vídeo aparece na tela

### Linha do tempo ao abrir um jogo

```
[Você aperta "Jogar"]
        ↓
[Vídeo/imagem de ENTRADA aparece]  ← entrada.mp4
        ↓
[Vídeo termina ou atinge tempo máximo]
        ↓
[Emulador abre o jogo]
        ↓
[Você joga...]
        ↓
[Você sai do emulador]
        ↓
[Vídeo/imagem de SAÍDA aparece]    ← saida.mp4
        ↓
[Volta ao menu do EmulationStation]
```

### Quanto tempo fica na tela?

| Tipo | Tempo |
|------|-------|
| **Imagem** (PNG, JPG) | 2 segundos |
| **Vídeo** (MP4) | Do início ao fim do vídeo |
| **Vídeo muito longo** | Máximo de 30 segundos (corta se passar disso) |
| **Vídeo muito curto** | Mínimo de 0,8 segundo (evita "piscar" na tela) |

### O que acontece se não tiver arquivo de splash?

O programa usa o **plano B**:

1. Mostra a **capa do jogo** que você selecionou.
2. Mostra o **nome do jogo** embaixo.
3. Usa o visual do tema (`gamesplash.xml`) se existir.

Ou seja: sem arquivos em `game-splashes`, tudo funciona como antes — só sem vídeo personalizado.

---

## 8. As melhorias de memória explicadas para todos

Esta seção explica **o que o programa faz por baixo dos panos** para não travar. Você não precisa fazer nada — mas entender ajuda a ajustar se necessário.

### 8.1 — Vídeos removidos de dentro do tema

**Antes:** 18 vídeos MP4 vinham embutidos no tema TURBORAMA (~120 MB no disco, muito mais na memória ao tocar).

**Agora:** esses vídeos foram **removidos do pacote**. Você decide quais sistemas terão vídeo, colocando arquivos em `game-splashes`.

**Benefício:** o menu principal não carrega 18 decodificadores de vídeo. A memória fica livre para capas, fundos e navegação.

---

### 8.2 — O programa "limpa a mesa" sozinho (VRAM e RAM)

Imagine que o EmulationStation tem uma **mesa de trabalho** (memória) onde coloca imagens que você vê na tela.

**VRAM** = mesa da placa de vídeo (imagens na tela)  
**RAM cache** = gaveta onde guarda cópias de imagens já abertas (para abrir mais rápido depois)

Quando a mesa enche, o programa:

1. Olha quais imagens **não estão sendo usadas agora**.
2. **Tira** essas imagens da mesa.
3. Se precisar de novo, carrega outra vez (um pouco mais lento, mas sem travar).

**Novidade:** agora você define o tamanho máximo dessa mesa nas configurações (`MaxVRAM` e `MaxRAM`).

| Configuração | O que limita | Padrão no PC 64-bit |
|--------------|--------------|---------------------|
| **MaxVRAM** | Memória da placa de vídeo | 4096 MB |
| **MaxRAM** | Gaveta de imagens em RAM | Configurável (512 MB se não definir) |

---

### 8.3 — Fila de carregamento de imagens (MaxAsyncQueue)

Quando você rola a lista de jogos, o programa carrega capas em **segundo plano**.

**Antes:** podia tentar carregar muitas imagens ao mesmo tempo → pico de memória.

**Agora:** existe uma **fila** com limite (padrão: 16 imagens na fila). Se a memória estiver quase cheia (90%), a fila **pausa** até liberar espaço.

**Analogia:** em vez de 50 pessoas entrarem no elevador de uma vez, entram 16 e as outras esperam na fila.

---

### 8.4 — Só o sistema selecionado toca vídeo no menu

**Antes:** ao navegar no carrossel de consoles, o programa preparava mídia de **5 sistemas** (o atual, 2 antes e 2 depois), **incluindo vídeos**.

**Agora:** prepara apenas **3 sistemas** (atual + 1 antes + 1 depois), e **só o sistema que você está olhando** pode tocar vídeo de fundo. Os vizinhos carregam imagem estática, não vídeo.

**Analogia:** no shopping, só a loja que você está olhando tem a TV ligada. As vizinhas mostram um cartaz fixo.

---

### 8.5 — Limite de vídeos ao mesmo tempo (MaxConcurrentVideos)

Se o tema tiver muitos vídeos de fundo e você ativar o limite, o programa usa um sistema de **prioridades**:

| O que está na tela | Prioridade |
|--------------------|------------|
| Vídeo de fundo do sistema selecionado | Alta |
| Vídeos decorativos do tema | Média |
| Vídeos invisíveis ou quase transparentes | Baixa |
| Screensaver (protetor de tela) | Muito alta |

Quando o limite de slots é atingido (padrão: 8 vídeos), o vídeo **menos importante** para para dar lugar ao mais importante.

**Por padrão esse limite está DESLIGADO** (`EnforceVideoLimit = false`) para temas pesados funcionarem livremente em PCs potentes. Ligue só se tiver instabilidade.

---

### 8.6 — Splash de jogo libera memória imediatamente

Este é um dos pontos mais importantes para vídeos de entrada/saída:

```
Abre splash → toca vídeo → fecha splash → APAGA tudo da memória
```

Diferente dos vídeos do tema (que ficam no menu), o splash:

- Só existe **durante** a transição.
- Ao terminar, o vídeo é **destruído** — decoder, buffers e textura são liberados.
- O emulador abre com a memória "limpa".

**Por isso** vídeos de splash podem ser mais elaborados que vídeos de fundo do menu — eles tocam poucos segundos e somem.

---

## 9. Configurações que você pode ajustar

Todas ficam no **Menu de Desenvolvedor** (protegido por senha). Para abrir:

1. Vá em **Menu Principal → Configurações de UI e Ajustes**.
2. Procure a opção de **menu desenvolvedor** (requer senha).

### Opções de memória e vídeo

| Opção no menu | O que faz | Valor padrão |
|---------------|-----------|--------------|
| **VRAM LIMIT** | Quanta memória da placa de vídeo o programa pode usar para imagens | 4096 MB (PC 64-bit) |
| **RAM CACHE LIMIT** | Quanta RAM usar para guardar imagens já carregadas | Depende do hardware |
| **ASYNC IMAGE QUEUE SIZE** | Quantas imagens podem carregar ao mesmo tempo em segundo plano | 16 |
| **ENFORCE VIDEO LIMIT** | Liga/desliga o limite de vídeos simultâneos | Desligado |
| **MAX CONCURRENT VIDEOS** | Quantos vídeos podem tocar juntos (se o limite estiver ligado) | 8 |
| **SHOW FRAMERATE** | Mostra informações de memória no canto da tela | Desligado |

### Linha de comando (avançado)

Se você inicia o EmulationStation por um atalho ou script, pode passar:

```
emulationstation.exe --max-vram 2048 --max-ram 512
```

| Parâmetro | Significado |
|-----------|-------------|
| `--max-vram 2048` | Limita VRAM a 2048 MB |
| `--max-ram 512` | Limita cache RAM a 512 MB |

> Na maioria dos casos, **não é necessário** usar linha de comando — o menu desenvolvedor basta.

---

## 10. Qual configuração usar no seu PC

### PC potente (16 GB RAM, placa de vídeo dedicada)

Você quer o visual máximo. Use:

| Opção | Valor sugerido |
|-------|----------------|
| VRAM LIMIT | 4096 |
| RAM CACHE LIMIT | 1024 – 2048 |
| ASYNC IMAGE QUEUE SIZE | 16 |
| ENFORCE VIDEO LIMIT | **Desligado** |
| Vídeos em game-splashes | Pode usar MP4 em 1080p |

### PC médio (8 GB RAM, placa integrada ou modesta)

| Opção | Valor sugerido |
|-------|----------------|
| VRAM LIMIT | 1024 – 2048 |
| RAM CACHE LIMIT | 512 |
| ASYNC IMAGE QUEUE SIZE | 12 |
| ENFORCE VIDEO LIMIT | **Desligado** (ligue se travar) |
| Vídeos em game-splashes | MP4 em 720p, 3–5 segundos |

### PC fraco (4 GB RAM ou menos)

| Opção | Valor sugerido |
|-------|----------------|
| VRAM LIMIT | 512 – 1024 |
| RAM CACHE LIMIT | 256 – 384 |
| ASYNC IMAGE QUEUE SIZE | 8 |
| ENFORCE VIDEO LIMIT | **Ligado** |
| MAX CONCURRENT VIDEOS | 4 |
| Vídeos em game-splashes | Prefira **imagens PNG** em vez de vídeo |

---

## 11. Como saber se a memória está ok

### Ativar o monitor na tela

1. Abra o menu desenvolvedor.
2. Ative **SHOW FRAMERATE**.
3. No canto da tela aparecerá algo como:

```
60.0fps, 16.67ms
Font VRAM: 12.5  Tex VRAM: 256.3  Cached Tex RAM: 180.1
Known Tex: 320.0  Max VRAM: 4096  Max RAM: 512  Queued: 2
```

### O que cada número significa

| Informação | O que é | Quando se preocupar |
|------------|---------|---------------------|
| **fps** | Quadros por segundo | Abaixo de 30 = menu lento |
| **Tex VRAM** | Memória de vídeo em uso (MB) | Perto do Max VRAM |
| **Cached Tex RAM** | Imagens guardadas em RAM (MB) | Perto do Max RAM |
| **Queued** | Imagens esperando para carregar | Sempre em 16 = fila cheia |
| **Max VRAM / Max RAM** | Seus limites configurados | — |

### Sinais de que precisa reduzir limites ou usar menos vídeos

- Menu trava ao trocar de sistema.
- Imagens de capa somem e demoram a voltar.
- "Queued" fica sempre no máximo.
- O programa fecha sozinho ao navegar.
- Tela preta ao voltar do emulador.

**Solução rápida:** reduza RAM CACHE LIMIT, ligue ENFORCE VIDEO LIMIT, ou troque vídeos de splash por imagens PNG.

---

## 12. Dicas para criar bons vídeos de splash

### Tamanho e duração recomendados

| Item | Recomendação |
|------|--------------|
| Resolução | 1920×1080 (1080p) ou 1280×720 (720p) |
| Duração | 2 a 5 segundos |
| Formato | MP4 com codec H.264 |
| Áudio | Pode ter som — o splash reproduz áudio |
| Tamanho do arquivo | Ideal: menos de 5 MB por vídeo |

### Ferramentas gratuitas para criar/editar

- **DaVinci Resolve** — editar e exportar MP4.
- **Shotcut** — simples e leve.
- **FFmpeg** (linha de comando) — converter e comprimir.

### Comando FFmpeg para comprimir um vídeo (opcional)

Se tiver FFmpeg instalado, este comando reduz o tamanho mantendo boa qualidade:

```
ffmpeg -i entrada_original.mp4 -c:v libx264 -crf 23 -preset medium -vf scale=1920:1080 -an entrada.mp4
```

- `-an` remove o áudio (deixa mais leve). Remova `-an` se quiser som.

### O que evitar

- Vídeos longos (mais de 10 segundos) — o usuário espera jogar, não assistir.
- Resolução 4K — desperdiça memória sem ganho visível na maioria das telas.
- Muitos efeitos e transições pesadas.
- Arquivos maiores que 20 MB — demora para carregar do disco.

---

## 13. Perguntas frequentes

### O vídeo de entrada não aparece. O que fazer?

1. Verifique se a pasta tem o nome correto do sistema (`snes`, `psx`, etc.).
2. Verifique se o arquivo se chama `entrada.mp4` (minúsculas).
3. Teste com `entrada.png` — se a imagem funcionar, o problema é o vídeo (codec ou arquivo corrompido).
4. Reencode o MP4 para H.264 com FFmpeg ou HandBrake.

### O vídeo aparece na entrada mas não na saída

- Confirme que existe `saida.mp4` (ou `saida.png`) na mesma pasta.
- A saída só aparece se `HideWindow` estiver desligado nas configurações.

### Posso usar só entrada sem saída?

Sim. Se não houver arquivo de saída, o programa volta direto ao menu sem splash.

### O splash atrapalha porque demora. Posso encurtar?

- Para imagens: duração fixa de 2 segundos (não configurável pelo usuário).
- Para vídeos: edite o MP4 para ser mais curto (2–3 segundos).
- Vídeos com mais de 30 segundos são cortados automaticamente.

### Posso ter splash diferente para cada jogo?

Não diretamente por jogo. O splash é **por sistema** (SNES, PS1, etc.). Todos os jogos de SNES usam o mesmo `entrada.mp4` de `game-splashes/snes/`.

### Os vídeos do tema TURBORAMA sumiram. É bug?

Não. Foram **movidos de propósito** para `game-splashes`. Coloque os MP4 na pasta do sistema correspondente se quiser o mesmo efeito de antes.

### Preciso recompilar o programa para usar splash?

Não. Basta colocar os arquivos na pasta `game-splashes` e reiniciar o EmulationStation.

### Funciona no Batocera / Linux?

Sim. A lógica é a mesma. A pasta pessoal no Linux fica em:

```
~/.emulationstation/game-splashes/
```

---

## 14. Glossário (palavras que aparecem no tutorial)

| Palavra | Significado simples |
|---------|---------------------|
| **Splash** | Tela de abertura — imagem ou vídeo que aparece por instantes |
| **VRAM** | Memória da placa de vídeo |
| **RAM** | Memória principal do computador |
| **MP4** | Formato de arquivo de vídeo mais comum |
| **H.264** | Tipo de compressão de vídeo — o mais compatível |
| **Codec** | Programa que decodifica vídeo para exibir na tela |
| **Decoder** | O "tradutor" que transforma o MP4 em imagens na tela |
| **Cache** | Cópia guardada na memória para abrir mais rápido depois |
| **Fallback** | Plano B — o que o programa usa quando não acha o arquivo principal |
| **game-splashes** | Pasta onde você coloca vídeos/imagens de entrada e saída |
| **Tema** | Visual do EmulationStation (cores, fontes, fundos, layout) |
| **Emulador** | Programa que roda o jogo (RetroArch, Dolphin, etc.) |
| **Overlay** | Informação exibida por cima da tela (como o monitor de FPS/memória) |
| **Pré-carregamento** | Carregar arquivos antes de você precisar, para aparecer mais rápido |
| **Slot** | "Vaga" disponível — no caso, vaga para um vídeo tocar ao mesmo tempo |

---

## Resumo final

| O que você quer | O que fazer |
|-----------------|-------------|
| Vídeo ao abrir jogo SNES | Coloque `entrada.mp4` em `game-splashes/snes/` |
| Vídeo ao fechar jogo SNES | Coloque `saida.mp4` em `game-splashes/snes/` |
| Splash para todos os sistemas | Coloque arquivos em `game-splashes/default/` |
| Menu mais leve | Reduza RAM CACHE LIMIT; use imagens em vez de vídeos no menu |
| Menu travando | Ligue ENFORCE VIDEO LIMIT; reduza MAX CONCURRENT VIDEOS para 4 |
| Ver uso de memória | Ative SHOW FRAMERATE no menu desenvolvedor |

---

*Tutorial do TurboRama EmulationStation — memória, vídeos e splash de jogos.*  
*Dúvidas sobre nomes de pasta: consulte também o arquivo `LEIA-ME.txt` nesta mesma pasta.*