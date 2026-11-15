# Evidência — T-F11-03 (Criar quadro, escritório e relatório)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #8 (`594ddf7`, run `43676281804`); ver `artifacts/evidence/M2-wave3-ci/summary.json`.

## Escopo implementado
- `BoardOfficeAndReportPresenter` (`ContractBoardCardView`, `OfficeJobView`, `QualityReportView`) em `src/Gsa.Missions/Presentation/BoardOfficeAndReportPresenter.cs` e `GameFacade.GetContractBoardCardSummary` em `src/Gsa.Engine/GameFacade.cs`:
  - Cartão pré-aceitação exibindo todas as exigências, selo tranquilo/risco, prazos recomendado e final, taxas/tetos de multa e política de entregáveis parciais antes da aceitação.
  - Visão de escritório refletindo progresso de job, hash de receita, revisão, taxa, risco de expiração e contagem de máscaras aplicadas vs. pendentes.
  - Relatórios nos modos `Arcade` (estrelas e cartões corretivos) e `Técnico` (unidades, valores, limites e metas de excelência localizados) gerados do mesmo `QualityReport` sem divergir em critérios ou resultados.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter BoardOfficeAndReportPresenterTests --no-restore
```

