# Código-fonte do TurboRama PIX Comercial v13

## Requisitos

- Windows 10 ou 11 x64.
- Visual Studio 2022 Build Tools com **Desenvolvimento para Desktop com C++**.
- CMake usado pelo projeto TurboRama/EmulationStation.
- .NET 8 SDK para compilar o agente PIX.

## Organização

- `EmulationStation`: integração dos menus START/SELECT, ponte PIX, créditos e inicialização automática do agente.
- `PixAgent`: agente .NET que consulta o provedor, publica o QR Code e confirma o pagamento.
- `Installer`: instalador nativo e empacotador do EXE único.

## Compilação do agente

Na pasta `PixAgent`, execute:

```powershell
dotnet restore TurboRamaPixAgent.csproj
dotnet build TurboRamaPixAgent.csproj -c Release
```

## Compilação do EmulationStation

Gere a solução CMake x64 conforme a configuração já usada pelo projeto e compile o alvo `emulationstation` em `Release`. Os arquivos listados em `EmulationStation` devem permanecer nas mesmas pastas relativas do repositório original.

## Compilação do instalador

Abra o Prompt de Ferramentas Nativas x64 do Visual Studio na pasta `Installer` e execute:

```text
rc TurboRamaInstaller.rc
cl /EHsc /O2 /W4 TurboRamaInstaller.cpp TurboRamaInstaller.res /link user32.lib shlwapi.lib /SUBSYSTEM:WINDOWS /OUT:TurboRamaInstaller.exe

rc TurboRamaBootstrapper.rc
cl /EHsc /O2 /W4 TurboRamaBootstrapper.cpp TurboRamaBootstrapper.res /link user32.lib bcrypt.lib /SUBSYSTEM:WINDOWS /OUT:TurboRamaBootstrapper.exe
```

O script `Build-TurboRamaPackage.ps1` recebe o lançador, instalador interno, `7za.exe`, `payload.7z` e o caminho do EXE final. Ele acrescenta hashes SHA-256 verificados pelo lançador antes da instalação.

Credenciais reais nunca devem ser incluídas no código-fonte ou no pacote. Elas são cadastradas no próprio EmulationStation e protegidas pelo Windows.
