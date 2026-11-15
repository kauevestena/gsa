# Tarefas — F19

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F19-01 — Instrumentar e medir protótipo

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #5 (`8294536`, run `43672603212`, gate `pending_reference_hardware` aguardando máquina de referência Radeon Vega 7 não-headless); [resumo CI](../../../artifacts/evidence/M1-wave4-ci/summary.json), [evidências](../../../artifacts/evidence/T-F19-01/README.md) e [relatório de benchmark](../../../artifacts/evidence/T-F19-01/benchmark-report.json). Instrumentação de counters/timers/memória, cena M1 (quarteirão + 50 carros + 30 pedestres + câmera + LiDAR L4/L5/L6) nos presets `dry_day` e `rain_night`, três execuções de 5 minutos após 30 s de aquecimento e verificação em runtime Godot integrados.

**Marco:** M1

**Dependências:** T-F03-02, T-F04-02, T-F15-01, T-F09-01, T-F17-01

**Requisitos:** REQ-F19-01

**Cenários relacionados:** AC-F19-01

**Entregável e limite:** Counters, timers, memória, cena de 50 carros e protocolo de benchmark.

**Critério de aceite:** Relatório registra limites, taxas e hardware; gate pendente se referência indisponível.

**Evidência:** Três execuções e relatório da montagem do quarteirão.

### T-F19-02 — Validar primeira experiência completa

- [ ] Aceita

**Estado:** implementado em `codex/m2-wave4-tutorial-playtest-gate`; em revisão (aguardando CI do PR #9); [evidências](../../../artifacts/evidence/T-F19-02/README.md) e [relatório do gate](../../../artifacts/evidence/T-F19-02/m2-gate-report.json). `M2PlaytestAndGateValidator` em `Gsa.Missions.Diagnostics` e comando CLI `run-m2-gate` (`Gsa.Tools.Campaign.M2GateTool`): validação ponta a ponta do loop técnico M2 (`technical_loop_status: "passed"`) vinculado às 15 tarefas do marco e à avaliação da coorte de 5 novatos segundo `specs/validacao.md` (`playtest_gate_status: "pending_human_playtest_cohort"` até a execução com `IsHumanCohortSession = true`).

**Marco:** M2

**Dependências:** T-F10-01, T-F11-03, T-F12-02, T-F07-04, T-F06-05

**Requisitos:** REQ-F19-02

**Cenários relacionados:** AC-F19-02

**Entregável e limite:** Playtest M2, relatório de aprendizagem/interesse e lista de correções.

**Critério de aceite:** Participantes/limiares definidos no protocolo atendidos; falhas geram retrabalho.

**Evidência:** Relatório agregado e evidências do loop.

### T-F19-03 — Validar MVP com os dois públicos

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F19-02, T-F10-02, T-F09-04, T-F07-02, T-F12-03, T-F18-01

**Requisitos:** REQ-F19-02

**Cenários relacionados:** AC-F19-02

**Entregável e limite:** Sessões M3, revisão técnica e validação das três missões/cinco templates.

**Critério de aceite:** Quatro de cinco por público concluem/corrigem e três desejam continuar.

**Evidência:** Relatório de gate M3 e problemas resolvidos.

### T-F19-04 — Otimizar e medir cidade completa

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F15-05, T-F02-06, T-F17-02

**Requisitos:** REQ-F19-01

**Cenários relacionados:** AC-F19-01

**Entregável e limite:** Profiling, LOD/streaming/caches e benchmark da van no cenário oneroso.

**Critério de aceite:** Meta de fps/memória atendida sem alterar métricas por redução visual.

**Evidência:** Comparação antes/depois e fixtures preservados.

### T-F19-05 — Produzir e validar release candidate

- [ ] Aceita

**Marco:** M9

**Dependências:** T-F19-04, T-F11-06, T-F17-04, T-F17-05, T-F18-04, T-F10-05, T-F12-04

**Requisitos:** REQ-F19-03

**Cenários relacionados:** AC-F19-03

**Entregável e limite:** Executáveis finais, campanha offline, benchmark final com áudio/arte, matriz de regressão e documentação do usuário.

**Critério de aceite:** Zero bloqueador; Windows/Linux e idiomas/dublagem passam smoke e campanha.

**Evidência:** ReleaseManifest e logs por plataforma/locale.

### T-F19-06 — Preparar distribuição e resolver gates externos

- [ ] Aceita

**Marco:** M9

**Dependências:** T-F19-05

**Requisitos:** REQ-F19-04

**Cenários relacionados:** AC-F19-04

**Entregável e limite:** Pacote gratuito, fonte/licenças/créditos, título, classificação e registro das decisões institucionais.

**Critério de aceite:** EXT-02/03/04/05/06 resolvidas documentalmente antes de marcar pronto para publicação.

**Evidência:** Pacote revisável e decisões de responsáveis; publicação real é registrada separadamente.
