# Tarefas — F13

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F13-01 — Implementar rack e revisões de montagem

- [x] Aceita

**Marco:** M4

**Dependências:** T-F03-05, T-F04-06

**Requisitos:** REQ-F13-01

**Cenários relacionados:** AC-F13-01

**Entregável e limite:** Slots, compatibilidade, transformação física/estimada e histórico de revisões.

**Critério de aceite:** Troca incompatível é recusada; mudança não altera lote antigo.

**Evidência:** Testes de montagem e inspeção de oficina.
**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #14](https://github.com/kauevestena/gsa/pull/14) (`dd96152`, merge `ecc1b7c`, execução `43685741329`); ver [resumo CI](../../../artifacts/evidence/M4-wave2-ci/summary.json) e [evidência T-F13-01](../../../artifacts/evidence/T-F13-01/README.md).

### T-F13-02 — Integrar descalibração por impacto

- [x] Aceita

**Marco:** M4

**Dependências:** T-F13-01

**Requisitos:** REQ-F13-02

**Cenários relacionados:** AC-F13-01, AC-F13-02

**Entregável e limite:** Perturbações determinísticas, sintomas e flag de inspeção.

**Critério de aceite:** Impacto produz assinatura de erro mensurável sem expor parâmetro verdadeiro ao jogador.

**Evidência:** Replay antes/depois e testes analíticos.
**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #14](https://github.com/kauevestena/gsa/pull/14) (`dd96152`, merge `ecc1b7c`, execução `43685741329`); ver [resumo CI](../../../artifacts/evidence/M4-wave2-ci/summary.json) e [evidência T-F13-02](../../../artifacts/evidence/T-F13-02/README.md).

### T-F13-03 — Criar minigames e solvers

- [x] Aceita

**Marco:** M4

**Dependências:** T-F13-01, T-F05-04

**Requisitos:** REQ-F13-03, REQ-F13-04

**Cenários relacionados:** AC-F13-02, AC-F13-03, AC-F13-04

**Entregável e limite:** Grade de câmera, planos, percurso temporal, solvers e UI de ajuste/assistência.

**Critério de aceite:** Conjunto reservado passa limiares; degeneração/nonconvergência é diagnosticada.

**Evidência:** Fixtures, resíduos e gravação dos quatro procedimentos.
**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #14](https://github.com/kauevestena/gsa/pull/14) (`dd96152`, merge `ecc1b7c`, execução `43685741329`); ver [resumo CI](../../../artifacts/evidence/M4-wave2-ci/summary.json) e [evidência T-F13-03](../../../artifacts/evidence/T-F13-03/README.md).

### T-F13-04 — Integrar oficina às passadas LiDAR

- [x] Aceita

**Marco:** M5

**Dependências:** T-F13-03, T-F15-02

**Requisitos:** REQ-F13-03, REQ-F13-04

**Cenários relacionados:** AC-F13-02, AC-F13-04

**Entregável e limite:** Alinhamento de passadas reais, serviço automático e receitas reprocessáveis.

**Critério de aceite:** Calibração reduz discrepância observada sem acessar oracle; taxa é idempotente.

**Evidência:** Relatório antes/depois e receita salva.
**Estado:** Aceita via revisão técnica delegada após aprovação na CI do [PR #17](https://github.com/kauevestena/gsa/pull/17) (`b313e0b`, merge `88b1c23`, execução `43690357152`, jobs Linux `132739514446` e Windows `132739514694`); ver [resumo CI](../../../artifacts/evidence/M5-wave2-ci/summary.json) e [evidência T-F13-04](../../../artifacts/evidence/T-F13-04/README.md).
