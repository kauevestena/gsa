# Integração de cães e estacionamento na cidade

Escopo e parâmetros: [encontros cotidianos](../../../../docs/city-everyday-encounters.md), REQ-F09-04/05 e AC-F09-04. Implementa encontros na cena sem declarar concluída a integração de tráfego errático ou o playtest humano.

Reprodução: `python3 scripts/install_toolchain.py` e `DOTNET_PROCESSOR_COUNT=1 python3 scripts/verify.py --exports host`. O pipeline exige `GSA_CITY_EVERYDAY_SMOKE_OK` no editor e no executável nativo.

Casos: `CityEverydayEncountersTests`, `GimbalinaPersistenceTests`, migração/versões de `SaveCompatibilityTests` e `RunCityEverydaySmoke`. A rotina usa um slot temporário e confere que os bytes do fixture original foram preservados.

Verificação local Linux em 20/11/2026: 508 testes de domínio passaram; as 24 etapas do pipeline passaram, incluindo build, validação de specs/conteúdo/arquitetura, importação Godot, cena no editor, exportação Linux e a mesma cena no executável exportado. Os logs de cena preservados abaixo incluem o marcador de encontros. [Resumo estruturado](validation.json).

- [Cena no editor](editor-city-save-smoke.log)
- [Executável Linux](export-city-save-smoke-linux.log)

A verificação nativa Linux/Windows do commit publicado fica no check CI do PR; não é inferida da execução local. Inspeção visual, acessibilidade, tráfego errático/colisões na cidade e benchmark em hardware de referência permanecem pendentes.
