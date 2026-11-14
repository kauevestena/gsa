# Tarefas — F12

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F12-01 — Implementar lotes e descarregamento atômico

- [ ] Aceita

**Marco:** M2

**Dependências:** T-F01-02, T-F04-03

**Requisitos:** REQ-F12-01

**Cenários relacionados:** AC-F12-01

**Entregável e limite:** Chunk store, manifest de custódia e comando de descarga idempotente.

**Critério de aceite:** Crash antes/depois do commit distingue proteção corretamente.

**Evidência:** Testes de interrupção e checksums.

### T-F12-02 — Implementar save e recuperação

- [ ] Aceita

**Marco:** M2

**Dependências:** T-F12-01, T-F06-02, T-F08-01

**Requisitos:** REQ-F12-02

**Cenários relacionados:** AC-F12-01, AC-F12-02

**Entregável e limite:** Save agregado, autosaves, backup anterior e persistência de perda de veículo.

**Critério de aceite:** Recarregar mantém prazos, RNG, saldo e custódia; erro não sobrescreve original.

**Evidência:** Fixtures e testes de crash/corrupção.

### T-F12-03 — Implementar replay e ferramenta de relatório

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F12-02, T-F05-02

**Requisitos:** REQ-F12-03

**Cenários relacionados:** AC-F12-03

**Entregável e limite:** Registro científico/visual e CLI replay-core/quality-report com hashes e comparação de métricas.

**Critério de aceite:** Replay reproduz métricas do mesmo build e render a 30/60/120 fps não altera amostragem.

**Evidência:** Relatórios comparados e arquivos de replay.

### T-F12-04 — Adicionar migração e compatibilidade

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F12-03

**Requisitos:** REQ-F12-04

**Cenários relacionados:** AC-F12-02, AC-F12-04

**Entregável e limite:** Versão inicial→seguinte, aliases de conteúdo e invalidação de caches derivados.

**Critério de aceite:** Fixtures antigo/futuro/corrompido preservam originais e explicam falha.

**Evidência:** Testes de migração e procedimento de recuperação.
