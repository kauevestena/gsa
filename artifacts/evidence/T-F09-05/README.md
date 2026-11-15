# Evidence — T-F09-05 (Perigos urbanos integrados e política de Cidade Calma)

## Escopo
Coordenação unificada dos perigos urbanos (`UrbanHazardEncounterCoordinator`) cobrindo buzina (`+10` estresse), buraco (`+15` estresse, impacto e descalibração do rack), cão perseguidor (`+20` estresse, perseguição `30 m`/`40 m`/`10 s`, petisco), fogos/bomba (`+25` estresse e onda de choque), sabotagem com tinta/tomate e respingo de lama na lente (`LensContaminationTracker`, limpeza apenas parado `< 0.1 m/s` por `>= 3 s`), flanelinha (`10 Coords` ou `+5` de dano cosmético na recusa), milho (`-30`, cooldown `10 s`), repouso (`-5/s`) e supressão completa dos eventos hostis sob `CalmPolicyCoordinator` preservando desafios viários não hostis.

## Implementação
- [UrbanHazardsAndCalmPolicy.cs](../../../src/Gsa.Core/Traffic/UrbanHazardsAndCalmPolicy.cs)

## Testes e Verificação
- [M4RackImuAndUrbanHazardsTests.cs](../../../tests/Gsa.Core.Tests/M4RackImuAndUrbanHazardsTests.cs) (`T_F09_05_UrbanHazardTableExecutesAllMitigationsAndCalmCitySuppression`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `203/203` em `Gsa.Core.Tests` e `405/405` no domínio completo, e `python3 scripts/verify_architecture.py` (`valid: true`).

## Estado
Implementada; em revisão delegada à CI (aguardando validação no PR da Onda 2 de M4).

