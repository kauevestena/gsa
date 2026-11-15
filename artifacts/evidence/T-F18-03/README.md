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
Implementada e em verificação para o marco M6.

