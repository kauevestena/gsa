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
Implementada; em revisão delegada à CI (aguardando validação no PR #15 da Onda 3 de M4).

