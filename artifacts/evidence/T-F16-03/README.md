# Evidence — T-F16-03 (Sabotagem avançada, detector 3-sigma de spoofing, custódia e contramedidas)

## Escopo
Coordenação avançada de sabotagem e contramedidas em `FactionSabotageAndCountermeasuresCoordinator`, `ProgressiveGnssSpoofingDetector` e `MovedBenchmarkRegistry`: spoofing progressivo de GNSS (até `10 m` em `5 s`) com rejeição baseada exclusivamente em inovação Kalman `> 3-sigma`, deslocamento físico de marcos mantendo histórico e referência de contrato intactos enquanto sinaliza discrepância pós-evento, bomba em veículo estacionado preservando `100%` dos lotes em custódia protegida (`ProtectedOffloaded`), invariante de não-violência (`ProtagonistInjured == false`, `GimbalinaInjured == false`), não repetição de ataques após save/reload, contramedidas multiplicativas (`Escolta 0,5x`, `Disfarce Kombi 0,5x` até 1º ataque, janela de madrugada `0,5x`) com piso `>= 0,25x`, autorização exclusivamente via associação de bairro legitimada por habilidade de negociação e cancelamento imediato pelo Modo Cidade Calma sem cobrança indevida.

## Implementação
- [FactionSabotageAndCountermeasuresCoordinator.cs](../../../src/Gsa.Missions/Economy/FactionSabotageAndCountermeasuresCoordinator.cs)

## Testes e Verificação
- [M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs](../../../tests/Gsa.Missions.Tests/M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs) (`T_F16_03_AdvancedSabotageSpoofing3SigmaMovedBenchmarkCustodyBombCountermeasuresAndCalmCity`)

## Estado
Aceita — PR #17 mergeado em main (88b1c23b90ba27d95ace458f1920400af66bb4c8) após aprovação nos jobs Linux (132739514446) e Windows (132739514694) da execução CI 43690357152; ver [resumo CI](../M5-wave2-ci/summary.json).

