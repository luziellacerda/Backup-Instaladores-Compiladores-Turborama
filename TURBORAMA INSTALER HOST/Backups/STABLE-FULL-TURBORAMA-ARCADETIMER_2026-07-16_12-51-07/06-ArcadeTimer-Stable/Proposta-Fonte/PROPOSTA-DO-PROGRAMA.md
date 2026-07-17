# Proposta do Programa — TurboRama Arcade Timer

## Objetivo

Criar um controlador externo de ficha e tempo para máquina arcade com Windows e EmulationStation/ES-DE.

## Regra absoluta

O programa não modifica nenhum comando, tecla, script, arquivo, configuração ou comportamento do kiosk existente.

## Funcionamento

1. O aceitador de ficha envia uma tecla exclusiva pelo encoder USB.
2. O programa detecta a tecla globalmente.
3. Cada ficha soma minutos ao crédito.
4. O tempo fica parado no EmulationStation.
5. O tempo corre apenas quando um emulador autorizado estiver aberto.
6. Ao zerar, somente o emulador autorizado é encerrado.
7. O EmulationStation permanece aberto.
8. O crédito é salvo em arquivo próprio.

## Segurança

- Lista branca de emuladores.
- Lista de processos protegidos.
- Uma única instância.
- Debounce contra ficha duplicada.
- Backup automático do crédito.
- Logs diários.
- Nenhuma alteração no kiosk base.

## Plataforma

- Windows 10/11 ou Windows IoT Enterprise/LTSC.
- .NET 8 WinForms.
- Compatível com EmulationStation e ES-DE.
