# Evidence — T-F08-03 (Implementar habilidades e reputação completa)

## Escopo
Complemento das 6 trilhas de habilidade com até 3 ranks (`DefensiveDriving`, `Navigation`, `Calibration`, `Processing`, `Negotiation`, `NightShift` a `500 XP/ponto` e `+10%/rank`), reputação global e por cliente com desconto limitado a `15%`, desbloqueios completos de plataformas (`Car` com rep global `>= 10`, `Kombi` com `city_hall >= 20`, `Pickup` com global `>= 40`, `Van` com global `>= 60`), cotações de upgrade de grau, inspeção da árvore, snapshot save/load (`FleetProgressionSnapshot`) e verificação de invariância científica (`VerifySkillsNeverAlterQualityMetrics`).

## Arquivos de Implementação e Testes
- [FleetShopAndProgression.cs](../../../src/Gsa.Missions/Economy/FleetShopAndProgression.cs)
- [en_US.po](../../../game/localization/en_US.po) & [pt_BR.po](../../../game/localization/pt_BR.po)
- [M4EconomyServicesAndCosmeticsTests.cs](../../../tests/Gsa.Missions.Tests/M4EconomyServicesAndCosmeticsTests.cs) (`T_F08_03_AllSixSkillTracksReputationDiscountsFleetGatesAndScientificInvariancePersistCleanly`)

## Execução e Verificação
- `dotnet test Gsa.Domain.slnf --no-restore`: `Gsa.Missions.Tests` validou todas as 6 trilhas, tetos de reputação e desconto (`15%`), portões de desbloqueio de frota, cotação de upgrade, invariância das métricas de CQ e serialização save/load.

## Estado
Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #13](https://github.com/kauevestena/gsa/pull/13) (SHA testado `8591eb655b2a2bb593d752623a91f402f97aeb76`, merge `ba18ae339d025405275ac6363ab7ee1625a1cbff`, execução [43683993914](https://github.com/kauevestena/gsa/actions/runs/43683993914), jobs Linux `132720474455` e Windows `132720474804`); ver [resumo CI](../M4-wave1-ci/summary.json).

