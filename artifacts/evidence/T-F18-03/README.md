# Evidência T-F18-03 — Adicionar eventos absurdos

## Escopo implementado
- `AbsurdUrbanEventsCoordinator` em [AbsurdUrbanEventsCoordinator.cs](../../../src/Gsa.Core/Traffic/AbsurdUrbanEventsCoordinator.cs):
  - Eventos `TrioEletrico`, `StreetMarathon` e `PigeonFlock` com aviso prévio mínimo de 15 s, duração limitada a 120 s e encerramento limpo.
  - Busca de desvio transitável via BFS (`TryFindTransitableDetour`), rejeitando qualquer evento que sele a única rota do contrato.
  - Suporte a `ApplyCalmPolicy` para conformidade em contratos silenciosos/calmos e grafo de teste `CreateDetourAndBottleneckTestGraph`.

## Verificação
- Teste automatizado em [M6SerraPickupPairedLidarAndEventsTests.cs](../../../tests/Gsa.Core.Tests/M6SerraPickupPairedLidarAndEventsTests.cs).
- Especificação em [F18-atividades/tasks.md](../../../specs/features/F18-atividades/tasks.md).

## Estado
Aceita — PR #18 (commit `61214b8`, merge `11bb19b`, execução CI `43692103902`: Linux `132744736874` SUCCESS, Windows `132744737031` SUCCESS). Ver [resumo M6 CI](../M6-ci/summary.json).

