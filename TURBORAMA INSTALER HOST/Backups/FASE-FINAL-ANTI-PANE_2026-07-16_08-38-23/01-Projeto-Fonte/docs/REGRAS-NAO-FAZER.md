# 15 regras — não fazer (estudo §30)

Base: `AUDITORIA GPT COMO CONSTRUIR PROGRAMA.txt`  
Projeto de referência (legado): `TurboRamaFactoryShell` — **não copiar estes padrões**.

1. Não alterar o shell **global (HKLM)** quando o shell **por usuário** for suficiente.
2. Não usar conta kiosk com **senha vazia**.
3. Não relaxar políticas globais de senha (`LimitBlankPasswordUse`, PasswordLess) sem necessidade.
4. Não habilitar **Keyboard Filter** por padrão.
5. Não alterar **BCD** sem exportação e rollback explícitos.
6. Não alterar timeouts globais do Windows para **1 segundo**.
7. Não ativar **UWF** sem exclusões completas (Data, Saves, Logs, Config).
8. Não apagar conta ou perfil sem validação de dados.
9. Não aplicar alteração sem **capturar o valor anterior**.
10. Não considerar restauração concluída só porque o Explorer voltou.
11. Não deixar watchdog/agente recriar configs durante o rollback.
12. Não misturar launcher, instalador, segurança e recuperação em um único executável.
13. Não executar o frontend permanentemente como administrador.
14. Não depender só de scripts BAT/PowerShell para operações críticas.
15. Não apresentar “estado original restaurado” sem **comparação real com o baseline**.

## Princípio

> Nenhuma alteração pode ser aplicada sem antes registrar exatamente o estado original e definir como restaurá-lo.
