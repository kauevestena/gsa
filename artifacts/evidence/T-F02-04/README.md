# Evidence — T-F02-04 (Expandir Centro, Porto e Orla)

## Escopo
Expansão espacial do Centro Antigo para `1 km²` com conexão ao anel viário e criação dos bairros Porto (`port.json`, `1 km²`) e Orla (`waterfront.json`, `1 km²`), preservando 100% dos IDs e posições dos alvos do MVP e aplicando portais com controle por campanha e nível de plataforma (incluindo acesso de teste ao túnel do Porto pela Kombi nível 4).

## Arquivos de Implementação e Testes
- [old-center.json](../../../content/regions/old-center.json)
- [port.json](../../../content/regions/port.json)
- [waterfront.json](../../../content/regions/waterfront.json)
- [RegionalCityExpansion.cs](../../../src/Gsa.Core/World/RegionalCityExpansion.cs)
- [M4WorldCar360AndRtkTests.cs](../../../tests/Gsa.Core.Tests/M4WorldCar360AndRtkTests.cs) (`T_F02_04_ExpandedCityManifestPreservesMvpIdsCoversThreeSquareKmAndGatesPortalsByLevel`)

## Execução e Verificação
- `dotnet test Gsa.Domain.slnf --no-restore`: `Gsa.Core.Tests` executou sem falhas com validação de 300 setores (`3 x 1 km²`), loop do anel viário, preservação de IDs do MVP (`RegionalSectorCatalog.VerifyMvpTargetIdsPreserved`) e travas/liberação de portais (`RegionalAccessGateEvaluator.EvaluatePortalAccess`).
- A validação de conteúdo regional via `Gsa.Tools.Tests` / `scripts/verify.py --exports host` e `python3 specs/validate.py` confirmam integridade dos novos manifests JSON.

## Estado
Implementada; em revisão delegada à CI.

