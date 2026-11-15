# Evidence — T-F10-06 (Catálogo de roteiros bilíngues pronto para gravação)

## Escopo
Manifesto estruturado `RecordingScriptCatalog` cobrindo em `pt-BR` e `en-US` todas as falas das missões `HIST-01`..`HIST-11` (5 beats por missão), cues de falha/retrabalho, introduções das variantes calmas, 11 cutscenes, falas de NPCs recorrentes (`Dona Clara`, `Eng. Arnaldo`, `Mestre Bastos`, `Inspetora Bia`, `Gimbalina`), atividades/minigames e programação das três emissoras de rádio (`Rádio Geodésia FM`, `Notícias São Datum`, `Orla Trópica FM`), com versionamento, contexto de gravação, speaker e reprodução de legendas independente de arquivos de áudio (`PlaybackSubtitlesWithoutAudio`).

## Implementação
- [Act1ClosureAndFullCampaignScripts.cs](../../../src/Gsa.Missions/Campaign/Act1ClosureAndFullCampaignScripts.cs)
- [en_US.po](../../../game/localization/en_US.po) & [pt_BR.po](../../../game/localization/pt_BR.po)

## Testes e Verificação
- [M4Act1ClosureAndScriptsTests.cs](../../../tests/Gsa.Missions.Tests/M4Act1ClosureAndScriptsTests.cs) (`T_F10_06_RecordingScriptCatalogCoversAllMissionsNpcsActivitiesAndThreeRadioStationsBilinguallyWithoutAudioFiles`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `150/150` em `Gsa.Missions.Tests` (`409/409` no domínio completo: `18` Localization, `205` Core, `150` Missions, `36` Tools).

## Estado
Aceita (`- [x] Aceita`) por revisão técnica delegada após CI Linux/Windows do [PR #15](https://github.com/kauevestena/gsa/pull/15) (commit testado `d1fe70a311606d002d9b5ee43f9f638aed2f9d43`, merge `8a0ddd42a460a33aa0882d889ec15bc4dfd5132e` em `2026-11-15T06:52:10Z`, run [`43687238085`](https://github.com/kauevestena/gsa/actions/runs/43687238085), jobs `132730158427` Linux e `132730158568` Windows); ver [resumo CI](../M4-wave3-ci/summary.json).

