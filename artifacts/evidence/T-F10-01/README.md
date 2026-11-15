# Evidência — T-F10-01 (Criar tutorial e catálogo inicial de história)

Estado: Implementado localmente; aceitação final delegada ao CI multiplataforma do PR de M2 Wave 4.

## Escopo implementado
- `StoryCampaignAndTutorial.cs` (`StoryCampaignCoordinator`, `TutorialMissionRunner`, `ControlVsCheckpointReference`, `MissionDefinition`, `CampaignState`) em `src/Gsa.Missions/Campaign/StoryCampaignAndTutorial.cs`:
  - Tutorial jogável `HIST-01` (`content/missions/HIST-01.json` + `content/contracts/facades.json`) executando o loop completo de ponta a ponta sobre `game/world/tutorial-block.json`: proposta/preparação, coleta inicial (carrinho manual + câmera + GNSS básico, sem sensores futuros `lidar`/`imu`/`dmi`/`rtk`/`ppk`), descarregamento atômico em `unload_point`, processamento inicial em modo `Technical` com reprovação em CQ (`privacy_redacted` + 5 fachadas com motion blur/ausentes -> `Retrabalho`), recoleta das 5 fachadas + confirmação de máscara de privacidade RGB (`32, 32, 32`) em miniatura derivada sem alterar SHA-256 bruto, aprovação 5 estrelas, pagamento único idempotente via ledger e round-trip de save (`SaveGameManager`).
  - Catálogo inicial acíclico do Ato 1 (`HIST-01` -> `HIST-02` -> `HIST-03`), desbloqueando bicicleta (`HIST-02`, chuva anunciada como obstáculo de qualidade sem falha instantânea) e patinete (`HIST-03`, alvo de controle local separado de checkpoints de CQ em `content/contracts/hist-03-sidewalks.json`).
  - Localização bilingue sincronizada (`game/localization/en_US.po`, `game/localization/pt_BR.po`), integração Godot em `GameFacade.RunM2TutorialSmoke` / `TutorialBlockScene` (`GSA_M2_LOOP_SMOKE_OK`) e CLI `gsa-tools run-m2-gate`.

## Artefato gerado
- Relatório determinístico do tutorial: `artifacts/evidence/T-F10-01/tutorial-execution-report.json` (`HIST-01`, `contract_hist_01`, 5 estrelas, 4 fases aprovadas).

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter StoryCampaignAndTutorialTests --no-restore
python3 scripts/verify.py --exports host
```

