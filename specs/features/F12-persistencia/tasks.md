# Tarefas — F12

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F12-01 — Implementar lotes e descarregamento atômico

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #7 (`d5ba4ea`, run `43675243347`); [resumo CI](../../../artifacts/evidence/M2-wave2-ci/summary.json) e [evidências](../../../artifacts/evidence/T-F12-01/README.md). `BatchCustodyStore` em `Gsa.Missions.Persistence`: `SurveyBatchArtifact` canônico com SHA-256, escrita em estágio + checksum antes do commit no `CustodyManifest` autoritativo, reconciliação `RecoverAfterCrash` (`Buffered` vs `Protected`), `HandleVehicleLoss` e descarregamento idempotente.

**Marco:** M2

**Dependências:** T-F01-02, T-F04-03

**Requisitos:** REQ-F12-01

**Cenários relacionados:** AC-F12-01

**Entregável e limite:** Chunk store, manifest de custódia e comando de descarga idempotente.

**Critério de aceite:** Crash antes/depois do commit distingue proteção corretamente.

**Evidência:** Testes de interrupção e checksums.

### T-F12-02 — Implementar save e recuperação

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #7 (`d5ba4ea`, run `43675243347`); [resumo CI](../../../artifacts/evidence/M2-wave2-ci/summary.json) e [evidências](../../../artifacts/evidence/T-F12-02/README.md). `SaveGameManager` e suporte polimórfico/snapshot em `ContractStateMachine` + `[JsonConstructor]` geométrico: save agregado em JSON com envelope `schema_version = 1` + SHA-256, rotação atômica `current.save.json` / `previous.save.json`, diagnósticos de corrupção/versão futura sem sobrescrever original, recuperação de geração anterior e autosave de perda de veículo.

**Marco:** M2

**Dependências:** T-F12-01, T-F06-02, T-F08-01

**Requisitos:** REQ-F12-02

**Cenários relacionados:** AC-F12-01, AC-F12-02

**Entregável e limite:** Save agregado, autosaves, backup anterior e persistência de perda de veículo.

**Critério de aceite:** Recarregar mantém prazos, RNG, saldo e custódia; erro não sobrescreve original.

**Evidência:** Fixtures e testes de crash/corrupção.

### T-F12-03 — Implementar replay e ferramenta de relatório

- [x] Aceita

**Marco:** M3

**Dependências:** T-F12-02, T-F05-02

**Requisitos:** REQ-F12-03

**Cenários relacionados:** AC-F12-03

**Entregável e limite:** Registro científico/visual e CLI replay-core/quality-report com hashes e comparação de métricas.

**Critério de aceite:** Replay reproduz métricas do mesmo build e render a 30/60/120 fps não altera amostragem.

**Evidência:** Relatórios comparados e arquivos de replay.

**Estado:** Aceita por revisão técnica delegada após CI Linux/Windows do [PR #12](https://github.com/kauevestena/gsa/pull/12) (`45fb52b`, merge `b2fa25f`, execução `43682314395`); ver [resumo CI](../../../artifacts/evidence/M3-wave3-ci/summary.json) e [evidência T-F12-03](../../../artifacts/evidence/T-F12-03/README.md).

### T-F12-04 — Adicionar migração e compatibilidade

- [x] Aceita

**Estado:** aceita por revisão técnica delegada após CI Linux/Windows do [PR #23](https://github.com/kauevestena/gsa/pull/23), código `e0b68b21b3f9d27c997ca0d4f7ba451dcfd1b776`, execução `44013474205`, jobs Linux `133805719610` e Windows `133805719851`. Conversão v1→v2, aliases, versões de conteúdo/modelo/mundo, invalidação de referências derivadas, proteção contra sobrescrita e recuperação explícita; snapshot da cidade e isolamento do Sandbox. Foram aprovados 455 testes e os smokes nativos da cidade, incluindo captura e recuperação pelo tablet. Ver [resumo CI](../../../artifacts/evidence/T-F12-04/ci-summary.json) e [evidências T-F12-04](../../../artifacts/evidence/T-F12-04/README.md). O head final do PR continua sujeito à CI antes do merge.

**Marco:** M7

**Dependências:** T-F12-03

**Requisitos:** REQ-F12-04

**Cenários relacionados:** AC-F12-02, AC-F12-04

**Entregável e limite:** Versão inicial→seguinte, aliases de conteúdo e invalidação de caches derivados.

**Critério de aceite:** Fixtures antigo/futuro/corrompido preservam originais e explicam falha.

**Evidência:** Testes de migração e procedimento de recuperação.
