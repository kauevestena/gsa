# Tarefas — F02

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. O campo Estado registra o andamento de cada incremento iniciado. O aceite de código segue a revisão delegada registrada em specs/README.md; não marcar por conclusão da documentação.

### T-F02-01 — Criar graybox semântico e coordenadas

- [x] Aceita

**Estado:** aceita tecnicamente pela revisão delegada; integrada no PR #2 após CI Linux/Windows verde no SHA 0f7ba2c18f4d3441f757e862ba213364758f3d26. Critério local verificado; [evidências](../../../artifacts/evidence/T-F02-01/README.md) e registro de CI em `artifacts/evidence/M1-world-ci/summary.json`. Quarteirão de 48 objetos/47 colliders, IDs, alturas e raycast passaram no projeto/export Linux e Windows CI. Inspeção visual e futuros incrementos de streaming continuam separados.

**Marco:** M1

**Dependências:** T-F01-02

**Requisitos:** REQ-F02-01, REQ-F02-02

**Cenários relacionados:** AC-F02-01, AC-F02-02

**Entregável e limite:** Quarteirão com 20 fachadas de tutorial, entrada do escritório/descarregamento, superfícies, controles e conversor central de coordenadas.

**Critério de aceite:** Transformações e IDs passam fixtures; manifest de cena tem todas as tags.

**Evidência:** Teste de round-trip e inspeção do quarteirão.

### T-F02-02 — Pré-calcular visibilidade de céu

- [x] Aceita

**Estado:** aceita tecnicamente pela revisão delegada; integrada no PR #2 após CI Linux/Windows verde no SHA 0f7ba2c18f4d3441f757e862ba213364758f3d26. Critério local verificado; [evidências](../../../artifacts/evidence/T-F02-02/README.md) e registro de CI em `artifacts/evidence/M1-world-ci/summary.json`. CLI, 22 testes puros de céu, importador e cache do export Linux e Windows CI passaram.

**Marco:** M1

**Dependências:** T-F02-01, T-F01-03

**Requisitos:** REQ-F02-05

**Cenários relacionados:** AC-F02-02

**Entregável e limite:** CLI com hemisfério de amostragem, grid e hashes; importador no adaptador.

**Critério de aceite:** Céu aberto=1, túnel≈0, fachada intermediária; mudança de malha invalida cache.

**Evidência:** Fixtures de três ambientes e arquivo de céu.

### T-F02-03 — Montar Centro Velho do MVP

- [x] Aceita

**Marco:** M3

**Dependências:** T-F02-01, T-F03-01

**Requisitos:** REQ-F02-02, REQ-F02-03, REQ-F02-05

**Cenários relacionados:** AC-F02-02, AC-F02-03

**Entregável e limite:** Área 500×500 m, inventário-alvo de parâmetros, escritórios e streaming de setores.

**Critério de aceite:** Todos os produtos do MVP têm alvos suficientes e cruzar células não muda observações por falta de geometria.

**Evidência:** Mapa de alvos, percurso gravado e teste de streaming.

**Estado:** aceita via revisão técnica delegada após aprovação na CI do [PR #10](https://github.com/kauevestena/gsa/pull/10) (SHA testado `bf2ddd590bcebe58739160b63dbb9c6a27933c74`, merge `d8d7dc0`, execução [43679717318](https://github.com/kauevestena/gsa/actions/runs/43679717318), jobs Linux `132707485195` e Windows `132707484916`); [evidências](../../../artifacts/evidence/T-F02-03/README.md) e [resumo CI](../../../artifacts/evidence/M3-wave1-ci/summary.json).

### T-F02-04 — Expandir Centro, Porto e Orla

- [ ] Aceita

**Marco:** M4

**Dependências:** T-F02-03, T-F10-02

**Requisitos:** REQ-F02-04, REQ-F02-05

**Cenários relacionados:** AC-F02-03, AC-F02-04

**Entregável e limite:** Expandir os setores, portais, anel viário e boca acessível do túnel.

**Critério de aceite:** Acessos dependem dos eventos de história e os IDs do MVP não mudam.

**Evidência:** Manifest regional e percurso de acesso por nível.

**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F02-04](../../../artifacts/evidence/T-F02-04/README.md)).

### T-F02-05 — Produzir Serra e terrenos especiais

- [ ] Aceita

**Marco:** M6

**Dependências:** T-F02-04

**Requisitos:** REQ-F02-02, REQ-F02-04, REQ-F02-05

**Cenários relacionados:** AC-F02-02, AC-F02-03

**Entregável e limite:** Região com encostas, terra, vegetação e conexão ao túnel.

**Critério de aceite:** Rotas da picape permitem coleta e possuem alternativas calmas.

**Evidência:** Mapa semântico e inspeção de terreno.

### T-F02-06 — Integrar cidade e rede de marcos

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F02-05

**Requisitos:** REQ-F02-01, REQ-F02-04, REQ-F02-05

**Cenários relacionados:** AC-F02-01, AC-F02-04

**Entregável e limite:** Conectar quatro regiões e oito marcos; manifest de áreas do levantamento final.

**Critério de aceite:** Área total aproximada de 4 km²; viagem completa sem quebra de save/captura.

**Evidência:** Relatório de área e replay de travessia.
