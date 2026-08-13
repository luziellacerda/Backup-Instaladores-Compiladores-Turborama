# Reconhecimento externo da máquina TurboRama PIX

Aplicativo gráfico nativo que registra uma máquina já licenciada no servidor TurboRama. O nome histórico do binário foi mantido por compatibilidade com o instalador:

`CONFIGURAR-ACCESS-TOKEN-PIX.exe`

## Fluxo de uso

1. O administrador cria a licença no painel e anota seu identificador permanente (`TR-...`).
2. No painel, gera um código único de ativação para essa licença.
3. O Windows e o TurboRama usam a conta local `Admin` como usuário automático do gabinete.
4. O administrador abre a cópia instalada deste programa em `D:\emulationstation` e confirma uma única vez a autorização administrativa do Windows para alinhar configurações antigas.
5. Informa a licença permanente, escolhe o tipo de proteção e cola o código único.
6. O próprio `Admin` grava o cadastro on-line e cria a identidade criptográfica no mesmo perfil usado pelo EmulationStation.
7. Nenhuma segunda sessão e nenhuma senha são solicitadas.
8. Depois da confirmação, o EmulationStation pode ser aberto normalmente. O administrador pode apagar o configurador temporário.

Ao detectar uma configuração antiga apontando para outro usuário, o programa cria backup do `turborama.json`, alinha `kioskUser` e o login automático para `Admin` e continua na própria sessão atual.

## Segurança e comportamento

- O código único passa ao agente por pipe anônimo local e nunca aparece em argumento, arquivo ou variável de ambiente.
- O código é apagado do campo e da memória logo após a entrega.
- Nenhuma senha é solicitada, consultada ou armazenada.
- O processo é validado pelo SID exato do `Admin` configurado no Launcher e no login automático; outro administrador não é aceito.
- Variáveis que poderiam redirecionar o agente, o runtime ou a pasta PIX são removidas antes da ativação.
- O arquivo persistente contém a licença permanente, o servidor fixo e o perfil de proteção; não contém o código único.
- A ativação preserva o provedor de pagamento, o PDV, a credencial protegida e os preços que já existem no TurboRama.
- O servidor on-line reconhece a licença/máquina; ele não é o provedor PIX e não controla os preços locais.
- Os preços já configurados são preservados. Em uma instalação nova, são usados os valores-padrão existentes.
- Em rejeição confirmada, o cadastro anterior é restaurado.
- Se a resposta final ficar incerta, o cadastro candidato é preservado para conferência no painel e o programa orienta não gerar outro código.
- `SOFTWARE_BOUND_ONLINE` atende computadores sem TPM. `TPM_BOUND` deve ser escolhido somente quando o TPM estiver disponível e pronto.
- Este programa não altera ROMs, temas, créditos, executável ou lógica do EmulationStation.

## Compilação

No Prompt de Ferramentas Nativas x64 do Visual Studio:

```text
rc /nologo /foTurboRamaPixCredentialEditor.res TurboRamaPixCredentialEditor.rc
cl /nologo /std:c++17 /utf-8 /EHsc /O2 /W4 /Fo:TurboRamaPixCredentialEditor.obj /Fe:CONFIGURAR-ACCESS-TOKEN-PIX.exe TurboRamaPixCredentialEditor.cpp TurboRamaPixCredentialEditor.res user32.lib gdi32.lib crypt32.lib comdlg32.lib shell32.lib advapi32.lib bcrypt.lib credui.lib ole32.lib userenv.lib /link /SUBSYSTEM:WINDOWS
```

Autoteste sem licença, código ou conexão reais:

```text
CONFIGURAR-ACCESS-TOKEN-PIX.exe --self-test
```
