# Integração incremental dos perigos na cidade

Implementação: [matriz jogável e limites](../../../../docs/city-hazards.md). O escopo liga modelos anteriores à cena real; T-F16-04 permanece aberta para playthrough final/inspeção visual.

Reprodução: `python3 scripts/install_toolchain.py` e `DOTNET_PROCESSOR_COUNT=1 python3 scripts/verify.py --exports host`. O pipeline exige o marcador `GSA_CITY_HAZARDS_SMOKE_OK` no editor e export nativo.

Testes específicos: `CityHazardPhysicsTests`, `CityHazardStateTests`, `SaveCompatibilityTests` e `RunCityHazardSmoke`. A rotina usa slot temporário, preserva bytes do fixture original e verifica dano/contagem de bomba, upload/perda/seguro, calm/autorização, observações e localização dinâmica.

Resultados do incremento inicial (`2f75ee9`): **493 testes de domínio**, **24 verificações de pipeline**, build sem warnings/erros, smoke da cidade no editor e no export Linux. Ver [relatório local](local-verification.json), [cena](city-save-smoke.log) e [executável](export-city-save-smoke-linux.log). A [CI Linux/Windows](https://github.com/kauevestena/gsa/actions/runs/44649286288) desse mesmo SHA passou nos dois hosts; [metadados](ci-initial-summary.json).

Revisão posterior: consultas de descarga pré-calculadas, invalidação de cache somente quando uma barreira muda, avisos prioritários no HUD/tablet e cenário de autorização cancelando ameaça com disfarce legado ativo. O [smoke da revisão](post-review-city-smoke.log) registra esses fluxos. A CI do SHA final será conferida antes do merge e vinculada no [PR #26](https://github.com/kauevestena/gsa/pull/26). Não há alegação de ganho de FPS por essas alterações. Não certifica aparência, controle, acessibilidade a 150% ou hardware de referência.
