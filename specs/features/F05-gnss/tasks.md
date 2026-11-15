# Tarefas — F05

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. O campo Estado registra o andamento de cada incremento iniciado. O aceite de código segue a revisão delegada registrada em specs/README.md; não marcar por conclusão da documentação.

### T-F05-01 — Implementar observações GNSS e heading

- [x] Aceita

**Estado:** aceita tecnicamente pela revisão delegada; integrada no PR #2 após CI Linux/Windows verde no SHA 0f7ba2c18f4d3441f757e862ba213364758f3d26. Critério local verificado; [evidências](../../../artifacts/evidence/T-F05-01/README.md) e registro de CI em `artifacts/evidence/M1-world-ci/summary.json`. 37 testes de observações/replay/RNG/estatística passaram. Integração de plataforma e base/RTK permanecem pendentes.

**Marco:** M1

**Dependências:** T-F01-02

**Requisitos:** REQ-F05-01, REQ-F05-02

**Cenários relacionados:** AC-F05-01, AC-F05-04

**Entregável e limite:** Modelos de ruído, multipath, heading e fixtures de ausência de solução.

**Critério de aceite:** API não devolve verdade junto da medição; reprodução preserva série de erros.

**Evidência:** Testes estatísticos e fixtures de orientação.

### T-F05-02 — Integrar céu e estado de solução

- [ ] Aceita

**Marco:** M2

**Dependências:** T-F02-02, T-F05-01

**Requisitos:** REQ-F05-01, REQ-F05-02

**Cenários relacionados:** AC-F05-01, AC-F05-04

**Entregável e limite:** Consulta ao grid, seleção de perfil por plataforma e saída para trajetória inicial.

**Critério de aceite:** Túnel não produz observação zero válida; mapa de céu corresponde ao cenário.

**Evidência:** Percurso aberto/cânion/túnel e log de solução.

### T-F05-03 — Adicionar aluguel e DGNSS

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F05-02, T-F08-01

**Requisitos:** REQ-F05-03

**Cenários relacionados:** AC-F05-02

**Entregável e limite:** Base alugada, correção comum, distância, custo e gravação dos logs.

**Critério de aceite:** Base ausente/distantemente decorrelacionada não produz precisão nominal indevida.

**Evidência:** Comparação com/sem base e registro de cobrança.

### T-F05-04 — Adicionar RTK e instalação de base

- [ ] Aceita

**Marco:** M4

**Dependências:** T-F05-03

**Requisitos:** REQ-F05-03, REQ-F05-04

**Cenários relacionados:** AC-F05-02, AC-F05-03

**Entregável e limite:** Ocupação de marco, RTK float/fix, observações corrigíveis e ciclo de vida dos logs.

**Critério de aceite:** Flags correspondem ao modelo de ruído e transições são preservadas no save.

**Evidência:** Logs RTK e teste de indisponibilidade/retomada.
