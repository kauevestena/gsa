# Tarefas — F11

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F11-01 — Criar localização e glossário inicial

- [ ] Aceita

**Marco:** M0

**Dependências:** T-F01-02

**Requisitos:** REQ-F11-01, REQ-F11-05

**Cenários relacionados:** AC-F11-01

**Entregável e limite:** PO, fallback de idioma, formatação numérica e glossário versionado.

**Critério de aceite:** Plural e coordenadas 1234,56/1,234.56 seguem cultura sem afetar JSON.

**Evidência:** Fixtures de texto e teste de troca.

### T-F11-02 — Implementar HUD, mapa e ajustes

- [ ] Aceita

**Marco:** M2

**Dependências:** T-F11-01, T-F06-01

**Requisitos:** REQ-F11-02, REQ-F11-05

**Cenários relacionados:** AC-F11-02

**Entregável e limite:** Radar, heatmap provisório, recursos, sensores, rota, escala e remapeamento.

**Critério de aceite:** Campos indisponíveis ficam ocultos e indicadores não dependem só de cor.

**Evidência:** Screenshots nos dois idiomas e navegação por controle.

### T-F11-03 — Criar quadro, escritório e relatório

- [ ] Aceita

**Marco:** M2

**Dependências:** T-F11-02, T-F06-02

**Requisitos:** REQ-F11-02, REQ-F11-03

**Cenários relacionados:** AC-F11-02, AC-F11-03

**Entregável e limite:** Fluxos aceitar/processar/revisar/entregar com números e causas.

**Critério de aceite:** Todas as exigências e política de prazo/parcial aparecem antes da aceitação.

**Evidência:** Roteiro de UI e relatório comparado entre modos.

### T-F11-04 — Entregar Geopédia e revisão do MVP

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F11-01, T-F10-01

**Requisitos:** REQ-F11-01, REQ-F11-04

**Cenários relacionados:** AC-F11-01, AC-F11-04

**Entregável e limite:** Verbetes iniciais, marcos e textos completos do MVP nos dois idiomas.

**Critério de aceite:** Nenhuma chave ausente; exemplos correspondem à simulação implementada.

**Evidência:** Catálogo revisado e inspeção de telas longas.

### T-F11-05 — Completar UI técnica e Geopédia avançada

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F11-03, T-F15-03, T-F14-04

**Requisitos:** REQ-F11-02, REQ-F11-03, REQ-F11-04

**Cenários relacionados:** AC-F11-02, AC-F11-03, AC-F11-04

**Entregável e limite:** Overlays de nuvem/deriva/atenção e todos os conceitos avançados.

**Critério de aceite:** Parâmetros científicos explicados e limiares locais apresentados corretamente.

**Evidência:** Revisão técnica e screenshots de cada subsistema.

### T-F11-06 — Revisar localização e acessibilidade final

- [ ] Aceita

**Marco:** M8

**Dependências:** T-F17-03, T-F10-05, T-F11-05, T-F18-04

**Requisitos:** REQ-F11-01, REQ-F11-05

**Cenários relacionados:** AC-F11-01, AC-F11-04

**Entregável e limite:** Auditoria de chaves, fontes, plural, vozes/legendas, remapeamento e escala.

**Critério de aceite:** Zero fala/texto obrigatório ausente; nenhum comando cortado a 150% em 1080p.

**Evidência:** Matriz de telas/idiomas/entradas e checklist de voz.
