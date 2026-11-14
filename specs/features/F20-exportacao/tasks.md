# Tarefas — F20

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F20-01 — Definir adaptadores e dependências de exportação

- [ ] Aceita

**Marco:** E1

**Dependências:** T-F19-05

**Requisitos:** REQ-F20-01, REQ-F20-02

**Cenários relacionados:** AC-F20-01, AC-F20-02

**Entregável e limite:** Selecionar versões/licenças de escritor/leitor e fixar contrato/fixtures de export.

**Critério de aceite:** Windows/Linux suportados e redistribuição documentada; sem consulta a oracle.

**Evidência:** ADR de biblioteca e fixtures binários/JSON esperados.

### T-F20-02 — Implementar LAS e LAZ

- [ ] Aceita

**Marco:** E1

**Dependências:** T-F20-01

**Requisitos:** REQ-F20-02

**Cenários relacionados:** AC-F20-01, AC-F20-02

**Entregável e limite:** Escrita em streaming, sidecar, compressão e cancelamento seguro.

**Critério de aceite:** Leitor independente confirma campos/quantização e equivalência LAS/LAZ.

**Evidência:** Round-trip e teste de disco cheio/cancelamento.

### T-F20-03 — Implementar CSV e GeoJSON sintético

- [ ] Aceita

**Marco:** E1

**Dependências:** T-F20-01

**Requisitos:** REQ-F20-03

**Cenários relacionados:** AC-F20-01, AC-F20-03

**Entregável e limite:** Trajetória invariante e transformação explícita ENU/ECEF/WGS84 para inventário.

**Critério de aceite:** Não há ENU em coordenadas geográficas nem perda de marca de mundo fictício.

**Evidência:** Fixtures de transformação e parse de formatos.

### T-F20-04 — Validar exportação em ferramentas externas

- [ ] Aceita

**Marco:** E1

**Dependências:** T-F20-02, T-F20-03

**Requisitos:** REQ-F20-04

**Cenários relacionados:** AC-F20-04

**Entregável e limite:** Abertura em CloudCompare/QGIS, UI Técnico e documentação de referenciais.

**Critério de aceite:** Todos os formatos abrem nos dois sistemas, com contagem/posição conferidas.

**Evidência:** Screenshots, versões e relatório de diferenças.
