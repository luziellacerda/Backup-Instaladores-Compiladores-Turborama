# InstallerHost Neon Fresh PC

O `InstallerHost` é o host gráfico do instalador TurboRama. A versão 9 combina:

- interface WinForms neon responsiva a 100%, 150% e 200% de DPI;
- diagnóstico de hardware, Direct3D, Vulkan e runtimes para jogos/emuladores;
- pacote offline com dependências Microsoft aprovadas;
- validação por tamanho, SHA-256, Authenticode e certificado fixados em
  `InstallerHost/prerequisites.lock.json`;
- suporte ao pacote principal dividido em `setup.exe.pkg.001`, `.002`, etc.

## Compilar em um PC novo

Requisitos de desenvolvimento:

- Windows 10/11 x64;
- PowerShell 5.1 ou superior;
- Visual Studio 2022 ou Build Tools 2022 com **Desenvolvimento para desktop com .NET**;
- targeting pack do .NET Framework 4.7.2;
- acesso HTTPS às fontes registradas no lockfile.

No PowerShell, a partir desta pasta:

```powershell
.\Baixar_Prerequisitos_Instalador.ps1 -ForBuild
.\Compilar_InstallerHost_Moderno.ps1
.\Testar_InstallerHost_Moderno.ps1
```

O downloader apenas baixa e audita arquivos; ele não executa instaladores. O build
normal falha se a árvore Git estiver suja. Para uma compilação local de diagnóstico,
use `-AllowDirty`; esse artefato será marcado como não publicável.

## Montar o instalador completo

O executável gerado é o **host**, não inclui ROMs, BIOS, firmware nem jogos
comerciais. O `RetroBuild` gera o ZIP do produto e o divide em partes. Distribua na
mesma pasta:

```text
TurboRama-setup.exe
TurboRama-setup.exe.pkg.001
TurboRama-setup.exe.pkg.002   (quando existir)
TurboRama-setup.exe.sha256.txt
```

O formato do sidecar gerado pelo RetroBuild é estrito: cada linha contém 64
caracteres hexadecimais do SHA-256, exatamente dois espaços e somente o nome do
arquivo. Ele precisa listar exatamente o próprio `setup.exe`, todas as partes
contíguas `.pkg.001` a `.pkg.NNN` e um único `.zip` lógico. Antes de extrair, o
host confere os hashes individuais e o hash streaming da concatenação; mantém
abertos o setup, o sidecar e **todas** as partes sem compartilhamento de escrita
ou exclusão até o fim da extração. Nomes antigos como `setup.pkg.001`, gaps,
sobras, duplicatas, reparse points e o formato anexado sem sidecar são recusados.

Escolha uma pasta local vazia e gravável pela conta padrão. Embora o host permaneça
elevado para os instaladores de pré-requisitos, toda a abertura do pacote principal,
extração, validação e reversão é executada com o token vinculado não elevado
(integridade Medium ou inferior e sem o grupo Administradores habilitado). Se o
Windows não fornecer esse contexto, o fluxo falha fechado. O destino padrão fica em
`%LOCALAPPDATA%\TurboRama`.

A extração canonicaliza cada caminho, recusa junctions/symlinks em toda a cadeia
relevante, mantém handles sem compartilhamento de escrita/exclusão, cria arquivos
somente com semântica `CreateNew` e nunca sobrescreve um arquivo ou hardlink
preexistente. A transação registra apenas arquivos e diretórios que ela própria
criou. Uma falha de escrita ou validação reverte esses objetos, por identidade e
handle, na ordem inversa; uma pasta raiz vazia que já existia e qualquer objeto não
registrado são preservados. O botão Cancelar, X e Alt+F4 ficam bloqueados enquanto a
transação está ativa, para que uma falha tratável conclua a reversão antes de sair.

Essa reversão cobre exceções tratadas durante a execução; ela **não** é crash-safe
contra encerramento forçado do processo, queda de energia ou falha do sistema. Após
um evento desse tipo, não apague dados automaticamente: inspecione o destino e tente
novamente em outra pasta vazia. O host também não cria atalhos nem executa o produto
extraído dentro do processo elevado; a tela final apenas apresenta o resultado.

O sidecar simples protege a integridade de transporte e detecta corrupção, mas
não autentica sozinho quem publicou um conjunto inteiramente substituído. Uma
distribuição de produção também deve assinar o host e um manifesto de conteúdo
com Authenticode/CMS confiável e timestamp.

Sem um pacote `.pkg`, o host continua útil para inspeção/diagnóstico, mas a etapa de
instalação do produto informa que o conteúdo principal está ausente.

Emuladores e jogos devem ser obtidos de suas fontes oficiais. BIOS, firmware, ROMs e
chaves permanecem sob responsabilidade do usuário e devem ter origem legal.

## Atualizar dependências

Não troque um binário dentro de `resources/prerequisites` isoladamente. Uma atualização
deliberada exige revisão da URL oficial, assinatura, certificado, versão, tamanho e
SHA-256, seguida da alteração do lockfile e de todos os testes. Downloads parciais,
arquivos extras e pacotes sem catálogo são recusados.

O .NET Framework 3.5 não é empacotado. Use o recurso oficial do Windows; a forma de
instalação depende da versão do sistema. Dokany, WinFsp, OpenAL, drivers de GPU e
serviços de lojas são opcionais e devem vir diretamente do fornecedor quando uma
ferramenta específica os exigir.

## Assinatura do host

O pipeline registra o estado Authenticode do `InstallerHost.exe`. Sem um certificado
de assinatura de código confiável e timestamp, o resultado deve ser publicado como
pré-lançamento e acompanhado do SHA-256 e do manifesto de build. Nunca substitua essa
etapa por um certificado autoassinado apresentado como produção.
