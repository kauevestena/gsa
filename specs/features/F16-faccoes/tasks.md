# Tarefas — F16

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F16-01 — Criar territórios e identidades fictícias

- [x] Aceita

**Marco:** M4

**Dependências:** T-F02-04

**Requisitos:** REQ-F16-01

**Cenários relacionados:** AC-F16-01

**Entregável e limite:** Catálogo das cinco facções, territórios e pistas narrativas.

**Critério de aceite:** Nomes/símbolos não remetem a organizações reais e áreas aparecem no planejamento.

**Evidência:** Revisão narrativa e mapa de territórios.
**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #15](https://github.com/kauevestena/gsa/pull/15) (`d1fe70a`, merge `8a0ddd4`, execução `43687238085`, jobs `132730158427` Linux e `132730158568` Windows; 2 de 4 tarefas de F16 aceitas na main, fechando M4 e restando T-F16-03 em M5 e T-F16-04 em M7); ver [resumo CI](../../../artifacts/evidence/M4-wave3-ci/summary.json) e [evidência T-F16-01](../../../artifacts/evidence/T-F16-01/README.md).

### T-F16-02 — Implementar atenção e ameaças iniciais

- [x] Aceita

**Marco:** M4

**Dependências:** T-F16-01, T-F09-05

**Requisitos:** REQ-F16-02, REQ-F16-03

**Cenários relacionados:** AC-F16-01, AC-F16-02

**Entregável e limite:** Acúmulo/decaimento, avisos, tinta, pneus e perseguição básica.

**Critério de aceite:** Cooldowns e limiares persistem; protagonista não dispõe de ataque.

**Evidência:** Cenários determinísticos por nível.
**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #15](https://github.com/kauevestena/gsa/pull/15) (`d1fe70a`, merge `8a0ddd4`, execução `43687238085`, jobs `132730158427` Linux e `132730158568` Windows; 2 de 4 tarefas de F16 aceitas na main, fechando M4 e restando T-F16-03 em M5 e T-F16-04 em M7); ver [resumo CI](../../../artifacts/evidence/M4-wave3-ci/summary.json) e [evidência T-F16-02](../../../artifacts/evidence/T-F16-02/README.md).

### T-F16-03 — Adicionar sabotagem avançada e contramedidas

- [ ] Aceita

**Marco:** M5

**Dependências:** T-F16-02, T-F08-04, T-F14-03

**Requisitos:** REQ-F16-03, REQ-F16-04, REQ-F16-05

**Cenários relacionados:** AC-F16-02, AC-F16-03, AC-F16-04

**Entregável e limite:** Spoofing, marcos, bloqueios, bomba e serviços de proteção/negociação.

**Critério de aceite:** Upload protege dados e modo calmo remove eventos pendentes sem cobrança indevida.

**Evidência:** Matriz ataque/contramedida/save.

### T-F16-04 — Integrar facções à cidade completa

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F16-03, T-F02-06

**Requisitos:** REQ-F16-01, REQ-F16-02, REQ-F16-05

**Cenários relacionados:** AC-F16-01, AC-F16-04

**Entregável e limite:** Territórios finais e pressão simultânea no levantamento final com variantes calmas.

**Critério de aceite:** Sem impasse obrigatório de combate e sem escalada em áreas protegidas.

**Evidência:** Playthrough de território em ambos os modos.
