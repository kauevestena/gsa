# Tarefas — F04

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. O campo Estado registra o andamento de cada incremento iniciado. O aceite de código segue a revisão delegada registrada em specs/README.md; não marcar por conclusão da documentação.

### T-F04-01 — Implementar projeção e fixtures

- [x] Aceita

**Estado:** aceita tecnicamente pela revisão delegada; integrada no PR #2 após CI Linux/Windows verde no SHA 0f7ba2c18f4d3441f757e862ba213364758f3d26. Critério local verificado; [evidências](../../../artifacts/evidence/T-F04-01/README.md) e registro de CI em `artifacts/evidence/M1-world-ci/summary.json`. 34 testes analíticos passaram para projeção/extrínsecos/tempo.

**Marco:** M1

**Dependências:** T-F01-02, T-F02-01

**Requisitos:** REQ-F04-01, REQ-F04-02

**Cenários relacionados:** AC-F04-01, AC-F04-02

**Entregável e limite:** Pinhole, distorção e transformação sensor/corpo/mapa com entradas estimadas.

**Critério de aceite:** Fixtures de pixel, boresight e offset passam; teste impede parâmetro TruthSample no estimador.

**Evidência:** Testes analíticos e revisão das APIs.

### T-F04-02 — Integrar observações e miniaturas

- [x] Aceita

**Estado:** aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #3](https://github.com/kauevestena/gsa/pull/3) (SHA testado `6ca633eb54e63844b8261ccbc8932c0cdbd16680`, merge `684a3bf`, execução [43670010849](https://github.com/kauevestena/gsa/actions/runs/43670010849), jobs Linux `132677755968` e Windows `132677755785`); [evidências](../../../artifacts/evidence/T-F04-02/README.md) e [resumo CI](../../../artifacts/evidence/M1-mobility-camera-ci/summary.json). Scheduler a 2 Hz no relógio físico, 5 raios com oclusão real na engine, arrasto de dois instantes, rolling shutter, máscaras de privacidade e miniatura 256×144 sem textura cheia persistida.

**Marco:** M1

**Dependências:** T-F04-01

**Requisitos:** REQ-F04-01, REQ-F04-03

**Cenários relacionados:** AC-F04-01

**Entregável e limite:** Render target a 2 Hz no relógio físico de bancada, visibilidade, métricas de exposição/arrasto e miniatura.

**Critério de aceite:** Salvar amostra não exige textura cheia persistente; oclusão impede feição falsa.

**Evidência:** Capturas de bancada e manifesto de lote.

### T-F04-03 — Implementar scheduler e inventário

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #6 (`876ca1d`, run `43674076883`); [resumo CI](../../../artifacts/evidence/M2-wave1-ci/summary.json) e [evidências](../../../artifacts/evidence/T-F04-03/README.md). Agenda multissensor com orçamento de armazenamento virtual, lotes imutáveis, triangulação 2-raios (`>= 5°`), plano de suporte conhecido (`>= 5°`), diagnóstico `InsufficientGeometry` e modos `Arcade`/`Technical` implementados sem acesso a `TruthSample`.

**Marco:** M2

**Dependências:** T-F04-02, T-F05-01

**Requisitos:** REQ-F04-02, REQ-F04-03

**Cenários relacionados:** AC-F04-02, AC-F04-04

**Entregável e limite:** Agendas de sensor, buffers imutáveis, triangulação e confirmação de inventário.

**Critério de aceite:** Sequência determinística; dados insuficientes não geram coordenadas perfeitas.

**Evidência:** Replay de duas passadas e cenário degenerado.

### T-F04-04 — Adicionar ambiente e sobreposição

- [x] Aceita

**Marco:** M3

**Dependências:** T-F04-03, T-F09-02

**Requisitos:** REQ-F04-01, REQ-F04-04

**Cenários relacionados:** AC-F04-03, AC-F04-04

**Entregável e limite:** Noite, chuva, ofuscamento, rolling shutter e footprint para sobreposição.

**Critério de aceite:** Comparação controlada mostra causas/efeitos; velocidade alta reduz sobreposição.

**Evidência:** Matriz de cenários dia/noite/seco/chuva.

**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #11](https://github.com/kauevestena/gsa/pull/11) (`fe78e05`, merge `10697f3`, execução `43680847031`); ver [resumo CI](../../../artifacts/evidence/M3-wave2-ci/summary.json) e [evidência T-F04-04](../../../artifacts/evidence/T-F04-04/README.md).

### T-F04-05 — Integrar capacete e câmeras frente/trás

- [x] Aceita

**Marco:** M3

**Dependências:** T-F04-04, T-F03-04

**Requisitos:** REQ-F04-03, REQ-F04-04

**Cenários relacionados:** AC-F04-04

**Entregável e limite:** Perfis da bicicleta e motoneta com agendas e montagem próprias.

**Critério de aceite:** Câmeras não duplicam IDs e respeitam tempo/armazenamento.

**Evidência:** Relatório de cobertura por sensor.

**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #11](https://github.com/kauevestena/gsa/pull/11) (`fe78e05`, merge `10697f3`, execução `43680847031`); ver [resumo CI](../../../artifacts/evidence/M3-wave2-ci/summary.json) e [evidência T-F04-05](../../../artifacts/evidence/T-F04-05/README.md).

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
