# Tarefas — F06

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. O aceite de código segue a revisão delegada registrada em specs/README.md; não marcar por conclusão da documentação.

### T-F06-01 — Definir e validar dados de contrato

- [x] Aceita

**Estado:** aceita tecnicamente pela revisão delegada; integrada no PR #1 após CI Linux/Windows verde no SHA de773cd8227994cc9da18a36d352d02a9654c583. Critério local verificado; registros históricos e pendências de inspeção visual/isolamento Windows preservados em artifacts/evidence/M0-ci/summary.json.

**Marco:** M0

**Dependências:** T-F01-02

**Requisitos:** REQ-F06-01

**Cenários relacionados:** AC-F06-01

**Entregável e limite:** Schemas e fixtures dos quatro produtos, políticas de prazo/parcial e requisitos quantitativos/binários.

**Critério de aceite:** Validador rejeita limites sem unidade, excelente incoerente e produto incompatível.

**Evidência:** JSON de referência e testes de validação.

### T-F06-02 — Implementar máquina de contratos

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #6 (`876ca1d`, run `43674076883`); [resumo CI](../../../artifacts/evidence/M2-wave1-ci/summary.json) e [evidências](../../../artifacts/evidence/T-F06-02/README.md). Parser `ContractDefinition`, ciclo completo de estados/comandos/eventos, snapshot imutável ao aceitar, expiração em todos os estados ativos, custódia separada (`PendingField` vs `ProtectedOffloaded`) e entrega idempotente.

**Marco:** M2

**Dependências:** T-F06-01

**Requisitos:** REQ-F06-02

**Cenários relacionados:** AC-F06-03, AC-F06-04

**Entregável e limite:** Comandos/eventos, snapshot ao aceitar, relógio de prazo e estados terminais.

**Critério de aceite:** Transições inválidas retornam diagnóstico; todos os estados ativos verificam expiração.

**Evidência:** Testes de estados e calendário.

### T-F06-03 — Implementar CQ de fotos e inventário

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #6 (`876ca1d`, run `43674076883`); [resumo CI](../../../artifacts/evidence/M2-wave1-ci/summary.json) e [evidências](../../../artifacts/evidence/T-F06-03/README.md). `EvaluationReference` isolada em `Gsa.Missions.Quality`, cobertura por célula recortada `2×2 m`, deduplicação por ID, falha explícita para conjunto vazio ou `< 3` checkpoints, `privacy_redacted`/`format_valid`, `failure_reason_keys`, áreas de recoleta e entregáveis parciais.

**Marco:** M2

**Dependências:** T-F06-01, T-F04-01

**Requisitos:** REQ-F06-03, REQ-F06-05

**Cenários relacionados:** AC-F06-01, AC-F06-05

**Entregável e limite:** Cobertura/completude/imagem/RMSE e conformidade com referência isolada.

**Critério de aceite:** Vazio, oclusão, checkpoint ausente e célula parcial têm resultados corretos.

**Evidência:** Fixtures com cálculo independente.

### T-F06-04 — Implementar estrelas e valor de entrega

- [ ] Aceita

**Estado:** implementado em `codex/m2-wave2-stars-economy-persistence-settlement`; em revisão (aguardando CI do PR #7); [evidências](../../../artifacts/evidence/T-F06-04/README.md). `DeliveryQuoteCalculator` (`PaymentQuote`, `DeliveryMode`) em `Gsa.Missions.Quality`: agregação de margens não binárias com precisão IEEE-754 em `MapFullStars`, bloqueio de privacidade/formato mesmo em parcial, fatores de qualidade, arredondamento único (`Floor`) e multa de atraso/expiração (`-1` reputação).

**Marco:** M2

**Dependências:** T-F06-03

**Requisitos:** REQ-F06-04, REQ-F06-05

**Cenários relacionados:** AC-F06-01, AC-F06-03

**Entregável e limite:** Agregação de margens, parcial explícito, fatores e multa; retornar proposta de liquidação.

**Critério de aceite:** Privacidade bloqueia mesmo parcial; simular atraso produz valores esperados.

**Evidência:** Tabela de casos e testes de arredondamento.

### T-F06-05 — Integrar retrabalho e liquidação idempotente

- [ ] Aceita

**Estado:** implementado em `codex/m2-wave2-stars-economy-persistence-settlement`; em revisão (aguardando CI do PR #7); [evidências](../../../artifacts/evidence/T-F06-05/README.md). `ContractSettlementCoordinator` e `CareerProgressionState` em `Gsa.Missions.Contracts`: união deduplicada de lotes protegidos para CQ, ciclo `Retrabalho`, reputação por cliente/região clamped `>= 0` sem dupla contagem global, desbloqueios de história e liquidação idempotente `settlement:{ContractId}`.

**Marco:** M2

**Dependências:** T-F06-02, T-F06-04, T-F12-01, T-F08-01

**Requisitos:** REQ-F06-02, REQ-F06-04

**Cenários relacionados:** AC-F06-02, AC-F06-04

**Entregável e limite:** Unir lotes únicos, repetir CQ e entregar via ledger uma vez.

**Critério de aceite:** Destruição não elimina dados protegidos; repetir entrega não muda dinheiro.

**Evidência:** Cenário ponta a ponta e teste de crash/retry.

### T-F06-06 — Adicionar CQ de nuvem e pavimento

- [ ] Aceita

**Marco:** M5

**Dependências:** T-F06-03, T-F15-02

**Requisitos:** REQ-F06-03, REQ-F06-05

**Cenários relacionados:** AC-F06-01, AC-F06-05

**Entregável e limite:** Densidade por suporte, precisão relativa e interface para perfil de pavimento.

**Critério de aceite:** LOD/duplicação não alteram métricas; contraexemplos de oclusão reprovam.

**Evidência:** Planos conhecidos e relatórios numéricos.
