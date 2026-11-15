# Tarefas — F08

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F08-01 — Implementar ledger e pagamentos

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #7 (`d5ba4ea`, run `43675243347`); [resumo CI](../../../artifacts/evidence/M2-wave2-ci/summary.json) e [evidências](../../../artifacts/evidence/T-F08-01/README.md). `CareerLedger` em `Gsa.Missions.Economy`: saldo e dívida de multas em Coords inteiros sem saldo negativo, subsídio em contrato calmo (`< 50` Coords), abatimento limitado a `20%` da dívida, recuperação gratuita do kit inicial e replay idempotente por `TransactionId` após serialização JSON.

**Marco:** M2

**Dependências:** T-F06-04

**Requisitos:** REQ-F08-01

**Cenários relacionados:** AC-F08-01

**Entregável e limite:** Livro-caixa com chave idempotente, arredondamento e proposta de liquidação.

**Critério de aceite:** Evento repetido não altera saldo novamente e custo não gera saldo negativo.

**Evidência:** Testes de transação e repetição após serialização.

### T-F08-02 — Adicionar loja e progressão inicial

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F08-01, T-F10-01

**Requisitos:** REQ-F08-02, REQ-F08-04

**Cenários relacionados:** AC-F08-02, AC-F08-04

**Entregável e limite:** Compra de bicicleta/motoneta, reputação, XP e kit de recuperação inicial.

**Critério de aceite:** Três missões do MVP abrem as plataformas previstas; saldo zero não bloqueia retorno.

**Evidência:** Percurso de compra e cenário de falência.

**Estado:** Implementada; em revisão delegada à CI (ver [evidência T-F08-02](../../../artifacts/evidence/T-F08-02/README.md)).

### T-F08-03 — Implementar habilidades e reputação completa

- [ ] Aceita

**Marco:** M4

**Dependências:** T-F08-02

**Requisitos:** REQ-F08-02

**Cenários relacionados:** AC-F08-02

**Entregável e limite:** Seis trilhas, três postos, reputação cliente/bairro e descontos.

**Critério de aceite:** Benefícios não modificam diretamente CQ; pontos/reputação persistem.

**Evidência:** Testes de desbloqueio e inspeção da árvore.

### T-F08-04 — Integrar serviços, seguro e frota avançada

- [ ] Aceita

**Marco:** M4

**Dependências:** T-F08-02

**Requisitos:** REQ-F08-03, REQ-F08-04

**Cenários relacionados:** AC-F08-03, AC-F08-04

**Entregável e limite:** Manutenção, seguro/franquia, taxas, multas, catálogo de níveis 3–6 e slots.

**Critério de aceite:** Cada custo é informado e persistido; seguro não restaura lotes perdidos.

**Evidência:** Matriz de serviços e save de sinistro.
