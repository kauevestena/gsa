# Evidência — T-F10-05 (Ato 3, levantamento final e templates avançados)

**Tarefa:** `T-F10-05` — Implementar Ato 3 e levantamento final (`REQ-F10-01`, `REQ-F10-02`, `REQ-F10-03`, `AC-F10-03`, `AC-F10-04`)

## O que foi entregue

1. **Template procedural de pavimento municipal (`PROC-PAVIMENTO`)**:
   - `Act3PavementProceduralGenerator` em [Act3CampaignAndPavementTemplates.cs](../../../src/Gsa.Missions/Campaign/Act3CampaignAndPavementTemplates.cs): gera contratos `pavement_profile` para a Van L6 nas quatro regiões (`old_center`, `port`, `waterfront`, `serra`) com certificados de rota, exigência conjunta de capacidades `["lidar", "camera"]` e validação de 100 sementes contra `ContractValidator`.
2. **Fechamento completo da campanha de 3 atos**:
   - `Act3AndFullCampaignRunner.RunAct3AndFullCampaignPlaythrough` executa Ato 1 (`HIST-01..04`) → save/restore → Ato 2 (`HIST-05..09`) → save/restore → Ato 3 (`HIST-10` perímetro clandestino no Porto desbloqueando a Van L6 + `HIST-11` quatro setores nas quatro regiões com prazo recomendado de 24 h, multa de atraso entre 24 h e 30 h, expiração rígida após 30 h e desbloqueio idempotente do filtro `city_point_cloud_filter`), com paridade total entre Modo Padrão e Cidade Calma e entre cutscenes assistidas e puladas.
3. **Integração jogável na cidade (`SaoDatumCityScene`)**:
   - Postes da Serra (`serra_utility_pole_01..08`) assentados no terreno com pista de serviço na Serra (`SerraUtilityServiceRoad`), além de suporte completo no tablet, orelhões de despacho e GPS 3D para `PROC-ENERGIA`, `PROC-TUNEL`, `PROC-OBRAS` e `PROC-PAVIMENTO`, avaliadores setoriais em [FinalCityPavementQuality.cs](../../../src/Gsa.Missions/Quality/FinalCityPavementQuality.cs) e suíte headless em [CityAdvancedProceduralSmoke.cs](../../../src/Gsa.Engine/World/CityAdvancedProceduralSmoke.cs) (`GSA_ADVANCED_PROCEDURAL_SMOKE_OK`).

## Verificação

- Testes de domínio em [M7Act3CampaignAndPavementTemplatesTests.cs](../../../tests/Gsa.Missions.Tests/M7Act3CampaignAndPavementTemplatesTests.cs).
- Pipeline completo `python3 scripts/verify.py --exports host` executado após a última edição, validando SDD, arquitetura, 100 sementes de `PROC-PAVIMENTO`, campanha completa de 3 atos, cena Godot e exportação nativa Linux.

