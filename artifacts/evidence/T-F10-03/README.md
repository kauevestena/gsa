# Evidence — T-F10-03 (Fechamento do Ato 1 com HIST-04, campanha HIST-01..11 e variantes calmas)

## Escopo
Conclusão jogável do Ato 1 via `HIST-04` (`Placas da Avenida`, `contract_hist_04` com 12 placas `sign_01..sign_12` em `old_center` / `commerce_street_signs`, plataforma mínima nível 2 motoneta, desbloqueando `car` e os distritos `port` e `waterfront`), especificação completa das 11 missões (`HIST-01`..`HIST-11`, Atos 1–3) com os 5 beats narrativos obrigatórios, cues de falha/retrabalho, cutscenes puláveis com equivalência exata de estado (`CutsceneSkipAndStateCoordinator`), variantes calmas paritárias em produto/recompensa/reputação/desbloqueios e ausência total de armas do jogador ou objetivos presos a estímulos audiovisuais.

## Implementação
- [hist-04-signs.json](../../../content/contracts/hist-04-signs.json)
- [HIST-04.json](../../../content/missions/HIST-04.json) & [HIST-03.json](../../../content/missions/HIST-03.json)
- [Act1ClosureAndFullCampaignScripts.cs](../../../src/Gsa.Missions/Campaign/Act1ClosureAndFullCampaignScripts.cs)
- [MvpCampaignAndProceduralGenerator.cs](../../../src/Gsa.Missions/Campaign/MvpCampaignAndProceduralGenerator.cs)

## Testes e Verificação
- [M4Act1ClosureAndScriptsTests.cs](../../../tests/Gsa.Missions.Tests/M4Act1ClosureAndScriptsTests.cs) (`T_F10_03_Hist04ClosesAct1UnlocksCarPortWaterfrontAndValidatesFullCampaignGraphWithCalmParity`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `150/150` em `Gsa.Missions.Tests` (`409/409` no domínio completo: `18` Localization, `205` Core, `150` Missions, `36` Tools).

## Estado
Aceita (`- [x] Aceita`) por revisão técnica delegada após CI Linux/Windows do [PR #15](https://github.com/kauevestena/gsa/pull/15) (commit testado `d1fe70a311606d002d9b5ee43f9f638aed2f9d43`, merge `8a0ddd42a460a33aa0882d889ec15bc4dfd5132e` em `2026-11-15T06:52:10Z`, run [`43687238085`](https://github.com/kauevestena/gsa/actions/runs/43687238085), jobs `132730158427` Linux e `132730158568` Windows); ver [resumo CI](../M4-wave3-ci/summary.json).

