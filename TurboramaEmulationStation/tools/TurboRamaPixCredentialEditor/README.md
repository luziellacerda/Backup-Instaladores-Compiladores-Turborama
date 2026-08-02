# Editor externo do Access Token PIX

Utilitário gráfico nativo para o proprietário colar ou importar de um arquivo TXT o Access Token do Mercado Pago.

- Grava em `D:\emulationstation\.emulationstation\pix\secret.dat`.
- Usa Windows DPAPI com a mesma entropia do EmulationStation e do agente PIX.
- Nunca grava o token em texto simples.
- Cria backup da credencial anterior.
- Reinicia somente o agente PIX depois de salvar.
- Não altera ROMs, temas, créditos ou `owner-settings.json`.

Compilação no Prompt de Ferramentas Nativas x64 do Visual Studio:

```text
rc TurboRamaPixCredentialEditor.rc
cl /std:c++17 /EHsc /O2 /W4 TurboRamaPixCredentialEditor.cpp TurboRamaPixCredentialEditor.res /link user32.lib gdi32.lib crypt32.lib comdlg32.lib shell32.lib /SUBSYSTEM:WINDOWS /OUT:CONFIGURAR-ACCESS-TOKEN-PIX.exe
```
