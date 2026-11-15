# Evidência T-F10-04 — Implementar ato 2 e templates avançados

## Escopo implementado
- Alinhamento de `HIST-05` a `HIST-11` no catálogo de campanha em [Act1ClosureAndFullCampaignScripts.cs](../../../src/Gsa.Missions/Campaign/Act1ClosureAndFullCampaignScripts.cs).
- `Act2CampaignRunner` e `Act2AdvancedProceduralGenerator` em [Act2CampaignAndAdvancedTemplates.cs](../../../src/Gsa.Missions/Campaign/Act2CampaignAndAdvancedTemplates.cs):
  - Execução do Ato 2 (`HIST-05` a `HIST-09`) com validação de plataforma mínima (`CanExecuteMissionWithPlatform`), desbloqueio progressivo de `kombi`, `pickup` e `serra`, proteção de referência altimétrica em viadutos (`ValidateViaductHeightReference`) e variantes padrão vs. Cidade Calma.
  - Templates avançados `PROC-ENERGIA`, `PROC-OBRAS` e `PROC-TUNEL` com geração e validação de 300 sementes (100 por template) contra `ContractValidator`, verificação de invalidação de certificado por hash e filtragem do quadro de contratos.

## Verificação
- Teste automatizado em [M6Act2CampaignAndAdvancedTemplatesTests.cs](../../../tests/Gsa.Missions.Tests/M6Act2CampaignAndAdvancedTemplatesTests.cs).
- Especificação em [F10-missoes/tasks.md](../../../specs/features/F10-missoes/tasks.md).

## Estado
Implementada e em verificação para o marco M6.

