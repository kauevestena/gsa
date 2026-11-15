# Tarefas — F10

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F10-01 — Criar tutorial e catálogo inicial de história

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #9 (`ecfb666`, run `43678477743`); [resumo CI](../../../artifacts/evidence/M2-wave4-ci/summary.json), [evidências](../../../artifacts/evidence/T-F10-01/README.md) e [relatório](../../../artifacts/evidence/T-F10-01/tutorial-execution-report.json). `StoryCampaignAndTutorial.cs` (`StoryCampaignCoordinator`, `TutorialMissionRunner`, `ControlVsCheckpointReference`) + `content/missions/HIST-{01,02,03}.json` + `content/contracts/hist-{02-drainage,03-sidewalks}.json` + chaves `.po` + `GameFacade.RunM2TutorialSmoke`: tutorial completo `HIST-01` (coleta sem sensores futuros, CQ com retrabalho, máscara RGB `(32,32,32)` em miniatura, 5 estrelas, liquidação idempotente, round-trip de save) e catálogo acíclico do Ato 1 desbloqueando `bicycle` e `scooter`.

**Marco:** M2

**Dependências:** T-F06-05, T-F07-04, T-F11-03, T-F12-02, T-F05-02, T-F03-02

**Requisitos:** REQ-F10-01, REQ-F10-04

**Cenários relacionados:** AC-F10-01

**Entregável e limite:** HIST-01 jogável no quarteirão de bancada e definições/roteiros de HIST-02/03 com objetivos e chaves.

**Critério de aceite:** HIST-01 ensina coleta, descarga, correção e pagamento sem exigir sensor futuro.

**Evidência:** Roteiro e execução do tutorial.

### T-F10-02 — Completar MVP e gerador de cinco templates

- [x] Aceita

**Marco:** M3

**Dependências:** T-F10-01, T-F05-03, T-F08-02, T-F04-05, T-F09-04, T-F07-02, T-F11-04

**Requisitos:** REQ-F10-01, REQ-F10-02, REQ-F10-03

**Cenários relacionados:** AC-F10-01, AC-F10-02

**Entregável e limite:** HIST-02/03 jogáveis, templates e certificados de rota/seed.

**Critério de aceite:** 100 seeds/template passam validação; há contrato tranquilo em qualquer saldo.

**Evidência:** Relatórios de geração e três missões gravadas.

**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #12](https://github.com/kauevestena/gsa/pull/12) (`45fb52b`, merge `b2fa25f`, execução `43682314395`); ver [resumo CI](../../../artifacts/evidence/M3-wave3-ci/summary.json) e [evidência T-F10-02](../../../artifacts/evidence/T-F10-02/README.md).

### T-F10-03 — Concluir ato 1 e escrever campanha restante

- [x] Aceita

**Marco:** M4

**Dependências:** T-F10-02, T-F03-05, T-F16-02

**Requisitos:** REQ-F10-01, REQ-F10-03, REQ-F10-04

**Cenários relacionados:** AC-F10-03

**Entregável e limite:** HIST-04, abertura Porto/Orla, especificação narrativa completa HIST-05 a 11 e variantes calmas.

**Critério de aceite:** Não há objetivo dependente de evento audiovisual ou de arma do jogador.

**Evidência:** Grafo de objetivos e revisão de roteiro.
**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #15](https://github.com/kauevestena/gsa/pull/15) (`d1fe70a`, merge `8a0ddd4`, execução `43687238085`, jobs `132730158427` Linux e `132730158568` Windows; 4 de 6 tarefas de F10 aceitas na main nos PRs #9, #12 e #15, restando T-F10-04 em M6 e T-F10-05 em M7); ver [resumo CI](../../../artifacts/evidence/M4-wave3-ci/summary.json) e [evidência T-F10-03](../../../artifacts/evidence/T-F10-03/README.md).

### T-F10-04 — Implementar ato 2 e templates avançados

- [ ] Aceita

**Marco:** M6

**Dependências:** T-F10-03, T-F15-04, T-F03-07, T-F07-05, T-F16-03

**Requisitos:** REQ-F10-01, REQ-F10-02, REQ-F10-03

**Cenários relacionados:** AC-F10-02, AC-F10-03

**Entregável e limite:** HIST-05 a 09, liberação Serra e templates energia/obras/túnel.

**Critério de aceite:** Rotas demonstram CQ e variantes calmas; mínimos de plataforma são respeitados.

**Evidência:** Playthrough do ato e certificados de rota.

### T-F10-05 — Implementar ato 3 e levantamento final

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F10-04, T-F03-08, T-F02-06, T-F16-04, T-F04-07

**Requisitos:** REQ-F10-01, REQ-F10-02, REQ-F10-03

**Cenários relacionados:** AC-F10-03, AC-F10-04

**Entregável e limite:** HIST-10/11, template pavimento, quatro setores e final com desbloqueios.

**Critério de aceite:** 24h recomendadas/30h finais são testadas e cobertura exigida é realizável.

**Evidência:** Playthrough completo e relatório final de CQ.

### T-F10-06 — Estabilizar roteiros para gravação

- [x] Aceita

**Marco:** M4

**Dependências:** T-F10-03

**Requisitos:** REQ-F10-04

**Cenários relacionados:** AC-F10-03

**Entregável e limite:** Catálogo de falas/cues de missões, NPCs, atividades e programas das três rádios, com IDs, contexto e versões nos dois idiomas para gravação.

**Critério de aceite:** Roteiro inclui entradas, retrabalho, falha, variante calma e cutscenes, sem depender da gravação para testar.

**Evidência:** Revisão narrativa/técnica e manifesto de falas.
**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #15](https://github.com/kauevestena/gsa/pull/15) (`d1fe70a`, merge `8a0ddd4`, execução `43687238085`, jobs `132730158427` Linux e `132730158568` Windows; 4 de 6 tarefas de F10 aceitas na main nos PRs #9, #12 e #15, restando T-F10-04 em M6 e T-F10-05 em M7); ver [resumo CI](../../../artifacts/evidence/M4-wave3-ci/summary.json) e [evidência T-F10-06](../../../artifacts/evidence/T-F10-06/README.md).
