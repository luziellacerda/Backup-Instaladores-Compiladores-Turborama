# Critérios de liberação Windows — InstallerHost.Next

Data da revisão: 05/09/2026. Este documento define critérios; não certifica um executável.

## Estado e limites

O projeto Next é novo e, nesta etapa, oferece diagnóstico e simulação explícita. Compilar uma prévia para testar a interface não significa aprovar um instalador de produção. Não anunciar “sem vírus”, “sem erros”, “aceito em todo Windows” ou compatibilidade com todos os jogos. Resultados devem identificar o artefato, o ambiente, a data e o alcance efetivamente testado.

O relatório anterior registrou scan custom do EXE sem detecção naquele momento, mas também declarou EXE `NotSigned`, ausência de instalação real e ausência de validação em Windows limpo. Isso não comprovava aprovação comportamental, reputação SmartScreen nem o fluxo completo.

A investigação do agente principal encontrou eventos Defender 1116 em 04/09/2026 às 19:55:50 e 20:39:25, com `Trojan:Win32/ClickFix.IIN!MTB`, vinculados a linhas de comando PowerShell dos testes anteriores; eventos 1117 associados registraram remediação bem-sucedida. Nos registros filtrados não foi identificada detecção de arquivo. Esta revisão não reproduziu essas ações nem reconsultou os eventos: a evidência foi fornecida pelo agente principal. Não é conclusão de falso positivo, nem prova de que o EXE é malicioso ou inofensivo. Preservar os registros locais e investigar sua correlação antes de reaproveitar qualquer mecanismo de teste anterior.

## Lacunas encontradas no processo anterior

- `Compilar_InstallerHost_Moderno.ps1:1455–1468`: `Publishable` era derivado de assinatura válida e Git limpo/estável; não exigia teste real em VM, aceite visual/funcional nem revisão dos alertas Defender da sessão.
- `Testar_InstallerHost_Moderno.ps1:392–404`: assinatura ausente era aceita como resultado esperado de pré-lançamento. A contagem de testes aprovados, portanto, não significava assinatura ou confiança Windows.
- `Publish-VerifiedInstallerHost.ps1:18–22`: verificava proveniência, canal e hash, mas não bloqueava publicação por ausência dessas evidências de execução e segurança.
- O relatório anterior mediu layout e componentes isolados, mas não demonstrou todos os percursos de navegação, novas seleções após Voltar, instalação em conta padrão/elevada, reboot e repetição após falha.

## Gates obrigatórios

| Gate | Evidência mínima e condição de aprovação |
|---|---|
| Escopo verdadeiro | Versão, arquitetura e edições/builds Windows suportadas declaradas. A prévia identifica simulação; ações reais não implementadas ficam indisponíveis, sem mensagem de sucesso fictícia. |
| Origem | Commit exato limpo, inventário dos novos arquivos e dependências, ferramentas de build identificadas, zero referência ao código/assembly antigo. Artefatos com SHA-256 e tamanhos registrados. |
| Código e navegação | Testes de seleção, estado ocupado, consentimento, teclado, ida/volta, descarte, resultados assíncronos obsoletos e recuperação. Duplo clique não duplica operações. Falhas não viram sucesso. |
| Interface real | Todas as páginas revisadas no aplicativo novo; 1366×768 e escalas 100/125/150/200%, além dos menores tamanhos suportados. Texto, contraste, sobreposições, rolagem e foco realmente inspecionados. |
| Segurança do artefato | Scan dos arquivos finais com proteção ativa, data/versão do mecanismo e inteligência registradas. Revisão dos eventos Defender durante a janela de teste, incluindo detecções de comportamento/comando, não somente arquivo. Detecção relevante não esclarecida bloqueia promoção. |
| Execução isolada | VM descartável Windows limpa e atualizada, sem exceções de antivírus; teste do arquivo efetivamente distribuído e baixado com a marca de origem preservada. Registrar Defender, SmartScreen e, quando aplicável, Smart App Control separadamente. |
| Assinatura | Para distribuição de produção fora da Store: assinatura Authenticode válida de editor autorizado e cadeia confiável, timestamp verificável, identidade esperada e verificação do arquivo final. Sem certificado/serviço autorizado, manter bloqueio de produção. |
| Instalação real futura | Somente após implementação: validar pacotes oficiais, integridade, identidade, privilégio mínimo, preflight, falta de espaço, offline, timeout, reinicialização necessária, falha parcial, rollback e nova tentativa. Usar VM com snapshot, nunca instalar tudo no PC de trabalho para testar. |
| Publicação | Agregador exige todos os gates aplicáveis em `Pass`; pendência não vira `Pass`. Separar `PreviewBuildPassed` de `ProductionReleaseApproved`. Assinar antes de calcular hashes finais e testar exatamente os bytes distribuídos. Conferir download publicado e não alterar binários assinados. |

Para a prévia, o teste de instalação real é `Não aplicável — runner simulado`, não `Aprovado`. A assinatura ausente permanece uma pendência expressa; uma prévia unsigned não pode ser apresentada como a versão solicitada “aceita pelo Windows”.

## Assinatura, antivírus e reputação são coisas diferentes

Uma assinatura identifica o editor e protege a integridade, mas não certifica ausência de malware. SmartScreen considera reputação do arquivo e do editor; um arquivo novo pode apresentar aviso mesmo assinado. Certificado autossinado não estabelece confiança pública, e EV não garante eliminar o aviso inicial. A distribuição pela Store tem regras próprias. Não prometer nem tentar fabricar reputação. [Microsoft: reputação SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

O usuário precisa disponibilizar um certificado de assinatura de código confiável ou autorizar/configurar um serviço de assinatura com identidade validada. Não comprar certificados, criar identidade, exportar chaves privadas nem alterar repositórios de confiança automaticamente. Segredos e chaves não entram no Git. Esta auditoria não procurou nem acessou chaves.

Uma detecção Defender deve ser investigada. Se houver fundamento para contestá-la, o desenvolvedor pode submeter a amostra adequada à Microsoft e aguardar a determinação; isso não é um programa de prevenção de falsos positivos nem uma aprovação automática. A submissão pode enviar o arquivo a terceiros e requer autorização apropriada. [Microsoft: FAQ para desenvolvedores](https://learn.microsoft.com/en-us/defender-xdr/developer-faq).

## Regras sem exceção

- Não desativar Defender/SmartScreen/Smart App Control, adicionar exclusões, limpar histórico, remover marca de origem ou orientar o usuário a contornar bloqueios.
- Não repetir comandos detectados, suas variantes, carregamento por reflexão do EXE anterior ou técnicas alternativas para escapar da inspeção.
- Não reaproveitar uma aprovação de scan, screenshot ou conjunto de testes para um binário alterado.
- Qualquer resultado incompleto deve permanecer pendente com motivo explícito. Nenhuma promessa absoluta substitui evidência.
