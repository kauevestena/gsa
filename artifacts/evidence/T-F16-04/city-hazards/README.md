# Integração incremental dos perigos na cidade

Implementação: [matriz jogável e limites](../../../../docs/city-hazards.md). O escopo liga modelos anteriores à cena real; T-F16-04 permanece aberta para playthrough final/inspeção visual.

Reprodução: `python3 scripts/install_toolchain.py` e `DOTNET_PROCESSOR_COUNT=1 python3 scripts/verify.py --exports host`. O pipeline exige o marcador `GSA_CITY_HAZARDS_SMOKE_OK` no editor e export nativo.

Testes específicos: `CityHazardPhysicsTests`, `CityHazardStateTests`, `SaveCompatibilityTests` e `RunCityHazardSmoke`. A rotina usa slot temporário, preserva bytes do fixture original e verifica dano/contagem de bomba, upload/perda/seguro, calm/autorização, observações e localização dinâmica.

Estado: restauração da implementação e verificação completa em andamento. Resultados executados serão anexados antes da publicação final. Não certifica aparência, controle, acessibilidade a 150% ou hardware de referência.
