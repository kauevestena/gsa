# Tarefas — F18

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F18-01 — Adicionar ovos, marcos e cartas

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F02-03, T-F11-04

**Requisitos:** REQ-F18-01

**Cenários relacionados:** AC-F18-01

**Entregável e limite:** Sistema de coleções, dois marcos iniciais e catálogo futuro dos oito; seis cartas.

**Critério de aceite:** Recompensas são idempotentes e verbetes funcionam nos dois idiomas.

**Evidência:** Save de coleção e inspeção dos pontos de spawn.

**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F18-01](../../../artifacts/evidence/T-F18-01/README.md)).

### T-F18-02 — Adicionar álbum e cosméticos

- [ ] Aceita

**Marco:** M4

**Dependências:** T-F18-01, T-F08-02

**Requisitos:** REQ-F18-02

**Cenários relacionados:** AC-F18-01, AC-F18-02

**Entregável e limite:** Bloopers limitados, fantasias, cores/adesivos/buzinas e seleção persistente.

**Critério de aceite:** Cosméticos não alteram parâmetros científicos; álbum respeita limite.

**Evidência:** Comparação de CQ e catálogo visual.

### T-F18-03 — Adicionar eventos absurdos

- [ ] Aceita

**Marco:** M6

**Dependências:** T-F09-04, T-F02-05

**Requisitos:** REQ-F18-03

**Cenários relacionados:** AC-F18-03

**Entregável e limite:** Trio elétrico, maratona, pombos, aviso, desvio e término.

**Critério de aceite:** Nenhum evento sela a única rota do contrato nem viola CalmPolicy.

**Evidência:** Replay de cada evento e teste do grafo.

### T-F18-04 — Entregar desafio, filtros e coleção completa

- [ ] Aceita

**Marco:** M8

**Dependências:** T-F18-02, T-F15-05, T-F10-05

**Requisitos:** REQ-F18-01, REQ-F18-04

**Cenários relacionados:** AC-F18-01, AC-F18-02, AC-F18-04

**Entregável e limite:** Desafio da Galinha, Daguerreótipo, nuvem visual e marcos nas quatro regiões.

**Critério de aceite:** Medalha usa amostras reais de orientação e filtro final depende do evento de campanha.

**Evidência:** Fixtures do desafio e screenshots antes/depois.
