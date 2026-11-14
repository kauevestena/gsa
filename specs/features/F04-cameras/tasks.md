# Tarefas — F04

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F04-01 — Implementar projeção e fixtures

- [ ] Aceita

**Marco:** M1

**Dependências:** T-F01-02, T-F02-01

**Requisitos:** REQ-F04-01, REQ-F04-02

**Cenários relacionados:** AC-F04-01, AC-F04-02

**Entregável e limite:** Pinhole, distorção e transformação sensor/corpo/mapa com entradas estimadas.

**Critério de aceite:** Fixtures de pixel, boresight e offset passam; teste impede parâmetro TruthSample no estimador.

**Evidência:** Testes analíticos e revisão das APIs.

### T-F04-02 — Integrar observações e miniaturas

- [ ] Aceita

**Marco:** M1

**Dependências:** T-F04-01

**Requisitos:** REQ-F04-01, REQ-F04-03

**Cenários relacionados:** AC-F04-01

**Entregável e limite:** Render target a 2 Hz no relógio físico de bancada, visibilidade, métricas de exposição/arrasto e miniatura.

**Critério de aceite:** Salvar amostra não exige textura cheia persistente; oclusão impede feição falsa.

**Evidência:** Capturas de bancada e manifesto de lote.

### T-F04-03 — Implementar scheduler e inventário

- [ ] Aceita

**Marco:** M2

**Dependências:** T-F04-02, T-F05-01

**Requisitos:** REQ-F04-02, REQ-F04-03

**Cenários relacionados:** AC-F04-02, AC-F04-04

**Entregável e limite:** Agendas de sensor, buffers imutáveis, triangulação e confirmação de inventário.

**Critério de aceite:** Sequência determinística; dados insuficientes não geram coordenadas perfeitas.

**Evidência:** Replay de duas passadas e cenário degenerado.

### T-F04-04 — Adicionar ambiente e sobreposição

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F04-03, T-F09-02

**Requisitos:** REQ-F04-01, REQ-F04-04

**Cenários relacionados:** AC-F04-03, AC-F04-04

**Entregável e limite:** Noite, chuva, ofuscamento, rolling shutter e footprint para sobreposição.

**Critério de aceite:** Comparação controlada mostra causas/efeitos; velocidade alta reduz sobreposição.

**Evidência:** Matriz de cenários dia/noite/seco/chuva.

### T-F04-05 — Integrar capacete e câmeras frente/trás

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F04-04, T-F03-04

**Requisitos:** REQ-F04-03, REQ-F04-04

**Cenários relacionados:** AC-F04-04

**Entregável e limite:** Perfis da bicicleta e motoneta com agendas e montagem próprias.

**Critério de aceite:** Câmeras não duplicam IDs e respeitam tempo/armazenamento.

**Evidência:** Relatório de cobertura por sensor.

### T-F04-06 — Adicionar câmera 360°

- [ ] Aceita

**Marco:** M4

**Dependências:** T-F04-05, T-F03-05

**Requisitos:** REQ-F04-03, REQ-F04-04

**Cenários relacionados:** AC-F04-04

**Entregável e limite:** Seis faces e cobertura panorâmica com montagem conjunta.

**Critério de aceite:** Costuras e oclusões não viram cobertura falsa.

**Evidência:** Panorâmica e inspeção por face.

### T-F04-07 — Implementar produto de pavimento

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F04-06, T-F15-05

**Requisitos:** REQ-F04-02, REQ-F04-04

**Cenários relacionados:** AC-F04-02, AC-F04-04

**Entregável e limite:** Câmeras inferiores e extração de perfil estimado por faixa/estaqueamento.

**Critério de aceite:** Lacunas permanecem explícitas e altura é comparada aos checkpoints no CQ.

**Evidência:** Fixture de perfil e contrato de engenharia.
