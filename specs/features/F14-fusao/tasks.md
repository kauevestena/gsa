# Tarefas — F14

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F14-01 — Implementar IMU e atitude

- [x] Aceita

**Marco:** M4

**Dependências:** T-F05-04, T-F13-01

**Requisitos:** REQ-F14-01, REQ-F14-03

**Cenários relacionados:** AC-F14-01

**Entregável e limite:** Síntese de IMU, integração quaternion e correções observáveis de atitude.

**Critério de aceite:** Ideal/ruidoso passam fixtures e não há acesso a TruthSample no estimador.

**Evidência:** Testes de rotação, gravidade e bias.
**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #14](https://github.com/kauevestena/gsa/pull/14) (`dd96152`, merge `ecc1b7c`, execução `43685741329`); ver [resumo CI](../../../artifacts/evidence/M4-wave2-ci/summary.json) e [evidência T-F14-01](../../../artifacts/evidence/T-F14-01/README.md).

### T-F14-02 — Implementar DMI e ZUPT

- [ ] Aceita

**Marco:** M5

**Dependências:** T-F14-01

**Requisitos:** REQ-F14-02

**Cenários relacionados:** AC-F14-02

**Entregável e limite:** Velocidade/escala/slip, detector estacionário e observação zero.

**Critério de aceite:** Falso parado/derrapagem não zera trajetória indevidamente.

**Evidência:** Fixtures parado/movimento/molhado.
**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F14-02](../../../artifacts/evidence/T-F14-02/README.md)).

### T-F14-03 — Integrar filtro de trajetória

- [ ] Aceita

**Marco:** M5

**Dependências:** T-F14-02

**Requisitos:** REQ-F14-03

**Cenários relacionados:** AC-F14-01, AC-F14-02

**Entregável e limite:** Kalman seis estados, GNSS/DMI, covariância e blackout.

**Critério de aceite:** 100 seeds por perfil permitem comparar deriva; estados/covariâncias permanecem finitos.

**Evidência:** Relatório de deriva e teste matricial independente.
**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F14-03](../../../artifacts/evidence/T-F14-03/README.md)).

### T-F14-04 — Adicionar PPK, controles e heading duplo

- [ ] Aceita

**Marco:** M5

**Dependências:** T-F14-03, T-F05-04

**Requisitos:** REQ-F14-04, REQ-F14-03

**Cenários relacionados:** AC-F14-03, AC-F14-04

**Entregável e limite:** RTS, correção por base, controles separados e suporte a duas antenas por perfil.

**Critério de aceite:** Logs ausentes não produzem solução PPK; checagem não participa do ajuste.

**Evidência:** Datasets antes/depois, túnel/viaduto de bancada e revisão da fronteira de verdade.
**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F14-04](../../../artifacts/evidence/T-F14-04/README.md)).
