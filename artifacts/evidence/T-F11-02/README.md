# Evidência — T-F11-02 (Implementar HUD, mapa e ajustes)

Estado: implementado em `codex/m2-wave3-office-masks-hud-board`; em revisão (aguardando CI do PR #8).

## Escopo implementado
- `HudAndMapPresenter` (`HudSnapshotAggregator`, `RouteWaypointPlanner`, `MapOverlayView`, `UiPresentationSettings`, `VisualIndicator`) em `src/Gsa.Missions/Presentation/HudAndMapPresenter.cs`, `GameFacade.GetHudCapabilities` em `src/Gsa.Engine/GameFacade.cs` e chaves bilíngues em `game/localization/{en_US,pt_BR}.po`:
  - Snapshots agregados em cadência máxima de `10 Hz` com rótulo explícito de cobertura provisória (`IsOfficialQc = false`), indicadores acessíveis acoplando cor, ícone, padrão e chave, e ocultação de sistemas indisponíveis (LiDAR antes do nível 4, atenção antes de facções).
  - Planejamento de rotas no mapa limitado a `10` waypoints e sem piloto automático (`AutoPilotEnabled = false`).
  - Ajustes de escala de interface (`[0.85, 1.50]` verificada sem corte de comandos em `1280×720`), legendas, ordem de foco por controle e remapeamento persistente em `InputActionMap`.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter HudAndMapPresenterTests --no-restore
```

