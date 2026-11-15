# Evidência — T-F05-02 (Integrar céu e estado de solução)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #6 (`876ca1d`, run `43674076883`); ver `artifacts/evidence/M2-wave1-ci/summary.json`.

## Escopo implementado
- `GnssProfileSelector`: mapeamento determinístico de `PlatformProfile` / upgrade de receptor para `GnssProfile` (`gnss_cellphone` / `gnss_navigation`) e `HeadingProfile` (`heading_level_0..2`).
- `GnssSkyEnvironmentResolver`: consulta a `SkyVisibilityCache` validado por `WorldSceneManifest`, cálculo de `dop = 1 + 4 * (1 - openness)`, bloqueio em `openness < 0.1` ou fora do grid (sem inventar céu aberto), e amplificação persistente de multipath dentro de `15 m` de fachadas de `metal` ou `glass` conservando a direção da célula.
- `GnssHeadingRig` + `InitialTrajectoryBuilder`: amostragem acoplada GNSS/heading com replay determinístico de estado e montagem de `EstimatedBodyTrajectory` somente a partir de observações disponíveis (sem `TruthSample`, erro realizado ou preenchimento de rumo parado).

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter GnssSkyIntegrationTests --no-restore
```

