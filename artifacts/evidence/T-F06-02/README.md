# Evidência — T-F06-02 (Implementar máquina de contratos)

Estado: em revisão técnica delegada (aguardando CI nativa Linux/Windows do PR #6, `codex/m2-wave1-sensors-contracts-cq`).

## Escopo implementado
- `ContractDefinition.Parse`: carga tipada de JSON `schema_version = 1` validada pelo `ContractValidator`.
- `ContractStateMachine`: ciclo completo `Disponivel -> Aceito -> Coletando -> ProntoProcessar -> Processando -> EmCQ <-> Retrabalho -> ProntoEntregar -> Entregue / EntregueParcial`, além de `Expirado` e `Abandonado`.
- Snapshot imutável dos requisitos ao aceitar (isolado de reloads do catálogo), comandos explícitos com diagnósticos/códigos de retorno, eventos de domínio ordenados, custódia separada entre lotes `PendingField` (descartados em destruição/fuga) e `ProtectedOffloaded` (preservados em `unload_point`/escritório), verificação de expiração em todos os estados ativos e entrega idempotente.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter ContractStateMachineTests --no-restore
```

