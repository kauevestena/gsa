# Tarefas — F09

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F09-01 — Implementar tráfego regular de bancada

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa do PR #4 (`4a11e48`, [workflow run `43671438816`](https://github.com/kauevestena/gsa/actions/runs/43671438816), [resumo CI](../../../artifacts/evidence/M1-wave3-ci/summary.json)) e [evidências](../../../artifacts/evidence/T-F09-01/README.md). Grafo viário/calçadas com semáforos, faixas de pedestres, zona de exclusão no escritório, 50 carros + 30 pedestres em 60 Hz e remoção de impasse fora da câmera (>= 20 s) integrados e verificados em Linux e Windows headless.

**Marco:** M1

**Dependências:** T-F02-01

**Requisitos:** REQ-F09-01

**Cenários relacionados:** AC-F09-01

**Entregável e limite:** Grafo de faixas, semáforos, pedestres, agentes e remoção de impasses.

**Critério de aceite:** 50 carros percorrem o quarteirão sem bloquear o jogador permanentemente.

**Evidência:** Replay de tráfego e métricas.

### T-F09-02 — Implementar calendário, clima e aderência

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F02-03

**Requisitos:** REQ-F09-02

**Cenários relacionados:** AC-F09-02

**Entregável e limite:** Relógio acelerado, chuva/seco, sol, rush e snapshot para sensores.

**Critério de aceite:** Pausa congela relógios; avançar horário aplica deadline; chuva altera aderência.

**Evidência:** Testes de relógio e comparativos visuais.

**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F09-02](../../../artifacts/evidence/T-F09-02/README.md)).

### T-F09-03 — Adicionar bêbados, blitz e cães

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F09-01, T-F09-02

**Requisitos:** REQ-F09-03, REQ-F09-04

**Cenários relacionados:** AC-F09-03

**Entregável e limite:** Perfis erráticos, sinais, densidade por contexto e eventos de cão/blitz.

**Critério de aceite:** Probabilidades respeitam contextos; ameaças têm aviso e saída por direção.

**Evidência:** Cenários noturno/bar/fim de semana e blitz.

**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F09-03](../../../artifacts/evidence/T-F09-03/README.md)).

### T-F09-04 — Implementar contratos tranquilos e Cidade calma

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F09-03, T-F11-02

**Requisitos:** REQ-F09-05

**Cenários relacionados:** AC-F09-04

**Entregável e limite:** Filtro de perigos, proteção de área/rotas e toggle persistente.

**Critério de aceite:** Ativar modo cancela perigos pendentes sem custo/progressão inferior.

**Evidência:** Testes de eventos atrasados e missão tranquila.

**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F09-04](../../../artifacts/evidence/T-F09-04/README.md)).

### T-F09-05 — Integrar perigos e contramedidas urbanas

- [ ] Aceita

**Marco:** M4

**Dependências:** T-F09-03, T-F13-02

**Requisitos:** REQ-F09-04, REQ-F09-05

**Cenários relacionados:** AC-F09-02, AC-F09-04

**Entregável e limite:** Tinta/limpeza, buracos, flanelinha, fogos, multas e aplicação de impacto no rack.

**Critério de aceite:** Impacto/limpeza alteram sensores e CalmPolicy cobre todas as fontes hostis.

**Evidência:** Tabela de perigos executada e logs de montagem.
