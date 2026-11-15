# Tarefas — F15

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F15-01 — Construir bancada LiDAR

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa do PR #4 (`4a11e48`, [workflow run `43671438816`](https://github.com/kauevestena/gsa/actions/runs/43671438816), [resumo CI](../../../artifacts/evidence/M1-wave3-ci/summary.json)) e [evidências](../../../artifacts/evidence/T-F15-01/README.md). Perfis L4/L5/L6 de `parametros.md` sem oferta na loja/MVP, materiais, chuva, vegetação multi-retorno, fixture analítico de georreferenciamento sem verdade e fila limitada a 0,5 s integrados com raycast Godot e verificados em Linux e Windows headless.

**Marco:** M1

**Dependências:** T-F02-01

**Requisitos:** REQ-F15-01, REQ-F15-02

**Cenários relacionados:** AC-F15-01

**Entregável e limite:** Plano, raycasts seguros, padrão de varredura e contador de raios/retornos/tempo.

**Critério de aceite:** Rodar sem oferecer equipamento na loja/MVP; raio correto e backlog observável.

**Evidência:** Benchmark e fixture analítico.

### T-F15-02 — Integrar perfilador e georreferenciamento

- [x] Aceita

**Marco:** M5

**Dependências:** T-F15-01, T-F14-03, T-F03-06

**Requisitos:** REQ-F15-02, REQ-F15-03, REQ-F15-05

**Cenários relacionados:** AC-F15-01, AC-F15-02

**Entregável e limite:** Aquisição da Kombi com tempo por raio, material e trajetória estimada.

**Critério de aceite:** Velocidade/oclusão afetam nuvem; não usar pose verdadeira no produto.

**Evidência:** Percurso com planos e relatório de densidade.
**Estado:** Aceita — PR #16 mergeado em main (13e3378fd3c2922be4af044de71e3c99c8d8b35f); ver [resumo CI](../../../artifacts/evidence/M5-wave1-ci/summary.json) e [evidência T-F15-02](../../../artifacts/evidence/T-F15-02/README.md).

### T-F15-03 — Implementar octree, chunks e LOD

- [x] Aceita

**Marco:** M5

**Dependências:** T-F15-02

**Requisitos:** REQ-F15-03, REQ-F15-04

**Cenários relacionados:** AC-F15-03

**Entregável e limite:** Paginação, checksums, cache LRU, índice por revisão e desenho GPU.

**Critério de aceite:** Cache ≤256 MiB; alternar LOD não altera CQ nem perde amostra.

**Evidência:** Teste de reload e perfil de memória/GPU.
**Estado:** Aceita — PR #16 mergeado em main (13e3378fd3c2922be4af044de71e3c99c8d8b35f); ver [resumo CI](../../../artifacts/evidence/M5-wave1-ci/summary.json) e [evidência T-F15-03](../../../artifacts/evidence/T-F15-03/README.md).

### T-F15-04 — Adicionar sensores pareados e múltiplos retornos

- [ ] Aceita

**Marco:** M6

**Dependências:** T-F15-03, T-F03-07

**Requisitos:** REQ-F15-02, REQ-F15-05

**Cenários relacionados:** AC-F15-02, AC-F15-04

**Entregável e limite:** Perfis picape, vegetação transmissiva, chuva e incidência.

**Critério de aceite:** Retornos têm IDs/número coerentes e usam a montagem de cada sensor.

**Evidência:** Fixtures por material e replay em vegetação.

**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F15-04](../../../artifacts/evidence/T-F15-04/README.md)).

### T-F15-05 — Entregar perfil de engenharia e otimizar aquisição

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F15-04, T-F03-08

**Requisitos:** REQ-F15-04, REQ-F15-05

**Cenários relacionados:** AC-F15-01, AC-F15-03

**Entregável e limite:** Perfil van, orçamento final e caminho de aquisição otimizado apenas se necessário.

**Critério de aceite:** Alvos de engenharia são atingíveis com rota planejada e CQ invariável sob LOD.

**Evidência:** Benchmark comparado e contrato de referência.
