# Editor seguro do Access Token PIX

Aplicativo gráfico nativo para o proprietário colar ou importar de um arquivo TXT o Access Token do Mercado Pago.

- Interface comercial com identidade LZ Games / TurboRama.
- O token fica mascarado por padrão e pode ser exibido temporariamente.
- Valida o formato `APP_USR-` antes do envio.
- Cifra a credencial com a chave pública do agente PIX.
- Aguarda a confirmação do agente antes de informar sucesso.
- O agente protege a credencial pelo Windows para esta máquina e usuário.
- Nunca grava o Access Token como texto comum.
- Não altera ROMs, temas, créditos ou o cadastro do proprietário.

Compilação no Prompt de Ferramentas Nativas x64 do Visual Studio:

```text
rc TurboRamaPixCredentialEditor.rc
cl /std:c++17 /utf-8 /EHsc /O2 /W4 TurboRamaPixCredentialEditor.cpp TurboRamaPixCredentialEditor.res /link user32.lib gdi32.lib crypt32.lib advapi32.lib bcrypt.lib comdlg32.lib shell32.lib /SUBSYSTEM:WINDOWS /OUT:CONFIGURAR-ACCESS-TOKEN-PIX.exe
```

Autoteste sem credenciais reais:

```text
CONFIGURAR-ACCESS-TOKEN-PIX.exe --self-test
```
