# Evidence — T-F16-02 (Atenção territorial graduada, ameaças iniciais, bomba e Cidade Calma)

## Escopo
Máquina de estados determinística `TerritoryAttentionTracker` em `[0..100]` com acúmulo de `+1 pt/s` durante captura ativa no território, decaimento de `-2 pt/s` fora da zona ou após `10 s` sem coleta, limiares `20/40/60/80/100` (níveis 1–5: olheiro/aviso, tinta, pneu/perseguição, bloqueio/perseguição e tentativa de bomba em veículo parado), pré-aviso audiovisual `>= 3 s`, cooldown territorial de `30 s`, armamento da bomba após `15 s` estacionado com aviso de `10 s` e desarme parado em `5 s` (ou cancelamento ao sair do território ou ativar Cidade Calma), serialização de snapshot (`TerritoryAttentionSnapshot`) e integração com `UrbanHazardEncounterCoordinator`.

## Implementação
- [FactionsAndTerritoryAttention.cs](../../../src/Gsa.Core/Traffic/FactionsAndTerritoryAttention.cs)
- [UrbanHazardsAndCalmPolicy.cs](../../../src/Gsa.Core/Traffic/UrbanHazardsAndCalmPolicy.cs)

## Testes e Verificação
- [M4FactionsAndAttentionTests.cs](../../../tests/Gsa.Core.Tests/M4FactionsAndAttentionTests.cs) (`T_F16_02_TerritoryAttentionRatesThresholdsCooldownsBombDisarmAndCalmCityWorkDeterministically`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `205/205` em `Gsa.Core.Tests` (`409/409` no domínio completo: `18` Localization, `205` Core, `150` Missions, `36` Tools).

## Estado
Implementada; em revisão delegada à CI (aguardando validação no PR #15 da Onda 3 de M4).

