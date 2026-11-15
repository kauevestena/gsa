# Tarefas — F07

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F07-01 — Criar processamento básico por receita

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #8 (`594ddf7`, run `43676281804`); [resumo CI](../../../artifacts/evidence/M2-wave3-ci/summary.json) e [evidências](../../../artifacts/evidence/T-F07-01/README.md). `OfficeProcessingQueue` (`ProcessingRecipe`, `ProcessedProduct`, `ProcessingJob`) em `Gsa.Missions.Office` + `CancelProcessing` em `ContractStateMachine`: fila única, prévia de tempo/custo, débito idempotente de taxa, avanço de calendário com expiração, cancelamento sem corromper brutos e produtos versionados (`Revision` + SHA-256 de receita).

**Marco:** M2

**Dependências:** T-F04-03, T-F06-03

**Requisitos:** REQ-F07-01

**Cenários relacionados:** AC-F07-01

**Entregável e limite:** Seleção de imagens, produto versionado, progresso/cancelamento e tempo de escritório.

**Critério de aceite:** Reprocessar mantém brutos; job cancelado não gera produto entregável incompleto.

**Evidência:** Hashes antes/depois e teste de cancelamento.

### T-F07-02 — Integrar Arcade e Técnico simplificados

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F07-01, T-F11-02

**Requisitos:** REQ-F07-02

**Cenários relacionados:** AC-F07-02

**Entregável e limite:** Mesmos operadores com assistência automática ou seleção manual, custo/tempo informados.

**Critério de aceite:** Mesma receita produz mesmo CQ nos dois modos.

**Evidência:** Comparação de produtos e roteiro de UI.

**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F07-02](../../../artifacts/evidence/T-F07-02/README.md)).

### T-F07-03 — Integrar processamento avançado

- [ ] Aceita

**Marco:** M5

**Dependências:** T-F14-04, T-F13-03, T-F15-03

**Requisitos:** REQ-F07-04

**Cenários relacionados:** AC-F07-04

**Entregável e limite:** PPK, controles, checagem reservada, alinhamento de passadas e filtro com preview.

**Critério de aceite:** Dados ruins não são curados por oracle; cobertura é recalculada após filtro.

**Evidência:** Fixtures e relatório antes/depois por operação.

### T-F07-04 — Implementar máscaras e conformidade

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #8 (`594ddf7`, run `43676281804`); [resumo CI](../../../artifacts/evidence/M2-wave3-ci/summary.json) e [evidências](../../../artifacts/evidence/T-F07-04/README.md). `PrivacyMaskingService` (`SyntheticSensitiveRegion`, `PrivacyComplianceManifest`) em `Gsa.Missions.Office`: detecção sintética de rostos/placas, confirmação/aplicação em Arcade ou Técnico, redação irreversível dos pixels da miniatura derivada sem alterar captura bruta e bloqueio de entrega (inclusive parcial) com região pendente.

**Marco:** M2

**Dependências:** T-F07-01, T-F04-03

**Requisitos:** REQ-F07-03

**Cenários relacionados:** AC-F07-03

**Entregável e limite:** Detecção sintética, confirmação/aplicação de máscaras e validação final no CQ.

**Critério de aceite:** Rosto/placa residual bloqueia entrega inclusive parcial.

**Evidência:** Miniaturas antes/depois e fixture de conformidade.

### T-F07-05 — Consolidar jobs e caches

- [ ] Aceita

**Marco:** M5

**Dependências:** T-F07-03, T-F12-02

**Requisitos:** REQ-F07-01, REQ-F07-02, REQ-F07-04

**Cenários relacionados:** AC-F07-01, AC-F07-02

**Entregável e limite:** Persistir receitas, invalidar derivados e retomar job sem taxa duplicada.

**Critério de aceite:** Recarregar/cancelar/retomar preserva origem e idempotência do serviço.

**Evidência:** Testes de save em cada fase do job.
